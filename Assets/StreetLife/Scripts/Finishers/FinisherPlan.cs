// Finishers chat, 14 Nov 2026: plans a finishing move before it starts, so all of it can be seen and nothing in it goes
// through anything.
//
// The move: each move's action (both bodies, the props, the swings and slams, the dogs running in, the throw) is laid out
// in advance where it would happen and checked against the world. It has to fit (nothing solid where a body, prop or
// swing goes), and a camera has to see every part of it the whole way through. Moves are tried in a random order (Pancake
// twice as likely, never the same move twice running) and the first that fits and is seen in full is used, otherwise the
// one that comes closest. If nothing fits from the side the attacker is on, the attacker steps round to another side.
//
// The camera: 144 shots round the action (24 headings, 3 heights, full and closer distance) are tried in order of how
// good a shot they'd make (the move's own designed angle first, never straight down the line of the two fighters), and
// the first that sees everything is used. Glass and the arena's invisible walls don't block the view; buildings, cars,
// trees' leaves and the like do.
//
// Throws: a thrown body is a box round its middle, swept along its whole flight. Glass, crates, bins and drums on the way
// break, the arena's invisible walls let it out, and it never goes through anything else: the throw goes high enough to
// clear it, or another direction is picked.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    /// What a finisher was planned with.
    public class FinPlan
    {
        public string fx;
        public Vector3 v0, a0, fwd, side; public float yaw0, dA;
        /// The camera: a unit direction from the move's focus, and a scale on the move's own distance.
        public Vector3 camDir; public float camScale = 1; public bool camSet;
        public Finisher.Throw? thr;
        public float H = -1, gloveTop = 10;
        /// How much of the action the camera sees (1 = all of it), and whether the move's space is clear.
        public float seen = 1; public bool fits = true;
        public float Score => (fits ? 2 : 0) + seen;
    }

    public static class FinPlanner
    {
        const float SC = Fighter.SC, PI = Mathf.PI, TAU = Mathf.PI * 2, G = Finisher.G;
        static readonly Vector3 up = Vector3.up;
        /// A flying body: a box this big each way round its middle (SC above the feet).
        public static readonly Vector3 BODY = new Vector3(.42f, .55f, .42f);
        /// Planning stops looking for something better after this many sight lines (keeps it to a few milliseconds).
        public static int Budget = 45000;
        public static int LastRays; public static double LastMs, ThrowMs, CamMs, PrepMs, SpotMs; static readonly System.Diagnostics.Stopwatch swT = new System.Diagnostics.Stopwatch(), swC = new System.Diagnostics.Stopwatch();

        // ------------------------------------------------------------------ choosing
        /// Plans a finisher by a on v: the move (force, or a random one that fits and can be seen), where it happens, the
        /// camera and any throw. view: someone's camera follows this one (the player is in it).
        public static FinPlan Choose(Fighter a, Fighter v, string force, string last, bool view)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); rays = 0; swT.Reset(); swC.Reset();
            try
            {
                var sp0 = System.Diagnostics.Stopwatch.StartNew(); Prepare(v.pos); PrepMs = sp0.Elapsed.TotalMilliseconds;
                var spot = FinGeo.ClearSpot(v.pos, 2.6f, 5); SpotMs = sp0.Elapsed.TotalMilliseconds - PrepMs;
                FinGeo.ScopeOn(spot, 18);   // every move's floor and room queries stay within 18 m of the spot
                var fwd0 = v.pos - a.pos; fwd0.y = 0; float dA0 = fwd0.magnitude; if (dA0 < 1e-4f) { fwd0 = a.Forward; dA0 = 1; } fwd0 /= dA0;
                float d = Mathf.Clamp(dA0, 1.2f, 2f);
                var order = Order(force, last);
                FinPlan best = null;
                // the attacker's own side first, then the other three
                float[] turns = { 0, PI / 2, -PI / 2, PI };
                for (int fi = 0; fi < turns.Length; fi++)
                {
                    var f = Rot(fwd0, turns[fi]);
                    Vector3 v0 = spot, a0 = v0 - f * d; a0.y = v0.y;
                    if (!FinGeo.BodyFits(a0.x, a0.z, a0.y)) { a0 = v0 - f * 1.2f; a0.y = v0.y; if (fi > 0 && !FinGeo.BodyFits(a0.x, a0.z, a0.y)) continue; }
                    if (fi == 0 && (v0 - v.pos).magnitude <= .25f && (a0 - a.pos).magnitude <= .25f) { a0 = a.pos; v0 = v.pos; }
                    foreach (var fx in order)
                    {
                        var p = Plan(fx, v0, a0, view);
                        float sc = p.Score - fi * .01f;
                        if (best == null || sc > best.Score + .001f) best = p;
                        if (p.fits && p.seen >= 1) return p;
                        if (rays > Budget) return best;
                    }
                    if (!string.IsNullOrEmpty(force) && best.fits) return best;
                }
                return best;
            }
            finally { FinGeo.ScopeOff(); LastRays = rays; LastMs = sw.Elapsed.TotalMilliseconds; ThrowMs = swT.Elapsed.TotalMilliseconds; CamMs = swC.Elapsed.TotalMilliseconds; }
        }

        static List<string> Order(string force, string last)
        {
            var L = new List<string>();
            if (!string.IsNullOrEmpty(force)) { L.Add(force); return L; }
            var pool = new List<string>(); var w = new List<float>();
            foreach (var m in Finishers.Moves) if (m != last) { pool.Add(m); w.Add(m == "Pancake" ? 2 : 1); }
            // a weighted shuffle: the first that works is a fair weighted pick among the ones that work
            while (pool.Count > 0)
            {
                float tot = 0; foreach (var x in w) tot += x; float r = Random.value * tot; int i = 0;
                for (; i < pool.Count - 1; i++) { r -= w[i]; if (r <= 0) break; }
                L.Add(pool[i]); pool.RemoveAt(i); w.RemoveAt(i);
            }
            return L;
        }

        static Vector3 Rot(Vector3 f, float a) { float c = Mathf.Cos(a), s = Mathf.Sin(a); return new Vector3(f.x * c + f.z * s, 0, -f.x * s + f.z * c); }

        // ------------------------------------------------------------------ one move, laid out
        struct Key { public float t, dist; public Vector3 focus; public int p0, p1; }
        static readonly List<Key> keys = new List<Key>();
        static readonly List<Vector3> pts = new List<Vector3>();
        static readonly List<Vector4> room = new List<Vector4>();
        static Vector3 V0, A0, FWD, SIDE; static float YAW0, DA, BACK, AHEAD, camD, camSide, camBack;
        static bool fitsExtra;

        public static FinPlan Plan(string fx, Vector3 v0, Vector3 a0, bool view)
        {
            var p = new FinPlan { fx = fx, v0 = v0, a0 = a0 };
            p.yaw0 = Mathf.Atan2(v0.x - a0.x, v0.z - a0.z); p.fwd = Fwd(p.yaw0); p.side = -Right(p.yaw0); p.dA = Flat(a0 - v0).magnitude;
            V0 = v0; A0 = a0; FWD = p.fwd; SIDE = p.side; YAW0 = p.yaw0; DA = p.dA;
            BACK = Finisher.FreeLineAt(v0, p.fwd, 1); AHEAD = Finisher.FreeLineAt(v0, p.fwd, -1);
            keys.Clear(); pts.Clear(); room.Clear(); fitsExtra = true; camD = 8.5f; camSide = 1; camBack = .22f;
            Vector3? throwFrom = null; float upMin = 13; System.Func<Vector3, Vector3> startOf = null;
            switch (fx)
            {
                case "Roast": Roast(); break;
                case "Piledriver": Piledriver(); break;
                case "Clear!": Clear(); break;
                case "Overcharge": Overcharge(); break;
                case "Hot potato": throwFrom = HotPotato(); upMin = 13; break;
                case "Knockout": throwFrom = Knockout(p); upMin = 15; break;
                case "Heartbreaker": Heartbreaker(); break;
                case "Ice sculpture": throwFrom = Ice(); upMin = 13; break;
                case "Dog pile": DogPile(); break;
                case "Sky drop": startOf = SkyDrop(p); upMin = 6; break;
                case "Pancake": Pancake(); break;
                case "Combat": throwFrom = Combat(); upMin = 18; break;
            }
            // the space: nothing solid where a body, prop or swing goes
            bool fits = fitsExtra;
            for (int i = 0; i < room.Count && fits; i++) if (Solid(room[i])) fits = false;
            // a throw: candidates, cheapest first (three cheapest in a random order, so it isn't always the same way)
            List<Finisher.Throw> throws = null;
            if (throwFrom.HasValue || startOf != null)
            {
                var o = throwFrom ?? Vector3.zero;
                // the body has to be clear of everything solid where it's thrown from (not brushing a table or a rail)
                if (throwFrom.HasValue && Overlaps(o + up * SC, BODY + new Vector3(.05f, 0, .05f), o.y + Fighter.STEP * .5f)) fits = false;
                swT.Start(); throws = Throws(startOf ?? (_ => o), upMin, v0.y); swT.Stop();
                if (throws.Count == 0 || !throws[0].ok) fits = false;
            }
            p.fits = fits;
            if (!view) { if (throws != null && throws.Count > 0) p.thr = throws[0]; p.seen = 1; return p; }
            swC.Start(); PickCamera(p, throws); swC.Stop();
            return p;
        }

        // ------------------------------------------------------------------ the camera
        struct Shot { public Vector3 dir; public float scale, rank; }
        static readonly List<Shot> shots = new List<Shot>();
        static readonly List<Key> tkeys = new List<Key>();
        static readonly List<Vector3> tpts = new List<Vector3>();

        static void PickCamera(FinPlan p, List<Finisher.Throw> throws)
        {
            Shots(p.fwd, p.side, camSide, camBack, CurrentCamSide(p));
            // the throws' flights, as keys of their own
            var tk = new List<(int k0, int k1, int i)>(); tkeys.Clear(); tpts.Clear();
            if (throws != null)
                for (int i = 0; i < throws.Count && tk.Count < 4; i++)
                {
                    if (!throws[i].ok) continue;
                    int k0 = tkeys.Count; ThrowKeys(throws[i], p); tk.Add((k0, tkeys.Count, i));
                }
            int total = pts.Count + keys.Count, bestBad = int.MaxValue; Shot bestS = shots[0]; int bestT = -1;
            foreach (var s in shots)
            {
                int bad = Bad(keys, pts, 0, keys.Count, s, bestBad);
                if (bad >= bestBad) continue;
                if (tk.Count == 0 && throws == null) { bestBad = bad; bestS = s; if (bad == 0) break; continue; }
                // with a throw: the first throw whose whole flight this shot sees
                int bt = -1, btBad = int.MaxValue;
                foreach (var (k0, k1, i) in tk)
                {
                    int b = Bad(tkeys, tpts, k0, k1, s, Mathf.Min(btBad, bestBad - bad));
                    if (b < btBad) { btBad = b; bt = i; if (b == 0) break; }
                }
                int all = bad + (bt >= 0 ? btBad : 0);
                if (all < bestBad) { bestBad = all; bestS = s; bestT = bt; if (all == 0) break; }
                if (rays > Budget) break;
            }
            if (throws != null && throws.Count > 0) p.thr = throws[bestT >= 0 ? bestT : 0];
            if (tk.Count > 0) total += (tk[0].k1 - tk[0].k0) * 3;
            p.camDir = bestS.dir; p.camScale = bestS.scale; p.camSet = true;
            p.seen = bestBad == int.MaxValue ? 0 : Mathf.Clamp01(1 - bestBad / (float)Mathf.Max(1, total));
            if (bestBad == 0) p.seen = 1;
        }

        static int CurrentCamSide(FinPlan p)
        {
            var cam = UnityEngine.Camera.main; if (!cam) return 0;
            return Vector3.Dot(cam.transform.position - p.a0, p.side) >= 0 ? 1 : -1;
        }

        /// The shots to try, best-looking first: the move's designed angle, then turning away from it, higher, closer.
        static void Shots(Vector3 fwd, Vector3 side, float cs, float cb, int curSide)
        {
            shots.Clear();
            float hm = Mathf.Sqrt(cs * cs + cb * cb), el0 = Mathf.Atan2(.3f, Mathf.Max(.05f, hm));
            Vector3 hA = (side * cs - fwd * cb).normalized, hB = (-side * cs - fwd * cb).normalized;
            float yA = Mathf.Atan2(hA.x, hA.z);
            foreach (var el in new[] { el0, el0 + .25f, el0 + .52f })
                for (int i = 0; i < 24; i++)
                {
                    float y = yA + i / 24f * TAU; var h = new Vector3(Mathf.Sin(y), 0, Mathf.Cos(y));
                    float off = Mathf.Min(Vector3.Angle(h, hA), Vector3.Angle(h, hB));
                    float along = Mathf.Abs(Vector3.Dot(h, fwd));
                    int sd = Vector3.Dot(h, side) >= 0 ? 1 : -1;
                    float rank = off + (el - el0) * Mathf.Rad2Deg * .6f + (along > .92f ? 45 : 0) + (curSide != 0 && sd != curSide ? 6 : 0);
                    var dir = h * Mathf.Cos(el) + up * Mathf.Sin(el);
                    shots.Add(new Shot { dir = dir, scale = 1, rank = rank });
                    shots.Add(new Shot { dir = dir, scale = .72f, rank = rank + 18 });
                }
            shots.Sort((a, b) => a.rank.CompareTo(b.rank));
        }

        /// How many of the keys' points the shot can't see (stops counting at stopAt), counting a key whose camera gets
        /// squeezed right in by a wall as one more.
        static int Bad(List<Key> K, List<Vector3> P, int k0, int k1, Shot s, int stopAt)
        {
            int bad = 0;
            for (int k = k0; k < k1; k++)
            {
                var key = K[k]; float len = key.dist * s.scale;
                float h = Ray(key.focus, s.dir, len + .3f, true);
                var cam = h >= 0 ? key.focus + s.dir * Mathf.Max(.3f, h - .3f) : key.focus + s.dir * len;
                if (h >= 0 && h - .3f < len * .6f) bad++;
                for (int i = key.p0; i < key.p1; i++) if (!See(cam, P[i])) { bad++; if (bad >= stopAt) return bad; }
                if (bad >= stopAt) return bad;
            }
            return bad;
        }

        // ------------------------------------------------------------------ the moves' layouts (what Finisher.cs does)
        static void K(float t, Vector3 focus) => keys.Add(new Key { t = t, focus = focus, dist = camD * Mag(), p0 = pts.Count, p1 = pts.Count });
        static float Mag() => Mathf.Sqrt(camSide * camSide + camBack * camBack + .09f);
        static void P(Vector3 p) { pts.Add(p); var k = keys[keys.Count - 1]; k.p1 = pts.Count; keys[keys.Count - 1] = k; }
        static void Room(Vector3 p, float r) => room.Add(new Vector4(p.x, p.y, p.z, r));
        static void Stand(Vector3 feet, bool space = true) { P(feet + up * .35f); P(feet + up * 1.0f); P(feet + up * 1.55f); if (space) Room(feet + up * 1.0f, .25f); }
        static void Lie(Vector3 feet, Vector3 dir, bool space = true) { P(feet + dir * .15f + up * .3f); P(feet + dir * .8f + up * .3f); P(feet + dir * 1.45f + up * .3f); if (space) Room(feet + dir * .8f + up * .35f, .2f); }
        static Vector3 Att(float dist) { dist = dist >= 0 ? Mathf.Min(dist, BACK) : -Mathf.Min(-dist, AHEAD); return V0 - FWD * dist; }
        static Vector3 Mid(Vector3 a, Vector3 b, float y) => new Vector3((a.x + b.x) / 2, V0.y + y, (a.z + b.z) / 2);
        static IEnumerable<float> Times(float t1) { for (float t = 0; t < t1; t += .15f) yield return t; yield return t1; }

        static void Roast()
        {
            var (ry, rl) = Finisher.RoastRun(V0, FWD); var rd = Fwd(ry);
            foreach (var t in Times(1.7f))
            {
                var A = Att(L(DA, 2.3f, EaseIO(Seg(t, .05f, .35f)))); var V = t < .75f ? V0 : V0 + rd * rl * Seg(t, .75f, 1.35f);
                camD = 8.5f + Flat(V - V0).magnitude * .6f; K(t, Mid(A, V, 1.3f));
                Stand(A); if (t < 1.4f) Stand(V); else Lie(V, rd);
            }
        }
        static void Piledriver()
        {
            camD = 7; camBack = -.3f;
            // the body's driven 1.2 m into the floor: on a deck or platform that thin it would poke out underneath
            if (V0.y > .1f ? !Solid(new Vector4(V0.x, V0.y - 1.25f, V0.z, 0)) : FinGeo.NoGround(V0.x, V0.z)) fitsExtra = false;
            foreach (var t in Times(2.9f))
            {
                var A = Att(L(DA, 1.3f, EaseIO(Seg(t, .05f, .4f))));
                float dep = (t >= 2.1f ? 1.5f : t >= 1.55f ? 1.05f : t >= 1.0f ? .55f : 0) * SC;
                K(t, Mid(A, V0, 1.3f)); Stand(A);
                P(V0 + up * (1.55f - dep)); if (1.0f - dep > .25f) P(V0 + up * (1.0f - dep));
                if (t < 1.0f) Room(V0 + up * 1.0f, .25f);
            }
        }
        static void Clear()
        {
            camD = 6.8f; camSide = .9f; camBack = .35f;
            var C = V0 + FWD * 1.15f * SC; var T = C + SIDE * 1.05f; var mon = C + FWD * 1.6f + SIDE * .7f + up * .5f;
            foreach (var t in Times(3.3f))
            {
                var A = Vector3.Lerp(A0, T, EaseIO(Seg(t, 0, .9f))); A.y = V0.y;
                K(t, Mid(A, C, 1.1f)); Stand(A); Lie(V0, FWD); if (t > .45f) { P(mon); Room(mon, .25f); }
            }
        }
        static void Overcharge()
        {
            camD = 7.5f;
            foreach (var t in Times(1.95f)) { var A = Att(L(DA, 1.35f, EaseIO(Seg(t, .05f, .45f)))); K(t, Mid(A, V0, 1.6f)); Stand(A); Stand(V0 + up * .15f); }
        }
        static Vector3 HotPotato()
        {
            camD = 10.5f; Vector3 V = V0;
            foreach (var t in Times(2.55f))
            {
                var A = t < .8f ? Att(L(DA, 1.15f, EaseIO(Seg(t, .2f, .6f)))) : Att(L(1.15f, 10.5f, Seg(t, .95f, 2.3f)));
                V = V0 - FWD * Mathf.Min(2.6f * EaseIn(Seg(t, .95f, 2.5f)), BACK);
                K(t, new Vector3(V.x * .65f + A.x * .35f, V0.y + 1.4f, V.z * .65f + A.z * .35f)); Stand(A); Stand(V);
            }
            return V;
        }
        static Vector3 Knockout(FinPlan p)
        {
            camD = 6.5f; camSide = .9f; camBack = .35f;
            var C = V0 + FWD * 1.15f * SC; var T = C + SIDE * 1.15f;
            p.gloveTop = Finisher.GloveTop(T);
            foreach (var t in Times(3.25f))
            {
                var A = Vector3.Lerp(A0, T, EaseIO(Seg(t, 0, 1))); A.y = V0.y; bool look = t > 1.0f && t < 1.6f;
                K(t, Mid(A, C, look ? 2.6f : 1.2f)); Stand(A); Lie(V0, FWD);
                if (t >= 1.0f && t < 1.55f) for (int i = 0; i < 2; i++)
                    {
                        var top = T + new Vector3(i == 1 ? -.5f : .5f, p.gloveTop, 0); var g = Vector3.Lerp(top, A + up * 1.3f, EaseIn(Seg(t, 1.0f, 1.55f)));
                        P(g); Room(g, .12f);
                    }
            }
            return V0;
        }
        static void Heartbreaker()
        {
            camD = 5.6f; camSide = .8f; camBack = -.6f;
            foreach (var t in Times(5.2f))
            {
                float back = t < 1.25f ? L(DA, .95f, EaseIO(Seg(t, .05f, .45f))) : L(.95f, 1.55f, EaseIO(Seg(t, 1.25f, 1.6f)));
                var A = Att(back); var V = V0 + FWD * (.95f * EaseOut(Seg(t, 1.3f, 2.1f)));
                K(t, Mid(A, V0, t > 1.5f && t < 2.6f ? 2.3f : 1.7f)); Stand(A);
                if (EaseIn(Seg(t, 2.45f, 3.05f)) < .5f) Stand(V); else Lie(V, FWD);
                if (t > 1.25f) { var h = A + FWD * .35f + up * (1.3f + 1.2f * EaseIO(Seg(t, 1.5f, 2.0f))); P(h); Room(h, .15f); }
            }
        }
        static Vector3 Ice()
        {
            camD = 8.5f;
            foreach (var t in Times(1.82f))
            {
                float dist = t < 1.0f ? L(DA, 3.4f, EaseIO(Seg(t, .35f, 1.0f))) : t < 1.5f ? 3.4f : L(3.4f, 1.3f, EaseIn(Seg(t, 1.5f, 1.82f)));
                var A = Att(dist); K(t, Mid(A, V0, 1.5f)); Stand(A); Stand(V0);
                if (t > .6f) { P(V0 + up * 2.3f); P(V0 + new Vector3(.6f, 2.2f, .55f)); P(V0 + new Vector3(-.6f, 2.2f, -.55f)); }
            }
            float hw = .85f * SC, hd = .75f * SC;
            foreach (var (x, z) in new[] { (hw, hd), (-hw, hd), (hw, -hd), (-hw, -hd) }) { Room(V0 + new Vector3(x, .35f, z), .05f); Room(V0 + new Vector3(x, 2.3f, z), .05f); }
            return V0;
        }
        static void DogPile()
        {
            camD = 9.5f; camSide = .85f; camBack = .55f;
            var C = V0 + FWD * 1.3f * SC; var dogs = Finisher.DogSpots(V0, FWD, SIDE);
            foreach (var (T, st, o) in dogs) if ((st - T).magnitude < 2.5f) fitsExtra = false;
            foreach (var t in Times(3.9f))
            {
                float back = t < .9f ? L(DA, 1.2f, EaseIO(Seg(t, .05f, .45f))) : L(1.2f, 3.4f, EaseIO(Seg(t, .9f, 1.3f)));
                var A = Att(back); K(t, new Vector3(C.x - FWD.x * .6f, V0.y + .9f, C.z - FWD.z * .6f)); Stand(A);
                if (t < 2.95f) { if (t < .55f) Stand(V0); else Lie(V0, FWD); }
                for (int i = 0; i < dogs.Length; i++)
                {
                    float t0 = 1.15f + i * .08f, t1 = 1.7f + i * .08f; if (t < t0) continue;
                    var (T, st, o) = dogs[i];
                    var q = t < t1 ? Vector3.Lerp(st, T, EaseOut(Seg(t, t0, t1))) : t < 3.05f ? T : Vector3.Lerp(T, st, Seg(t, 3.05f, 3.9f));
                    P(q + up * .35f); Room(q + up * .35f, .15f);
                }
            }
        }
        static System.Func<Vector3, Vector3> SkyDrop(FinPlan p)
        {
            camD = 11;
            var Ax = Att(1.0f); p.H = Finisher.SkyH(Ax); float H = p.H;
            if (H < 4) fitsExtra = false;
            foreach (var t in Times(1.8f))
            {
                var A = Att(L(DA, 1.0f, EaseIO(Seg(t, .05f, .3f))));
                A.y = V0.y + H * EaseOut(Seg(t, .55f, 1.55f)) - Mathf.Sin(Seg(t, .35f, .55f) * PI) * .3f;
                K(t, A + FWD * .5f + up * 1.3f); Stand(A, false);
                if (t > .3f) { var V = A + FWD * .95f + up * .25f; P(V + up * .3f); P(V + up * 1.0f); } else Stand(V0, false);
            }
            Room(V0 + up * (H + 1.5f), .3f);
            float y = V0.y + H; return dir => new Vector3(Ax.x, y, Ax.z) + dir * .41f + up * 1.69f;
        }
        static void Pancake()
        {
            camD = 10.5f; camSide = .65f; camBack = .75f;
            const float ANG = 2.1f; float hS = 1.92f * SC, armL = 1.0f * SC;
            foreach (var t in Times(3.6f))
            {
                var A = Att(L(DA, 1.0f, EaseIO(Seg(t, .05f, .4f)))); var S0 = A + up * hS;
                K(t, A + FWD * 1.2f + up * 2.0f); Stand(A);
                if (t < .4f) { Stand(V0); continue; }
                if (t >= 2.55f) { var flat = new Vector3(A.x, V0.y, A.z) + FWD * 1.75f; Lie(flat, FWD); P(flat + FWD * .8f + SIDE * .5f + up * .1f); P(flat + FWD * .8f - SIDE * .5f + up * .1f); continue; }
                var dir = PanDir(t, ANG);
                foreach (var k in new[] { .3f, 1.0f, 1.7f }) { var b = S0 + dir * (armL + k); P(b); Room(b, .2f); }
            }
            // the two slams: where the body hits the ground on each side
            foreach (var t in new[] { 1.15f, 1.75f })
            {
                var A = Att(1.0f); var g = A + up * hS + PanDir(t, ANG) * (armL + 2.4f * SC); g.y = V0.y + .3f; Room(g, .2f);
            }
        }
        static Vector3 PanDir(float t, float ANG)
        {
            float th = 0, phk = 0;
            if (t < .4f) phk = 1.95f; else if (t < .85f) phk = L(1.95f, 0, EaseIO(Seg(t, .4f, .85f)));
            else if (t < 1.15f) th = -ANG * EaseIn(Seg(t, .85f, 1.15f)); else if (t < 1.25f) th = -ANG;
            else if (t < 1.75f) th = L(-ANG, ANG, EaseIO(Seg(t, 1.25f, 1.75f))); else if (t < 1.85f) th = ANG;
            else if (t < 2.15f) th = L(ANG, 0, EaseOut(Seg(t, 1.85f, 2.15f))); else if (t < 2.35f) phk = L(0, -.5f, EaseOut(Seg(t, 2.15f, 2.35f))); else phk = L(-.5f, ANG, EaseIn(Seg(t, 2.35f, 2.55f)));
            return SIDE * -Mathf.Sin(th) + up * (Mathf.Cos(th) * Mathf.Cos(phk)) + FWD * (Mathf.Cos(th) * Mathf.Sin(phk));
        }
        static Vector3 Combat()
        {
            camD = 8; Vector3 V = V0;
            foreach (var t in Times(1.8f))
            {
                var A = Att(t < .5f ? L(DA, 4.6f, EaseIO(Seg(t, .05f, .45f))) : 4.6f);
                var near = new Vector3(A.x + FWD.x * 1.15f, V0.y, A.z + FWD.z * 1.15f); float reel = EaseIn(Seg(t, .9f, 1.45f));
                V = Vector3.Lerp(V0, near, reel); V.y = V0.y + Mathf.Sin(reel * PI) * .25f;
                K(t, Mid(A, V, 1.4f)); Stand(A); Stand(V);
            }
            V.y = V0.y; return V;
        }

        static void ThrowKeys(Finisher.Throw k, FinPlan p)
        {
            path.Clear(); Corridor(k.start, k.dir, 120); var end = Fly(k.start, k.vel, p.v0.y, path, out var kind, cor);
            float D = Mathf.Max(p.fx == "Sky drop" ? 13 : camD, 10) * Mag();
            for (int i = 3; i < path.Count; i += 4)
            {
                var b = path[i]; var f = new Vector3(b.x, Mathf.Max(b.y, p.v0.y - 12) + 1.2f, b.z);
                tkeys.Add(new Key { focus = f, dist = D, p0 = tpts.Count, p1 = tpts.Count + 2 }); tpts.Add(b + up * .8f); tpts.Add(b + up * .3f);
            }
            if (kind != Flight.Abyss) { var f = end + up * 1.2f; tkeys.Add(new Key { focus = f, dist = D, p0 = tpts.Count, p1 = tpts.Count + 1 }); tpts.Add(end + up * .5f); }
        }

        // ------------------------------------------------------------------ throws
        public enum Flight { Land, Abyss, Wall }
        static readonly List<Vector3> path = new List<Vector3>();

        /// Throws out of the arena from startOf(direction), 32 directions: each goes just high enough to clear everything
        /// solid on its way (things that break don't count). Valid ones first, cheapest first, the three cheapest shuffled.
        public static List<Finisher.Throw> Throws(System.Func<Vector3, Vector3> startOf, float upMin, float floorY)
        {
            Prepare(startOf(Vector3.forward));
            var b = BoxWorld.Bounds; var cs = new List<Finisher.Throw>(); float a0r = Random.Range(0, TAU);
            for (int i = 0; i < 32; i++)
            {
                float y = a0r + i / 32f * TAU; var dir = Fwd(y); var o = startOf(dir);
                float tx = dir.x > 1e-6f ? (b.x1 - o.x) / dir.x : dir.x < -1e-6f ? (b.x0 - o.x) / dir.x : 1e9f, tz = dir.z > 1e-6f ? (b.z1 - o.z) / dir.z : dir.z < -1e-6f ? (b.z0 - o.z) / dir.z : 1e9f;
                float R = Mathf.Max(1, Mathf.Min(tx, tz)) + 7, upv = upMin, spd = R * G / (2 * upv);
                Corridor(o, dir, R + 8);
                // first guess: high enough to clear the tops of the solid things right on the line (web flingOut), then
                // the sweep checks it properly
                for (int it = 0; it < 3; it++)
                {
                    float need = upMin;
                    foreach (var (s0, s1, bi) in corS)
                    {
                        if (s0 < .3f || s0 > R) continue;
                        var bx = BoxWorld.Boxes[bi]; if (FinGeo.EdgeBox(bx) || (Breaks(bx) && Breakable(bi)) || bx.y1 < o.y + .3f) continue;
                        float cx = Mathf.Clamp(o.x + dir.x * s0, bx.x0, bx.x1) - (o.x + dir.x * s0), cz = Mathf.Clamp(o.z + dir.z * s0, bx.z0, bx.z1) - (o.z + dir.z * s0);
                        if (cx * cx + cz * cz > (BODY.x + .3f) * (BODY.x + .3f)) continue;
                        float tt = Mathf.Max(.05f, (s0 - BODY.x) / Mathf.Min(spd, 70));
                        need = Mathf.Max(need, (bx.y1 + BODY.y + .15f - (o.y + SC) + G / 2 * tt * tt) / tt);
                    }
                    if (need <= upv + .01f) break;
                    upv = Mathf.Min(40, need); spd = R * G / (2 * upv);
                }
                Flight kind = Flight.Wall; float leaves = 0;
                for (int it = 0; it < 9; it++)
                {
                    leaves = 0;
                    Fly(o, dir * Mathf.Min(spd, 70) + up * upv, floorY, null, out kind, cor, l => leaves += l);
                    if (kind != Flight.Wall) break;
                    upv = Mathf.Min(40, upv * 1.3f); spd = R * G / (2 * upv);
                    if (upv >= 40) { Fly(o, dir * Mathf.Min(spd, 70) + up * upv, floorY, null, out kind, cor, l => leaves += l); break; }
                }
                cs.Add(new Finisher.Throw { dir = dir, up = upv, spd = Mathf.Min(spd, 70), yaw = y, start = o, ok = kind != Flight.Wall, cost = R + upv * 1.5f + leaves * 6 });
            }
            cs.Sort((p, q) => p.ok != q.ok ? (p.ok ? -1 : 1) : p.cost.CompareTo(q.cost));
            int n = 0; while (n < cs.Count && n < 3 && cs[n].ok) n++;
            for (int i = n - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var t = cs[i]; cs[i] = cs[j]; cs[j] = t; }
            return cs;
        }

        /// Is this throw, from this start, still clean (nothing solid in its way)?
        public static bool Clean(Vector3 start, Finisher.Throw k, float floorY)
        {
            Prepare(start); Corridor(start, k.dir, 120);
            Fly(start, k.dir * k.spd + up * k.up, floorY, null, out var kind, cor);
            return kind != Flight.Wall;
        }

        /// Flies a thrown body like Finisher.FlingUpd does (the same steps, the same sweep): where it ends and how.
        static Vector3 Fly(Vector3 feet, Vector3 vel, float floorY, List<Vector3> outPath, out Flight kind, List<int> scope, System.Action<float> leaves = null)
        {
            const float dt = Fighter.SIM; var mapB = FinGeo.SceneryBounds();
            bool windowed = scope == cor; int next = 0; winK.Clear();
            for (float t = 0; t < 4; t += dt)
            {
                var prev = feet; vel.y -= G * dt; var step = vel * dt;
                if (windowed)
                {
                    // only the boxes alongside this part of the line (the line's boxes come in order of distance along it)
                    float sNow = (prev.x - corO.x) * corDir.x + (prev.z - corO.z) * corDir.z, lo = sNow - 1.5f, hi = sNow + Mathf.Abs(step.x * corDir.x + step.z * corDir.z) + 1.5f;
                    while (next < corS.Count && corS[next].s0 <= hi) winK.Add(next++);
                    for (int w = winK.Count - 1; w >= 0; w--) if (corS[winK[w]].s1 < lo) winK.RemoveAt(w);
                    win.Clear(); foreach (var w in winK) win.Add(corS[w].i); scope = win;
                }
                if (Step(prev + up * SC, step, scope, false, null, out var at, out var n))
                {
                    if (n.y > .5f && vel.y < 0) { kind = Flight.Land; feet = at - up * SC; return feet; }
                    kind = Flight.Wall; return at - up * SC;
                }
                feet = prev + step; outPath?.Add(feet);
                if (leaves != null && Canopy(feet + up * SC)) leaves(dt);
                bool over = feet.x > mapB.min.x && feet.x < mapB.max.x && feet.z > mapB.min.z && feet.z < mapB.max.z;
                float gs = over ? Surface(scope, feet.x, feet.z, prev.y + .2f) : FinGeo.NONE;
                if (MapRules.DeepWater && gs <= FinGeo.NONE + 1 && feet.y + .6f < Finisher.WaterY(feet.x, feet.z)) { kind = Flight.Abyss; return feet; }
                if (gs <= FinGeo.NONE + 1 && (feet.y < floorY - 25 || t > 3.5f)) { kind = Flight.Abyss; return feet; }
                if (vel.y < 0 && feet.y <= gs) { kind = Flight.Land; feet.y = gs; return feet; }
            }
            kind = Flight.Land; return feet;
        }

        static readonly HashSet<int> skip = new HashSet<int>();
        /// One step of a flying body (its middle at c, moving by d). Things that break are broken (doBreak) or assumed
        /// broken, and it carries on; the arena's invisible walls let it through. True if it runs into anything else:
        /// at = where its middle stops, n = the face it hit.
        public static bool Step(Vector3 c, Vector3 d, List<int> scope, bool doBreak, Fighter by, out Vector3 at, out Vector3 n)
        {
            at = c + d; n = Vector3.zero; float len = d.magnitude; if (len < 1e-6f) return false;
            var dir = d / len; float done = 0; skip.Clear();
            for (int guard = 0; guard < 8; guard++)
            {
                float h = Sweep(c + dir * done, dir, len - done, scope, out int ix, out n);
                if (h < 0) return false;
                var bx = BoxWorld.Boxes[ix]; var p = c + dir * (done + h);
                if (FinGeo.EdgeBox(bx)) { skip.Add(ix); continue; }
                if (Breaks(bx))
                {
                    if (!doBreak) { if (Breakable(ix)) { skip.Add(ix); continue; } at = p; return true; }
                    var hitAt = new Vector3(Mathf.Clamp(p.x, bx.x0, bx.x1), Mathf.Clamp(p.y, bx.y0, bx.y1), Mathf.Clamp(p.z, bx.z0, bx.z1));
                    if (Destruction.Hit(ix, 1000, hitAt, dir, by)) { skip.Add(ix); continue; }
                }
                at = p; return true;
            }
            return false;
        }
        /// Things a flying body smashes through: glass, crates, bins, drums (not trees, frames, cars or walls).
        public static bool Breaks(in Box b) => b.glass || (b.prop && b.y1 - b.y0 < 1.8f);
        /// The box belongs to something that can be broken (Destruction knows it and it's still standing).
        static bool Breakable(int ix)
        {
            var D = Destruction.I; if (D == null) return false;
            if (breakFrame != Time.frameCount || breakCount != D.ents.Count)
            {
                breakFrame = Time.frameCount; breakCount = D.ents.Count; breakable.Clear();
                foreach (var e in D.ents) if (e != null && e.alive && e.boxes != null) foreach (var i in e.boxes) breakable.Add(i);
            }
            return breakable.Contains(ix);
        }
        static readonly HashSet<int> breakable = new HashSet<int>(); static int breakFrame = -1, breakCount = -1;

        /// Sweeps the body box from o along dir for len: distance to the first box it would touch (-1 for none).
        static float Sweep(Vector3 o, Vector3 dir, float len, List<int> scope, out int ix, out Vector3 n)
        {
            ix = -1; n = Vector3.zero; float best = len; var B = BoxWorld.Boxes; int cnt = scope != null ? scope.Count : B.Count;
            for (int k = 0; k < cnt; k++)
            {
                int i = scope != null ? scope[k] : k; if (skip.Contains(i)) continue;
                var b = B[i]; if (b.x0 < -9000) continue;
                float x0 = b.x0 - BODY.x, x1 = b.x1 + BODY.x, y0 = b.y0 - BODY.y, y1 = b.y1 + BODY.y, z0 = b.z0 - BODY.z, z1 = b.z1 + BODY.z;
                if (o.x > x0 && o.x < x1 && o.y > y0 && o.y < y1 && o.z > z0 && o.z < z1) continue;   // already overlapping: let it out
                float t0 = 0, t1 = best; int ax = -1;
                if (!Slab(o.x, dir.x, x0, x1, ref t0, ref t1, 0, ref ax) || !Slab(o.y, dir.y, y0, y1, ref t0, ref t1, 1, ref ax) || !Slab(o.z, dir.z, z0, z1, ref t0, ref t1, 2, ref ax)) continue;
                if (t0 < best || ix < 0) { best = t0; ix = i; n = ax == 0 ? new Vector3(-Mathf.Sign(dir.x), 0, 0) : ax == 1 ? new Vector3(0, -Mathf.Sign(dir.y), 0) : ax == 2 ? new Vector3(0, 0, -Mathf.Sign(dir.z)) : Vector3.zero; }
            }
            return ix >= 0 ? best : -1;
        }
        static bool Slab(float o, float d, float mn, float mx, ref float t0, ref float t1, int axis, ref int ax)
        {
            if (Mathf.Abs(d) < 1e-9f) return o > mn && o < mx;
            float a = (mn - o) / d, b = (mx - o) / d; if (a > b) { var q = a; a = b; b = q; }
            if (a > t0) { t0 = a; ax = axis; }
            if (b < t1) t1 = b; return t0 <= t1;
        }

        // ------------------------------------------------------------------ the world near the move
        static readonly List<int> near = new List<int>(), nearSee = new List<int>(), cor = new List<int>();
        static readonly List<Bounds> canopies = new List<Bounds>();
        static Vector3 nearC; static int nearFrame = -1, nearCount = -1;
        static int rays;

        /// The boxes round c (for the space and the sight lines) and the trees' leaves; cached for the frame.
        static void Prepare(Vector3 c)
        {
            float ddx = c.x - nearC.x, ddz = c.z - nearC.z;
            if (nearFrame == Time.frameCount && ddx * ddx + ddz * ddz < 25 && nearCount == BoxWorld.Boxes.Count) return;
            nearFrame = Time.frameCount; nearC = c; nearCount = BoxWorld.Boxes.Count; near.Clear(); nearSee.Clear();
            const float R = GN * CELL / 2; var B = BoxWorld.Boxes;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i]; if (b.x0 < -9000 || FinGeo.EdgeBox(b)) continue;
                if (b.x1 < c.x - R || b.x0 > c.x + R || b.z1 < c.z - R || b.z0 > c.z + R) continue;
                near.Add(i); if (!b.glass) nearSee.Add(i);
            }
            Grid();
            canopies.Clear();
            var D = Destruction.I;
            if (D != null)
                foreach (var e in D.ents)
                {
                    if (e == null || !e.alive || e.d == null || e.d.kind != "tree" || !e.t || !e.t.gameObject.activeInHierarchy) continue;
                    bool any = false; Bounds bb = default;
                    foreach (var r in e.rends) if (r) { if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
                    if (!any || bb.size.y < 2.5f) continue;
                    float trunk = bb.min.y + Mathf.Max(2.2f, bb.size.y * .3f);
                    var mn = new Vector3(bb.center.x - bb.extents.x * .8f, trunk, bb.center.z - bb.extents.z * .8f);
                    var mx = new Vector3(bb.center.x + bb.extents.x * .8f, bb.max.y, bb.center.z + bb.extents.z * .8f);
                    var cb = new Bounds(); cb.SetMinMax(mn, mx); canopies.Add(cb);
                }
        }
        static readonly List<(float s0, float s1, int i)> corS = new List<(float, float, int)>();
        static Vector3 corO, corDir;
        /// The boxes along a throw's line (within 2.5 m of it), ordered by how far along it they start.
        static void Corridor(Vector3 o, Vector3 dir, float L)
        {
            corS.Clear(); cor.Clear(); corO = o; corDir = dir; var B = BoxWorld.Boxes; float m = 2.5f; var e = o + dir * L;
            float minx = Mathf.Min(o.x, e.x) - m, maxx = Mathf.Max(o.x, e.x) + m, minz = Mathf.Min(o.z, e.z) - m, maxz = Mathf.Max(o.z, e.z) + m;
            var nrm = new Vector2(-dir.z, dir.x);
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i]; if (b.x0 < -9000) continue;
                if (b.x1 < minx || b.x0 > maxx || b.z1 < minz || b.z0 > maxz) continue;
                float d0 = (b.x0 - o.x) * nrm.x + (b.z0 - o.z) * nrm.y, d1 = (b.x1 - o.x) * nrm.x + (b.z0 - o.z) * nrm.y, d2 = (b.x0 - o.x) * nrm.x + (b.z1 - o.z) * nrm.y, d3 = (b.x1 - o.x) * nrm.x + (b.z1 - o.z) * nrm.y;
                float lo = Mathf.Min(Mathf.Min(d0, d1), Mathf.Min(d2, d3)), hi = Mathf.Max(Mathf.Max(d0, d1), Mathf.Max(d2, d3));
                if (lo > m || hi < -m) continue;
                float a0 = (b.x0 - o.x) * dir.x + (b.z0 - o.z) * dir.z, a1 = (b.x1 - o.x) * dir.x + (b.z0 - o.z) * dir.z, a2 = (b.x0 - o.x) * dir.x + (b.z1 - o.z) * dir.z, a3 = (b.x1 - o.x) * dir.x + (b.z1 - o.z) * dir.z;
                corS.Add((Mathf.Min(Mathf.Min(a0, a1), Mathf.Min(a2, a3)), Mathf.Max(Mathf.Max(a0, a1), Mathf.Max(a2, a3)), i));
            }
            corS.Sort((p, q) => p.s0.CompareTo(q.s0));
            foreach (var c in corS) cor.Add(c.i);
        }
        static readonly List<int> win = new List<int>(), winK = new List<int>();
        static float Surface(List<int> scope, float x, float z, float y)
        {
            float best = 0, bd = y + .7f >= 0 ? y : (0 - y) * 4;
            if (FinGeo.NoGround(x, z)) { best = FinGeo.NONE; bd = 1e9f; }
            var B = BoxWorld.Boxes; int cnt = scope != null ? scope.Count : B.Count;
            for (int k = 0; k < cnt; k++)
            {
                var b = B[scope != null ? scope[k] : k];
                if (x <= b.x0 || x >= b.x1 || z <= b.z0 || z >= b.z1) continue;
                float h = b.y1, d = h <= y + .7f ? y - h : (h - y) * 4;
                if (d < bd) { bd = d; best = h; }
            }
            return best;
        }
        static bool Canopy(Vector3 p) { foreach (var c in canopies) if (c.Contains(p)) return true; return false; }
        /// Is a body inside one of the trees' leaves? (The fling knocks leaves off.)
        public static bool InLeaves(Vector3 p) => Canopy(p);
        public static void RefreshLeaves(Vector3 c) { nearFrame = -1; Prepare(c); }

        /// Something solid (higher up than a floor at minY) overlapping the box half-size h round c.
        static bool Overlaps(Vector3 c, Vector3 h, float minY)
        {
            var B = BoxWorld.Boxes;
            foreach (var i in near)
            {
                var b = B[i]; if (b.y1 <= minY) continue;
                if (c.x + h.x > b.x0 && c.x - h.x < b.x1 && c.y + h.y > b.y0 && c.y - h.y < b.y1 && c.z + h.z > b.z0 && c.z - h.z < b.z1) return true;
            }
            return false;
        }
        /// Something solid within r of p.
        static bool Solid(Vector4 q)
        {
            var B = BoxWorld.Boxes; float r = q.w;
            foreach (var i in near)
            {
                var b = B[i];
                if (q.x > b.x0 - r && q.x < b.x1 + r && q.y > b.y0 - r && q.y < b.y1 + r && q.z > b.z0 - r && q.z < b.z1 + r) return true;
            }
            return false;
        }

        /// A sight line from the camera to p: glass and the arena's invisible walls don't block it, leaves do.
        public static bool See(Vector3 from, Vector3 p)
        {
            rays++;
            var d = p - from; float L = d.magnitude - .2f; if (L <= 0) return true; d /= (L + .2f);
            if (Ray(from, d, L, false) >= 0) return false;
            foreach (var c in canopies) if (c.IntersectRay(new Ray(from, d), out float h) && h < L && !c.Contains(p)) return false;
            return true;
        }
        /// Distance along a ray to the first box near the move (glass counts when solid is set), or -1.
        static float Ray(Vector3 o, Vector3 d, float L, bool solid)
        {
            var grid = solid ? gAll : gSee; var B = BoxWorld.Boxes; float best = L; bool hit = false;
            if (stamp.Length < B.Count) stamp = new int[B.Count + 64]; stampId++;
            // walk the grid cells along the ray (flat), nearest first, and stop once a hit is nearer than the next cell
            float x = o.x - gx0, z = o.z - gz0; int cx = Mathf.FloorToInt(x / CELL), cz = Mathf.FloorToInt(z / CELL);
            int sx = d.x > 0 ? 1 : -1, sz = d.z > 0 ? 1 : -1;
            float tmx = Mathf.Abs(d.x) > 1e-7f ? ((sx > 0 ? (cx + 1) * CELL : cx * CELL) - x) / d.x : float.MaxValue;
            float tmz = Mathf.Abs(d.z) > 1e-7f ? ((sz > 0 ? (cz + 1) * CELL : cz * CELL) - z) / d.z : float.MaxValue;
            float tdx = Mathf.Abs(d.x) > 1e-7f ? CELL / Mathf.Abs(d.x) : float.MaxValue, tdz = Mathf.Abs(d.z) > 1e-7f ? CELL / Mathf.Abs(d.z) : float.MaxValue;
            for (int it = 0; it < 96; it++)
            {
                if (cx >= 0 && cz >= 0 && cx < GN && cz < GN)
                {
                    var list = grid[cz * GN + cx];
                    if (list != null)
                        foreach (var i in list)
                        {
                            if (stamp[i] == stampId) continue; stamp[i] = stampId;
                            var b = B[i]; float t0 = 0, t1 = best; int ax = 0;
                            if (!Slab(o.x, d.x, b.x0, b.x1, ref t0, ref t1, 0, ref ax) || !Slab(o.y, d.y, b.y0, b.y1, ref t0, ref t1, 1, ref ax) || !Slab(o.z, d.z, b.z0, b.z1, ref t0, ref t1, 2, ref ax)) continue;
                            if (o.x > b.x0 && o.x < b.x1 && o.y > b.y0 && o.y < b.y1 && o.z > b.z0 && o.z < b.z1) continue;   // starts inside it
                            best = t0; hit = true;
                        }
                }
                float exit = Mathf.Min(tmx, tmz);
                if (exit >= best || exit > L) break;
                if (tmx < tmz) { cx += sx; tmx += tdx; } else { cz += sz; tmz += tdz; }
            }
            return hit ? best : -1;
        }
        const float CELL = 4; const int GN = 26;
        static readonly List<int>[] gAll = new List<int>[GN * GN], gSee = new List<int>[GN * GN];
        static float gx0, gz0; static int[] stamp = new int[0]; static int stampId;
        static void Grid()
        {
            gx0 = nearC.x - GN * CELL / 2; gz0 = nearC.z - GN * CELL / 2;
            for (int k = 0; k < gAll.Length; k++) { gAll[k]?.Clear(); gSee[k]?.Clear(); }
            var B = BoxWorld.Boxes;
            foreach (var i in near)
            {
                var b = B[i];
                int x0 = Mathf.Max(0, Mathf.FloorToInt((b.x0 - gx0) / CELL)), x1 = Mathf.Min(GN - 1, Mathf.FloorToInt((b.x1 - gx0) / CELL));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((b.z0 - gz0) / CELL)), z1 = Mathf.Min(GN - 1, Mathf.FloorToInt((b.z1 - gz0) / CELL));
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                    {
                        int k = z * GN + x;
                        (gAll[k] ?? (gAll[k] = new List<int>())).Add(i);
                        if (!b.glass) (gSee[k] ?? (gSee[k] = new List<int>())).Add(i);
                    }
            }
        }

        /// p, or just above whatever it's sunk into (a piledriven body's visible part).
        public static Vector3 Above(Vector3 p, float floor)
        {
            foreach (var b in BoxWorld.Boxes)
                if (b.x0 > -9000 && p.x > b.x0 && p.x < b.x1 && p.y > b.y0 && p.y < b.y1 && p.z > b.z0 && p.z < b.z1 && !FinGeo.EdgeBox(b)) p.y = b.y1 + .25f;
            if (p.y < floor) p.y = floor + .25f;   // below the floor the move's on (the open ground has no box)
            return p;
        }

        /// For the camera at run time: the best of the shots for the move's current spot (all its points seen), or null.
        public static Vector3? Reaim(Finisher F, Vector3 focus, float dist, Vector3[] see)
        {
            Prepare(focus); Shots(F.fwd, F.side, F.camSide, F.camBack, 0);
            keys.Clear(); pts.Clear(); keys.Add(new Key { focus = focus, dist = dist, p0 = 0, p1 = see.Length }); pts.AddRange(see);
            foreach (var s in shots) if (s.scale == 1 && Bad(keys, pts, 0, 1, s, 1) == 0) return s.dir;
            return null;
        }

        // ------------------------------------------------------------------ the move code's helpers
        static Vector3 Fwd(float y) => new Vector3(Mathf.Sin(y), 0, Mathf.Cos(y));
        static Vector3 Right(float y) => new Vector3(Mathf.Cos(y), 0, -Mathf.Sin(y));
        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0, p.z);
        static float Seg(float t, float t0, float t1) => Mathf.Clamp01((t - t0) / (t1 - t0));
        static float EaseIO(float u) { u = Mathf.Clamp01(u); return u * u * (3 - 2 * u); }
        static float EaseIn(float u) { u = Mathf.Clamp01(u); return u * u; }
        static float EaseOut(float u) { u = Mathf.Clamp01(u); return 1 - (1 - u) * (1 - u); }
        static float L(float a, float b, float t) => a + (b - a) * t;
    }
}
