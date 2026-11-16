#!/usr/bin/env python3
"""Street Life: slide the two buildings along the road so they stand more opposite each other (Pete, 16 Nov 2026).

  python3 Tools/streetshift.py            dry run: says what would move and what would be left straddling or colliding
  python3 Tools/streetshift.py --apply    writes Assets/StreetLife/Data/map.json, map.bytes and dyn.json

Everything that lies wholly inside a building's rectangle moves with it: static mesh pieces (the meshes are merged by
material, so each connected piece is judged on its own), collision boxes, breakable entities with their boxes, ladders,
lights, spawns and line art. Lawns, pavements and anything larger than the rectangle stay where they are.
map.json gets "shifted": [...] so it can't be applied twice. After a fresh export from the web game, run it again
(after zfix / dynfix / lodgen / quantize). Kiosk.cs holds the shop spot for the map and has to follow by hand.
"""
import json, os, sys, struct
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
D = os.path.join(HERE, '..', 'Assets', 'StreetLife', 'Data')
# name, x0, x1, z0, z1, dz
RECTS = [('west', -43.6, -9.7, -25.6, 9.6, 4.0), ('east', 9.6, 43.6, -9.6, 21.6, -4.0)]
APPLY = '--apply' in sys.argv

def rect_of(x0, x1, z0, z1):
    for r in RECTS:
        if x0 >= r[1] and x1 <= r[2] and z0 >= r[3] and z1 <= r[4]: return r
    return None
def touches(x0, x1, z0, z1):
    for r in RECTS:
        if x1 > r[1] and x0 < r[2] and z1 > r[3] and z0 < r[4]: return r
    return None

m = json.load(open(os.path.join(D, 'map.json'))); dyn = json.load(open(os.path.join(D, 'dyn.json')))
if m.get('shifted'): print('already shifted:', m['shifted']); sys.exit(0)
bin_ = bytearray(open(os.path.join(D, 'map.bytes'), 'rb').read())
boxes = m['boxes']; rep = {}
def note(k, n=1): rep[k] = rep.get(k, 0) + n

# ---- breakable entities and the boxes they own
owned = {}; moved_box = {}
for e in dyn['entities']:
    for b in e.get('boxes') or []: owned[b] = e
for e in dyn['entities']:
    bs = [boxes[b] for b in (e.get('boxes') or [])]
    x, z = e['pos'][0], e['pos'][2]
    x0 = min([b['x0'] for b in bs] + [x]); x1 = max([b['x1'] for b in bs] + [x]); z0 = min([b['z0'] for b in bs] + [z]); z1 = max([b['z1'] for b in bs] + [z])
    r = rect_of(x0, x1, z0, z1)
    if e.get('kind') in ('car', 'anim'): r = None
    if r:
        e['pos'][2] += r[5]; note('entities ' + r[0] + ' ' + e.get('kind', '?'))
        for b in e.get('boxes') or []: moved_box[b] = r
    elif touches(x0, x1, z0, z1) and e.get('kind') not in ('car', 'anim'): print('  entity straddles, stays:', e.get('kind'), e.get('prop'), [round(v, 1) for v in (x0, x1, z0, z1)])
# ---- boxes nobody owns
for i, b in enumerate(boxes):
    if i in owned: continue
    r = rect_of(b['x0'], b['x1'], b['z0'], b['z1'])
    if r: moved_box[i] = r; note('boxes ' + r[0])
    elif touches(b['x0'], b['x1'], b['z0'], b['z1']) and b['y1'] > 0.3: print('  box straddles, stays:', b.get('prop'), [round(b[k], 1) for k in ('x0', 'x1', 'z0', 'z1', 'y1')])
for i, r in moved_box.items(): boxes[i]['z0'] += r[5]; boxes[i]['z1'] += r[5]
# moved boxes that now overlap something that stayed
for i, r in moved_box.items():
    a = boxes[i]
    for j, b in enumerate(boxes):
        if j in moved_box or b['y1'] - b['y0'] < 0.3 or a['y1'] - a['y0'] < 0.3: continue
        if a['x0'] < b['x1'] - .05 and a['x1'] > b['x0'] + .05 and a['z0'] < b['z1'] - .05 and a['z1'] > b['z0'] + .05 and a['y0'] < b['y1'] - .05 and a['y1'] > b['y0'] + .05:
            print('  COLLISION after move:', r[0], a.get('prop'), [round(a[k], 1) for k in ('x0', 'x1', 'z0', 'z1')], 'with', b.get('prop'), [round(b[k], 1) for k in ('x0', 'x1', 'z0', 'z1', 'y1')], 'owned' if j in owned else '')
