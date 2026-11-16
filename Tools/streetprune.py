#!/usr/bin/env python3
"""Street Life: take things out of the map (Pete, 16 Nov 2026): the three sports cars (the white van stays), the green
play frame by the house, and the alien on the hospital roof.

  python3 Tools/streetprune.py            dry run
  python3 Tools/streetprune.py --apply    writes Assets/StreetLife/Data/map.json and dyn.json

An entity that is taken out keeps its place in the list (others refer to entities by number): its kind becomes "none"
(Destruction and MapBuilder skip it), it loses its meshes, and its collision boxes are collapsed far under the map.
Run it after Tools/streetshift.py: the positions below are the shifted ones. It is safe to run twice.
"""
import json, os, sys
D = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'StreetLife', 'Data')
m = json.load(open(os.path.join(D, 'map.json'))); dyn = json.load(open(os.path.join(D, 'dyn.json'))); M = dyn['meshes']
def near(e, x, y, z, r=1.5): p = e['pos']; return abs(p[0] - x) < r and abs(p[1] - y) < r and abs(p[2] - z) < r
def out(e):
    if e['kind'] == 'car' and e.get('carKind') in ('gtr', 'lambo', 'porsche'): return 'car ' + e['carKind']
    if e['kind'] == 'play' and near(e, -30, 0, 20): return 'green play frame'
    if e['kind'] == 'prop' and near(e, 18.6, 9.2, -3.9) and sum(M[i]['verts'] for i in e.get('meshes') or []) > 1000: return 'alien'
    return None
n = 0
for e in dyn['entities']:
    why = out(e)
    if not why: continue
    n += 1; print('out:', why, 'id', e['id'], [round(v, 1) for v in e['pos']], 'boxes', len(e.get('boxes') or []))
    for b in e.get('boxes') or []:
        q = m['boxes'][b]; q['x0'] = q['x1'] = q['z0'] = q['z1'] = 0; q['y0'] = q['y1'] = -999
    e['kind'] = 'none'; e['boxes'] = []; e['meshes'] = []; e['lod1'] = []; e['wheels'] = []; e['wheelOf'] = []
print(n, 'taken out')
if '--apply' in sys.argv:
    json.dump(m, open(os.path.join(D, 'map.json'), 'w'), separators=(',', ':')); json.dump(dyn, open(os.path.join(D, 'dyn.json'), 'w'), separators=(',', ':')); print('written')
else: print('dry run: nothing written')
