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
    [Serializable] public class WMesh { public int mat, verts, tris; public long offset; public bool cast; }
    [Serializable] public class WLine { public string color; public float opacity; public float[] pos; }
    [Serializable] public class WLight { public string type, color, ground; public float intensity, distance, decay, angle, penumbra; public float[] pos, target; public bool castShadow; }
    [Serializable] public class WFog { public string color; public float near, far; }

    [Serializable]
    public class WorldJson
    {
        public WMesh[] meshes; public WLine[] lines; public WLight[] lights; public WLight[] globalLights; public WFog fog;
        public WBox[] boxes; public WLadder[] ladders; public WSpawn[] spawns; public WBounds bounds;
    }

    [Serializable] public class WTex { public int id, w, h; public string wrapS, wrapT; public bool point; }
    [Serializable]
    public class WMat
    {
        public int id; public string kind, color, emissive; public int map, emissiveMap, alphaMap;
        public float opacity, alphaTest, roughness, metalness, emissiveIntensity, transmission;
        public bool transparent, doubleSided, backSide, toneMapped, depthWrite;
    }
    [Serializable] public class MatJson { public WTex[] textures; public WMat[] materials; }

    [Serializable] public class WNode { public string name; public int parent; public float[] pos, rot, scl; public int[] meshes; }
    // arm lengths for the two-bone IK: shoulder to elbow (up), elbow to the hand point (fore), and where the hand sits on the forearm (web game space)
    [Serializable] public class WArm { public float up, fore; public float[] hand; }
    [Serializable] public class WArms { public WArm L, R; public bool elbows; }
    [Serializable] public class CharJson { public WNode[] nodes; public WMesh[] meshes; public float handY, scale; public WArms arms; }

    // ---- weapons: one model per gun, exported in its own space (Unity axes) ----
    // held: the detailed two-handed model the web game poses with holdGun (grip r, handguard l, scale s, muzzle, twin pistols, nf = no flash, sh = on the shoulder)
    [Serializable] public class WHold { public float[] r, l; public float s, muzY, muzZ, dx; public bool twin, nf, sh; }
    // hand / thrown: the model hangs off a node of the character (aR.p) at a fixed offset
    [Serializable] public class WLow { public string parent; public float[] pos, rot; }
    [Serializable] public class WeaponJson { public string id, mode; public WMesh[] meshes; public float[] min, max; public WHold hold; public WLow low; public float scale; }

    // ---- catalog: names, abilities and stats straight from the web game's CHARS and GUNS tables ----
    [Serializable]
    public class CharInfo
    {
        public string id, name, desc, ability, color; public float cooldown, hp, speed; public string[] killFx; public bool hasModel;
        public Color Colour => Hex.ToColor(color);
    }
    [Serializable]
    public class WeaponInfo
    {
        public string id, slot, cls, name, note, type, color;
        public float dmg, direct, head, rate, mag, reload, swap, spread, adsSpread, ads, pb, zoom, kick, bloom;
        public int pellets, burst;
        public bool noReload, auto;
        public float[] fall;
        public float radius, speed, maxSpeed, accel, grav, fuse, landFuse, burstGap, range, reach, arc, lunge;
        public bool Infinite => mag < 0;
        public bool HasFall => fall != null && fall.Length >= 2;
        public bool Sniper => id == "sniper" || id == "scout";
        public bool Shotgun => pellets > 1;
        public Color Colour => Hex.ToColor(color);
    }
    [Serializable] public class CatalogOrder { public string[] primaries, secondaries, lethals; }
    [Serializable] public class CatalogJson { public string source, exported; public CharInfo[] characters; public WeaponInfo[] weapons; public CatalogOrder order; }

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
    }
}
