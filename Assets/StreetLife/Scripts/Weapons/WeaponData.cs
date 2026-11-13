// Weapon data exported from the imok 1v1 web game (guns.json): the final stats of every gun after the game's own
// multipliers (reloads 50% quicker, +20% damage on everything, +25% on primaries and secondaries), the held and
// first-person models, projectiles, the Sentry, and Wednesday's arm measurements for the two-handed hold.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [Serializable]
    public class GunStat
    {
        public string id, name, @base, type, color, note, xhair, cat;
        public bool sec, auto, noReload, primary, secondary, vm;
        public float dmg, direct, radius, head, rate, burstGap, mag, reload, swap, spread, adsSpread, ads, pellets, pb;
        public float zoom, kick, bloom, speed, maxSpeed, accel, grav, fuse, landFuse, reach, arc, lunge, range;
        public int burst;
        public float[] fall;

        // the web game's `x||default` fallbacks
        public string Shape => string.IsNullOrEmpty(@base) ? id : @base;
        public bool IsMelee => type == "melee";
        public bool IsProj => type == "proj";
        public bool IsFlame => type == "flame";
        public bool HasFall => fall != null && fall.Length == 2;
        public bool InfiniteMag => mag < 0;
        public int Pellets => pellets > 0 ? Mathf.RoundToInt(pellets) : 1;
        public float AdsTime => ads > 0 ? ads : .2f;
        public float SwapTime => swap > 0 ? swap : .35f;
        public float Lunge => lunge > 0 ? lunge : 6f;
        public float Fuse => fuse > 0 ? fuse : 4f;
        public float PointBlank => pb > 0 ? pb : 3f;
        public bool Shotgun => id == "shotgun" || @base == "shotgun";
        public bool Sniper => id == "sniper" || id == "scout";
    }

    [Serializable] public class HeldJson { public string id; public float s, dx; public float[] hr, hl, muz; public bool twin, nf, sh; public WNode[] nodes; }
    [Serializable] public class VmJson { public string id; public float sightY, rearZ, dx, muzY, muzZ; public WNode[] nodes; }
    [Serializable] public class NodesJson { public string id; public WNode[] nodes; }
    [Serializable] public class TurretCfg { public float life, hp, range, rate, pause, dmg, spread, turn; public int burst; }
    [Serializable] public class TurretJson { public WNode[] nodes; public float[] muz; public TurretCfg cfg; }
    [Serializable] public class ConsJson { public string id, name, @short, desc, meta; public int uses; public float cd, dur; public bool passive; }
    [Serializable] public class ShopJson { public string id, name, desc, col; }
    [Serializable] public class ArmJson { public float[] pos, hand; public float up, fore; }
    [Serializable] public class ArmsJson { public ArmJson aL, aR; public float handY; }

    [Serializable]
    public class GunsJson
    {
        public GunStat[] guns; public HeldJson[] held; public VmJson[] vm; public NodesJson[] proj;
        public NodesJson sword; public TurretJson turret; public int goldMat;
        public ConsJson[] cons; public ShopJson[] shop; public ArmsJson arms; public WMesh[] meshes;
    }

    /// The loaded weapon table. Load once (WeaponWorld does it) and look things up by the web game's ids.
    public static class GunsDb
    {
        public static GunsJson Data;
        public static byte[] Bin;
        static readonly Dictionary<string, GunStat> stats = new Dictionary<string, GunStat>();
        static readonly Dictionary<string, HeldJson> held = new Dictionary<string, HeldJson>();
        static readonly Dictionary<string, VmJson> vms = new Dictionary<string, VmJson>();
        static readonly Dictionary<string, NodesJson> projs = new Dictionary<string, NodesJson>();
        static readonly Dictionary<string, ConsJson> cons = new Dictionary<string, ConsJson>();
        public static readonly List<string> Primaries = new List<string>(), Secondaries = new List<string>();
        public static readonly string[] Lethals = { "cluster", "mine", "stun", "flash" };   // the loadout's lethal slot (Gold Armour and HeartBeat aren't weapons)
        public static bool Loaded => Data != null;

        public static void Load(string json, byte[] bin)
        {
            Data = JsonUtility.FromJson<GunsJson>(json); Bin = bin;
            stats.Clear(); held.Clear(); vms.Clear(); projs.Clear(); cons.Clear(); Primaries.Clear(); Secondaries.Clear();
            foreach (var g in Data.guns) stats[g.id] = g;
            foreach (var h in Data.held) held[h.id] = h;
            foreach (var v in Data.vm) vms[v.id] = v;
            foreach (var p in Data.proj) projs[p.id] = p;
            foreach (var c in Data.cons) cons[c.id] = c;
            // menu order, as the web game lists them
            foreach (var id in new[] { "gar", "ar", "burst", "crossbow", "mg", "m60", "sniper", "scout", "shotgun", "autoshot" }) if (stats.ContainsKey(id)) Primaries.Add(id);
            foreach (var id in new[] { "smg", "hornet", "vector", "p90", "ump", "sting", "duals", "rocket", "swarm", "sword" }) if (stats.ContainsKey(id)) Secondaries.Add(id);
            WeaponFixes.Apply();   // smaller sights, the rocket's front ring, the pistols' missing parts
        }

        public static GunStat Gun(string id) => id != null && stats.TryGetValue(id, out var g) ? g : null;
        public static HeldJson Held(string id) => id != null && held.TryGetValue(id, out var h) ? h : null;
        public static VmJson Vm(string id) => id != null && vms.TryGetValue(id, out var v) ? v : null;
        public static NodesJson Proj(string id) => id != null && projs.TryGetValue(id, out var p) ? p : null;
        public static ConsJson Cons(string id) => id != null && cons.TryGetValue(id, out var c) ? c : null;
    }

    /// The web game is right-handed; Unity is left-handed. Everything exported is mirrored across z.
    /// Pose maths (arm IK, the held gun, sword swings) is done with the web game's own numbers and converted at the end.
    public static class Web
    {
        public static Vector3 V(Vector3 v) => new Vector3(v.x, v.y, -v.z);              // web <-> Unity position (its own inverse)
        public static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);            // raw array, no conversion
        public static Quaternion Q(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w); // web <-> Unity rotation
        static Quaternion Ax(float r) => new Quaternion(Mathf.Sin(r / 2), 0, 0, Mathf.Cos(r / 2));
        static Quaternion Ay(float r) => new Quaternion(0, Mathf.Sin(r / 2), 0, Mathf.Cos(r / 2));
        static Quaternion Az(float r) => new Quaternion(0, 0, Mathf.Sin(r / 2), Mathf.Cos(r / 2));
        /// three.js Euler (radians), default order XYZ
        public static Quaternion EulerXYZ(float x, float y, float z) => Ax(x) * Ay(y) * Az(z);
        public static Quaternion EulerYXZ(float x, float y, float z) => Ay(y) * Ax(x) * Az(z);
        /// three.js Matrix4.makeBasis(x, y, z) as a rotation (x = y cross z)
        public static Quaternion Basis(Vector3 y, Vector3 z) => Quaternion.LookRotation(z, y);
        public static Color Hex(string h) => StreetLife.Hex.ToColor(h);
    }

    /// The web game's seeded random number for spread (mulberry32), so spread patterns have the same distribution.
    public static class GR
    {
        static int seed = 1;
        public static void Seed(int s) { seed = s; }
        public static float Next()
        {
            unchecked
            {
                seed += 0x6D2B79F5;
                int t = (seed ^ (int)((uint)seed >> 15)) * (1 | seed);
                t = (t + (t ^ (int)((uint)t >> 7)) * (61 | t)) ^ t;
                uint u = (uint)(t ^ (int)((uint)t >> 14));
                return Mathf.Min(.9999999f, (float)(u / 4294967296.0));
            }
        }
    }
}
