// Finishers chat: one finishing move in progress (the web game's v.finishing), and all twelve moves, ported from
// updMove2 / flingOut / flingUpd in the web game's index.html. Positions are worked out in Unity's space; the poses
// are kept in the web game's numbers (FinisherPose mirrors them), so each line can be checked against the original.
// Mirroring note: Unity's world is the web world with z flipped, so "turn by +a" there is "turn by -a" here, and the
// web's `side` (the attacker's local +x, their left) is -Right here.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    /// A pose for one step, in the web game's terms: root (lean rx, yaw ry, roll rz), arms ar/al (x,y,z), legs ll/lr, head.
    public class FPose
    {
        public bool on, root, ar, al, ll, lr, head, hasScale;
        public float rx, ry, rz, LL, LR, HEAD, snap;
        public Vector3 AR, AL, scale;
        public void Clear() { on = root = ar = al = ll = lr = head = hasScale = false; snap = 0; }
        public FPose Root(float x, float y, float z) { on = root = true; rx = x; ry = y; rz = z; return this; }
        public FPose Ar(float x, float y, float z) { on = ar = true; AR = new Vector3(x, y, z); return this; }
        public FPose Al(float x, float y, float z) { on = al = true; AL = new Vector3(x, y, z); return this; }
        public FPose Ar(Vector3 v) { on = ar = true; AR = v; return this; }
        public FPose Al(Vector3 v) { on = al = true; AL = v; return this; }
        public FPose Ll(float x) { on = ll = true; LL = x; return this; }
        public FPose Lr(float x) { on = lr = true; LR = x; return this; }
        public FPose Head(float x) { on = head = true; HEAD = x; return this; }
        public FPose Scale(Vector3 s) { on = hasScale = true; scale = s; return this; }
        public FPose Snap(float l) { snap = l; return this; }
        public FPose Copy(FPose o) { on = o.on; root = o.root; ar = o.ar; al = o.al; ll = o.ll; lr = o.lr; head = o.head; hasScale = o.hasScale; rx = o.rx; ry = o.ry; rz = o.rz; LL = o.LL; LR = o.LR; HEAD = o.HEAD; AR = o.AR; AL = o.AL; scale = o.scale; snap = o.snap; return this; }
    }

    public class Finisher
    {
        const float SC = Fighter.SC, PI = Mathf.PI, TAU = Mathf.PI * 2;
        public const float G = 34f;   // FLING_G
        public readonly Fighter a, v;
        public readonly string fx;
        public float t, dur;
        public Vector3 a0, v0, fwd, side;
        public float yaw0, dA;
        public class Pre { public float t, len = .45f; public Vector3 av, vv; }
        public Pre pre;
        public bool cine = true, done, gone, bannerOn;
        // camera (web F.focus / camD / camK / camSide / camBack / side)
        public Vector3? focus; public float camD = 8.5f, camK = 3.5f, camSide = 1, camBack = .22f;
        /// The planned camera (FinPlanner): a direction from the focus and a scale on the move's distance.
        public Vector3 camDir; public float camScale = 1; public bool camSet;
        public readonly FinPlan plan;
        public Vector3? imp; public bool abyss;
        // drawing
        public readonly FPose aP = new FPose(), vP = new FPose();
        public float aYaw, vYaw;
        public bool vDown = true;
        readonly List<GameObject> obj = new List<GameObject>();
        readonly HashSet<int> sfxDone = new HashSet<int>(); readonly Dictionary<int, int> sfxEvery = new Dictionary<int, int>();

        public Finisher(Fighter a, Fighter v, FinPlan p)
        {
            this.a = a; this.v = v; fx = p.fx; dur = Finishers.Dur[p.fx]; plan = p;
            // both shuffle over to the planned spot first (a clear spot, the attacker on the side the plan picked)
            v0 = p.v0; a0 = p.a0;
            bool moved = (v0 - v.pos).magnitude > .25f || (a0 - a.pos).magnitude > .25f;
            if (moved) pre = new Pre { av = a.pos, vv = v.pos, len = Mathf.Clamp(.45f + .08f * Mathf.Max((a0 - a.pos).magnitude, (v0 - v.pos).magnitude), .45f, .75f) };
            else { a0 = a.pos; v0 = v.pos; }
            yaw0 = Mathf.Atan2(v0.x - a0.x, v0.z - a0.z);
            a.yaw = yaw0; v.yaw = yaw0 + PI; a.pitch = 0;
            fwd = Fwd(yaw0); side = -Right(yaw0);
            dA = Flat(a0 - v0).magnitude;
            a.vel = Vector3.zero; v.vel = Vector3.zero; a.cv = 0;
            aYaw = a.yaw; vYaw = v.yaw;
            if (p.camSet) { camDir = p.camDir; camScale = p.camScale; camSet = true; }
        }

        public bool Involves(Fighter f) => f && (f == a || f == v);
        bool Mine => Finishers.I && (a == Finishers.I.Player);
        bool Me(Fighter f) => Finishers.I && f == Finishers.I.Player;

        // ------------------------------------------------------------------ helpers (web names)
        static Vector3 Fwd(float y) => new Vector3(Mathf.Sin(y), 0, Mathf.Cos(y));
        static Vector3 Right(float y) => new Vector3(Mathf.Cos(y), 0, -Mathf.Sin(y));
        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0, p.z);
        static float Seg(float t, float t0, float t1) => Mathf.Clamp01((t - t0) / (t1 - t0));
        static float EaseIO(float u) { u = Mathf.Clamp01(u); return u * u * (3 - 2 * u); }
        static float EaseIn(float u) { u = Mathf.Clamp01(u); return u * u; }
        static float EaseOut(float u) { u = Mathf.Clamp01(u); return 1 - (1 - u) * (1 - u); }
        static float L(float a, float b, float t) => a + (b - a) * t;      // web lerp (not clamped)
        static Vector3 L(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
        static float Rnd(float a, float b) => Random.Range(a, b);
        static float Damp(float a, float b, float l, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-l * dt));
        static float Wrap(float d) => ((d + PI) % TAU + TAU) % TAU - PI;
        static readonly Vector3 up = Vector3.up;

        void Shake(float k) { if (Me(v)) Finishers.Shake = Mathf.Max(Finishers.Shake, k); else if (Me(a)) Finishers.Shake = Mathf.Max(Finishers.Shake, k * .35f); }
        void Slow(float k) => Finishers.SlowMoFor(k, a, v);
        void Vig(float k) => Finishers.VigFor(k, a, v);
        Vector3 Hand(Fighter f, bool left) => FinisherPose.Hand(f, left);
        static readonly string[] SPLASH = { "#ffffff", "#dff3ff", "#bfe6f5" };
        static Waterside Water { get { var w = MapWorld.I; return w ? w.GetComponent<Waterside>() : null; } }
        public static float WaterY(float x, float z) { var w = Water; return w ? w.WaterY(x, z) : MapRules.DeepWater ? MapRules.LagoonWaterY(x, z, Time.timeSinceLevelLoad) : MapRules.BeachWaterY(x, z, Time.timeSinceLevelLoad); }
        static void Splash(float x, float z, float size) { var w = Water; if (w) w.Splash(x, z, size); }
        void TintV(Color c, float k, float smooth = -1) { var p = FinisherPose.Of(v); if (p) p.Tint(c, k, smooth); }

        static readonly string[] DUST = { "#c9bfae", "#9a8f80" }, RUBBLE3 = { "#8a7f72", "#6e665c", "#a89c8a" }, RUBBLE4 = { "#8a7f72", "#6e665c", "#a89c8a", "#4a4540" },
            FIRE = { "#ff3b1f", "#ff7a1f", "#ffb347", "#ffe08a" }, SMOKE = { "#4a4d55", "#6b6f78" }, SMOKE3 = { "#4a4d55", "#6b6f78", "#8a8f99" },
            ICE = { "#ffffff", "#bfeaff" }, ELEC = { "#7ff3ff", "#ffffff" }, BOLT = { "#fff27a", "#7ff3ff", "#ffffff" }, BLOOD = { "#c8102e", "#7d0d17" },
            SPARK = { "#ffffff", "#ffd23f" }, WHITE = { "#ffffff" }, FUSE = { "#ffd23f", "#ff7a1f", "#ffffff" }, GREY = { "#9aa0a8" }, EMBER = { "#ff7a1f", "#ffb347" },
            PAW = { "#c9bfae" }, LEAVES = { "#3f7a2e", "#5e9b3a", "#2d5a22", "#7aa84a" }, WHISTLE = { "#ffffff", "#ffd23f" }, ASH = { "#6b6f78", "#4a4d55", "#ff7a1f" }, ASH3 = { "#4a4d55", "#6b6f78", "#2b2d33" }, HIT = { "#ffffff", "#ffd23f", "#c9bfae" },
            ICEBITS = { "#e6f7ff", "#bfeaff", "#9fd8f5", "#ffffff" }, WALLDUST = { "#c9bfae", "#9a8f80", "#ffffff" }, BLAST = { "#ffb347", "#ff7a1f", "#ffe08a" }, DRIP = { "#7d0d17", "#c8102e" }, PANRUB = { "#8a7f72", "#a89c8a", "#6e665c" }, JETFIRE = { "#ffb347", "#ff7a1f", "#ffffff" }, JETSMOKE = { "#d8dbe2", "#9aa0a8" };

        static void Emit(Vector3 p, int n, float speed, float size, float life, float grav, string[] cols, float up = 0) => FinisherFx.Emit(p, n, speed, size, life, grav, cols, up);
        static void DustRing(Vector3 c, int n, float r, float speed, float size, float life, int per = 1)
        {
            for (int i = 0; i < n; i++) { float an = i / (float)n * TAU; Emit(c + new Vector3(Mathf.Cos(an) * r, 0, Mathf.Sin(an) * r), per, speed, size, life, 0, DUST, 1); }
        }

        T Keep<T>(T c) where T : Component { obj.Add(c.gameObject); return c; }

        // web setAtt / freeLine: put the attacker `dist` metres short of the victim's spot along the move's line (never into a wall)
        float ffwd = -1, fback = -1;
        float FreeLine(int sgn)
        {
            if (sgn > 0 && ffwd >= 0) return ffwd; if (sgn < 0 && fback >= 0) return fback;
            float m = FreeLineAt(v0, fwd, sgn);
            if (sgn > 0) ffwd = m; else fback = m;
            return m;
        }
        /// How far the attacker can stand from v0 along -fwd*sgn: short of walls (taller than a step) and never out over a
        /// roof edge, open water or a drop. (The planner uses it too.)
        public static float FreeLineAt(Vector3 v0, Vector3 fwd, int sgn)
        {
            float m = 12;
            foreach (var y in new[] { .35f, 1.2f, 2.0f })
            {
                var o = v0 + up * y; var d = -fwd * sgn;
                float h = BoxWorld.Ray(o, d, 12, out int ix);
                if (h >= 0 && ix >= 0 && BoxWorld.Boxes[ix].y1 - v0.y > Fighter.STEP) m = Mathf.Min(m, h - Fighter.R - .05f);
            }
            m = Mathf.Min(m, FinGeo.FloorRun(v0, -fwd * sgn, 12) - Fighter.R * .5f);
            return Mathf.Max(0, m);
        }
        /// Roast: which way the burning victim runs (the clearest way out, roughly away from the attacker) and how far.
        public static (float yaw, float len) RoastRun(Vector3 vpos, Vector3 fwd)
        {
            float away = Mathf.Atan2(fwd.x, fwd.z); var o = vpos + up * .9f; float by = away, bl = -1;
            foreach (var da in new[] { 0f, .35f, -.35f, .7f, -.7f, 1.05f, -1.05f, 1.4f, -1.4f })
            { float y = away + da; float h = BoxWorld.Ray(o, Fwd(y), 10); float Lh = Mathf.Min(h >= 0 ? h - .9f : 10, FinGeo.FloorRun(vpos, Fwd(y), 10) - .4f); if (bl < 0 || Lh > bl + .4f) { by = y; bl = Lh; } }
            return (by, Mathf.Clamp(bl, .4f, 1.8f));
        }
        /// Dog pile: each dog's spot round the body, where it runs in from (along a clear line on the same floor, up to
        /// 7 m out) and the way it faces out.
        public static (Vector3 T, Vector3 st, Vector3 o)[] DogSpots(Vector3 v0, Vector3 fwd, Vector3 side)
        {
            var C = v0 + fwd * 1.3f * SC; var spots = new[] { (-1f, .55f), (1f, .55f), (-1f, -.55f), (1f, -.55f) };
            var r = new (Vector3, Vector3, Vector3)[4];
            for (int i = 0; i < 4; i++)
            {
                var (s, f) = spots[i]; var T = C + side * s * 1.05f * SC + fwd * f * 1.0f * SC; T.y = v0.y;
                var o = (side * s * .75f + fwd * f * .65f).normalized;
                float run = FinGeo.FloorRun(new Vector3(T.x, v0.y, T.z), o, 7);
                foreach (var y in new[] { .25f, .6f }) { float h = BoxWorld.Ray(new Vector3(T.x, v0.y + y, T.z), o, 7); if (h >= 0) run = Mathf.Min(run, h - .45f); }
                r[i] = (T, T + o * Mathf.Max(.6f, run), o);
            }
            return r;
        }
        /// Sky drop: how high the jetpack can go above a spot (under any roof, up to 20 m).
        public static float SkyH(Vector3 apos) { float h = BoxWorld.Ray(apos + up * 2.6f, up, 24); return h >= 0 ? Mathf.Max(0, h - .4f) : 20; }
        /// Knockout: how high above T the gloves can drop from (under any roof, up to 10 m).
        public static float GloveTop(Vector3 T) { float h = BoxWorld.Ray(T + up * .5f, up, 10); return h >= 0 ? Mathf.Max(1.2f, h + .5f - .4f) : 10; }
        void SetAtt(float dist, float dt)
        {
            dist = dist >= 0 ? Mathf.Min(dist, FreeLine(1)) : -Mathf.Min(-dist, FreeLine(-1));
            float x = v0.x - fwd.x * dist, z = v0.z - fwd.z * dist;
            a.vel = new Vector3((x - a.pos.x) / Mathf.Max(dt, 1e-3f), 0, (z - a.pos.z) / Mathf.Max(dt, 1e-3f));
            a.pos = new Vector3(x, v0.y, z); a.grounded = true;
        }

        // ------------------------------------------------------------------ banners and call-outs (web elimStart / say)
        public void Banner()
        {
            if (Me(a) || Me(v))
            {
                bannerOn = true;
                FinisherHud.Banner("Eliminated", Me(a) ? fx + "  ·  You finished " + v.displayName : fx + "  ·  " + a.displayName + " finished you", Me(a), Mathf.Max(1.2f, dur * .5f));
            }
            else Finishers.FeedLine($"{a.displayName}  [{fx}]  {v.displayName}", a, v);
        }
        void Say(string big, string winSub, string loseSub)
        {
            if (bannerOn && (Me(a) || Me(v)) && FinisherHud.BannerUp)
            { FinisherHud.Banner("Eliminated", Me(a) ? big + "  ·  You finished " + v.displayName : big + "  ·  " + loseSub, Me(a), -1); return; }
            if (Me(a)) FinisherHud.Center(big, winSub, true, 1.1f);
            else if (Me(v)) FinisherHud.Center(big, loseSub, false, 1.1f);
        }

        // ------------------------------------------------------------------ step
        public void Tick(float dt)
        {
            aP.Clear(); vP.Clear();
            if (pre != null)
            {
                // both shuffle over to the clear spot first (web F.pre, .45 s)
                pre.t += dt; float u = Mathf.Min(1, pre.t / pre.len), e = u * u * (3 - 2 * u);
                var pv = Vector3.Lerp(pre.vv, v0, e); var pa = Vector3.Lerp(pre.av, a0, e);
                v.vel = (pv - v.pos) / dt; a.vel = (pa - a.pos) / dt; v.pos = pv; a.pos = pa;
                a.yaw = Mathf.Atan2(v.pos.x - a.pos.x, v.pos.z - a.pos.z); v.grounded = a.grounded = true;
                aYaw = a.yaw; vYaw = v.yaw;
                if (u < 1) return;
                pre = null; a.yaw = yaw0; v.vel = a.vel = Vector3.zero;
            }
            t += dt;
            if (t > .15f) { v.cv = Mathf.Max(0, v.cv - dt * 5); v.crouching = false; }
            switch (fx)
            {
                case "Ice sculpture": IceSculpture(dt); break;
                case "Dog pile": DogPile(dt); break;
                case "Sky drop": SkyDrop(dt); break;
                case "Hot potato": HotPotato(dt); break;
                case "Heartbreaker": Heartbreaker(dt); break;
                case "Knockout": Knockout(dt); break;
                case "Roast": Roast(dt); break;
                case "Piledriver": Piledriver(dt); break;
                case "Clear!": Clear(dt); break;
                case "Overcharge": Overcharge(dt); break;
                case "Combat": Combat(dt); break;
                case "Pancake": Pancake(dt); break;
            }
            if (fall != null && fx != "Sky drop")
            {
                camD = Mathf.Max(camD, 10); camK = imp.HasValue ? 3.5f : 6;
                focus = imp.HasValue && !abyss ? imp.Value + up * 1.2f : new Vector3(v.pos.x, Mathf.Max(v.pos.y, v0.y - 12) + 1.2f, v.pos.z);
            }
            FinSfx();
            aYaw = a.yaw; vYaw = v.yaw;
        }

        /// The visual time (fighters are drawn between the last two steps).
        float TL => Finishers.I ? t - Fighter.SIM * (1 - v.Alpha) : t;

        // ------------------------------------------------------------------ flings (web flingOut / flingStart / flingUpd)
        public class Fall { public Vector3 v; public float rx, t0, spin; public bool pass, leaves; public bool? edgeOK; }
        public Fall fall;
        public struct Throw { public Vector3 dir, start; public float up, spd, yaw, cost; public bool ok; public Vector3 vel => dir * spd + Vector3.up * up; }

        /// The throw out of the arena from o: the planned one if it's still clear from here, else a fresh one (the planner's
        /// sweep: high enough to clear everything solid on the way, glass and crates breaking).
        Throw TakeThrow(Vector3 o, float upMin)
        {
            if (plan != null && plan.thr.HasValue && FinPlanner.Clean(o, plan.thr.Value, v0.y)) { var k = plan.thr.Value; k.start = o; return k; }
            var L = FinPlanner.Throws(_ => o, upMin, v0.y);
            return L[0];
        }

        void FlingStart(Vector3 dir, float spd, float upV, bool pass)
        {
            fall = new Fall { v = dir * spd + up * upV, rx = 0, t0 = t, pass = pass, spin = (Random.value < .5f ? -1 : 1) * Rnd(7, 11) };
            vDown = false; v.yaw = Mathf.Atan2(dir.x, dir.z) + PI;
        }

        FPose frozen;   // the pose a stiff body (in ice) keeps while it flies
        /// Moves the flying body; on impact: a crater (or wall dust), the body bursts into blocks, and the move's call-out.
        bool FlingUpd(float dt, string big, string sub, string lose, float power, float upk, bool stiff = false, System.Action<Vector3, bool> onHit = null)
        {
            var P = fall; var prevFeet = v.pos;
            P.v.y -= G * dt; P.rx += dt * P.spin; float k = t * 10;
            if (stiff) { if (frozen != null) vP.Copy(frozen); vP.Root(P.rx, v.yaw, 0).Snap(45); }
            else vP.Root(P.rx, v.yaw, Mathf.Sin(P.rx * .7f) * .4f).Ar(-2.8f, 0, .6f + Mathf.Sin(k) * .3f).Al(-2.8f, 0, -.6f - Mathf.Sin(k) * .3f).Ll(.5f).Lr(-.4f).Snap(45);
            if (Random.value < .6f) Emit(v.pos + up, 1, .2f, .25f, .4f, 0, WHITE);
            dur = Mathf.Max(dur, t + .6f);
            // the body (a box round its middle) is swept along this step: glass, crates, bins and drums on the way break and
            // it carries on, the arena's invisible walls let it out, and anything else stops it (web breakProp / the wall
            // hit); landing on top of something counts as landing
            var step = P.v * dt; Vector3? wallAt = null; float topAt = FinGeo.NONE;
            if (FinPlanner.Step(prevFeet + up * SC, step, null, true, a, out var at, out var nrm))
            {
                if (nrm.y > .5f && P.v.y < 0) { v.pos = at - up * SC; topAt = FinGeo.GroundAt(v.pos.x, v.pos.z, v.pos.y + .6f); }
                else { wallAt = at; v.pos = at - up * SC; }
            }
            else v.pos = prevFeet + step;
            // through a tree's leaves: they're knocked off (leaves aren't solid)
            if (FinPlanner.InLeaves(v.pos + up * SC)) { if (Random.value < .7f) Emit(v.pos + up * SC, 3, 3, .14f, .9f, 4, LEAVES, 1); if (!P.leaves) { P.leaves = true; Emit(v.pos + up * SC, 24, 5, .16f, 1.2f, 4, LEAVES, 2); Sfx.Play("whiz", v.pos, .6f, 30); } }
            bool over = FinGeo.OverMap(v.pos.x, v.pos.z);
            float gs = topAt > FinGeo.NONE + 1 ? topAt : over ? FinGeo.SurfaceAt(v.pos.x, v.pos.z, prevFeet.y + .2f) : FinGeo.NONE;
            // deep water (Lagoon): into the lagoon with a splash, and gone (web: M.waterY)
            if (!wallAt.HasValue && MapRules.DeepWater && gs <= FinGeo.NONE + 1)
            {
                float wy = WaterY(v.pos.x, v.pos.z);
                if (v.pos.y + .6f < wy)
                {
                    done = gone = abyss = true; imp = v.pos; var pose = FinisherPose.Of(v); if (pose) pose.Hidden = true;
                    Splash(v.pos.x, v.pos.z, 2.2f); Splash(v.pos.x + .6f, v.pos.z + .4f, 1.4f);
                    FinisherFx.Emit(new Vector3(v.pos.x, wy + .1f, v.pos.z), 30, 6, .22f, .7f, 14, SPLASH, 6);
                    Sfx.Play("splash", v.pos, 1, 30); Shake(.4f);
                    Say(big, sub, lose); dur = Mathf.Max(dur, t + .9f); return true;
                }
            }
            if (!wallAt.HasValue && gs <= FinGeo.NONE + 1 && (v.pos.y < v0.y - 25 || t - P.t0 > 3.5f))
            {
                // off the edge of the world (past Sky High's roof, beyond the scenery): gone (web: the abyss)
                done = gone = abyss = true; imp = v.pos; var pose = FinisherPose.Of(v); if (pose) pose.Hidden = true;
                Shake(.3f); Say(big, sub, lose); dur = Mathf.Max(dur, t + .8f); return true;
            }
            Vector3? hit = null; bool wall = false;
            if (wallAt.HasValue) { hit = wallAt; wall = true; }
            else if ((P.v.y < 0 && v.pos.y <= gs + 1e-3f) || t - P.t0 > 4) hit = new Vector3(v.pos.x, Mathf.Max(gs, v.pos.y), v.pos.z);
            if (!hit.HasValue) return false;
            done = true; var c = hit.Value; float fl = !wall ? gs : FinGeo.GroundAt(c.x, c.z, c.y);
            onHit?.Invoke(c, wall);
            Shake(1.1f); Slow(.5f); Vig(.25f); imp = c;
            if (!wall && MapRules.Wade(c.x, c.z) && fl < .3f)
            {
                Splash(c.x, c.z, 2.4f); Splash(c.x + .5f, c.z - .4f, 1.5f); Sfx.Play("splash", c, .9f, 30);
                FinisherFx.Emit(new Vector3(c.x, WaterY(c.x, c.z) + .1f, c.z), 26, 6, .2f, .6f, 14, SPLASH, 5);
            }
            if (!wall)
            {
                v.pos = new Vector3(v.pos.x, fl, v.pos.z);
                FinisherFx.Decal(c.x, fl, c.z, 2.0f, "#2b2622", "#8a7f72");
                FinisherFx.Spray(new Vector3(c.x, fl + .3f, c.z), 36, RUBBLE4, .12f, .32f, new Vector3(1.2f, .2f, 1.2f), 7, 9, null, fl);
                for (int i = 0; i < 24; i++) { float an = i / 24f * TAU; Emit(new Vector3(c.x + Mathf.Cos(an) * .8f, fl + .2f, c.z + Mathf.Sin(an) * .8f), 2, 8, .4f, .7f, 0, DUST, 1); }
                FinisherFx.Shatter(v, FinisherFx.SO(power, upk, fl, new Vector3(c.x, fl - .5f, c.z)));
            }
            else
            {
                Emit(c, 40, 7, .35f, .7f, 2, WALLDUST);
                FinisherFx.Spray(c, 16, RUBBLE3, .1f, .25f, null, 5, 3, P.v * -.25f, fl);
                FinisherFx.Shatter(v, FinisherFx.SO(power * .8f, 3, fl, c + P.v.normalized * .6f, P.v * -.2f));
            }
            Say(big, sub, lose); dur = Mathf.Max(dur, t + 1.1f);
            return true;
        }

        // ------------------------------------------------------------------ the moves
        Transform ice; FPose iceLast;
        void IceSculpture(float dt)
        {
            if (!ice) { ice = Keep(FinisherProps.IceBlock(1.7f * SC, 3.0f * SC, 1.5f * SC)); ice.position = v0; ice.localScale = new Vector3(1, .01f, 1); }
            vDown = false;
            if (fall == null) v.pos = v0;
            float g = EaseOut(Seg(t, .3f, .85f)); if (!done) ice.localScale = new Vector3(1, Mathf.Max(.01f, g), 1);
            if (t > .3f && t < .9f && Random.value < .8f) Emit(v.pos + new Vector3(Rnd(-.8f, .8f), g * 2.6f * SC, Rnd(-.8f, .8f)), 2, 1.5f, .14f, .4f, -1, ICE);
            if (t > .3f && !done) TintV(FinisherFx.C("#bfe8ff"), Mathf.Min(.45f, t - .3f), .8f);
            if (!done && fall == null)
            {
                // turned away, mid-stride, looking back
                float tr = EaseOut(Seg(t, .05f, .3f)); v.yaw = yaw0 - PI * (1 - tr);
                vP.Ar(-1.3f * tr, 0, .25f * tr).Al(.7f * tr, 0, -.15f * tr).Ll(-.55f * tr).Lr(.45f * tr).Head(-.25f * tr).Root(.12f * tr, v.yaw, 0);
                frozen = (frozen ?? new FPose()).Copy(vP);
            }
            float dist = t < 1.0f ? L(dA, 3.4f, EaseIO(Seg(t, .35f, 1.0f))) : t < 1.5f ? 3.4f : L(3.4f, 1.3f, EaseIn(Seg(t, 1.5f, 1.82f)));
            SetAtt(dist, dt); a.yaw = yaw0;
            if (t < 1.0f) a.vel *= .6f;
            bool kick = t > 1.68f && t < 2.15f;
            aP.Ar(kick ? .5f : -.2f, 0, .2f).Al(kick ? .5f : -.2f, 0, -.2f); if (kick) aP.Lr(-1.55f).Ll(.25f);
            if (t >= 1.82f && fall == null)
            {
                var c = v.pos + up * 1.3f * SC; Emit(c, 24, 6, .16f, .4f, 6, ICE); Shake(.7f); Slow(.3f);
                FinisherFx.Flash(c, "#dff6ff", 1.6f);
                var k = TakeThrow(v.pos, 13); FlingStart(k.dir, k.spd, k.up, true); fall.spin *= .45f;
            }
            if (fall != null && !done)
            {
                FlingUpd(dt, "Shattered!", "Ice sculpture", "You got shattered", 6, 4, true, (c, w) =>
                {
                    FinisherFx.Spray(c + up * .8f, 46, ICEBITS, .12f, .3f, new Vector3(.7f, 1.2f, .6f), 7, 5);
                    Emit(c + up * .8f, 40, 8, .18f, .6f, 6, ICE);
                });
            }
            if (done && ice) ice.gameObject.SetActive(false);
            focus = new Vector3((a.pos.x + v0.x) / 2, v0.y + 1.5f, (a.pos.z + v0.z) / 2); camD = 8.5f;
        }

        FinisherProps.Dog[] dogs; string[] pal; bool thud;
        void DogPile(float dt)
        {
            var C = v0 + fwd * 1.3f * SC;
            if (dogs == null)
            {
                string[] cols = { "#8a5a34", "#2b2d33", "#d9a441", "#d8dbe2" };
                // each dog runs in along a clear line on the same floor (never through a wall, car or fence), and back out the same way
                var spots = DogSpots(v0, fwd, side);
                dogs = new FinisherProps.Dog[4];
                for (int i = 0; i < 4; i++)
                {
                    var d = FinisherProps.DogMesh(cols[i]); obj.Add(d.g.gameObject);
                    (d.T, d.st, d.@out) = spots[i];
                    d.face = Mathf.Atan2(-d.@out.x, -d.@out.z); d.i = i; d.g.position = d.st; d.g.gameObject.SetActive(false); dogs[i] = d;
                }
            }
            // attacker walks up, shoves, backs off and whistles
            float back = t < .9f ? L(dA, 1.2f, EaseIO(Seg(t, .05f, .45f))) : L(1.2f, 3.4f, EaseIO(Seg(t, .9f, 1.3f)));
            SetAtt(back, dt); a.yaw = yaw0; if (t > .9f && t < 1.3f) a.vel *= -.5f;
            bool shove = t > .48f && t < .78f, wh = t > 1.15f && t < 1.65f;
            if (shove) aP.Ar(-1.6f, 0, -.05f).Al(-1.6f, 0, .05f); else if (wh) aP.Ar(-2.5f, 0, -.55f).Al(-.25f, 0, -.1f).Head(-.2f); else aP.Ar(-.25f, 0, .1f).Al(-.25f, 0, -.1f);
            if (wh && Random.value < .35f) Emit(Hand(a, false) + up * .3f, 1, 1, .16f, .6f, -1, WHISTLE, 3);
            if (t < .3f) vDown = false;
            if (t >= .52f && pal == null) pal = FinisherFx.Palette(v);
            if (!done)
            {
                float fallk = EaseIn(Seg(t, .55f, .88f)), j = t > 1.6f ? .04f : 0;
                v.pos = new Vector3(v0.x + Rnd(-j, j), v0.y + .1f * fallk, v0.z + Rnd(-j, j));
                float k = t * 16, kick = t > 1.6f ? Mathf.Sin(k) * .5f : 0;
                vP.Root(-PI / 2 * fallk + (t > 1.6f ? Rnd(-.05f, .05f) : 0), v.yaw, 0);
                if (t < .55f) vP.Ar(-1.2f, 0, .3f).Al(-1.2f, 0, -.3f); else vP.Ar(-.4f + kick, 0, 1.25f).Al(-.4f - kick, 0, -1.25f);
                vP.Ll(t > 1.6f ? kick : .2f).Lr(t > 1.6f ? -kick : -.2f).Head(t > 1.6f ? Mathf.Sin(k * 1.3f) * .3f : 0);
                if (t >= .88f && !thud) { thud = true; Shake(.3f); DustRing(new Vector3(C.x, C.y + .15f, C.z), 14, .8f, 4, .3f, .5f); }
            }
            // the dogs biting (bits of the victim fly off); the dogs themselves are placed every frame in LatePlace
            if (!done && t >= 1.7f)
                foreach (var d in dogs)
                    if (t >= 1.7f + d.i * .08f && Random.value < dt * 7)
                    {
                        var m = d.head.TransformPoint(FinisherProps.W(0, 0, .4f));
                        FinisherFx.Spray(m, 1, new[] { pal[Random.Range(0, pal.Length)] }, .09f, .16f, null, 3, 4, d.@out * 2.5f, v0.y);
                    }
            if (t >= 2.95f && !done)
            {
                done = true;
                FinisherFx.Shatter(v, FinisherFx.SO(4, 5, v0.y, C + new Vector3(0, -.4f, 0)));
                // each dog runs off with a piece in its mouth
                foreach (var d in dogs) { var cube = FinisherProps.Bit(pal[d.i % pal.Length], .2f); cube.SetParent(d.head, false); cube.localPosition = FinisherProps.W(0, -.08f, .45f); }
                Emit(C + up * .4f, 40, 5, .35f, .8f, 0, DUST, 2); Shake(.6f); Slow(.35f);
                Say("Dog pile!", "Dog pile", "The dogs got you");
            }
            focus = new Vector3(C.x - fwd.x * .6f, v0.y + .9f, C.z - fwd.z * .6f); camD = 9.5f; camSide = .85f; camBack = .55f;
        }
        void PlaceDogs(float t)
        {
            if (dogs == null) return;
            foreach (var d in dogs)
            {
                float t0 = 1.15f + d.i * .08f, t1 = 1.7f + d.i * .08f;
                d.g.gameObject.SetActive(t >= t0); if (t < t0) continue;
                if (t < t1 || (done && t >= 3.05f))
                {
                    bool run = t < t1; float u = run ? EaseOut(Seg(t, t0, t1)) : Seg(t, 3.05f, 3.9f);
                    var p = run ? Vector3.Lerp(d.st, d.T, u) : Vector3.Lerp(d.T, d.st, u);
                    d.g.position = new Vector3(p.x, v0.y + Mathf.Abs(Mathf.Sin(t * 18)) * .12f, p.z);
                    d.g.rotation = Quaternion.Euler(0, (run ? d.face : d.face + PI) * Mathf.Rad2Deg + 180, 0);
                    float k = t * 20 + d.i;
                    for (int j = 0; j < 4; j++) d.legs[j].localRotation = FinisherProps.WR(Mathf.Sin(k + (j < 2 ? 0 : PI)) * .8f, 0, 0);
                    d.tail.localRotation = FinisherProps.WR(-.6f + Mathf.Sin(k) * .4f, 0, 0); d.jaw.localRotation = Quaternion.identity; d.head.localRotation = Quaternion.identity; d.body.localRotation = Quaternion.identity;
                    if (run && Random.value < .4f) Emit(new Vector3(p.x, v0.y + .1f, p.z), 1, 1, .22f, .4f, 0, PAW, 1);
                }
                else
                {
                    float k = t * 12 + d.i * 1.7f, pull = Mathf.Sin(k) * .2f;
                    d.g.position = new Vector3(d.T.x + d.@out.x * (pull + .1f), v0.y, d.T.z + d.@out.z * (pull + .1f));
                    d.g.rotation = Quaternion.Euler(0, d.face * Mathf.Rad2Deg + 180, 0);
                    d.body.localRotation = FinisherProps.WR(-.12f + Mathf.Sin(k) * .1f, 0, 0);
                    d.head.localRotation = FinisherProps.WR(0, 0, Mathf.Sin(t * 24 + d.i) * .35f);
                    d.jaw.localRotation = FinisherProps.WR(.25f + Mathf.Sin(t * 30 + d.i) * .25f, 0, 0);
                    for (int j = 0; j < 4; j++) d.legs[j].localRotation = FinisherProps.WR(j < 2 ? -.35f : .45f, 0, 0);
                    d.tail.localRotation = FinisherProps.WR(-.9f + Mathf.Sin(t * 25 + d.i) * .5f, 0, 0);
                }
            }
        }

        float? H, ty; Throw? tyK;
        void SkyDrop(float dt)
        {
            if (t < .3f) vDown = false;
            if (!H.HasValue && t >= .3f) H = SkyH(a.pos);
            if (!ty.HasValue && t >= 1.4f)
            {
                // the planned direction (its flight checked clear from the top), else a fresh one from up here
                float hy = v0.y + (H ?? 0); var ap = new Vector3(a.pos.x, hy, a.pos.z);
                System.Func<Vector3, Vector3> from = d => ap + d * .41f + up * 1.69f;
                Throw k;
                if (plan != null && plan.thr.HasValue && FinPlanner.Clean(from(plan.thr.Value.dir), plan.thr.Value, v0.y)) k = plan.thr.Value;
                else k = FinPlanner.Throws(from, 6, v0.y)[0];
                ty = k.yaw; tyK = k;
            }
            float Hh = H ?? 0, rise = EaseOut(Seg(t, .55f, 1.55f)), dip = Mathf.Sin(Seg(t, .35f, .55f) * PI) * .3f, down = EaseIO(Seg(t, 2.1f, 3.3f));
            SetAtt(L(dA, 1.0f, EaseIO(Seg(t, .05f, .3f))), dt);
            a.pos = new Vector3(a.pos.x, v0.y + Hh * (t < 2.1f ? rise : 1 - down) - dip, a.pos.z); if (t > .55f) a.vel = Vector3.zero;
            a.yaw = !ty.HasValue ? yaw0 : yaw0 + Wrap(ty.Value - yaw0) * EaseIO(Seg(t, 1.45f, 1.75f));
            var fa = Fwd(a.yaw);
            bool flying = (t > .55f && t < 1.6f) || (t > 2.1f && t < 3.3f);
            if (flying && Random.value < .9f)
            {
                Emit(a.pos + up * .1f, 3, 2, .3f, .35f, 2, JETFIRE, -8);
                Emit(a.pos - up * .3f, 1, .5f, .5f, .9f, -.5f, JETSMOKE, -2);
            }
            bool held = t > .3f && t < 1.8f, wind = t > 1.6f && t < 1.8f;
            if (held) aP.Ar(wind ? -2.9f : -1.95f, 0, -.1f).Al(wind ? -2.9f : -1.95f, 0, .1f);
            else if (t < .3f) aP.Ar(-.3f, 0, 0).Al(-.3f, 0, 0);
            else if (t < 2.1f) aP.Ar(-1.2f, 0, -.2f).Al(-1.2f, 0, .2f);
            else aP.Ar(-.6f, 0, 1.2f).Al(-.6f, 0, -1.2f);
            if (flying) aP.Lr(.2f).Ll(-.1f);
            if (held)
            {
                float lift = wind ? .9f * Seg(t, 1.6f, 1.8f) : 0;
                v.pos = a.pos + fa * (.95f - lift * .6f) + up * (.25f + lift * 1.6f); v.yaw = a.yaw + PI;
                float k = t * 9; vP.Ar(-2.6f + Mathf.Sin(k) * .4f, 0, .3f).Al(-2.6f - Mathf.Sin(k) * .4f, 0, -.3f).Ll(Mathf.Sin(k * 1.3f) * .5f).Lr(-Mathf.Sin(k * 1.3f) * .5f);
            }
            else if (t >= 1.8f && !done)
            {
                if (fall == null) FlingStart(Flat(fa).normalized, tyK.HasValue ? tyK.Value.spd : 17, tyK.HasValue ? tyK.Value.up : 6, true);
                FlingUpd(dt, "Sky drop!", "Sky drop", "You got dropped from the sky", 10, 7);
            }
            camD = t < 1.8f ? 11 : 13; camK = t > 1.8f && !imp.HasValue ? 6 : 3.5f;
            focus = t < 1.8f ? a.pos + fa * .5f + up * 1.3f : imp.HasValue && !abyss ? imp.Value + up * 1.2f : new Vector3(v.pos.x, Mathf.Max(v.pos.y, v0.y - 12) + 1.2f, v.pos.z);
        }

        FinisherProps.Potato pot; Vector3 gp; float potSpin;
        void HotPotato(float dt)
        {
            if (pot == null) { pot = FinisherProps.PotatoMesh(); obj.Add(pot.g.gameObject); }
            if (t < .35f) vDown = false;
            if (fall == null)
            {
                float ch = Mathf.Min(2.6f * EaseIn(Seg(t, .95f, 2.5f)), FreeLine(1)); float nx = v0.x - fwd.x * ch, nz = v0.z - fwd.z * ch;
                v.vel = new Vector3((nx - v.pos.x) / Mathf.Max(dt, 1e-3f), 0, (nz - v.pos.z) / Mathf.Max(dt, 1e-3f)); v.pos = new Vector3(nx, v0.y, nz); v.grounded = true;
            }
            if (t < .8f) { SetAtt(L(dA, 1.15f, EaseIO(Seg(t, .2f, .6f))), dt); a.yaw = yaw0; }
            else { float u = EaseIO(Seg(t, .8f, .95f)); a.yaw = yaw0 - PI * u; SetAtt(L(1.15f, 10.5f, Seg(t, .95f, 2.3f)), dt); if (t > 2.3f) a.vel = Vector3.zero; }
            if (t > .3f && t < .78f) aP.Ar(-1.4f, 0, 0); else aP.Ar(-.15f, 0, 0);
            aP.Al(-.15f, 0, 0);
            if (t < .78f) gp = Hand(a, false);
            else if (fall == null)
            {
                float ph = (t - .78f) * 3.4f; int i = Mathf.FloorToInt(ph); float u = ph - i;
                var A = Hand(v, i % 2 == 0); var B = Hand(v, i % 2 != 0); gp = Vector3.Lerp(A, B, u); gp.y += Mathf.Sin(u * PI) * .75f;
                float s = Mathf.Sin(ph * PI * 2);
                vP.Ar(-1.15f + .35f * s, 0, .2f).Al(-1.15f - .35f * s, 0, -.2f).Head(t > 1.0f ? .25f : .45f).Root(0, v.yaw, Mathf.Sin(t * 11) * .05f);
                v.pos += side * Mathf.Sin(t * 7) * .12f;
            }
            if (!done && fall == null)
            {
                potSpin += dt * 5;
                var tip = pot.tip.position;
                Emit(tip, 2, 2, .07f, .25f, 0, FUSE, 2);
                if (Random.value < .3f) Emit(tip + up * .05f, 1, .3f, .12f, .5f, -1, GREY, 1.5f);
            }
            if (t >= 2.55f && fall == null)
            {
                pot.g.gameObject.SetActive(false); var c = gp; FinisherFx.Flash(c, "#ffd27a", 3.4f);
                Emit(c, 60, 11, .35f, .7f, 2, BLAST); Emit(c, 30, 3, .35f, 1.2f, -1.2f, SMOKE3, 3);
                FinisherFx.Decal(v.pos.x, v0.y, v.pos.z, 1.6f, "#1d1b1a"); charK = .85f; Shake(1); Slow(.4f); Vig(.2f);
                var k = TakeThrow(v.pos, 13); FlingStart(k.dir, k.spd, k.up, true);
            }
            if (charK > 0) TintV(FinisherFx.C("#231d1a"), charK);
            if (fall != null && !done) FlingUpd(dt, "Hot potato!", "Hot potato", "You held the hot potato", 10, 6);
            focus = new Vector3(v.pos.x * .65f + a.pos.x * .35f, v0.y + 1.4f, v.pos.z * .65f + a.pos.z * .35f); camD = 10.5f;
        }
        float charK;
        void PlacePotato(float tl)
        {
            if (pot == null || !pot.g.gameObject.activeSelf || done || fall != null) return;
            Vector3 p;
            if (tl < .78f) p = Hand(a, false);
            else { float ph = (tl - .78f) * 3.4f; int i = Mathf.FloorToInt(ph); float u = ph - i; p = Vector3.Lerp(Hand(v, i % 2 == 0), Hand(v, i % 2 != 0), u); p.y += Mathf.Sin(u * PI) * .75f; }
            pot.g.position = p; pot.g.rotation = Quaternion.Euler(0, potSpin * Mathf.Rad2Deg, 0);
            float Lf = Mathf.Max(.02f, .34f * (1 - Seg(tl, .15f, 2.55f)));
            pot.fuse.localScale = new Vector3(1, Lf, 1); pot.tip.localPosition = FinisherProps.W(.04f, pot.top + Lf, 0);
            pot.tip.localScale = Vector3.one * (.8f + Random.value * .6f);
        }

        Transform heart, hole; bool hitH, landed, drip; float beat;
        void Heartbreaker(float dt)
        {
            if (t < .35f) vDown = false;
            float back = t < 1.25f ? L(dA, .95f, EaseIO(Seg(t, .05f, .45f))) : L(.95f, 1.55f, EaseIO(Seg(t, 1.25f, 1.6f)));
            SetAtt(back, dt); a.yaw = yaw0; if (t > 1.6f) a.vel = Vector3.zero;
            // right arm: wind back, punch into the chest, rip out, then hold the heart up to the sky
            float raise = EaseIO(Seg(t, 1.5f, 2.0f));
            if (t < .45f) aP.Ar(-.3f, 0, 0);
            else if (t < .72f) aP.Ar(L(-.3f, .7f, Seg(t, .45f, .72f)), 0, .1f);
            else if (t < 1.25f) aP.Ar(-1.55f, 0, -.12f);
            else if (t < 1.5f) aP.Ar(-1.3f, 0, -.1f);
            else aP.Ar(L(-1.3f, -3.0f, raise), 0, L(-.1f, -.18f, raise));
            if (t > 1.5f) aP.Al(-.35f, 0, .25f); else aP.Al(-.4f, 0, .15f);
            if (t > 1.6f) aP.Head(-.35f * raise);
            if (t >= .78f && !hitH) { hitH = true; Shake(.45f); FinisherFx.Spray(Hand(a, false), 6, BLOOD, .05f, .09f, null, 2, 2, null, v0.y); }
            if (t >= 1.25f && !heart)
            {
                heart = Keep(FinisherProps.HeartMesh()); beat = 0;
                FinisherFx.Spray(Hand(a, false), 8, BLOOD, .05f, .1f, null, 3, 3, null, v0.y); Shake(.35f);
                // a dark hole where the heart was, on the chest facing the attacker
                var pose = FinisherPose.Of(v);
                if (pose)
                {
                    var chest = pose.ChestFront(new Vector3(v.pos.x, v.pos.y + 1.55f * SC, v.pos.z), -fwd);
                    hole = Keep(FinisherProps.Hole()); hole.position = chest; hole.rotation = Quaternion.LookRotation(-fwd); hole.SetParent(pose.Body, true);
                }
                Say("Heartbreaker!", "Heartbreaker", "Your heart got ripped out");
            }
            if (heart)
            {
                bool alive = !landed; float rate = alive ? L(1.9f, .7f, Seg(t, 1.25f, 3.0f)) : 0;
                beat += dt * rate; float b = alive ? Mathf.Pow(Mathf.Max(0, Mathf.Sin(beat * PI * 2)), 6) : 0;
                heart.localScale = Vector3.one * 1.8f * (1 + .28f * b);
                if (alive && b > .9f && !drip) { drip = true; Emit(heart.position, 2, .4f, .07f, .6f, 8, DRIP, -1); } else if (b < .5f) drip = false;
            }
            // victim: stands, jolts when struck, then slumps and falls back to the ground
            if (t >= .35f)
            {
                float slump = EaseIO(Seg(t, 1.5f, 2.5f)), fallk = EaseIn(Seg(t, 2.45f, 3.05f)), jolt = t > .78f && t < 1.3f ? 1 : 0;
                float stg = .95f * EaseOut(Seg(t, 1.3f, 2.1f)), wob = t > 1.3f && t < 2.1f ? Mathf.Sin(t * 16) * .06f : 0;
                v.pos = new Vector3(v0.x + fwd.x * stg + side.x * wob + Rnd(-.03f, .03f) * jolt, v0.y, v0.z + fwd.z * stg + side.z * wob + Rnd(-.03f, .03f) * jolt);
                v.vel = t > 1.3f && t < 2.1f ? fwd * 1.5f : Vector3.zero; v.grounded = true; v.cv = L(.55f * slump, .15f, fallk);
                vP.Root(-PI / 2 * fallk + Rnd(-.04f, .04f) * jolt, v.yaw, Rnd(-.06f, .06f) * jolt);
                if (jolt > 0) vP.Ar(-.9f, 0, .9f).Al(-.9f, 0, -.9f);
                else if (t < 2.1f) vP.Ar(-1.25f, 0, -.35f).Al(-1.25f, 0, .35f);
                else vP.Ar(L(-.6f, -.05f, slump), 0, L(.4f, .15f, slump)).Al(L(-.6f, -.05f, slump), 0, L(-.4f, -.15f, slump));
                vP.Head(jolt > 0 ? -.3f : .55f * slump);
                if (fallk > 0) vP.Ll(.3f).Lr(-.2f);
                if (fallk >= 1 && !landed) { landed = true; Shake(.35f); var C = v.pos + fwd * 1.2f * SC; DustRing(new Vector3(C.x, C.y + .15f, C.z), 14, .7f, 4, .3f, .5f); }
            }
            focus = new Vector3((a.pos.x + v0.x) / 2, v0.y + (t > 1.5f && t < 2.6f ? 2.3f : 1.7f), (a.pos.z + v0.z) / 2); camD = 5.6f; camSide = .8f; camBack = -.6f;
        }

        Transform[] gloves; bool ko; Vector3 T; float fy, ph, jolt, gloveTop = 10; int lastI = -1; bool[] caught = new bool[2];
        void Knockout(float dt)
        {
            var C = v0 + fwd * 1.15f * SC;
            if (!ko)
            {
                ko = true; v.yaw = yaw0; T = C + side * 1.15f; fy = Mathf.Atan2(-side.x, -side.z); gloveTop = GloveTop(T);   // the gloves drop from under any roof
                gloves = new[] { Keep(FinisherProps.GloveMesh()), Keep(FinisherProps.GloveMesh()) };
                foreach (var g in gloves) g.gameObject.SetActive(false);
                pal = FinisherFx.Palette(v);
            }
            if (fall == null) { vDown = true; v.pos = v0; v.yaw = yaw0; }
            // slow walk round to the side of the body
            float u = EaseIO(Seg(t, 0, 1.0f)); float nx = L(a0.x, T.x, u), nz = L(a0.z, T.z, u);
            a.vel = new Vector3((nx - a.pos.x) / Mathf.Max(dt, 1e-3f) * .6f, 0, (nz - a.pos.z) / Mathf.Max(dt, 1e-3f) * .6f); a.pos = new Vector3(nx, v0.y, nz); a.grounded = true;
            if (t < 1.0f) { float dx = T.x - a0.x, dz = T.z - a0.z; a.yaw = Mathf.Sqrt(dx * dx + dz * dz) > .3f ? Mathf.Atan2(dx, dz) : fy; if (t > .75f) a.yaw = fy; }
            else { a.yaw = fy; a.vel = Vector3.zero; }
            bool look = t > 1.0f && t < 1.6f;
            for (int i = 0; i < 2; i++)
                if (t >= 1.55f && !caught[i]) { caught[i] = true; Shake(.2f); Emit(Hand(a, i == 1), 8, 3, .12f, .3f, 12, SPARK); }
            // pummel: alternate fists into the body on the floor, getting faster, then a double-fist smash
            Vector3 ar = new Vector3(-.25f, 0, .1f), al = new Vector3(-.25f, 0, -.1f); float? head = null;
            if (look) { ar = new Vector3(-2.6f, 0, -.25f); al = new Vector3(-2.6f, 0, .25f); head = -.6f; }
            else if (t >= 1.6f && t < 1.75f) { ar = new Vector3(-1.6f, 0, -.2f); al = new Vector3(-1.6f, 0, .2f); }
            else if (t >= 1.75f && t < 3.0f)
            {
                ph += dt * L(4, 8, Seg(t, 1.75f, 3.0f)); int i = Mathf.FloorToInt(ph); float f = ph - i;
                float sw = f < .55f ? L(-2.6f, -.4f, EaseIn(f / .55f)) : L(-.4f, -2.6f, (f - .55f) / .45f);
                if (i % 2 == 1) { al = new Vector3(sw, 0, .15f); ar = new Vector3(-2.3f, 0, -.15f); } else { ar = new Vector3(sw, 0, -.15f); al = new Vector3(-2.3f, 0, .15f); }
                head = .35f;
                if (i != lastI && f >= .55f)
                {
                    lastI = i; var hit = C + new Vector3(Rnd(-.25f, .25f), .35f, Rnd(-.25f, .25f)); Shake(.18f);
                    Emit(hit, 6, 4, .16f, .3f, 12, HIT, 2);
                    FinisherFx.Spray(hit, 2, new[] { pal[Random.Range(0, pal.Length)] }, .07f, .13f, null, 3, 4, null, v0.y); jolt = 1;
                }
            }
            else if (t >= 3.0f && t < 3.25f) { float k = Seg(t, 3.0f, 3.25f); ar = new Vector3(k < .6f ? -2.9f : L(-2.9f, -.4f, (k - .6f) / .4f), 0, 0); al = new Vector3(ar.x, 0, -ar.z); head = .3f; }
            else if (t >= 3.25f) { float k = EaseIO(Seg(t, 3.45f, 3.8f)); ar = new Vector3(L(-.7f, -2.85f, k), 0, L(0, -.35f, k)); al = new Vector3(L(-.7f, -2.85f, k), 0, L(0, .35f, k)); head = -.25f * k; }
            a.cv = t >= 1.7f && t < 3.45f ? .5f : Mathf.Max(0, a.cv - dt * 4);
            float lean = t >= 1.7f && t < 3.4f ? .42f : 0;
            aP.Ar(ar).Al(al); if (head.HasValue) aP.Head(head.Value); if (lean > 0) aP.Root(lean, a.yaw, 0);
            if (!done && fall == null) { jolt = Mathf.Max(0, jolt - dt * 8); vP.Root(1.3f + Rnd(-.06f, .06f) * jolt, yaw0, Rnd(-.08f, .08f) * jolt); }
            if (t >= 3.25f && fall == null)
            {
                Shake(.9f); Vig(.2f); FinisherFx.Decal(C.x, v0.y, C.z, 1.3f, "#2b2622", "#8a7f72");
                for (int i = 0; i < 20; i++) { float an = i / 20f * TAU; Emit(new Vector3(C.x + Mathf.Cos(an) * .6f, v0.y + .2f, C.z + Mathf.Sin(an) * .6f), 2, 7, .35f, .6f, 0, DUST, 1); }
                var k = TakeThrow(v.pos, 15); FlingStart(k.dir, k.spd, k.up, true);
            }
            if (fall != null && !done) FlingUpd(dt, "Knockout!", "Knockout", "You got knocked out", 8, 7);
            focus = new Vector3((a.pos.x + C.x) / 2, v0.y + (look ? 2.6f : 1.2f), (a.pos.z + C.z) / 2); camD = 6.5f; camSide = .9f; camBack = .35f;
        }
        void PlaceGloves(float tl)
        {
            if (gloves == null) return;
            for (int i = 0; i < 2; i++)
            {
                var g = gloves[i]; if (tl < 1.0f) { g.gameObject.SetActive(false); continue; }
                g.gameObject.SetActive(true); var hand = Hand(a, i == 1);
                if (tl < 1.55f)
                {
                    // the gloves drop out of the sky, spinning, into his hands
                    float k = EaseIn(Seg(tl, 1.0f, 1.55f)); var top = T + new Vector3(i == 1 ? -.5f : .5f, gloveTop, 0);
                    g.position = Vector3.Lerp(top, hand, k); g.rotation = Quaternion.Euler((tl * 14 + i) * Mathf.Rad2Deg, tl * 9 * Mathf.Rad2Deg, tl * 6 * Mathf.Rad2Deg);
                }
                else { g.rotation = FinisherPose.ArmRot(a, i == 1); g.position = hand; }
            }
        }

        float? fd; float fL; Vector3 fs, ash;
        void Roast(float dt)
        {
            if (t < .35f) vDown = false;
            SetAtt(L(dA, 2.3f, EaseIO(Seg(t, .05f, .35f))), dt); a.yaw = yaw0; if (t > .4f) a.vel = Vector3.zero;
            bool blast = t > .4f && t < .85f;
            if (blast) aP.Ar(-1.55f, 0, -.1f); else if (t > 1.0f) aP.Ar(-1.25f, 0, .6f); else aP.Ar(-.3f, 0, 0);
            if (t > 1.0f) aP.Al(-1.25f, 0, -.6f); else aP.Al(-.3f, 0, 0);
            if (blast)
            {
                var h = Hand(a, false); var tg = v.pos + up * 1.3f * SC;
                for (int i = 0; i < 4; i++) { float u = Random.value; Emit(Vector3.Lerp(h, tg, u), 1, 1.2f, .12f + .22f * (1 - u * .5f), .3f, -2, FIRE); }
                if (Random.value < .5f) FinisherFx.Glow(Vector3.Lerp(h, tg, .5f), "#ff7a1f", 3, 6, .15f);
            }
            if (!done)
            {
                if (t >= .6f)
                {
                    TintV(FinisherFx.C("#231d1a"), .92f * Seg(t, .6f, 1.6f));
                    var hp = v.pos + up * 2.1f * SC;
                    for (int i = 0; i < 2; i++) Emit(hp + new Vector3(Rnd(-.25f, .25f), Rnd(0, .3f), Rnd(-.25f, .25f)), 1, .6f, .3f, .45f, -3, FIRE, 3);
                    if (Random.value < .6f) Emit(v.pos + new Vector3(Rnd(-.4f, .4f), Rnd(.2f, 1.8f) * SC, Rnd(-.4f, .4f)), 1, .4f, .2f, .4f, -2, EMBER, 2);
                    if (Random.value < .25f) Emit(hp + up * .5f, 1, .3f, .35f, 1, -1, SMOKE, 1.5f);
                    if (Random.value < .08f) FinisherFx.Glow(hp, "#ff7a1f", 2.5f, 5, .25f);
                }
                if (t < .75f) { if (t > .45f) vP.Ar(-1.4f, 0, .4f).Al(-1.4f, 0, -.4f).Head(-.2f); }
                else if (t < 1.35f)
                {
                    // two panicked running steps away from the fire, picking the clearest way out
                    if (!fd.HasValue) { var (ry, rl) = RoastRun(v.pos, fwd); fd = ry; fL = rl; fs = v.pos; }
                    float u = Seg(t, .75f, 1.35f), d = fL * u, zig = Mathf.Sin(u * 6) * .25f * Mathf.Min(1, u * 3);
                    var dir = Fwd(fd.Value); var perp = new Vector3(dir.z, 0, -dir.x);
                    float nx = fs.x + dir.x * d + perp.x * zig, nz = fs.z + dir.z * d + perp.z * zig, ny = FinGeo.GroundAt(nx, nz, v.pos.y + .6f);
                    v.vel = new Vector3((nx - v.pos.x) / Mathf.Max(dt, 1e-3f), 0, (nz - v.pos.z) / Mathf.Max(dt, 1e-3f));
                    v.pos = new Vector3(nx, Mathf.Max(ny, v0.y - 3), nz); v.grounded = true; v.yaw = Mathf.Atan2(v.vel.x, v.vel.z);
                    float k = t * 14; vP.Ar(-2.8f + Mathf.Sin(k) * .5f, 0, .4f).Al(-2.8f - Mathf.Sin(k) * .5f, 0, -.4f).Head(Mathf.Sin(k * .7f) * .2f);
                }
                else
                {
                    v.vel = Vector3.zero; float u = EaseIn(Seg(t, 1.4f, 1.65f)), w = t < 1.4f ? Mathf.Sin(t * 30) * .12f : 0;
                    vP.Root(1.45f * u, v.yaw, w).Ar(-.3f, 0, .3f).Al(-.3f, 0, -.3f).Head(.4f);
                    if (t >= 1.7f)
                    {
                        done = gone = true; ash = v.pos + Fwd(v.yaw) * 1.1f * SC;
                        FinisherFx.Shatter(v, FinisherFx.SO(1.3f, 1.2f, v.pos.y, v.pos + up * 3, null, .15f)); Shake(.4f);
                        Emit(new Vector3(ash.x, v.pos.y + .3f, ash.z), 30, 2, .35f, 1.2f, -1, ASH3, 2);
                        Say("Roasted!", "Roast", "You got roasted");
                    }
                }
            }
            else if (Random.value < .5f) Emit(new Vector3(ash.x + Rnd(-.6f, .6f), v.pos.y + .2f, ash.z + Rnd(-.6f, .6f)), 1, .3f, .3f, 1, -1, ASH, 1.2f);
            focus = new Vector3((a.pos.x + v.pos.x) / 2, v0.y + 1.3f, (a.pos.z + v.pos.z) / 2); camD = 8.5f + Flat(v.pos - v0).magnitude * .6f;
        }

        bool[] hp3 = new bool[3]; float dep;
        void Piledriver(float dt)
        {
            if (t < .35f) vDown = false;
            SetAtt(L(dA, 1.3f, EaseIO(Seg(t, .05f, .4f))), dt); a.yaw = yaw0; if (t > .4f) a.vel = Vector3.zero;
            float[] hits = { 1.0f, 1.55f, 2.1f }, deps = { .55f, 1.05f, 1.5f }; int n = 0; foreach (var h in hits) if (t >= h) n++;
            float armx = -.4f;
            foreach (var h in hits) if (t > h - .42f && t < h + .1f) armx = t < h - .1f ? L(-1.1f, -3.0f, EaseOut(Seg(t, h - .42f, h - .1f))) : L(-3.0f, -1.15f, Seg(t, h - .1f, h));
            bool tap = t > 2.65f && t < 2.95f;
            if (tap) aP.Ar(-1.4f + Mathf.Sin(Seg(t, 2.65f, 2.95f) * PI) * .4f, 0, -.15f); else aP.Ar(armx, 0, -.2f);
            if (t > 2.3f) aP.Al(-.3f, 0, .1f); else aP.Al(armx, 0, .2f);
            aP.Head(.3f);
            for (int i = 0; i < 3; i++)
                if (t >= hits[i] && !hp3[i])
                {
                    hp3[i] = true; Shake(.5f + i * .12f); if (i == 0) FinisherFx.Decal(v0.x, v0.y, v0.z, 1.1f, "#3a3530", "#6e665c");
                    DustRing(new Vector3(v0.x, v0.y + .15f, v0.z), 16, .5f, 5, .3f, .5f);
                    FinisherFx.Spray(v0 + up * .2f, 8, RUBBLE3, .08f, .18f, new Vector3(.4f, .05f, .4f), 4, 5, null, v0.y);
                }
            dep = Damp(dep, n > 0 ? deps[n - 1] * SC : 0, 22, dt);
            if (!done) { v.pos = v0 - up * dep; vP.Ar(-.15f, 0, .12f).Al(-.15f, 0, -.12f).Head(t > 2.2f ? Mathf.Sin(t * 5) * .3f : 0); }
            if (t >= 2.9f && !done)
            {
                done = gone = true; FinisherFx.Shatter(v, FinisherFx.SO(7, 6, v0.y, v0 - up * .6f)); Shake(.6f);
                Say("Piledriven!", "Piledriver", "You got piledriven");
            }
            focus = new Vector3((a.pos.x + v0.x) / 2, v0.y + 1.3f, (a.pos.z + v0.z) / 2); camD = 7; camBack = -.3f;
        }

        Transform[] pads; FinisherProps.Monitor mon; bool cl; bool[] sh3 = new bool[3]; float bump, hb;
        void Clear(float dt)
        {
            var C = v0 + fwd * 1.15f * SC;
            if (!cl)
            {
                cl = true; v.yaw = yaw0; T = C + side * 1.05f; fy = Mathf.Atan2(-side.x, -side.z);
                pads = new[] { Keep(FinisherProps.PaddleMesh()), Keep(FinisherProps.PaddleMesh()) }; foreach (var p in pads) p.gameObject.SetActive(false);
                mon = FinisherProps.MonitorMesh(); obj.Add(mon.g.gameObject); mon.g.position = C + fwd * 1.6f + side * .7f; mon.g.localScale = Vector3.one * .01f;
            }
            vDown = true; v.yaw = yaw0;
            float u = EaseIO(Seg(t, 0, .9f)); float nx = L(a0.x, T.x, u), nz = L(a0.z, T.z, u);
            a.vel = new Vector3((nx - a.pos.x) / Mathf.Max(dt, 1e-3f) * .6f, 0, (nz - a.pos.z) / Mathf.Max(dt, 1e-3f) * .6f); a.pos = new Vector3(nx, v0.y, nz); a.grounded = true;
            if (t < .9f) { float dx = T.x - a0.x, dz = T.z - a0.z; a.yaw = Mathf.Sqrt(dx * dx + dz * dz) > .3f && t < .7f ? Mathf.Atan2(dx, dz) : fy; }
            else { a.yaw = fy; a.vel = Vector3.zero; }
            mon.g.localScale = Vector3.one * Mathf.Max(.01f, EaseOut(Seg(t, .3f, .6f)));
            float[] shocks = { 1.6f, 2.25f, 2.9f }; var ar = new Vector3(-.3f, 0, .1f); var al = new Vector3(-.3f, 0, -.1f); float lean = 0;
            if (t >= .9f && t < 1.35f)
            {
                float k = Mathf.Sin(t * 30) * .15f; ar = new Vector3(-1.35f, 0, .45f + k); al = new Vector3(-1.35f, 0, -.45f - k);
                if (Random.value < .7f) Emit(Vector3.Lerp(Hand(a, false), Hand(a, true), .5f), 2, 3, .08f, .25f, 0, ELEC);
            }
            foreach (var s in shocks) if (t > s - .45f && t < s + .25f) { lean = .35f; float w = t < s ? L(-2.4f, -.55f, EaseIn(Seg(t, s - .3f, s))) : -.55f; ar = new Vector3(w, 0, -.1f); al = new Vector3(w, 0, .1f); }
            a.cv = t > .95f && t < 3.4f ? .5f : Mathf.Max(0, a.cv - dt * 4);
            aP.Ar(ar).Al(al); if (lean > 0) aP.Root(lean, a.yaw, 0); if (t > 1.3f && t < 3.2f) aP.Head(.3f);
            var hands = new[] { Hand(a, false), Hand(a, true) };
            for (int i = 0; i < 3; i++)
                if (t >= shocks[i] && !sh3[i])
                {
                    sh3[i] = true; bump = .18f + i * .1f; Shake(.3f + i * .15f); FinisherFx.Flash(C + up * .5f, "#bfe8ff", 1.2f + i * .5f);
                    for (int k = 0; k < 4; k++) FinisherFx.Bolt(hands[k % 2], C + new Vector3(Rnd(-.5f, .5f), .3f, Rnd(-.5f, .5f)), "#bfe8ff", .06f, 3, .3f);
                    Emit(C + up * .4f, 20, 5, .12f, .3f, 0, ELEC);
                    if (i < 2) FinisherFx.Spray(C + up * .4f, 3, FinisherFx.Palette(v), .07f, .12f, null, 3, 4, null, v0.y);
                }
            bump = Mathf.Max(0, bump - dt * 1.6f);
            if (!done) { v.pos = new Vector3(v0.x, v0.y + Mathf.Sin(Mathf.Min(1, bump / .18f) * PI * .5f) * bump, v0.z); vP.Root(1.3f, yaw0, bump > 0 ? Rnd(-.1f, .1f) : 0); }
            bool flat = t >= 2.95f; hb += dt * (flat ? 0 : 2.2f); float phh = hb % 1, sp = flat ? 0 : (phh < .08f ? phh / .08f : phh < .14f ? 1 - (phh - .08f) / .03f * 2 : 0);
            FinisherProps.DrawMonitor(mon, flat ? 0 : Mathf.Max(-.5f, sp), flat);
            if (t >= 3.3f && !done)
            {
                done = gone = true; FinisherFx.Shatter(v, FinisherFx.SO(8, 6, v0.y, C - up * .2f)); Shake(.6f);
                Say("Clear!", "Clear!", "You flatlined");
            }
            focus = new Vector3((a.pos.x + C.x) / 2, v0.y + 1.1f, (a.pos.z + C.z) / 2); camD = 6.8f; camSide = .9f; camBack = .35f;
        }
        void PlacePads()
        {
            if (pads != null) for (int i = 0; i < 2; i++) { pads[i].gameObject.SetActive(t >= .85f && !done); pads[i].rotation = FinisherPose.ArmRot(a, i == 1); pads[i].position = Hand(a, i == 1); }
            if (mon != null) { var cam = Camera.main; if (cam) { var d = cam.transform.position - mon.g.position; mon.g.rotation = Quaternion.Euler(0, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 180, 0); } }
        }

        FinisherProps.Skel skel;
        void Overcharge(float dt)
        {
            if (skel == null) { skel = FinisherProps.SkelMesh(); obj.Add(skel.g.gameObject); }
            if (t < .4f) vDown = false;
            SetAtt(L(dA, 1.35f, EaseIO(Seg(t, .05f, .45f))), dt); a.yaw = yaw0;
            bool grab = t > .42f && !done; aP.Ar(grab ? -1.5f : -.3f, 0, grab ? -.15f : 0).Al(grab ? -1.5f : -.3f, 0, grab ? .15f : 0);
            var pose = FinisherPose.Of(v);
            if (!done)
            {
                float lift = EaseOut(Seg(t, .5f, .8f)) * .15f, j = t > .6f ? .06f : 0;
                v.pos = v0 + new Vector3(Rnd(-j, j), lift + Rnd(-j, j), Rnd(-j, j));
                float fl = t > .55f ? 1 : 0;
                var ar = new Vector3(-1.5f + Rnd(-.5f, .5f) * fl, 0, 1.25f * fl + Rnd(-.3f, .3f) * fl); var al = new Vector3(-1.5f + Rnd(-.5f, .5f) * fl, 0, -1.25f * fl + Rnd(-.3f, .3f) * fl);
                float ll = Rnd(-.4f, .4f) * fl, lr = Rnd(-.4f, .4f) * fl, head = Rnd(-.3f, .3f) * fl;
                vP.Root(Rnd(-.07f, .07f) * fl, v.yaw, Rnd(-.1f, .1f) * fl).Ar(ar).Al(al).Ll(ll).Lr(lr).Head(head).Snap(40);
                v.cv = .55f * Seg(t, 1.3f, 1.9f);
                if (t > .5f)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        var h = Hand(a, k == 0); var p = v.pos + new Vector3(Rnd(-.5f, .5f) * SC, Rnd(.4f, 2.5f) * SC, Rnd(-.4f, .4f) * SC);
                        FinisherFx.Bolt(h, p, Random.value < .5f ? "#fff27a" : "#7ff3ff", .09f, 3, .25f);
                    }
                    if (Random.value < .7f) Emit(v.pos + new Vector3(Rnd(-.5f, .5f), Rnd(.4f, 2.4f), Rnd(-.5f, .5f)), 2, 4, .1f, .25f, 0, BOLT);
                    if (Random.value < .25f) FinisherFx.Glow(v.pos + up * 1.4f, Random.value < .5f ? "#fff27a" : "#7ff3ff", 4, 7, .1f);
                }
                bool xr = t > .7f && t < 1.95f && Mathf.FloorToInt(t * (t < 1.3f ? 7 : 15)) % 2 == 0;
                if (pose) pose.Xray = xr;
                skel.g.gameObject.SetActive(xr);
                if (xr)
                {
                    skel.g.position = v.VisualPos; skel.g.rotation = Quaternion.Euler(-vP.rx * Mathf.Rad2Deg, v.yaw * Mathf.Rad2Deg + 180, vP.rz * Mathf.Rad2Deg); skel.g.localScale = Vector3.one * SC;
                    skel.arms[0].localRotation = FinisherProps.WR(al.x, al.y, al.z); skel.arms[1].localRotation = FinisherProps.WR(ar.x, ar.y, ar.z);
                    skel.legs[0].localRotation = FinisherProps.WR(ll, 0, 0); skel.legs[1].localRotation = FinisherProps.WR(lr, 0, 0); skel.hd.localRotation = FinisherProps.WR(head, 0, 0);
                }
            }
            if (t >= 1.95f && !done)
            {
                done = gone = true; skel.g.gameObject.SetActive(false); if (pose) pose.Xray = false;
                FinisherFx.Shatter(v, FinisherFx.SO(8, 5));
                Emit(v.pos + up * 1.2f, 50, 3, .45f, 1.3f, -1.5f, SMOKE3, 2); Emit(v.pos + up * 1.2f, 30, 9, .14f, .4f, 0, BOLT);
                FinisherFx.Flash(v.pos + up * 1.2f, "#bff6ff", 2.2f);
                Shake(.6f); Slow(.4f); Vig(.15f); Say("Overcharged!", "Overcharge", "You got overcharged");
            }
            focus = new Vector3((a.pos.x + v0.x) / 2, v0.y + 1.6f, (a.pos.z + v0.z) / 2); camD = 7.5f;
        }

        Transform gun, gunTip, spear; LineRenderer rope; bool stuck; Vector3 tipW;
        void Combat(float dt)
        {
            if (!gun)
            {
                gun = Keep(FinisherProps.HarpoonMesh(out gunTip)); gun.gameObject.SetActive(false);
                spear = Keep(FinisherProps.SpearMesh()); spear.gameObject.SetActive(false);
                rope = new GameObject("rope").AddComponent<LineRenderer>(); obj.Add(rope.gameObject);
                rope.sharedMaterial = FinisherFx.UnlitMat(FinisherFx.C("#e9e2c8")); rope.startWidth = rope.endWidth = .02f; rope.positionCount = 2; rope.enabled = false;
            }
            if (t < .35f) vDown = false;
            // step back to harpoon range, then plant the feet
            SetAtt(t < .5f ? L(dA, 4.6f, EaseIO(Seg(t, .05f, .45f))) : 4.6f, dt); if (t >= .5f) a.vel = Vector3.zero; a.yaw = yaw0;
            bool aim = t > .3f && t < 1.5f, wind = t >= 1.5f && t < 1.8f, cut = t >= 1.8f;
            if (cut) aP.Ar(L(.5f, -3.0f, EaseOut(Seg(t, 1.8f, 1.95f))), 0, -.1f).Head(-.3f); else if (wind) aP.Ar(.5f, 0, -.25f); else if (aim) aP.Ar(-1.5f, 0, -.1f); else aP.Ar(-.3f, 0, 0);
            if (aim) aP.Al(-1.4f, 0, .35f); else aP.Al(-.3f, 0, .1f);
            a.cv = wind ? .45f : Mathf.Max(0, a.cv - dt * 4);
            // the victim staggers, gets harpooned, and is reeled in to just in front of the attacker
            var near = new Vector3(a.pos.x + fwd.x * 1.15f, v0.y, a.pos.z + fwd.z * 1.15f); float reel = EaseIn(Seg(t, .9f, 1.45f));
            if (fall == null)
            {
                var p = Vector3.Lerp(v0, near, reel); v.pos = new Vector3(p.x, v0.y + Mathf.Sin(reel * PI) * .25f, p.z); v.vel = Vector3.zero; v.grounded = true; v.yaw = yaw0 + PI;
                float k = t * 12;
                if (t < .85f) vP.Ar(-.5f, 0, .3f).Al(-.5f, 0, -.3f).Head(.3f).Root(-.08f, v.yaw, 0);
                else vP.Ar(-1.4f + Mathf.Sin(k) * .4f, 0, .5f).Al(-1.4f - Mathf.Sin(k) * .4f, 0, -.5f).Ll(-.5f * reel).Lr(-.3f * reel).Head(-.3f).Root(-.3f * (1 - reel * .5f), v.yaw, 0);
            }
            var chest = v.pos + up * 1.35f * SC;
            if (t >= .7f && t < 1.5f && Seg(t, .7f, .82f) >= 1 && !stuck) { stuck = true; Shake(.3f); FinisherFx.Spray(chest, 6, FinisherFx.Palette(v), .05f, .1f, null, 2, 2, null, v0.y); }
            // the uppercut
            if (t >= 1.8f && fall == null)
            {
                FinisherFx.Flash(chest, "#ffe08a", 1.4f); Emit(chest, 30, 9, .16f, .35f, 0, SPARK); Shake(1); Slow(.35f); Vig(.2f);
                var k = TakeThrow(v.pos, 18); FlingStart(k.dir, k.spd, k.up, true);
            }
            if (fall != null && !done) FlingUpd(dt, "Combat!", "Combat", "You got harpooned and uppercut", 9, 6);
            focus = new Vector3((a.pos.x + v.pos.x) / 2, v0.y + 1.4f, (a.pos.z + v.pos.z) / 2); camD = 8;
        }
        void PlaceHarpoon(float tl)
        {
            if (!gun) return;
            bool aim = tl > .3f && tl < 1.5f; gun.gameObject.SetActive(aim);
            if (aim) { gun.rotation = FinisherPose.ArmRot(a, false); gun.position = Hand(a, false); }
            tipW = gunTip.position;
            var chest = v.VisualPos + up * 1.35f * SC;
            bool flying = tl >= .7f && tl < 1.5f;
            spear.gameObject.SetActive(flying); rope.enabled = flying;
            if (flying)
            {
                float u = Seg(tl, .7f, .82f); var p = Vector3.Lerp(tipW, chest, u);
                spear.position = p; var d = chest - tipW; if (d.sqrMagnitude > 1e-6f) spear.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
                rope.SetPosition(0, tipW); rope.SetPosition(1, p);
            }
        }

        bool h1, h2; Vector3 flat; float flatT;
        void Pancake(float dt)
        {
            const float ANG = 2.1f; float hS = 1.92f * SC, armL = 1.0f * SC;
            SetAtt(L(dA, 1.0f, EaseIO(Seg(t, .05f, .4f))), dt); a.yaw = yaw0; if (t > .4f) a.vel = Vector3.zero;
            float th = 0, phk = 0;
            if (t < .4f) phk = 1.95f; else if (t < .85f) phk = L(1.95f, 0, EaseIO(Seg(t, .4f, .85f)));
            else if (t < 1.15f) th = -ANG * EaseIn(Seg(t, .85f, 1.15f)); else if (t < 1.25f) th = -ANG;
            else if (t < 1.75f) th = L(-ANG, ANG, EaseIO(Seg(t, 1.25f, 1.75f))); else if (t < 1.85f) th = ANG;
            else if (t < 2.15f) th = L(ANG, 0, EaseOut(Seg(t, 1.85f, 2.15f))); else if (t < 2.35f) phk = L(0, -.5f, EaseOut(Seg(t, 2.15f, 2.35f))); else phk = L(-.5f, ANG, EaseIn(Seg(t, 2.35f, 2.55f)));
            var dl = new Vector3(-Mathf.Sin(th), Mathf.Cos(th) * Mathf.Cos(phk), Mathf.Cos(th) * Mathf.Sin(phk));
            var dir = side * dl.x + up * dl.y + fwd * dl.z;
            var S0 = a.pos + up * hS;
            if (t >= .4f && !done)
            {
                vDown = false; v.yaw = yaw0; v.pos = S0 + dir * armL; float k = t * 14;
                vP.Root(phk, yaw0, th).Ar(-2.9f + Mathf.Sin(k) * .35f, 0, .35f).Al(-2.9f - Mathf.Sin(k) * .35f, 0, -.35f).Ll(.1f).Lr(-.1f).Head(Mathf.Sin(k * .7f) * .3f).Snap(36);
            }
            if (t >= 2.55f)
            {
                float u = EaseIO(Seg(t, 2.6f, 2.95f)), c = t > 3.0f && t < 3.35f ? Mathf.Abs(Mathf.Sin((t - 3.0f) * 28)) * .5f : 0;
                aP.Ar(L(ANG - PI, -.9f - c, u), 0, L(0, -.35f, u)).Al(L(ANG - PI, -.9f - c, u), 0, L(0, .35f, u)).Lr(L(-.25f, 0, u)).Ll(L(.25f, 0, u));
            }
            else if (t >= .4f) aP.Ar(phk - PI, 0, -th).Al(phk - PI, 0, -th).Lr(-.25f).Ll(.25f).Snap(36);
            else aP.Ar(-1.2f, 0, 0).Al(-1.2f, 0, 0);
            if (t >= 1.15f && !h1) { h1 = true; Slam(S0, dir, armL); }
            if (t >= 1.75f && !h2) { h2 = true; Slam(S0, dir, armL); }
            if (t >= 2.55f && !done)
            {
                done = true; flatT = t; var feet = new Vector3(a.pos.x, v0.y, a.pos.z) + fwd * 1.75f; flat = feet + up * .06f; var c = flat + fwd * 1.55f;
                FinisherFx.Decal(c.x, v0.y, c.z, 1.8f, "#2b2622", "#8a7f72");
                FinisherFx.Spray(c + up * .3f, 44, RUBBLE4, .12f, .34f, new Vector3(1.3f, .2f, 1.3f), 7, 9, null, v0.y);
                for (int i = 0; i < 24; i++) { float an = i / 24f * TAU; Emit(new Vector3(c.x + Mathf.Cos(an) * .9f, v0.y + .2f, c.z + Mathf.Sin(an) * .9f), 2, 9, .4f, .7f, 0, DUST, 1); }
                Shake(1.2f); Slow(.5f); Vig(.25f); Say("Pancake!", "Pancake", "You got pancaked");
            }
            if (done)
            {
                v.pos = flat; float e = t - flatT;
                vP.Scale(new Vector3(SC * 1.3f, SC * 1.3f, SC * .16f * (1 + Mathf.Sin(e * 30) * .45f * Mathf.Exp(-e * 6)))).Root(PI / 2, yaw0, 0).Ar(-2.9f, 0, .5f).Al(-2.9f, 0, -.5f).Ll(0).Lr(0).Head(0).Snap(60);
            }
            focus = new Vector3(a.pos.x + fwd.x * 1.2f, v0.y + 2.0f, a.pos.z + fwd.z * 1.2f); camD = 10.5f; camSide = .65f; camBack = .75f;
        }
        void Slam(Vector3 S0, Vector3 dir, float armL)
        {
            var g = S0 + dir * (armL + 2.4f * SC); g.y = v0.y; Shake(.5f);
            FinisherFx.Spray(g + up * .2f, 12, PANRUB, .08f, .2f, new Vector3(.5f, .1f, .5f), 4, 6, null, v0.y);
            DustRing(new Vector3(g.x, g.y + .15f, g.z), 12, .4f, 5, .35f, .5f);
        }

        // ------------------------------------------------------------------ sounds (web FIN_SFX: time into the move, sound, options)
        struct Snd { public float t; public bool imp; public string n; public float vol, rate, every, until; }
        static Snd S(float t, string n, float rate = 1, float vol = 1, float every = 0, float until = 0) => new Snd { t = t, n = n, rate = rate, vol = vol, every = every, until = until };
        static Snd I(string n, float rate = 1, float vol = 1) => new Snd { imp = true, n = n, rate = rate, vol = vol };
        static readonly Dictionary<string, Snd[]> FIN_SFX = new Dictionary<string, Snd[]> {
            { "Roast", new[] { S(.4f, "fireball", .8f), S(.62f, "fireball", 1.25f, .7f), S(1.7f, "vanish", .8f) } },
            { "Piledriver", new[] { S(1.0f, "smash", 1.1f, .8f), S(1.55f, "smash", 1, .9f), S(2.1f, "smash", .9f), S(2.9f, "brick") } },
            { "Clear!", new[] { S(.9f, "volt", 1.4f, .5f), S(1.6f, "volt"), S(2.25f, "volt", .95f), S(2.9f, "volt", .9f), S(3.3f, "smash") } },
            { "Overcharge", new[] { S(.5f, "volt"), S(.95f, "volt", 1.15f), S(1.4f, "volt", .9f), S(1.95f, "explode", 1.3f, .6f) } },
            { "Hot potato", new[] { S(.3f, "throw"), S(.4f, "tick", 1, .6f, .32f, 2.5f), S(2.55f, "explode"), I("smash", .9f), I("land") } },
            { "Knockout", new[] { S(1.0f, "whiz", .8f), S(1.55f, "punch", .8f, .6f), S(1.8f, "punch", 1, .55f, .16f, 3.0f), S(3.25f, "smash", .85f), I("smash", .9f), I("land") } },
            { "Heartbreaker", new[] { S(.78f, "punch", .75f), S(1.25f, "impact", .8f), S(1.3f, "heart", 1, 1, .6f, 2.9f), S(3.05f, "land") } },
            { "Ice sculpture", new[] { S(.3f, "frost"), S(1.82f, "punch", .8f), S(1.84f, "whiz"), I("smash", 1.3f), I("brick", 1.5f, .8f) } },
            { "Dog pile", new[] { S(.5f, "punch", .8f), S(.88f, "land"), S(1.2f, "thorn"), S(1.75f, "punch", 1.4f, .4f, .22f, 2.9f), S(2.95f, "smash") } },
            { "Sky drop", new[] { S(.55f, "jet"), S(1.8f, "throw"), S(1.85f, "whiz", .8f), I("smash"), I("explode", .8f, .6f) } },
            { "Combat", new[] { S(.4f, "reload", 1.2f, .6f), S(.7f, "hook"), S(.82f, "impact", 1.3f), S(.9f, "zip", .6f, .6f, .18f, 1.45f), S(1.8f, "punch", .7f), S(1.8f, "smash", 1.1f, .8f), S(1.83f, "whiz"), I("smash"), I("land") } },
            { "Pancake", new[] { S(.4f, "swing"), S(1.15f, "smash"), S(1.75f, "smash", .9f), S(2.35f, "swing", .8f), S(2.55f, "smash", .75f), S(2.55f, "explode", .7f, .5f) } } };

        void FinSfx()
        {
            if (!FIN_SFX.TryGetValue(fx, out var list)) return;
            for (int i = 0; i < list.Length; i++)
            {
                var s = list[i];
                if (s.imp) { if (imp.HasValue && !sfxDone.Contains(i)) { sfxDone.Add(i); Sfx.Play(s.n, imp.Value, s.vol, 30, s.rate); } continue; }
                if (t < s.t) continue;
                if (s.every <= 0) { if (!sfxDone.Contains(i)) { sfxDone.Add(i); Sfx.Play(s.n, v.pos, s.vol, 30, s.rate); } continue; }
                if (t > s.until) continue;
                int k = Mathf.FloorToInt((t - s.t) / s.every);
                if (!sfxEvery.TryGetValue(i, out var last) || k > last) { sfxEvery[i] = k; Sfx.Play(s.n, v.pos, s.vol, 30, s.rate); }
            }
        }

        // ------------------------------------------------------------------ every frame, after the poses: props that follow hands and paths
        public void LatePlace()
        {
            float tl = pre != null ? 0 : Mathf.Max(0, TL);
            if (ice && fall != null && !done) { ice.position = v.VisualPos; ice.rotation = Quaternion.Euler(-fall.rx * Mathf.Rad2Deg, v.yaw * Mathf.Rad2Deg + 180, 0); }
            else if (ice) ice.rotation = Quaternion.Euler(0, vYaw * Mathf.Rad2Deg + 180, 0);
            PlaceDogs(tl);
            PlacePotato(tl);
            if (heart) { heart.position = Hand(a, false) + up * (tl > 1.5f ? .32f : .12f); heart.rotation = Quaternion.Euler(0, a.yaw * Mathf.Rad2Deg + 180, 0); }
            PlaceGloves(tl);
            PlacePads();
            PlaceHarpoon(tl);
        }

        // ------------------------------------------------------------------ ending
        void Release()
        {
            foreach (var o in obj) if (o) Object.Destroy(o);
            obj.Clear();
            if (v.finishing == this) v.finishing = null;
            if (a.finishingOther == this) a.finishingOther = null;
            var pv = FinisherPose.Of(v); if (pv) pv.Xray = false;
        }

        /// The move is over: the victim is out (web: the kill goes to whoever finished them; the feed names the move).
        public void Complete()
        {
            Release();
            a.vel = Vector3.zero; a.FinPlace(a.pos);
            v.downed = false; v.corpse = true; v.vel = Vector3.zero;
            var pose = FinisherPose.Of(v); if (pose) pose.Hidden = true;
            var prev = RoundManager.WeaponNameOf;
            RoundManager.WeaponNameOf = f => f == a ? fx : prev != null ? prev(f) : f.primary;
            try { v.Die(a, false); } finally { RoundManager.WeaponNameOf = prev; }
        }

        /// web cancelFinish: the victim drops back to being down where they were; the attacker is let go.
        public void Interrupt()
        {
            Release();
            v.FinPlace(v0); v.cv = 0; a.cv = 0;
            v.downed = true; v.revive = Mathf.Max(v.revive, .25f); v.bleed = Mathf.Max(v.bleed, 10); v.dhp = Mathf.Max(v.dhp, 15);
            v.vel = Vector3.zero; v.grounded = false;
            a.vel = new Vector3(0, Mathf.Min(a.vel.y, 0), 0); a.grounded = false; a.FinPlace(a.pos);
            { var pv = FinisherPose.Of(v); if (pv) pv.ResetVisual(); var pa = FinisherPose.Of(a); if (pa) pa.ResetVisual(); }
            if (bannerOn) FinisherHud.ClearBanner();
            if (Me(a)) FinisherHud.Center("Interrupted!", "Your finisher was stopped", false, 1.3f);
            else if (Me(v)) FinisherHud.Center("Saved!", "The finisher was stopped", true, 1.3f);
            Finishers.FeedLine($"{a.displayName}'s finisher on {v.displayName} was stopped", a, v);
        }

        /// Round reset: tidy up without a word.
        public void Abort() { Release(); if (bannerOn) FinisherHud.ClearBanner(); }
    }
}
