"""Shrinks the exported mesh blobs (map / dyn / character / gun .bytes) to about half: positions as 16-bit fractions of the
record's bounding box (under 2 mm steps across a whole map), normals as signed bytes, UVs as half floats (full floats
kept where a UV runs past +-32, e.g. long tiled surfaces), indices as 16-bit when the record has under 65,536 vertices.
Records already done (q = 1) are left alone, so it's safe to run twice. MeshIO.Build (WorldData.cs) reads both layouts.
Usage: python3 quantize.py <file.json> <file.bytes>   (rewrites both in place)"""
import json, sys, os
import numpy as np

def pad4(b): return b + b'\0' * ((-len(b)) % 4)

def main(jp, bp):
    J = json.load(open(jp)); B = open(bp, 'rb').read()
    meshes = J['meshes']
    if any(m.get('q') == 1 for m in meshes): print(os.path.basename(bp) + ': already done'); return
    out = bytearray(); done = {}; before = len(B)
    for m in meshes:
        key = (m['offset'], m['verts'], m['tris'])
        if key in done:
            m.update(done[key]); continue
        n, t, o = m['verts'], m['tris'], int(m['offset'])
        f = np.frombuffer(B, '<f4', n * 8, o)
        pos, nrm, uv = f[:n * 3].reshape(n, 3), f[n * 3:n * 6].reshape(n, 3), f[n * 6:].reshape(n, 2)
        idx = np.frombuffer(B, '<u4', t * 3, o + n * 32)
        mn = pos.min(0) if n else np.zeros(3, np.float32); sz = (pos.max(0) - mn) if n else np.ones(3, np.float32)
        sz = np.where(sz < 1e-6, 1e-6, sz)
        qp = np.clip(np.round((pos - mn) / sz * 65535), 0, 65535).astype('<u2')
        ln = np.linalg.norm(nrm, axis=1, keepdims=True); ln[ln < 1e-8] = 1
        qn = np.zeros((n, 4), np.int8); qn[:, :3] = np.clip(np.round(nrm / ln * 127), -127, 127)
        uvh = bool(n == 0 or np.abs(uv).max() <= 32)
        rec = dict(q=1, bmin=[float(x) for x in mn], bsize=[float(x) for x in sz], uvh=uvh, offset=len(out))
        blob = pad4(qp.tobytes()) + qn.tobytes() + (uv.astype('<f2').tobytes() if uvh else uv.astype('<f4').tobytes())
        blob += pad4(idx.astype('<u2').tobytes() if n <= 65535 else idx.astype('<u4').tobytes())
        out += blob
        m.update(rec); done[key] = rec
    tmp = bp + '.tmp'
    open(tmp, 'wb').write(out); os.replace(tmp, bp)
    tj = jp + '.tmp'; json.dump(J, open(tj, 'w'), separators=(',', ':')); os.replace(tj, jp)
    print(f"{os.path.basename(bp)}: {before/1048576:.1f} MB -> {len(out)/1048576:.1f} MB")

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
