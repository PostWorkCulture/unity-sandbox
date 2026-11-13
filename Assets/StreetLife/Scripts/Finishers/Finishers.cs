// Finishers chat: the finishing moves from the web game (imok 1v1), all twelve of them:
// Roast, Piledriver, Clear!, Overcharge, Hot potato, Knockout, Heartbreaker, Ice sculpture, Dog pile, Sky drop, Pancake and Combat.
//
// Down an enemy, stand within 3 m and press F: a move is picked at random (never the same one twice in a row; Pancake
// comes up twice as often). Both fighters shuffle to a clear spot first so nobody swings through a wall, the camera cuts
// to a cinematic shot, and at the end the victim is out. Clicking (fire) during your own move, or being shot by someone
// else, stops it; the victim goes back to being down.
//
// The moves run at the same fixed 60 Hz as movement and are drawn interpolated, so they're smooth at any frame rate.
// Test keys (until weapons can down people): F9 knocks the bot down in front of you, Shift+F9 downs you (the bot comes
// to finish you), F10 picks which move comes next (Random by default). Phones get the web game's Finish button.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StreetLife
{
    [DefaultExecutionOrder(-40)]
    public class Finishers : MonoBehaviour
    {
        public static Finishers I;
        /// Set false to go back to dying at 0 health (no downed state, no finishers).
        public static bool DownedStateOn = true;
        /// The twelve moves, in the web game's order, with their lengths in seconds.
        public static readonly string[] Moves = { "Roast", "Piledriver", "Clear!", "Overcharge", "Hot potato", "Knockout", "Heartbreaker", "Ice sculpture", "Dog pile", "Sky drop", "Pancake", "Combat" };
        public static readonly Dictionary<string, float> Dur = new Dictionary<string, float> {
            { "Roast", 2.6f }, { "Piledriver", 3.6f }, { "Clear!", 4.0f }, { "Overcharge", 2.8f }, { "Hot potato", 3.4f }, { "Knockout", 4.1f },
            { "Heartbreaker", 5.2f }, { "Ice sculpture", 2.9f }, { "Dog pile", 3.9f }, { "Sky drop", 3.5f }, { "Pancake", 3.6f }, { "Combat", 2.4f } };
        /// Test: the next move (empty for random). F9 cycles it.
        public static string ForceMove = "";
        public static event System.Action<Finisher> Started, Ended, Interrupted;

        public readonly List<Finisher> active = new List<Finisher>();
        public static readonly List<Fighter> Fighters = new List<Fighter>();
        public Fighter Player { get; private set; }
        static string lastMove;
        float acc, scanT, slowT, ts = 1; bool slowing;
        /// Screen shake and vignette for the player's own moments (the camera and HUD read these).
        public static float Shake, Vig;
        public static Color VigColor = new Color(.55f, 0, .02f);
        static readonly string[] Wisp = { "#8a1620", "#6e0f18", "#a3202a" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I || !FindAnyObjectByType<MapBuilder>()) return;
            var go = new GameObject("Finishers");
            go.AddComponent<Finishers>(); go.AddComponent<FinisherLate>(); go.AddComponent<FinisherHud>();
        }

        void Awake() { I = this; RoundManager.RoundStarted += OnRoundStarted; }
        void OnDestroy()
        {
            RoundManager.RoundStarted -= OnRoundStarted;
            if (I == this) I = null;
            if (slowing) Time.timeScale = 1;
        }

        // ------------------------------------------------------------------ queries
        public static bool InFight => !RoundManager.Instance || RoundManager.Instance.state == RoundManager.State.Fight;

        /// web canFinish: on your feet, the victim down, within 3 m and roughly the same height, during the fight.
        public static bool CanFinish(Fighter a, Fighter v)
        {
            if (!a || !v || a == v || a.team == v.team) return false;
            if (!a.alive || a.downed || a.InFinisher || !v.alive || !v.downed || v.InFinisher || !InFight) return false;
            float dx = a.pos.x - v.pos.x, dz = a.pos.z - v.pos.z;
            return dx * dx + dz * dz < 9f && Mathf.Abs(a.pos.y - v.pos.y) < 1.9f;
        }

        public static Fighter Finishable(Fighter a)
        {
            foreach (var v in Fighters) if (v && CanFinish(a, v)) return v;
            return null;
        }

        public static Finisher Of(Fighter f) => f ? (f.finishing ?? f.finishingOther) : null;

        /// For weapons: a ray against a downed fighter lying on the ground (web rayFighter's downed box: 1.5 m wide,
        /// .85 m high, from just behind the feet to 2.35 m ahead along the way the body lies). Distance, or -1.
        public static float RayDowned(Vector3 o, Vector3 d, Fighter f)
        {
            if (!f || !f.alive || !f.downed || f.InFinisher) return -1;
            var pose = FinisherPose.Of(f); float yaw = pose ? pose.LyingYaw : f.yaw, sc = Fighter.SC;
            float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
            float wx = o.x - f.pos.x, wz = o.z - f.pos.z, oy = o.y - f.pos.y;
            float ox = wx * c - wz * s, oz = wx * s + wz * c - 1.0f * sc, dx = d.x * c - d.z * s, dz = d.x * s + d.z * c;
            float t0 = -1e9f, t1 = 1e9f;
            if (!Slab(ox, dx, -.75f * sc, .75f * sc, ref t0, ref t1) || !Slab(oy, d.y, 0, .85f * sc, ref t0, ref t1) || !Slab(oz, dz, -1.35f * sc, 1.35f * sc, ref t0, ref t1)) return -1;
            if (t1 < 0) return -1;
            return Mathf.Max(0, t0);
        }
        static bool Slab(float o, float d, float mn, float mx, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-9f) return o >= mn && o <= mx;
            float a = (mn - o) / d, b = (mx - o) / d; if (a > b) { var q = a; a = b; b = q; }
            if (a > t0) t0 = a; if (b < t1) t1 = b; return t0 <= t1;
        }
        /// The move the camera should follow: one the player is in.
        public Finisher PlayerMove { get { foreach (var F in active) if (F.cine && (F.a == Player || F.v == Player)) return F; return null; } }

        // ------------------------------------------------------------------ starting, stopping
        static string Pick()
        {
            if (!string.IsNullOrEmpty(ForceMove) && Dur.ContainsKey(ForceMove)) return ForceMove;
            var L = new List<string>();
            foreach (var m in Moves) if (m != lastMove) L.Add(m);
            if (L.Contains("Pancake")) L.Add("Pancake");     // Pancake comes up twice as often as any other move
            return L[Random.Range(0, L.Count)];
        }

        /// web finish(): starts a finishing move by a on v. Returns null if it can't.
        public static Finisher Begin(Fighter a, Fighter v, string force = null)
        {
            if (!I || !CanFinish(a, v)) return null;
            string fx = !string.IsNullOrEmpty(force) && Dur.ContainsKey(force) ? force : Pick();
            lastMove = fx;
            var F = new Finisher(a, v, fx, Dur[fx]);
            I.active.Add(F);
            v.finishing = F; a.finishingOther = F;
            a.ads = false; a.inFire = false; v.inFire = false;
            if (F.Involves(I.Player)) { I.SlowMo(.2f); }
            F.Banner();
            Started?.Invoke(F);
            return F;
        }

        /// web cancelFinish: the victim goes back to being down where they were; the attacker is let go.
        public static void Cancel(Finisher F)
        {
            if (F == null || F.gone || F.done || !I || !I.active.Contains(F)) return;
            I.active.Remove(F);
            F.Interrupt();
            if (I.Player && F.Involves(I.Player)) { I.slowT = 0; }
            Interrupted?.Invoke(F);
        }

        void Finish(Finisher F)
        {
            active.Remove(F);
            F.Complete();
            Ended?.Invoke(F);
        }

        // ------------------------------------------------------------------ events from FighterDowned
        internal static void OnDown(Fighter f, Fighter by, bool head)
        {
            Sfx.Play("down", f.pos, 1, 30);
            FinisherFx.HitSpray(f.pos + Vector3.up * 1.2f, 18, 1.6f);
            if (I && f == I.Player) { Shake = Mathf.Max(Shake, .35f); Vig = Mathf.Max(Vig, .5f); }
            else if (I && by == I.Player) Shake = Mathf.Max(Shake, .15f);
            Feed(f == (I ? I.Player : null) ? "You are down" : f.displayName + " is down", f, by);
        }

        internal static void OnRevive(Fighter f) { Feed(f.displayName == "You" ? "You got back up" : f.displayName + " got back up", f, null); }

        internal static void OnDownedKill(Fighter f, Fighter by) { FinisherFx.BloodBits(f.pos, 7); }

        static void Feed(string text, Fighter a, Fighter b)
        {
            var rm = RoundManager.Instance; if (!rm) return;
            rm.feed.Add(new RoundManager.FeedItem { text = text, t = Time.time, mine = a == rm.player || b == rm.player });
            if (rm.feed.Count > 5) rm.feed.RemoveAt(0);
        }
        internal static void FeedLine(string text, Fighter a, Fighter b) => Feed(text, a, b);

        void OnRoundStarted(int round)
        {
            foreach (var F in active.ToArray()) F.Abort();
            active.Clear();
            foreach (var f in Fighters) if (f) f.ResetDowned();
            FinisherFx.ClearAll();
            slowT = 0; Shake = 0; Vig = 0;
            if (slowing) { Time.timeScale = 1; ts = 1; slowing = false; }
            FinisherPose.ResetAll();
        }

        public void SlowMo(float t) { slowT = Mathf.Max(slowT, t); }
        /// web slowMo(t, ...who): only when the player is part of the moment.
        public static void SlowMoFor(float t, Fighter a, Fighter v) { if (I && I.Player && (a == I.Player || v == I.Player)) I.SlowMo(t); }
        public static void VigFor(float k, Fighter a, Fighter v) { if (I && I.Player && (a == I.Player || v == I.Player)) Vig = Mathf.Max(Vig, k); }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            float rdt = Time.unscaledDeltaTime;
            scanT -= rdt;
            if (scanT <= 0) { scanT = .5f; Scan(); }

            // slow motion (web: S.ts eases to .25 while slowT runs, back to 1 after)
            if (slowT > 0 || slowing)
            {
                if (slowT > 0) { slowT -= rdt; ts = Mathf.Lerp(ts, .25f, 1 - Mathf.Exp(-18 * rdt)); slowing = true; }
                else { ts = Mathf.Lerp(ts, 1, 1 - Mathf.Exp(-5 * rdt)); if (ts > .995f) { ts = 1; slowing = false; } }
                Time.timeScale = ts;
            }
            Shake = Mathf.Max(0, Shake - rdt * 1.6f);
            Vig = Mathf.Max(0, Vig - rdt * 1.2f);

            PlayerInput();
            foreach (var f in Fighters)
                if (f && f.downed && f.alive && !f.InFinisher && Random.value < .18f * Time.deltaTime * 60)
                    FinisherFx.Emit(f.pos + new Vector3(Random.Range(-.25f, .25f), .35f, Random.Range(-.25f, .25f)), 1, .25f, .085f, .75f, -.5f, Wisp, .9f);

            // the moves, at the fighters' fixed 60 Hz (both fighters are held still by Fighter.Update while in one)
            if (active.Count == 0) { acc = 0; return; }
            acc = Mathf.Min(acc + Time.deltaTime, Fighter.SIM * 4);
            while (acc >= Fighter.SIM - 1e-6f && active.Count > 0)
            {
                acc -= Fighter.SIM;
                foreach (var F in active.ToArray())
                {
                    F.a.FinStep(); F.v.FinStep();
                    F.Tick(Fighter.SIM);
                    if (F.t > F.dur && F.pre == null) Finish(F);
                }
            }
            foreach (var F in active) { F.a.FinAlpha(acc); F.v.FinAlpha(acc); }
        }

        void Scan()
        {
            Fighters.Clear();
            Fighters.AddRange(FindObjectsByType<Fighter>());
            Player = null;
            foreach (var f in Fighters)
            {
                if (f.GetComponent<PlayerController>()) Player = f;
                if (!f.GetComponent<FinisherPose>() && f.GetComponent<CharacterRig>()) f.gameObject.AddComponent<FinisherPose>();
                if (f.isBot && !f.GetComponent<BotFinisher>()) f.gameObject.AddComponent<BotFinisher>();
            }
            var cam = Camera.main;
            if (cam && !cam.GetComponent<FinisherCam>()) cam.gameObject.AddComponent<FinisherCam>();
        }

        bool fireWas, touchShown;
        void PlayerInput()
        {
            var kb = Keyboard.current; var me = Player;
            if (!me) return;
            // phones: the web game's Finish button (306, 70, 62), only while there's someone to finish
            bool can = Finishable(me) != null;
            if (TouchControls.I && can != touchShown)
            {
                touchShown = can;
                TouchControls.AddButton("finish", 306, 70, can ? 62 : 0, can ? "Finish" : "", down => { var p = Player; if (down && p) { var v = Finishable(p); if (v) Begin(p, v); } });
            }
            // firing during your own finisher stops it (web: a fresh click)
            bool fire = me.inFire;
            if (fire && !fireWas && me.finishingOther != null && me.finishingOther.t > .1f) Cancel(me.finishingOther);
            fireWas = fire;
            if (kb == null) return;
            if (kb.fKey.wasPressedThisFrame) { var v = Finishable(me); if (v) Begin(me, v); }

            // ---- test keys (F8 is the match chat's performance readout) ----
            if (kb.f10Key.wasPressedThisFrame)
            {
                int i = System.Array.IndexOf(Moves, ForceMove);
                ForceMove = i + 1 >= Moves.Length ? "" : Moves[i + 1];
                FinisherHud.Note("Next finisher: " + (ForceMove == "" ? "Random" : ForceMove), 2.5f);
            }
            if (kb.f9Key.wasPressedThisFrame && InFight)
            {
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                Fighter foe = null; foreach (var f in Fighters) if (f && f != me && f.team != me.team && f.alive) { foe = f; break; }
                if (shift) { if (me.alive && !me.downed && !me.InFinisher) { me.Damage(me.hp + me.armor + 1, foe); if (foe) { var bf = foe.GetComponent<BotFinisher>(); if (bf) bf.ForceFinish(); } } }
                else if (foe && !foe.downed && !foe.InFinisher)
                {
                    // put them on the ground 2 m in front of you, then down them
                    var p = me.pos + me.Forward * 2.2f;
                    var sp = FinGeo.ClearSpot(p, 1.2f, 4f);
                    if (FinGeo.BodyFits(sp.x, sp.z, sp.y)) { foe.FinPlace(sp); foe.vel = Vector3.zero; foe.grounded = true; }
                    foe.Damage(foe.hp + foe.armor + 1, me);
                }
            }
        }

        /// After poses are applied (hand-held props follow the hands with no lag).
        internal void LatePlace() { foreach (var F in active) F.LatePlace(); FinisherFx.LateFrame(); }
    }

    /// Runs after every rig, weapon pose and finisher pose: places hand-held props and draws the effects.
    [DefaultExecutionOrder(1050)]
    public class FinisherLate : MonoBehaviour
    {
        void LateUpdate() { if (Finishers.I) Finishers.I.LatePlace(); }
    }

    /// The web game's ground and room queries, on the collision boxes.
    public static class FinGeo
    {
        public const float R = Fighter.R, STEP = Fighter.STEP, NAV_H = 2.25f;

        // Queries near one spot (ClearSpot) look only at the boxes round it: tens instead of hundreds.
        static readonly List<Box> scoped = new List<Box>(); static bool scopeOn;
        static List<Box> Boxes => scopeOn ? scoped : BoxWorld.Boxes;
        static void Scope(Vector3 c, float r)
        {
            scoped.Clear();
            foreach (var b in BoxWorld.Boxes) if (b.x1 > c.x - r && b.x0 < c.x + r && b.z1 > c.z - r && b.z0 < c.z + r) scoped.Add(b);
            scopeOn = true;
        }

        /// Is (x,z) over the map's ground at all? (Beyond the scenery a thrown body falls away.)
        static Bounds? scenery; static MapBuilder sceneryOf;
        public static bool OverMap(float x, float z)
        {
            var mb = Object.FindAnyObjectByType<MapBuilder>();
            if (!scenery.HasValue || sceneryOf != mb)
            {
                Bounds b = default; bool any = false;
                void Add(Component root) { if (root) foreach (var r in root.GetComponentsInChildren<MeshRenderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); } }
                Add(mb); Add(MapWorld.I);
                if (!any || b.size.x < 1) { var d = BoxWorld.Bounds; b = new Bounds(new Vector3((d.x0 + d.x1) / 2, 0, (d.z0 + d.z1) / 2), new Vector3(d.x1 - d.x0 + 40, 100, d.z1 - d.z0 + 40)); }
                scenery = b; sceneryOf = mb;
            }
            var s = scenery.Value; return x > s.min.x && x < s.max.x && z > s.min.z && z < s.max.z;
        }
        /// No ground at all here: past Sky High's roof, or the Lagoon's open water (the maps chat's MapRules.Drop).
        public static bool NoGround(float x, float z) => MapRules.Drop(x, z);
        public const float NONE = -1e4f;

        /// How far you can walk from `from` along `dir` (flat) and stay on the same floor (within .6 m of from.y), up to max:
        /// stops at a roof edge, open water or a drop, so nobody in a move walks out over nothing.
        public static float FloorRun(Vector3 from, Vector3 dir, float max)
        {
            for (float s = .25f; s <= max; s += .25f)
            {
                float x = from.x + dir.x * s, z = from.z + dir.z * s, h = SurfaceAt(x, z, from.y + .3f);
                if (Mathf.Abs(h - from.y) > .6f) return Mathf.Max(0, s - .25f);
            }
            return max;
        }
        public static void ForgetMap() { scenery = null; }

        /// web surfaceAt: the floor at (x,z) nearest to height y, preferring floors below (box tops, or the ground at 0).
        public static float SurfaceAt(float x, float z, float y)
        {
            float best = 0, bd = y + .7f >= 0 ? y : (0 - y) * 4;
            if (NoGround(x, z)) { best = NONE; bd = 1e9f; }      // only box tops (Sky High's roof, the boardwalks), or nothing
            var B = Boxes;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i];
                if (x <= b.x0 || x >= b.x1 || z <= b.z0 || z >= b.z1) continue;
                float h = b.y1, d = h <= y + .7f ? y - h : (h - y) * 4;
                if (d < bd) { bd = d; best = h; }
            }
            return best;
        }

        /// web groundAt: the floor under a point (a little above it counts).
        public static float GroundAt(float x, float z, float y)
        {
            float best = NoGround(x, z) ? NONE : 0; var B = Boxes; float lim = y + .5f;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i];
                if (x <= b.x0 || x >= b.x1 || z <= b.z0 || z >= b.z1) continue;
                if (b.y1 <= lim + 1e-3f && b.y1 > best) best = b.y1;
            }
            return best;
        }

        /// Is there a floor at (x,z) within 5 cm of h?
        public static bool FloorAt(float x, float z, float h)
        {
            if (Mathf.Abs(h) < .05f && !NoGround(x, z)) { if (!Covered(x, z, h)) return true; }
            var B = Boxes;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i];
                if (x <= b.x0 || x >= b.x1 || z <= b.z0 || z >= b.z1) continue;
                if (Mathf.Abs(b.y1 - h) < .05f) return true;
            }
            return false;
        }
        static bool Covered(float x, float z, float h)
        {
            foreach (var b in Boxes) if (x > b.x0 && x < b.x1 && z > b.z0 && z < b.z1 && b.y0 <= h + .01f && b.y1 > h + .05f) return true;
            return false;
        }

        /// web bodyFits: a standing body (R + 6 cm) fits at (x, z) on floor h.
        public static bool BodyFits(float x, float z, float h)
        {
            float r = R + .06f; var B = Boxes;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i];
                if (b.y1 > h + STEP && b.y0 < h + NAV_H - .05f && x > b.x0 - r && x < b.x1 + r && z > b.z0 - r && z < b.z1 + r) return false;
            }
            var bd = BoxWorld.Bounds;
            return x > bd.x0 + R && x < bd.x1 - R && z > bd.z0 + R && z < bd.z1 - R;
        }

        /// web roomAt: how much room there is all round (rings of 8 at .9, 1.6, 2.3, 3.0 m), up to `need`.
        public static float RoomAt(float x, float z, float h, float need)
        {
            foreach (var r in new[] { .9f, 1.6f, 2.3f, 3.0f })
            {
                if (r > need + .01f) break;
                for (int k = 0; k < 8; k++) { float a = k / 8f * Mathf.PI * 2; if (!BodyFits(x + Mathf.Cos(a) * r, z + Mathf.Sin(a) * r, h)) return r - .7f; }
            }
            return need;
        }

        /// web flatLine: a straight walk on one floor from (x0,z0) to (x1,z1).
        public static bool FlatLine(float x0, float z0, float x1, float z1, float h)
        {
            float dd = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)); int n = Mathf.Max(1, Mathf.CeilToInt(dd / .25f));
            for (int s = 0; s <= n; s++)
            {
                float t = s / (float)n, x = Mathf.Lerp(x0, x1, t), z = Mathf.Lerp(z0, z1, t);
                if (!FloorAt(x, z, h) || !BodyFits(x, z, h)) return false;
            }
            return true;
        }

        /// web clearSpot: the nearest spot on the same floor with `need` metres of room all round, reachable in a straight
        /// line, so the move's arms, bodies and props never swing through a wall or post.
        public static Vector3 ClearSpot(Vector3 p, float need, float maxR)
        {
            Scope(p, maxR + need + 1.5f);
            try { return ClearSpot0(p, need, maxR); } finally { scopeOn = false; }
        }
        static Vector3 ClearSpot0(Vector3 p, float need, float maxR)
        {
            float x = p.x, y = p.y, z = p.z;
            float h0 = SurfaceAt(x, z, y);
            if (Mathf.Abs(h0 - y) < .6f && RoomAt(x, z, h0, need) >= need) return p;
            const float CS = .5f; int R = Mathf.CeilToInt(maxR / CS);
            float bs = 1e9f, br = -1, ad = 1e9f; Vector3? best = null, bestRoom = null, anyB = null;
            float cx0 = Mathf.Floor(x / CS) * CS + CS / 2, cz0 = Mathf.Floor(z / CS) * CS + CS / 2;
            for (int dj = -R; dj <= R; dj++)
                for (int di = -R; di <= R; di++)
                {
                    float d = Mathf.Sqrt(di * di + dj * dj) * CS; if (d > maxR || d >= bs) continue;
                    float cx = cx0 + di * CS, cz = cz0 + dj * CS, h = SurfaceAt(cx, cz, y + .3f);
                    if (Mathf.Abs(h - y) > .6f || !BodyFits(cx, cz, h)) continue;
                    float room = RoomAt(cx, cz, h, need);
                    if (room >= need && d < ad) { ad = d; anyB = new Vector3(cx, h, cz); }
                    if (room < need && room <= br) continue;
                    if (!FlatLine(x, z, cx, cz, h)) continue;
                    if (room >= need) { if (d < bs) { bs = d; best = new Vector3(cx, h, cz); } }
                    else if (room > br) { br = room; bestRoom = new Vector3(cx, h, cz); }
                }
            return best ?? bestRoom ?? anyB ?? p;
        }

        /// web edgeBox: one of the arena's invisible boundary walls.
        public static bool EdgeBox(in Box b)
        {
            var d = BoxWorld.Bounds;
            return b.x0 >= d.x1 - .6f || b.x1 <= d.x0 + .6f || b.z0 >= d.z1 - .6f || b.z1 <= d.z0 + .6f;
        }

        /// web topAt: height of the tallest box at (x,z) (boundary walls left out).
        public static float TopAt(float x, float z, List<Box> BX)
        {
            float top = -1e9f;
            foreach (var b in BX) if (x > b.x0 && x < b.x1 && z > b.z0 && z < b.z1) top = Mathf.Max(top, b.y1);
            return top;
        }

        /// The yaw a downed body can lie along without its head going into a wall (web downYaw).
        public static float DownYaw(Fighter f, float cur)
        {
            float L = Fighter.HH * .98f, best = cur, bl = -1;
            foreach (var off in new[] { 0f, .45f, -.45f, .9f, -.9f, 1.4f, -1.4f, 2f, -2f, 2.6f, -2.6f, Mathf.PI })
            {
                float a = f.yaw + off; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); float free = L;
                foreach (var hy in new[] { .3f, .7f }) { float t = BoxWorld.Ray(f.pos + Vector3.up * hy, d, L); if (t >= 0 && t < free) free = t; }
                if (free >= L - .01f) return a;
                if (free > bl + .05f) { bl = free; best = a; }
            }
            return best;
        }
    }
}