# ---- ladders, spawns, lights, lines
for l in m.get('ladders') or []:
    r = rect_of(l['x0'], l['x1'], l['z0'], l['z1'])
    if r:
        l['z0'] += r[5]; l['z1'] += r[5]
        if abs(l.get('nz', 0)) > .5: l['rung'] += r[5]
        note('ladders ' + r[0])
for s in m.get('spawns') or []:
    r = rect_of(s['x'], s['x'], s['z'], s['z'])
    if r: s['z'] += r[5]; note('spawns ' + r[0])
for L in m.get('lights') or []:
    for key in ('pos', 'target'):
        p = L.get(key)
        if p and len(p) >= 3:
            r = rect_of(p[0], p[0], p[2], p[2])
            if r: p[2] += r[5]; note('lights.' + key + ' ' + r[0])
for L in m.get('lines') or []:
    p = L.get('pos') or []
    if len(p) < 6: continue
    xs = p[0::3]; zs = p[2::3]; r = rect_of(min(xs), max(xs), min(zs), max(zs))
    if r:
        for k in range(2, len(p), 3): p[k] += r[5]
        note('lines ' + r[0])
# ---- static meshes, one connected piece at a time
for mi, w in enumerate(m['meshes']):
    assert w.get('q') == 1, 'mesh %d is not quantized: run quantize.py first' % mi
    n, t, o = w['verts'], w['tris'], w['offset']
    bmin = np.array(w['bmin'], dtype=np.float64); bsize = np.array(w['bsize'], dtype=np.float64)
    if not touches(bmin[0], bmin[0] + bsize[0], bmin[2], bmin[2] + bsize[2]): continue
    q = np.frombuffer(bytes(bin_[o:o + n * 6]), dtype='<u2').reshape(n, 3).astype(np.float64)
    pos = bmin + q * (bsize / 65535.0)
    on = o + ((n * 6 + 3) & ~3); ou = on + n * 4; oi = ou + n * (4 if w.get('uvh') else 8)
    idx = (np.frombuffer(bytes(bin_[oi:oi + t * 6]), dtype='<u2') if n <= 65535 else np.frombuffer(bytes(bin_[oi:oi + t * 12]), dtype='<i4')).astype(np.int64).reshape(t, 3)
    # union-find over vertices; vertices at the same place count as one, so a piece stays whole across its seams
    parent = np.arange(n)
    def find(a):
        while parent[a] != a: parent[a] = parent[parent[a]]; a = parent[a]
        return a
    key = {}
    for i in range(n):
        k = (int(q[i, 0]), int(q[i, 1]), int(q[i, 2]))
        if k in key: parent[find(i)] = find(key[k])
        else: key[k] = i
    for a, b, c in idx:
        ra, rb, rc = find(a), find(b), find(c)
        if ra != rb: parent[ra] = rb
        rc = find(c); rb = find(b)
        if rc != rb: parent[rc] = rb
    roots = np.array([find(i) for i in range(n)]); changed = False
    for rt in np.unique(roots):
        sel = roots == rt; p = pos[sel]
        x0, x1, z0, z1 = p[:, 0].min(), p[:, 0].max(), p[:, 2].min(), p[:, 2].max()
        r = rect_of(x0, x1, z0, z1)
        # a small piece that pokes over the edge goes with whichever rectangle holds its middle (the shop's ground mark)
        if not r and (x1 - x0) * (z1 - z0) < 50: r = rect_of((x0 + x1) / 2, (x0 + x1) / 2, (z0 + z1) / 2, (z0 + z1) / 2)
        if r: pos[sel, 2] += r[5]; changed = True; note('mesh pieces ' + r[0]); note('mesh verts ' + r[0], int(sel.sum()))
        elif touches(x0, x1, z0, z1) and p[:, 1].max() > 0.6 and (x1 - x0) * (z1 - z0) < 2500: print('  mesh piece straddles, stays: mat', w['mat'], [round(float(v), 1) for v in (x0, x1, z0, z1)], 'top', round(float(p[:, 1].max()), 1))
    if changed:
        nb = pos.min(axis=0); ns = np.maximum(pos.max(axis=0) - nb, 1e-6)
        nq = np.clip(np.rint((pos - nb) / ns * 65535.0), 0, 65535).astype('<u2')
        bin_[o:o + n * 6] = nq.tobytes(); w['bmin'] = [float(v) for v in nb]; w['bsize'] = [float(v) for v in ns]
for k in sorted(rep): print('%-28s %d' % (k, rep[k]))
if APPLY:
    m['shifted'] = [[r[0], r[5]] for r in RECTS]
    json.dump(m, open(os.path.join(D, 'map.json'), 'w'), separators=(',', ':')); json.dump(dyn, open(os.path.join(D, 'dyn.json'), 'w'), separators=(',', ':'))
    open(os.path.join(D, 'map.bytes'), 'wb').write(bytes(bin_)); print('written')
else: print('dry run: nothing written')
