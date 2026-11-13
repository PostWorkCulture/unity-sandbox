// Data exported from the imok 1v1 web game (Street Life). Coordinates are already in Unity space
// (the web game's z axis is negated), distances in metres, angles in radians.
using System;
using UnityEngine;

namespace StreetLife
{
    [Serializable] public class WBox { public float x0, x1, y0, y1, z0, z1; public string prop; public bool wood, car, touch; }
    [Serializable] public class WLadder { public float x0, x1, z0, z1, y0, y1, nx, nz, rung; }
    [Serializable] public class WSpawn { public float x, y, z, yaw; }
    [Serializable] public class WBounds { public float x0, x1, z0, z1; }
    [Serializable] public class WMesh { public int mat, verts, tris; public long offset; public bool cast; public int q; public float[] bmin, bsize; public bool uvh; }   // q = 1: quantized (exporter's quantize.py)
    [Serializable] public class WLine { public string color; public float opacity; public float[] pos; }
    [Serializable] public class WLight { public string type, color, ground; public float intensity, distance, decay, angle, penumbra; public float[] pos, target; public bool castShadow; }
    [Serializable] public class WFog { public string color; public float near, far; }

    [Serializable]
    public class WorldJson
    {
        public WMesh[] meshes; public WLine[] lines; public WLight[] lights; public WLight[] globalLights; public WFog fog;
        public WBox[] boxes; public WLadder[] ladders; public WSpawn[] spawns; public WBounds bounds;
    }

    [Serializable] public class WTex { public int id, w, h; public string wrapS, wrapT; public bool point, normal, linear; }
    [Serializable]
    public class WMat
    {
        public int id; public string kind, color, emissive; public int map, emissiveMap, alphaMap;
        public float opacity, alphaTest, roughness, metalness, emissiveIntensity, transmission;
        public bool transparent, doubleSided, backSide, toneMapped, depthWrite;
        public int normalMap = -1, msMap = -1;   // guns only: normal map and metallic (R) / smoothness (A) map
    }
    [Serializable] public class MatJson { public WTex[] textures; public WMat[] materials; }

    /// Something that moves or breaks: props, glass, drums, chips, trees, play frames, cars, loose items, ambient animation.
    [Serializable]
    public class WDyn
    {
        public int id; public string kind, prop, snd, color, carKind;
        public float[] pos, rot, scl; public int[] boxes, meshes;
        public float hp, treeH, kw, kh, kd, brk, heavy, hz;
        public bool shove, boom, wood, ball;
        public float[] keys, ppos, prot, pscl, wheels;
        public int[] lod1;   // per mesh: a lighter distance version's record, or -1
    }
    [Serializable] public class DynJson { public WDyn[] entities; public WMesh[] meshes; public float period; }

    [Serializable] public class WNode { public string name; public int parent; public float[] pos, rot, scl; public int[] meshes; }
    [Serializable] public class CharJson { public WNode[] nodes; public WMesh[] meshes; public float handY, scale; }

    public static class Hex
    {
        public static Color ToColor(string hex, float a = 1f)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c)) { c.a = a; return c; }
            return new Color(1, 1, 1, a);
        }
    }

    public static class MeshIO
    {
        // One record of the packed blob: float32 positions, normals, uvs, then uint32 indices.
        public static Mesh Build(byte[] bin, WMesh m, string name)
        {
            if (m.q == 1) return BuildQ(bin, m, name);
            int n = m.verts, o = (int)m.offset;
            var f = new float[n * 8];
            Buffer.BlockCopy(bin, o, f, 0, n * 8 * 4);
            var pos = new Vector3[n]; var nrm = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                pos[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
                int k = n * 3 + i * 3; nrm[i] = new Vector3(f[k], f[k + 1], f[k + 2]);
                int u = n * 6 + i * 2; uv[i] = new Vector2(f[u], f[u + 1]);
            }
            var idx = new int[m.tris * 3];
            Buffer.BlockCopy(bin, o + n * 8 * 4, idx, 0, idx.Length * 4);
            var mesh = new Mesh { name = name };
            mesh.indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(pos); mesh.SetNormals(nrm); mesh.SetUVs(0, uv);
            mesh.SetTriangles(idx, 0, true);
            return mesh;
        }

        // The quantized layout: uint16 positions (fractions of the record's box), sbyte normals (xyz + pad), half or float UVs,
        // uint16 indices under 65,536 vertices (uint32 above). Each block starts on a 4-byte boundary.
        static Mesh BuildQ(byte[] bin, WMesh m, string name)
        {
            int n = m.verts, t = m.tris, o = (int)m.offset;
            var qp = new ushort[n * 3]; Buffer.BlockCopy(bin, o, qp, 0, n * 6);
            int on = o + ((n * 6 + 3) & ~3);
            int ou = on + n * 4;
            int oi = ou + n * (m.uvh ? 4 : 8);
            var pos = new Vector3[n]; var nrm = new Vector3[n]; var uv = new Vector2[n];
            float mx = m.bmin[0], my = m.bmin[1], mz = m.bmin[2], sx = m.bsize[0] / 65535f, sy = m.bsize[1] / 65535f, sz = m.bsize[2] / 65535f;
            for (int i = 0; i < n; i++)
            {
                pos[i] = new Vector3(mx + qp[i * 3] * sx, my + qp[i * 3 + 1] * sy, mz + qp[i * 3 + 2] * sz);
                int k = on + i * 4;
                nrm[i] = new Vector3((sbyte)bin[k], (sbyte)bin[k + 1], (sbyte)bin[k + 2]).normalized;
            }
            if (m.uvh)
            {
                var hu = new ushort[n * 2]; Buffer.BlockCopy(bin, ou, hu, 0, n * 4);
                for (int i = 0; i < n; i++) uv[i] = new Vector2(Mathf.HalfToFloat(hu[i * 2]), Mathf.HalfToFloat(hu[i * 2 + 1]));
            }
            else
            {
                var fu = new float[n * 2]; Buffer.BlockCopy(bin, ou, fu, 0, n * 8);
                for (int i = 0; i < n; i++) uv[i] = new Vector2(fu[i * 2], fu[i * 2 + 1]);
            }
            var idx = new int[t * 3];
            if (n <= 65535) { var si = new ushort[t * 3]; Buffer.BlockCopy(bin, oi, si, 0, t * 6); for (int i = 0; i < si.Length; i++) idx[i] = si[i]; }
            else Buffer.BlockCopy(bin, oi, idx, 0, t * 12);
            var mesh = new Mesh { name = name };
            mesh.indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(pos); mesh.SetNormals(nrm); mesh.SetUVs(0, uv);
            mesh.SetTriangles(idx, 0, true);
            return mesh;
        }
    }
}
