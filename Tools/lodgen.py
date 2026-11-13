"""Distance versions (LOD1) of the heavy breakables and props (the Tripo furniture, cars, trees: up to 42k triangles each).
meshoptimizer collapses edges but keeps the original vertices, so colours, UVs and normals stay exact; texture seams stay as
borders. Each mesh over MIN_TRIS gets a LOD1 at about a quarter of its triangles (less if that would bend it too far),
appended to dyn.bytes; dyn.json gets per-entity `lod1` (record index per mesh, -1 = none).
Usage: python3 lodgen.py <dir with dyn.json and dyn.bytes>   (run after dynfix.py)"""
import json, sys, struct
import numpy as np
import meshoptimizer as mo

MIN_TRIS, RATIO, ERR = 1500, .25, .02
d = sys.argv[1]
J = json.load(open(d + '/dyn.json'))
B = bytearray(open(d + '/dyn.bytes', 'rb').read())
meshes = J['meshes']
if any(m.get('q') == 1 for m in meshes): sys.exit('dyn.bytes is already quantized: run lodgen.py before quantize.py')
made = {}
before = after = 0

def lod_of(mi):
    m = meshes[mi]; n, t, o = m['verts'], m['tris'], m['offset']
    f = np.frombuffer(B, dtype='<f4', count=n * 8, offset=o).copy()
    pos, nrm, uv = f[:n * 3].reshape(n, 3), f[n * 3:n * 6].reshape(n, 3), f[n * 6:].reshape(n, 2)
    idx = np.frombuffer(B, dtype='<u4', count=t * 3, offset=o + n * 32).copy()
    dst = np.zeros_like(idx)
    k = mo.simplify(dst, idx, np.ascontiguousarray(pos, dtype=np.float32), target_index_count=max(300, int(t * 3 * RATIO)) // 3 * 3, target_error=ERR)
    if k == 0 or k > t * 3 * .8: return None            # couldn't get it meaningfully smaller
    used = np.unique(dst[:k]); remap = np.full(n, -1, np.int64); remap[used] = np.arange(len(used))
    ni = remap[dst[:k]].astype('<u4')
    rec = dict(m); rec['verts'] = int(len(used)); rec['tris'] = int(k // 3); rec['offset'] = len(B)
    B.extend(np.ascontiguousarray(pos[used], dtype='<f4').tobytes()); B.extend(np.ascontiguousarray(nrm[used], dtype='<f4').tobytes())
    B.extend(np.ascontiguousarray(uv[used], dtype='<f4').tobytes()); B.extend(ni.tobytes())
    meshes.append(rec)
    return len(meshes) - 1, t, k // 3

for e in J['entities']:
    if e['kind'] in ('glass', 'anim'): e['lod1'] = []; continue
    lods = []
    for mi in e['meshes']:
        if meshes[mi]['tris'] < MIN_TRIS: lods.append(-1); continue
        if mi not in made: made[mi] = lod_of(mi)
        r = made[mi]
        if r: lods.append(r[0]); before += r[1]; after += r[2]
        else: lods.append(-1)
    e['lod1'] = lods if any(x >= 0 for x in lods) else []

json.dump(J, open(d + '/dyn.json', 'w'), separators=(',', ':'))
open(d + '/dyn.bytes', 'wb').write(B)
print(f"LOD1 for {sum(1 for v in made.values() if v)} meshes: {before} -> {after} triangles; dyn.bytes {len(B)/1048576:.1f} MB")
