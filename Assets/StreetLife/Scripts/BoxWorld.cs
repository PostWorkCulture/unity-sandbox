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
            Boxes.Clear(); Ladders.Clear(); Spawns.Clear();
            foreach (var b in w.boxes)
                Boxes.Add(new Box { x0 = b.x0, x1 = b.x1, y0 = b.y0, y1 = b.y1, z0 = b.z0, z1 = b.z1, wood = b.wood, glass = b.prop == "glass", prop = !string.IsNullOrEmpty(b.prop) });
            foreach (var l in w.ladders)
                Ladders.Add(new Ladder { x0 = l.x0, x1 = l.x1, z0 = l.z0, z1 = l.z1, y0 = l.y0, y1 = l.y1, nx = l.nx, nz = l.nz, rung = l.rung });
            if (w.spawns != null) Spawns.AddRange(w.spawns);
            if (w.bounds != null) Bounds = w.bounds;
            Loaded = true;
        }

        /// Nearest hit distance along a ray (d normalised), or -1. Same slab test as the web game's rayBoxes.
        public static float Ray(Vector3 o, Vector3 d, float maxT)
        {
            float best = maxT; bool hit = false;
            for (int i = 0; i < Boxes.Count; i++)
            {
                var b = Boxes[i];
                float t0 = -1e9f, t1 = 1e9f;
                if (!Slab(o.x, d.x, b.x0, b.x1, ref t0, ref t1)) continue;
                if (!Slab(o.y, d.y, b.y0, b.y1, ref t0, ref t1)) continue;
                if (!Slab(o.z, d.z, b.z0, b.z1, ref t0, ref t1)) continue;
                if (t1 < 0 || t0 > best) continue;
                float t = Mathf.Max(0, t0);
                if (t < best) { best = t; hit = true; }
            }
            return hit ? best : -1f;
        }

        /// Nearest hit that stops a bullet (wood is shot through: each wooden layer in front is listed in woods, nearest first).
        /// Returns the distance or -1, with the face normal of the box that was hit.
        public static float RayHit(Vector3 o, Vector3 d, float maxT, out Vector3 normal, List<float> woods = null)
        {
            float best = maxT; bool hit = false; normal = Vector3.up;
            woods?.Clear();
            for (int i = 0; i < Boxes.Count; i++)
            {
                var b = Boxes[i];
                float t0 = -1e9f, t1 = 1e9f;
                if (!Slab(o.x, d.x, b.x0, b.x1, ref t0, ref t1)) continue;
                if (!Slab(o.y, d.y, b.y0, b.y1, ref t0, ref t1)) continue;
                if (!Slab(o.z, d.z, b.z0, b.z1, ref t0, ref t1)) continue;
                if (t1 < 0 || t0 > maxT) continue;
                float t = Mathf.Max(0, t0);
                if (woods != null && b.wood && !b.glass) { woods.Add(t); continue; }
                if (t < best) { best = t; hit = true; normal = FaceNormal(b, o + d * t); }
            }
            if (woods != null) { woods.Sort(); for (int i = woods.Count - 1; i >= 0; i--) if (woods[i] > best) woods.RemoveAt(i); }
            return hit ? best : -1f;
        }

        static Vector3 FaceNormal(in Box b, Vector3 p)
        {
            float[] dist = { Mathf.Abs(p.x - b.x0), Mathf.Abs(p.x - b.x1), Mathf.Abs(p.y - b.y0), Mathf.Abs(p.y - b.y1), Mathf.Abs(p.z - b.z0), Mathf.Abs(p.z - b.z1) };
            int k = 0; for (int i = 1; i < 6; i++) if (dist[i] < dist[k]) k = i;
            return k == 0 ? Vector3.left : k == 1 ? Vector3.right : k == 2 ? Vector3.down : k == 3 ? Vector3.up : k == 4 ? Vector3.back : Vector3.forward;
        }

        /// Clear line between two points (no box in between).
        public static bool Los(Vector3 a, Vector3 b)
        {
            var d = b - a; float L = d.magnitude; if (L < 1e-4f) return true;
            return Ray(a, d / L, L - .05f) < 0;
        }

        /// Height of the floor under a point (top of the highest box below it), or 0.
        public static float FloorUnder(Vector3 p)
        {
            float best = 0;
            foreach (var b in Boxes) if (p.x > b.x0 && p.x < b.x1 && p.z > b.z0 && p.z < b.z1 && b.y1 <= p.y + .3f && b.y1 > best) best = b.y1;
            return best;
        }

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
