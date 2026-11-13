// Maps chat: the extra data exported per map next to map.json / dyn.json (see the Maps exporter's exportmap.js and mapprep.js):
// extras.json + extras.bytes (trains, live rails, vertex-coloured ground and water, far clouds, instanced crowds) and env.json
// (the map's name and its atmosphere: sky gradient, sun, fog, lights). Coordinates are already in Unity space.
using System;
using UnityEngine;

namespace StreetLife
{
    [Serializable] public class XMesh { public int mat, verts, tris; public long offset; public bool cast, col; }
    [Serializable] public class XPart { public float lx0, lx1, y0, y1, s0, s1; }
    [Serializable] public class XTrain { public float x, dir, len, speed, gap, ph; public int[] meshes; public XPart[] parts; }
    [Serializable] public class XGlow { public int ln; public float x; public bool plane; public int[] meshes; }
    [Serializable] public class XVc { public bool water; public int ro; public int[] meshes; public float y; }
    [Serializable] public class XInst { public int[] meshes; public float[] m; }
    [Serializable] public class XRl { public float X, Z; }
    [Serializable] public class ExtrasJson { public XMesh[] meshes; public XTrain[] trains; public XGlow[] glow; public XVc[] vc; public XInst[] inst; public XRl rl; }

    [Serializable] public class EnvSky { public string top, mid, bot, glow, disc; public float ex, glowA, discCos; public float[] sd; public int[] clouds; }
    [Serializable] public class EnvInfo { public string id, name, desc; public string[] modes, sw; }
    [Serializable] public class EnvJson { public EnvInfo info; public EnvSky env; public WFog fog; public WLight[] global; }

    public static class XMeshIO
    {
        /// One record of extras.bytes: float32 positions, normals, uvs, (colours), then uint32 indices.
        public static Mesh Build(byte[] bin, XMesh m, string name)
        {
            int n = m.verts, o = (int)m.offset, fl = n * (m.col ? 11 : 8);
            var f = new float[fl];
            Buffer.BlockCopy(bin, o, f, 0, fl * 4);
            var pos = new Vector3[n]; var nrm = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                pos[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
                int k = n * 3 + i * 3; nrm[i] = new Vector3(f[k], f[k + 1], f[k + 2]);
                int u = n * 6 + i * 2; uv[i] = new Vector2(f[u], f[u + 1]);
            }
            var idx = new int[m.tris * 3];
            Buffer.BlockCopy(bin, o + fl * 4, idx, 0, idx.Length * 4);
            var mesh = new Mesh { name = name };
            mesh.indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(pos); mesh.SetNormals(nrm); mesh.SetUVs(0, uv);
            if (m.col)
            {
                var col = new Color[n];
                for (int i = 0; i < n; i++) { int c = n * 8 + i * 3; col[i] = new Color(f[c], f[c + 1], f[c + 2], 1); }
                mesh.colors = col;
            }
            mesh.SetTriangles(idx, 0, true);
            return mesh;
        }
    }
}
