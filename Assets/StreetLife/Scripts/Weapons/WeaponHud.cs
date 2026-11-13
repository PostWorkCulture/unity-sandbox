// Weapon HUD, after the web game's: crosshair that opens with spread (and turns red on a target in range), hit markers,
// floating damage numbers, the ammo panel (slots 1 / 2, rounds, gun name, reload bar), lethal and grenade counts,
// the sniper scope, the RPG optic and Swarm lock-on screens, flash-bang white-out and the hurt vignette, name tags
// with health bars over the enemy, and the weapon picker (Tab) with every weapon and the shop items.
// The match chat's Hud draws the vitals, minimap, score, feed and hit-direction arc; this is the weapon panel.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StreetLife
{
    public class WeaponHud : MonoBehaviour
    {
        public static bool MenuOpen, ClickedUi;
        /// true while the weapon HUD is drawing its own crosshair (the match Hud can skip its plain one)
        public static bool Active;
        public Weapons weapons;
        Texture2D white, ring, hole, scopeRing, disc;
        GUIStyle big, mid, midC, midR, tagSt, small, smallC, smallR, num, numHead, btn, btnOn, head, title;
        float hitT = 9, vigA; bool hitHead;
        struct Num { public Vector3 p; public float t; public string s; public bool head; }
        readonly List<Num> nums = new List<Num>();
        static readonly Color Accent = Web.Hex("#ff4a3d"), Gold = Web.Hex("#ffc61a"), Panel = new Color(.07f, .075f, .09f, .82f);

        Hud matchHud;
        void OnEnable() { Combat.OnFeedback += OnHit; Active = true; }
        void OnDisable() { Combat.OnFeedback -= OnHit; Active = false; MenuOpen = false; if (matchHud) matchHud.drawCrosshair = true; }
        void Start() { HideMatchCrosshair(); }
        // the match HUD's plain crosshair gives way to this one (it opens with spread and changes per gun)
        void HideMatchCrosshair() { if (!matchHud) matchHud = Object.FindAnyObjectByType<Hud>(); if (matchHud) matchHud.drawCrosshair = false; }

        void OnHit(Fighter v, float shown, bool headshot, bool strong)
        {
            hitT = 0; hitHead = headshot || strong;
            Sfx.Play2D(headshot ? "headshot" : "hit", .9f, headshot ? 1 : 1.05f);
            if (v && shown > 0) nums.Add(new Num { p = new Vector3(v.pos.x + Random.Range(-.4f, .4f), v.pos.y + 2.9f * Fighter.SC, v.pos.z + Random.Range(-.4f, .4f)), s = Mathf.RoundToInt(shown).ToString(), head = headshot });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            hitT += dt;
            for (int i = nums.Count - 1; i >= 0; i--) { var n = nums[i]; n.t += dt; n.p.y += dt * 1.2f; nums[i] = n; if (n.t > .9f) nums.RemoveAt(i); }
            var f = weapons ? weapons.F : null;
            if (f)
            {
                if (f.flashHold > 0) { f.flashHold -= dt; f.flashA = Mathf.Max(f.flashA, .96f); } else f.flashA = Mathf.Max(0, f.flashA - dt * 1.65f);
                if (f.hurtFlash > 0) { vigA = Mathf.Max(vigA, f.hurtFlash); f.hurtFlash = 0; }
                vigA = Mathf.Max(0, vigA - dt * 1.2f);
                if (f.blastT > 0) f.blastT -= dt;
            }
            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame) SetMenu(!MenuOpen);
            if (kb != null && MenuOpen && kb.escapeKey.wasPressedThisFrame) SetMenu(false);
            ClickedUi = false;
        }

        void SetMenu(bool on)
        {
            MenuOpen = on;
            // the player's controls pause while the picker is open (so a click picks, not shoots or grabs the mouse)
            var pc = weapons ? weapons.GetComponent<PlayerController>() : null;
            if (pc) pc.enabled = !on;
            if (weapons) { var f = weapons.F; f.inFwd = f.inSide = 0; f.inFire = false; f.ads = false; f.inJump = false; }
            if (on) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        // ------------------------------------------------------------------ drawing helpers
        void Styles()
        {
            if (white) return;
            white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();
            ring = Circle(128, .9f, 1f, false); hole = Circle(256, 1f, 1f, true); scopeRing = Circle(256, .975f, 1f, false); disc = Circle(256, 0, .96f, false);
            big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            mid = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            midC = new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter }; midR = new GUIStyle(mid) { alignment = TextAnchor.MiddleRight };
            tagSt = new GUIStyle(midC) { fontSize = 13 }; title = new GUIStyle(mid) { fontSize = 22 };
            small = new GUIStyle(GUI.skin.label) { fontSize = 12 }; small.normal.textColor = new Color(1, 1, 1, .85f);
            smallC = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter }; smallR = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            head = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold }; head.normal.textColor = new Color(1, 1, 1, .6f);
            num = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            numHead = new GUIStyle(num) { fontSize = 20 }; numHead.normal.textColor = Gold;
            btn = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = true, padding = new RectOffset(8, 8, 4, 4) };
            btnOn = new GUIStyle(btn); btnOn.normal.textColor = btnOn.hover.textColor = Gold; btnOn.fontStyle = FontStyle.Bold;
        }

        /// ring (inner..outer radius as fraction) or, with invert, a black square with a round hole
        static Texture2D Circle(int n, float r0, float r1, bool invert)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp }; var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float r = Mathf.Sqrt((x + .5f - n / 2f) * (x + .5f - n / 2f) + (y + .5f - n / 2f) * (y + .5f - n / 2f)) / (n / 2f);
                    float a = invert ? Mathf.Clamp01((r - r0) * n * .5f) : Mathf.Clamp01((r - r0) * n * .5f) * Mathf.Clamp01((r1 - r) * n * .5f);
                    px[y * n + x] = new Color(1, 1, 1, a);
                }
            t.SetPixels(px); t.Apply(); return t;
        }

        void Rect(float x, float y, float w, float h, Color c) { GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), white); GUI.color = Color.white; }
        void Shadowed(Rect r, string s, GUIStyle st, Color c)
        {
            var c0 = st.normal.textColor; st.normal.textColor = new Color(.06f, .07f, .08f, c.a);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), s, st); st.normal.textColor = c; GUI.Label(r, s, st); st.normal.textColor = c0;
        }

        bool OnTarget(Fighter f, GunStat g)
        {
            var cam = Camera.main; if (!cam || g == null) return false;
            var c = cam.transform.position; var d = cam.transform.forward;
            if (!Combat.RayFoes(c, d, f, out var hf)) return false;
            if (Vector3.Distance(hf.who.pos, f.pos) > g.reach) return false;
            return !Combat.RayBoxes(c, d, hf.t, out _);
        }

        // ------------------------------------------------------------------ the HUD
        void OnGUI()
        {
            if (!weapons || !GunsDb.Loaded) return;
            Styles();
            var w = weapons; var f = w.F; var g = w.G; var cam = Camera.main; if (!cam) return;
            float s = Screen.height / 1080f * 1.4f; var m0 = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = Screen.height / s, cx = W / 2, cy = H / 2;
            string id = w.Gun;
            bool scoped = g != null && f.ads && g.Sniper && f.alive;
            bool optic = (id == "rocket" || id == "swarm") && f.ads && w.vmRaise > .85f;

            // name tags over the targets
            foreach (var o in Combat.Fighters)
            {
                if (!o || o == f || !o.alive) continue;
                var sp = cam.WorldToScreenPoint(o.pos + Vector3.up * (3.15f * Fighter.SC)); if (sp.z <= 0) continue;
                if (!Combat.Los(cam.transform.position, o.pos + Vector3.up * (2.4f * Fighter.SC))) continue;
                float x = sp.x / s, y = (Screen.height - sp.y) / s;
                string nm = o.downed ? "DOWNED" : o.displayName;
                Shadowed(new Rect(x - 60, y - 34, 120, 18), nm, tagSt, Combat.IsFoe(f, o) ? Web.Hex("#ff6a5a") : Color.white);
                Rect(x - 36, y - 14, 72, 7, new Color(0, 0, 0, .6f));
                float hf0 = o.downed ? o.dhp / Fighter.DOWN_HP : o.hp / o.hpMax;
                Rect(x - 35, y - 13, 70 * Mathf.Clamp01(hf0), 5, o.downed ? Accent : hf0 > .5f ? Web.Hex("#46d96a") : hf0 > .25f ? Gold : Accent);
                if (o.armor > 0) Rect(x - 35, y - 6, 70 * Mathf.Clamp01(o.armor / 100), 2, Web.Hex("#2f7fe0"));
            }

            // floating damage numbers
            foreach (var n in nums)
            {
                var sp = cam.WorldToScreenPoint(n.p); if (sp.z <= 0) continue;
                float a = 1 - Mathf.Max(0, n.t - .5f) / .4f, sc = 1 + Mathf.Max(0, .15f - n.t) * 3;
                var st = n.head ? numHead : num; var c = n.head ? Gold : Color.white; c.a = a;
                var mm = GUI.matrix; var pivot = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
                GUIUtility.ScaleAroundPivot(new Vector2(sc, sc), pivot * s);
                Shadowed(new Rect(pivot.x - 40, pivot.y - 14, 80, 28), n.s, st, c); GUI.matrix = mm;
            }

            // sniper scope: black outside the glass, thin cross, red dot
            if (scoped)
            {
                float R = Mathf.Min(W, H) * (id == "sniper" ? .34f : .40f);
                Rect(0, 0, cx - R + 1, H, Color.black); Rect(cx + R - 1, 0, W - cx - R + 1, H, Color.black);
                Rect(cx - R, 0, 2 * R, cy - R + 1, Color.black); Rect(cx - R, cy + R - 1, 2 * R, H - cy - R + 1, Color.black);
                GUI.color = Color.black; GUI.DrawTexture(new Rect(cx - R, cy - R, 2 * R, 2 * R), hole); GUI.color = Color.white;
                GUI.DrawTexture(new Rect(cx - R - 2, cy - R - 2, 2 * R + 4, 2 * R + 4), scopeRing);
                Rect(cx - R, cy - .75f, 2 * R, 1.5f, new Color(.04f, .05f, .06f, .85f)); Rect(cx - .75f, cy - R, 1.5f, 2 * R, new Color(.04f, .05f, .06f, .85f));
                Rect(cx - 1.75f, cy - 1.75f, 3.5f, 3.5f, Web.Hex("#ff2a2a"));
            }
            // RPG optic and Swarm lock-on
            if (optic)
            {
                bool locked = Combat.RayFoes(cam.transform.position, cam.transform.forward, f, out var hf) && hf.t < (id == "rocket" ? 200 : 45) && !Combat.RayBoxes(cam.transform.position, cam.transform.forward, hf.t, out _);
                var red = Web.Hex("#ff2a2a");
                if (id == "rocket")
                {
                    float R = Mathf.Min(W, H) * .44f;
                    Rect(0, 0, cx - R + 1, H, new Color(.03f, .03f, .04f)); Rect(cx + R - 1, 0, W - cx - R + 1, H, new Color(.03f, .03f, .04f));
                    Rect(cx - R, 0, 2 * R, cy - R + 1, new Color(.03f, .03f, .04f)); Rect(cx - R, cy + R - 1, 2 * R, H - cy - R + 1, new Color(.03f, .03f, .04f));
                    GUI.color = new Color(.03f, .03f, .04f); GUI.DrawTexture(new Rect(cx - R, cy - R, 2 * R, 2 * R), hole);
                    GUI.color = locked ? new Color(1, .16f, .16f, .2f) : new Color(1, .67f, .24f, .14f); GUI.DrawTexture(new Rect(cx - R, cy - R, 2 * R, 2 * R), disc); GUI.color = Color.white;
                    var lc = locked ? red : new Color(.08f, .08f, .1f);
                    for (int i = 0; i < 4; i++) { float yy = cy + 8 + i * 14 * R / 100; Rect(cx - (14 - i * 2) * R / 100, yy, (28 - i * 4) * R / 100, 1.6f, lc); }
                    for (int i = -3; i <= 3; i++) Rect(cx + i * 10 * R / 100, cy - 42 * R / 100, 1.6f, (i == 0 ? 10 : 5) * R / 100, lc);
                    Rect(cx - 1.6f, cy - 1.6f, 3.2f, 3.2f, red);
                    if (locked) { GUI.color = red; GUI.DrawTexture(new Rect(cx - 32, cy - 32, 64, 64), ring); GUI.color = Color.white; Shadowed(new Rect(cx - 220, cy - 10, 170, 20), "TARGET " + Mathf.RoundToInt(hf.t) + " m", midR, red); }
                }
                else
                {
                    float b = Mathf.Clamp(H * .06f, 14, 60); var cyan = Web.Hex("#7dfcff"); var edge = locked ? red : Web.Hex("#ff7a1f");
                    Rect(0, 0, W, H, locked ? new Color(1, .16f, .16f, .14f) : new Color(.08f, .7f, .78f, .1f));
                    for (float yy = 0; yy < H; yy += 4) Rect(0, yy, W, 1, new Color(0, 0, 0, .14f));
                    Rect(0, 0, W, b, Color.black); Rect(0, H - b, W, b, Color.black); Rect(0, 0, b, H, Color.black); Rect(W - b, 0, b, H, Color.black);
                    Rect(b, b, W - 2 * b, 3, edge); Rect(b, H - b - 3, W - 2 * b, 3, edge); Rect(b, b, 3, H - 2 * b, edge); Rect(W - b - 3, b, 3, H - 2 * b, edge);
                    float L = cx - 70, T = cy - 90, Wd = 140, Ht = 180;
                    if (locked)
                    {
                        var a = cam.WorldToScreenPoint(hf.who.pos + Vector3.up * (2.5f * Fighter.SC)); var b2 = cam.WorldToScreenPoint(hf.who.pos);
                        if (a.z > 0 && b2.z > 0) { Ht = Mathf.Max(30, (a.y - b2.y) / s) * 1.1f; Wd = Ht * .62f; L = a.x / s - Wd / 2; T = (Screen.height - a.y) / s - Ht * .05f; }
                    }
                    var bc = locked ? red : cyan;
                    foreach (var (px, py, sx, sy) in new[] { (L, T, 1, 1), (L + Wd, T, -1, 1), (L, T + Ht, 1, -1), (L + Wd, T + Ht, -1, -1) })
                    { Rect(sx > 0 ? px : px - 24, sy > 0 ? py : py - 3, 24, 3, bc); Rect(sx > 0 ? px : px - 3, sy > 0 ? py : py - 24, 3, 24, bc); }
                    Shadowed(new Rect(cx - 60, b + 12, 120, 22), locked ? "LOCK" : "SEARCH", midC, locked ? red : cyan);
                    if (locked) Shadowed(new Rect(cx - 60, H - b - 36, 120, 22), Mathf.RoundToInt(hf.t) + " m", midC, red);
                    int am = w.Ammo("swarm"), left = am > 4 ? am - 4 : am;
                    for (int i = 0; i < 4; i++) { float yy = cy - 52 + i * 26, xx = b + 14; Rect(xx, yy, 18, 18, Web.Hex("#ff4a3d")); if (i >= left) Rect(xx + 2, yy + 2, 14, 14, new Color(.05f, .05f, .06f)); }
                }
            }

            // crosshair
            if (!matchHud || matchHud.drawCrosshair) HideMatchCrosshair();
            if (g != null && !w.Busy && !scoped && !(optic))
            {
                float spr = w.CurSpread(), px = Mathf.Tan(spr * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2) * Screen.height / 2 / s;
                float gap = g.Shotgun ? Mathf.Clamp(px * .45f, 5, 14) : Mathf.Max(4, px);
                float op = id == "duals" || (id == "sting" && f.ads) ? 1 : Mathf.Clamp01(1 - w.vmRaise * 1.6f);
                bool tgt = OnTarget(f, g); var cc = tgt ? Web.Hex("#ff2a2a") : id == "sting" && f.ads ? Web.Hex("#25e0cf") : Color.white; cc.a = op;
                var sh = new Color(.04f, .04f, .055f, .85f * op);
                if (id == "sting" && f.ads && w.vmRaise > .3f)
                {   // chevron
                    var mm = GUI.matrix; GUIUtility.RotateAroundPivot(45, new Vector2(cx, cy) * s);
                    Rect(cx - 8, cy - 8, 16, 3, cc); Rect(cx - 8, cy - 8, 3, 16, cc); GUI.matrix = mm;
                }
                else if (id == "rocket") { GUI.color = sh; GUI.DrawTexture(new Rect(cx - gap - 1.5f, cy - gap - 1.5f, 2 * gap + 3, 2 * gap + 3), ring); GUI.color = cc; GUI.DrawTexture(new Rect(cx - gap, cy - gap, 2 * gap, 2 * gap), ring); GUI.color = Color.white; }
                else if (g.Sniper) { Rect(cx - 4, cy - 4, 8, 8, sh); Rect(cx - 3, cy - 3, 6, 6, cc); }
                else
                {
                    if (id != "sword")
                        foreach (var (x, y, ww, hh) in new[] { (cx - 1, cy - gap - 10, 2f, 10f), (cx - 1, cy + gap, 2f, 10f), (cx - gap - 10, cy - 1, 10f, 2f), (cx + gap, cy - 1, 10f, 2f) })
                        { Rect(x - 1.5f, y - 1.5f, ww + 3, hh + 3, sh); Rect(x, y, ww, hh, cc); }
                    Rect(cx - 3.5f, cy - 3.5f, 7, 7, sh); Rect(cx - 2, cy - 2, 4, 4, cc);
                }
            }
            // hit marker
            if (hitT < .22f)
            {
                float u = hitT / .22f, a = 1 - u, sc = 1.3f - .3f * u; var c = hitHead ? Accent : Color.white; c.a = a;
                var mm = GUI.matrix;
                foreach (var ang in new[] { 45f, -45f, 135f, -135f })
                {
                    GUI.matrix = mm; GUIUtility.RotateAroundPivot(ang, new Vector2(cx, cy) * s);
                    Rect(cx + 4 * sc - 1, cy - 2.5f, 18 * sc + 2, 5, new Color(.07f, .07f, .07f, a)); Rect(cx + 4 * sc, cy - 1.5f, 18 * sc, 3, c);
                }
                GUI.matrix = mm;
            }

            // flash bang white-out, stun, hurt vignette, low health pulse
            if (f.flashA > 0) Rect(0, 0, W, H, new Color(1, 1, 1, f.flashA));
            if (f.dazeT > 0) Rect(0, 0, W, H, new Color(.85f, .92f, 1, .18f));
            float vig = Mathf.Max(vigA, f.alive && f.hp < 30 ? .35f + Mathf.Sin(Time.time * 6) * .1f : 0);
            if (vig > 0) { GUI.color = new Color(.75f, 0, .05f, vig * .7f); GUI.DrawTexture(new Rect(-W * .25f, -H * .25f, W * 1.5f, H * 1.5f), hole); GUI.color = Color.white; }

            // ammo panel (bottom right)
            {
                float pw = 250, ph = 106, x = W - pw - 18, y = H - ph - 18;
                Rect(x, y, pw, ph, Panel);
                var g1 = GunsDb.Gun(w.primary); var g2 = GunsDb.Gun(w.secondary);
                Shadowed(new Rect(x + 10, y + 6, 115, 20), "1  " + (g1 != null ? g1.name : "-"), mid, w.slot == 0 ? Gold : new Color(1, 1, 1, .55f));
                Shadowed(new Rect(x + 125, y + 6, 120, 20), "2  " + (g2 != null ? g2.name : "-"), mid, w.slot == 1 ? Gold : new Color(1, 1, 1, .55f));
                string am = g == null ? "" : g.InfiniteMag ? "∞" : w.Ammo(id).ToString(), mg = g == null || g.InfiniteMag ? "" : "/ " + Mathf.RoundToInt(g.mag);
                Shadowed(new Rect(x + 10, y + 26, 150, 46), am, big, w.Ammo(id) <= 0 && g != null && !g.InfiniteMag ? Accent : Color.white);
                Shadowed(new Rect(x + 164, y + 50, 80, 20), mg, mid, new Color(1, 1, 1, .7f));
                Shadowed(new Rect(x + 10, y + 74, 230, 18), g != null ? g.name + (g.cat != "" ? "  ·  " + g.cat : "") : "", small, Color.white);
                Rect(x + 10, y + ph - 9, pw - 20, 4, new Color(1, 1, 1, .12f));
                if (w.reloadT > 0) Rect(x + 10, y + ph - 9, (pw - 20) * (1 - w.reloadT / Mathf.Max(.01f, w.reloadMax)), 4, Gold);
                if (id == "flamer") Rect(x + 10, y + ph - 9, (pw - 20) * Mathf.Clamp01(w.Ammo("flamer") / g.mag), 4, Web.Hex("#ff6a1a"));
            }
            // items (above the vitals, bottom left)
            {
                var c = GunsDb.Cons(w.lethal);
                var lines = new List<string> { "E  " + (c != null ? c.@short : "-") + "  x" + w.consN + (w.consT > 0 && w.consN > 0 ? "  ..." : "") };
                if (w.nades > 0 || w.goldNade > 0) lines.Add("T  Grenade x" + w.nades + (w.goldNade > 0 ? "  + Gold" : ""));
                if (w.turretKit > 0) lines.Add("V  Sentry");
                if (f.speedT > 0) lines.Add("Speed  " + Mathf.CeilToInt(f.speedT) + "s");
                float x = 18, y = H - 126 - lines.Count * 20;
                Rect(x, y, 230, lines.Count * 20 + 10, Panel);
                for (int i = 0; i < lines.Count; i++) Shadowed(new Rect(x + 10, y + 5 + i * 20, 220, 20), lines[i], mid, Color.white);
            }
            GUI.Label(new Rect(W - 268, H - 150, 250, 20), "Tab  weapons and shop", smallR);

            if (MenuOpen) Menu(W, H);
            GUI.matrix = m0;
        }

        // ------------------------------------------------------------------ loadout menu
        Vector2 scroll;
        void Menu(float W, float H)
        {
            var w = weapons;
            float mw = Mathf.Min(1180, W - 40), mh = Mathf.Min(720, H - 40), x0 = (W - mw) / 2, y0 = (H - mh) / 2;
            Rect(0, 0, W, H, new Color(0, 0, 0, .55f)); Rect(x0, y0, mw, mh, new Color(.09f, .095f, .11f, .97f));
            if (Event.current.type == EventType.MouseDown) ClickedUi = true;
            Shadowed(new Rect(x0 + 18, y0 + 12, 500, 30), "Weapons", title, Color.white);
            GUI.Label(new Rect(x0 + 18, y0 + 40, mw - 36, 20), "Pick your primary, secondary and lethal (the bot keeps the match kit). Stats are the web game's, after its damage and reload multipliers. Tab or Esc to close.", small);
            float colW = (mw - 36 - 24) / 4, y = y0 + 70;
            float bh = 52;
            scroll = GUI.BeginScrollView(new Rect(x0, y, mw, mh - (y - y0) - 12), scroll, new Rect(0, 0, mw - 20, 12 * (bh + 6) + 40));
            Column(18, colW, "PRIMARY", GunsDb.Primaries, w.primary, id => w.SetLoadout(id, w.secondary == "flamer" ? w.flamerPrev ?? "smg" : w.secondary, w.lethal));
            Column(18 + colW + 8, colW, "SECONDARY", GunsDb.Secondaries, w.secondary == "flamer" ? w.flamerPrev : w.secondary, id => w.SetLoadout(w.primary, id, w.lethal));
            // lethals
            float lx = 18 + 2 * (colW + 8), ly = 0;
            GUI.Label(new Rect(lx, ly, colW, 20), "LETHAL  (E)", head); ly += 22;
            foreach (var id in GunsDb.Lethals)
            {
                var c = GunsDb.Cons(id); var g = GunsDb.Gun(id); if (c == null) continue;
                string t = c.name + "\n" + c.desc + " · " + c.meta + (g != null && g.dmg > 0 ? " · " + Mathf.RoundToInt(g.dmg) + " dmg" : "") + (g != null && g.radius > 0 ? " · " + g.radius + " m" : "");
                if (GUI.Button(new Rect(lx, ly, colW, bh), t, id == w.lethal ? btnOn : btn)) w.SetLoadout(w.primary, w.secondary == "flamer" ? w.flamerPrev ?? "smg" : w.secondary, id);
                ly += bh + 6;
            }
            // shop
            float sx = 18 + 3 * (colW + 8), sy = 0;
            GUI.Label(new Rect(sx, sy, colW, 20), "SHOP  (free while testing)", head); sy += 22;
            foreach (var it in GunsDb.Data.shop)
            {
                string extra = it.id == "nades" ? "  (have " + w.nades + ")" : it.id == "armor" ? "  (have " + Mathf.RoundToInt(w.F.armor) + ")" : "";
                if (GUI.Button(new Rect(sx, sy, colW, bh), it.name + extra + "\n" + it.desc, btn)) w.Buy(it.id);
                sy += bh + 6;
            }
            sy += 10;
            if (GUI.Button(new Rect(sx, sy, colW, 34), "Refill ammo and lethals", btn)) w.ResetKit();
            GUI.EndScrollView();
        }

        void Column(float x, float cw, string title, List<string> ids, string sel, System.Action<string> pick)
        {
            float y = 0, bh = 52;
            GUI.Label(new Rect(x, y, cw, 20), title, head); y += 22;
            foreach (var id in ids)
            {
                var g = GunsDb.Gun(id); if (g == null) continue;
                string stat = g.IsMelee ? Mathf.RoundToInt(g.dmg) + " dmg · " + g.reach + " m reach"
                    : g.IsProj ? Mathf.RoundToInt(g.direct > 0 ? g.direct : g.dmg) + (g.direct > 0 ? " direct, " + Mathf.RoundToInt(g.dmg) + " splash" : " dmg") + (g.burst > 0 ? " x" + g.burst : "") + " · " + Mathf.RoundToInt(g.mag) + " rnd"
                    : Mathf.RoundToInt(g.dmg) + (g.Pellets > 1 ? "x" + g.Pellets : "") + " dmg · " + Mathf.RoundToInt(60f / Mathf.Max(.01f, g.rate)) + " rpm · " + Mathf.RoundToInt(g.mag) + " rnd";
                if (GUI.Button(new Rect(x, y, cw, bh), g.name + "   " + g.cat + "\n" + stat, id == sel ? btnOn : btn)) pick(id);
                y += bh + 6;
            }
        }
    }
}
