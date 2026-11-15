// The web game's collision world: every solid is an axis-aligned box. Movement and camera rays use these
// boxes directly (not PhysX) so the feel matches the web version exactly.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public struct Box
    {
        public float x0, x1, y0, y1, z0, z1;
        public bool wood, glass, prop;
        public int tag;   // non-zero: a placed object (weapons chat: the Sentry), so hits can find it
    }

    public class Ladder
    {
        public float x0, x1, z0, z1, y0, y1, nx, nz, rung;
    }

    public static class BoxWorld
    {
        public static readonly List<Box> Boxes = new List<Box>();
        public static readonly List<Ladder> Ladders = new List<Ladder>();
        public static readonly List<WSpawn> Spawns = new List<WSpawn>();
        public static WBounds Bounds = new WBounds { x0 = -46, x1 = 46, z0 = -30, z1 = 30 };
        public static bool Loaded;

        public static void Load(WorldJson w)
        {
            Boxes.Clear(); Ladders.Clear(); Spawns.Clear(); parked.Clear();
            foreach (var b in w.boxes)
                Boxes.Add(new Box { x0 = b.x0, x1 = b.x1, y0 = b.y0, y1 = b.y1, z0 = b.z0, z1 = b.z1, wood = b.wood, glass = b.prop == "glass", prop = !string.IsNullOrEmpty(b.prop) });
            foreach (var l in w.ladders)
                Ladders.Add(new Ladder { x0 = l.x0, x1 = l.x1, z0 = l.z0, z1 = l.z1, y0 = l.y0, y1 = l.y1, nx = l.nx, nz = l.nz, rung = l.rung });
            if (w.spawns != null) Spawns.AddRange(w.spawns);
            if (w.bounds != null) Bounds = w.bounds;
            Loaded = true;
        }

        /// Nearest hit distance along a ray (d normalised), or -1. Same slab test as the web game's rayBoxes.
        public static float Ray(Vector3 o, Vector3 d, float maxT) => Ray(o, d, maxT, out _);

        /// As Ray, also giving which box was hit (index into Boxes, -1 for none).
        public static float Ray(Vector3 o, Vector3 d, float maxT, out int index)
        {
            float best = maxT; bool hit = false; index = -1;
            for (int i = 0; i < Boxes.Count; i++)
            {
                var b = Boxes[i];
                float t0 = -1e9f, t1 = 1e9f;
                if (!Slab(o.x, d.x, b.x0, b.x1, ref t0, ref t1)) continue;
                if (!Slab(o.y, d.y, b.y0, b.y1, ref t0, ref t1)) continue;
                if (!Slab(o.z, d.z, b.z0, b.z1, ref t0, ref t1)) continue;
                if (t1 < 0 || t0 > best) continue;
                float t = Mathf.Max(0, t0);
                if (t < best) { best = t; hit = true; index = i; }
            }
            return hit ? best : -1f;
        }

        static readonly Dictionary<int, Box> parked = new Dictionary<int, Box>();

        /// Take a box out of the world (broken glass, a smashed crate...). Restore puts it back.
        public static void Disable(int i)
        {
            if (i < 0 || i >= Boxes.Count || parked.ContainsKey(i)) return;
            parked[i] = Boxes[i];
            Boxes[i] = new Box { x0 = -9999, x1 = -9999, y0 = -9999, y1 = -9999, z0 = -9999, z1 = -9999 };
            NavGrid.Refresh(parked[i]);
        }
        public static void Restore(int i) { if (parked.TryGetValue(i, out var b)) { Boxes[i] = b; parked.Remove(i); NavGrid.Refresh(b); } }
        public static bool IsDisabled(int i) => parked.ContainsKey(i);
        /// Move or resize a live box (shoved furniture, driving cars, chipped tables). The bots' grid follows.
        public static void Set(int i, Box b)
        {
            if (i < 0 || i >= Boxes.Count || parked.ContainsKey(i)) return;
            var o = Boxes[i]; Boxes[i] = b;
            // Refresh only where the box was and where it is. A box moved to or from the parking spot far off the map (wall
            // slots, 15 Nov) used to refresh one rectangle spanning the whole map: every node against every dynamic box,
            // 0.2 s at first and over 4 s once walls had come and gone (the freezes on Pete's laptop).
            bool oFar = o.x0 < -9000, bFar = b.x0 < -9000;
            if (oFar && bFar) return;
            if (oFar) { NavGrid.Refresh(b); return; }
            if (bFar) { NavGrid.Refresh(o); return; }
            float gap = Mathf.Max(Mathf.Max(o.x0, b.x0) - Mathf.Min(o.x1, b.x1), Mathf.Max(o.z0, b.z0) - Mathf.Min(o.z1, b.z1));
            if (gap > 1f) { NavGrid.Refresh(o); NavGrid.Refresh(b); }
            else NavGrid.Refresh(Mathf.Min(o.x0, b.x0), Mathf.Max(o.x1, b.x1), Mathf.Min(o.z0, b.z0), Mathf.Max(o.z1, b.z1));
        }
        public static void ClearParked() => parked.Clear();

        static bool Slab(float o, float d, float mn, float mx, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-9f) return o >= mn && o <= mx;
            float a = (mn - o) / d, b = (mx - o) / d;
            if (a > b) { var t = a; a = b; b = t; }
            if (a > t0) t0 = a; if (b < t1) t1 = b;
            return t0 <= t1;
        }
    }
}
