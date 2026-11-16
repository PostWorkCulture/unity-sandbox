// The match HUD, laid out like the web game: vitals bottom left (health green, armour blue, stamina purple),
// leaderboard top left, a minimap top right that turns with you (enemies only show as a red dot when they fire,
// a red X where someone went down), score and round timer top centre with the kill feed under it, a hit-direction
// arc round the crosshair, the countdown, a slim round-result bar and the end-of-match screen.
using UnityEngine;

namespace StreetLife
{
    public class Hud : MonoBehaviour
    {
        public Fighter fighter;
        public bool showHelp = true;
        bool helpPinned, helpOff;
        public bool drawCrosshair = true;   // the weapon HUD draws its own
        bool showBot;
        float fps, fpsT; int frames;
        Texture2D white, mapTex;
        GUIStyle label, small, big, huge, center, feedStyle;
        float s;   // UI scale

        void Update()
        {
            frames++; fpsT += Time.unscaledDeltaTime;
            if (fpsT >= .5f) { fps = frames / fpsT; frames = 0; fpsT = 0; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) { helpPinned = !(helpPinned || showHelp); helpOff = !helpPinned; }
            showHelp = helpPinned || (!helpOff && Time.timeSinceLevelLoad < 15f);   // shown for the first 15 s, then F1
            if (Dev.On && kb != null && kb.f2Key.wasPressedThisFrame) showBot = !showBot;
        }

        Rect R(float x, float y, float w, float h) => new Rect(x * s, y * s, w * s, h * s);

        void Fill(Rect r, Color c) => HudRound.Fill(r, c, white);   // rounded corners (HudRound.cs)

        void Bar(float x, float y, float w, float h, float v, Color c)
        {
            Fill(R(x, y, w, h), new Color(0, 0, 0, .55f));
            Fill(R(x + 2, y + 2, (w - 4) * Mathf.Clamp01(v), h - 4), c);
        }

        void Styles()
        {
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            int fs(float px) => Mathf.Max(8, Mathf.RoundToInt(px * s));
            label = new GUIStyle(GUI.skin.label) { fontSize = fs(14), fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = fs(12) };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            big = new GUIStyle(center) { fontSize = fs(25), wordWrap = false, clipping = TextClipping.Overflow };
            huge = new GUIStyle(center) { fontSize = fs(72) };
            feedStyle = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }

        void Shadowed(Rect r, string t, GUIStyle st, Color c)
        {
            var o = st.normal.textColor;
            st.normal.textColor = new Color(0, 0, 0, c.a * .8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, st);
            st.normal.textColor = c; GUI.Label(r, t, st);
            st.normal.textColor = o;
        }

        static string flashTitle, flashSub; static float flashUntil;
        /// a short message in the middle of the screen ("Air strike!")
        public static void Flash(string title, string sub) { flashTitle = title; flashSub = sub; flashUntil = Time.time + 1.4f; }

        void OnGUI()
        {
            if (!fighter) return;
            s = Screen.height / 1080f * 1.4f;
            Styles();
            float W = Screen.width / s, H = Screen.height / s;
            var rm = RoundManager.Instance;
            if (Cine.HideHud) { if (rm && rm.player) Banners(rm, W, H); return; }   // the throne room or Man Down: only the results

            Vitals(H);
            if (fighter.alive) { if (drawCrosshair) Crosshair(W, H); HitArc(W, H); }
            Minimap(W);
            if (rm && rm.player)
            {
                ScoreAndFeed(rm, W);
                Leaderboard(rm);
                Banners(rm, W, H);
                if (showBot && rm && rm.botBrain) GUI.Label(R(18, HudLayout.LeftColumnBottom + 8, 520, 60), $"Bot: {rm.botBrain.Style.name} ({rm.botBrain.Dif.name}), {rm.botBrain.Mode}", small);
            }
            if (showHelp && !(TouchControls.I && TouchControls.I.Showing))
            {
                float hw = HudLayout.HelpWidth;   // clear of the vitals and the ammo panel (HudLayout)
                Fill(R(W / 2 - hw / 2, H - 58, hw, 54), new Color(0, 0, 0, .35f));
                GUI.Label(R(W / 2 - hw / 2, H - 56, hw, 54),
                    "WASD move · Space jump (again for a double jump) · Left click fire · Right click aim · C crouch · walk into a ladder to climb · Esc frees the mouse\n" +
                    "Q builds a wall · E throws your lethal (hold to pick) · Tab at the shop kiosk · F8 frame rate · F1 shows this again", new GUIStyle(small) { alignment = TextAnchor.UpperCenter, wordWrap = true });
            }
        }

        // ------------------------------------------------------------------ vitals
        void Vitals(float H)
        {
            float x = 18, y = HudLayout.VitalsTop + 22;   // bottom left on desktop, under the leaderboard on phones
            Bar(x, y, 230, 18, fighter.hp / fighter.hpMax, new Color(.27f, .85f, .38f));
            Bar(x, y + 22, 230, 12, fighter.armor / 100f, new Color(.25f, .6f, 1f));
            Bar(x, y + 38, 230, 12, fighter.stam / Fighter.STAM_MAX, fighter.stamLock ? new Color(.45f, .3f, .55f) : new Color(.62f, .38f, 1f));
            Shadowed(R(x, y - 22, 300, 20), fighter.characterName + "   " + Mathf.CeilToInt(fighter.hp), label, Color.white);
            ExtrasInfo(x, y, H);
        }

        /// walls left, the shop kiosk, HeartBeat and tagging (match chat)
        void ExtrasInfo(float x, float y, float H)
        {
            if (!fighter.alive) return;
            float W = Screen.width / s;
            if (Walls.On && !(TouchControls.I && TouchControls.I.Showing)) Shadowed(R(x, Mathf.Min(y - 44, HudLayout.ItemsTop - 22), 300, 20), fighter.walls > 0 ? $"Q  wall  x{fighter.walls}" : "no walls left", small, new Color(1, 1, 1, fighter.walls > 0 ? .8f : .4f));
            string msg = null; var col = Color.white;
            if (fighter.downed && !fighter.InFinisher) { msg = "Down  ·  fire to tag an enemy (seen through walls for 3 s)"; col = new Color(1f, .55f, .6f); }
            else if (Intel.EnemySensing(fighter)) { msg = "An enemy used HeartBeat: they can see you"; col = new Color(1f, .45f, .45f); }
            else if (fighter.senseT > 0) { msg = "HeartBeat  " + Mathf.CeilToInt(fighter.senseT) + "s"; col = new Color(.5f, .95f, 1f); }
            else if (Kiosk.Near(fighter)) { msg = fighter.shopCD > 0 ? "Shop ready in " + Mathf.CeilToInt(fighter.shopCD) + "s" : (TouchControls.I && TouchControls.I.Showing ? "Shop open: tap Shop" : "Shop: press Tab, pick one item"); col = new Color(1f, .85f, .35f); }
            HudLayout.MessageShown = msg != null;
            float my = HudLayout.Touch ? HudLayout.MessageY : H - 150;   // phones: in the top-centre stack, clear of the Finish button
            float mw = HudLayout.Touch ? HudLayout.StackW(520) : 520, mx = (HudLayout.Touch ? HudLayout.StackX : W / 2) - mw / 2;
            if (msg != null) { Fill(R(mx, my, mw, 30), new Color(0, 0, 0, .45f)); Shadowed(R(mx, my + 3, mw, 26), msg, center, col); }
        }

        void FrostEdges(float k)
        {
            float W = Screen.width / s, H = Screen.height / s; var c = new Color(.75f, .92f, 1f, .22f * k);
            for (int i = 0; i < 6; i++) { float t = 6 + i * 7; var ci = new Color(c.r, c.g, c.b, c.a * (1 - i / 6f));
                Fill(R(0, i * t / 6, W, t / 6), ci); Fill(R(0, H - (i + 1) * t / 6, W, t / 6), ci); Fill(R(i * t / 6, 0, t / 6, H), ci); Fill(R(W - (i + 1) * t / 6, 0, t / 6, H), ci); }
        }

        void Crosshair(float W, float H)
        {
            float cx = W / 2, cy = H / 2; var c = new Color(1, 1, 1, .9f);
            Fill(R(cx - 1, cy - 9, 2, 6), c); Fill(R(cx - 1, cy + 3, 2, 6), c);
            Fill(R(cx - 9, cy - 1, 6, 2), c); Fill(R(cx + 3, cy - 1, 6, 2), c);
        }

        /// A short red arc round the crosshair pointing at whoever just hit you, turning as you turn.
        void HitArc(float W, float H)
        {
            float age = Time.time - fighter.lastHitTime;
            if (age > .8f || fighter.lastBy == null) return;
            var d = fighter.lastHitFrom - fighter.pos; d.y = 0;
            if (d.sqrMagnitude < .01f) return;
            float ang = Vector3.SignedAngle(fighter.Forward, d.normalized, Vector3.up);
            var m = GUI.matrix;
            var pivot = new Vector2(W / 2 * s, H / 2 * s);
            float a = 1 - age / .8f;
            for (int i = -2; i <= 2; i++)
            {
                GUI.matrix = m;
                GUIUtility.RotateAroundPivot(ang + i * 6, pivot);
                Fill(new Rect(pivot.x - 7 * s, pivot.y - 58 * s, 14 * s, 4 * s), new Color(1, .15f, .1f, a * (1 - Mathf.Abs(i) * .18f)));
            }
            GUI.matrix = m;
        }

        // ------------------------------------------------------------------ minimap
        void BuildMap()
        {
            var bd = BoxWorld.Bounds; const float k = 4f;
            int w = Mathf.CeilToInt((bd.x1 - bd.x0) * k), h = Mathf.CeilToInt((bd.z1 - bd.z0) * k);
            mapTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            var ground = new Color32(46, 84, 44, 215);
            for (int i = 0; i < px.Length; i++) px[i] = ground;
            var boxes = new System.Collections.Generic.List<Box>(BoxWorld.Boxes);
            boxes.Sort((a, b) => a.y1.CompareTo(b.y1));
            foreach (var b in boxes)
            {
                if (b.y1 < .15f) continue;
                Color c = b.glass ? new Color(.6f, .82f, .95f) : b.prop ? new Color(.62f, .6f, .56f) : Color.Lerp(new Color(.42f, .43f, .46f), new Color(.92f, .92f, .9f), Mathf.Clamp01(b.y1 / 14f));
                int x0 = Mathf.Clamp(Mathf.FloorToInt((b.x0 - bd.x0) * k), 0, w - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((b.x1 - bd.x0) * k), 0, w - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((b.z0 - bd.z0) * k), 0, h - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((b.z1 - bd.z0) * k), 0, h - 1);
                var c32 = (Color32)new Color(c.r, c.g, c.b, .95f);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) px[x + z * w] = c32;
            }
            foreach (var L in BoxWorld.Ladders)
            {
                int x0 = Mathf.Clamp(Mathf.FloorToInt((L.x0 - bd.x0) * k), 0, w - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((L.x1 - bd.x0) * k), 0, w - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((L.z0 - bd.z0) * k), 0, h - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((L.z1 - bd.z0) * k), 0, h - 1);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) px[x + z * w] = new Color32(255, 214, 64, 255);
            }
            mapTex.SetPixels32(px); mapTex.Apply();
        }

        Material miniMat;
        public static bool MiniPlain;   // remote check: "call Hud.MiniCheck plain" draws the old way to compare
        public static string MiniCheck(string a) { MiniPlain = a.Trim() == "plain"; if (a.Trim() == "flip") MiniFlipY = 1 - MiniFlipY; return "minimap " + (MiniPlain ? "plain" : "shader") + " flipY " + MiniFlipY; }
        public static float MiniFlipY = 0;   // set to 1 if the minimap ever shows upside down (Graphics.DrawTexture's uv.y in OnGUI)
        void Minimap(float W)
        {
            if (!BoxWorld.Loaded) return;
            if (!mapTex) BuildMap();
            var bd = BoxWorld.Bounds; const float k = 2.4f;   // UI px per metre
            float size = 190;
            var area = R(W - size - 14, 14, size, size);
            var me = fighter.VisualPos;
            // the map picture: turned round you and cut to a rounded square by a shader (no sharp corners); the plain
            // square version is the fallback if the shader is missing
            if (!miniMat) { var sh = Resources.Load<Shader>("StreetLife/HudMinimap"); if (sh) miniMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave }; }
            bool shaded = miniMat && GUI.matrix.isIdentity && !MiniPlain;
            if (shaded && Event.current.type == EventType.Repaint)
            {
                float spanX = (bd.x1 - bd.x0) * k * s, spanZ = (bd.z1 - bd.z0) * k * s;
                miniMat.SetVector("_Size", new Vector4(area.width, area.height, 22 * s, 1.5f * s));
                miniMat.SetVector("_Map", new Vector4((me.x - bd.x0) / (bd.x1 - bd.x0), (me.z - bd.z0) / (bd.z1 - bd.z0), 1f / spanX, 1f / spanZ));
                miniMat.SetFloat("_Rot", fighter.yaw);
                miniMat.SetFloat("_FlipY", MiniFlipY);
                miniMat.SetVector("_MapPx", new Vector4(spanX, spanZ, Mathf.Min(14 * s, Mathf.Min(spanX, spanZ) * .5f), 0));
                Graphics.DrawTexture(area, mapTex, miniMat);
            }
            GUI.BeginGroup(area);   // no square backing: only the rotating map shows (Pete, 13 Nov)
            var m = GUI.matrix;
            var c = new Vector2(area.width / 2, area.height / 2);
            Vector2 ToMap(Vector3 p) => new Vector2(c.x + (p.x - me.x) * k * s, c.y - (p.z - me.z) * k * s);
            GUIUtility.RotateAroundPivot(-fighter.yaw * Mathf.Rad2Deg, c);
            var tl = ToMap(new Vector3(bd.x0, 0, bd.z1));
            if (!shaded) GUI.DrawTexture(new Rect(tl.x, tl.y, (bd.x1 - bd.x0) * k * s, (bd.z1 - bd.z0) * k * s), mapTex, ScaleMode.StretchToFill, true, 0, Color.white, Vector4.zero, Vector4.one * 14 * s);
            var rm = RoundManager.Instance;
            if (rm)
            {
                // enemies only show where they fired from (fading over two seconds)
                foreach (var f in rm.fighters)
                {
                    if (!f || f == fighter) continue;
                    if (f.team == fighter.team) { if (f.alive) { var ap = ToMap(f.VisualPos); Fill(new Rect(ap.x - 3.5f * s, ap.y - 3.5f * s, 7 * s, 7 * s), new Color(.35f, .8f, 1f, .95f)); } continue; }   // team-mates always show
                    float age = Time.time - f.lastShotTime;
                    if (age < 2f) { var p = ToMap(f.lastShotPos); Fill(new Rect(p.x - 3 * s, p.y - 3 * s, 6 * s, 6 * s), new Color(1, .2f, .15f, 1 - age / 2)); }
                }
                foreach (var (p, t) in rm.deathMarks)
                {
                    var q = ToMap(p); var mm = GUI.matrix;
                    GUIUtility.RotateAroundPivot(45, q); Fill(new Rect(q.x - 6 * s, q.y - 1.5f * s, 12 * s, 3 * s), new Color(1, .1f, .1f, .9f));
                    GUIUtility.RotateAroundPivot(90, q); Fill(new Rect(q.x - 6 * s, q.y - 1.5f * s, 12 * s, 3 * s), new Color(1, .1f, .1f, .9f));
                    GUI.matrix = mm;
                }
            }
            GUI.matrix = m;
            // you: an arrow in the middle pointing up
            Fill(new Rect(c.x - 3 * s, c.y - 7 * s, 6 * s, 12 * s), new Color(.3f, .75f, 1f));
            Fill(new Rect(c.x - 6 * s, c.y - 2 * s, 12 * s, 4 * s), new Color(.3f, .75f, 1f));
            GUI.EndGroup();
        }

        // ------------------------------------------------------------------ score, feed, leaderboard
        void ScoreAndFeed(RoundManager rm, float W)
        {
            int me = fighter.team, them = 1 - me; foreach (var o in rm.fighters) if (o.team != me) { them = o.team; break; }
            string t = rm.state == RoundManager.State.Fight || rm.state == RoundManager.State.RoundEnd ? $"{Mathf.FloorToInt(Mathf.Max(0, rm.timer) / 60)}:{Mathf.FloorToInt(Mathf.Max(0, rm.timer) % 60):00}" : "";
            Fill(R(W / 2 - 170, 8, 340, 58), new Color(0, 0, 0, .45f));
            string head;
            if (rm.Deathmatch) { int best = 0; foreach (var o in rm.fighters) if (o != fighter) best = Mathf.Max(best, o.kills); head = $"YOU  {fighter.kills}   ·   BEST BOT  {best}"; }
            else if (Rules.TwoVTwo) head = $"YOUR TEAM  {rm.score[me]}  :  {rm.score[them]}  THEIRS";
            else head = $"YOU  {rm.score[me]}  :  {rm.score[them]}  {(rm.bot ? rm.bot.displayName.ToUpperInvariant() : "BOT")}";
            Shadowed(R(W / 2 - 170, 8, 340, 36), head, big, Color.white);
            Shadowed(R(W / 2 - 170, 42, 340, 20), $"{t}   First to {rm.toWin}{(rm.Deathmatch ? " kills" : "")}", center, new Color(1, 1, 1, .85f));
            float y = HudLayout.FeedTop;   // under the score, the finisher banner, the round result and (phones) the message (HudLayout)
            for (int fi = Mathf.Max(0, rm.feed.Count - 4); fi < rm.feed.Count; fi++)   // the newest 4 (FFA gets busy)
            {
                var it = rm.feed[fi];
                float a = Mathf.Clamp01(6 - (Time.time - it.t)) * .65f;
                Fill(R(HudLayout.StackX - HudLayout.StackW(340) / 2, y, HudLayout.StackW(340), 20), new Color(0, 0, 0, .4f * a / .65f));
                Shadowed(R(HudLayout.StackX - HudLayout.StackW(340) / 2, y, HudLayout.StackW(340), 20), it.text, feedStyle, it.mine ? new Color(1, .85f, .3f, a / .65f) : new Color(1, 1, 1, a / .65f));
                y += 22;
            }
            HudLayout.FeedBottom = y;
        }

        void Leaderboard(RoundManager rm)
        {
            float x = 18, y = 16;
            Fill(R(x - 6, y - 4, 230, 26 + 20 * rm.fighters.Count), new Color(0, 0, 0, .35f));
            HudLayout.LeaderboardBottom = y - 4 + 26 + 20 * rm.fighters.Count;
            GUI.Label(R(x, y, 120, 18), "Player", small); GUI.Label(R(x + 130, y, 90, 18), "K   A   D", small);
            y += 20;
            var list = new System.Collections.Generic.List<Fighter>(rm.fighters); list.Sort((a, b) => b.kills != a.kills ? b.kills.CompareTo(a.kills) : a.deaths.CompareTo(b.deaths));
            foreach (var f in list)
            {
                var col = f.alive ? (f.team == fighter.team ? new Color(.55f, .85f, 1f) : new Color(1f, .55f, .5f)) : new Color(.6f, .6f, .6f);
                Shadowed(R(x, y, 120, 18), f.displayName, label, col);
                Shadowed(R(x + 130, y, 120, 18), $"{f.kills}   {f.assists}   {f.deaths}", label, col);
                y += 20;
            }
        }

        // ------------------------------------------------------------------ countdown, round result, match end
        void Banners(RoundManager rm, float W, float H)
        {
            HudLayout.RoundBarShown = rm.state == RoundManager.State.RoundEnd;
            switch (rm.state)
            {
                case RoundManager.State.Intro:
                    Fill(R(0, H / 2 - 90, W, 180), new Color(0, 0, 0, .55f));
                    Shadowed(R(0, H / 2 - 70, W, 90), MapSet.CurrentEntry != null && !string.IsNullOrEmpty(MapSet.CurrentEntry.title) ? MapSet.CurrentEntry.title : "Street Life", huge, Color.white);
                    Shadowed(R(0, H / 2 + 20, W, 40), rm.Deathmatch ? $"{Rules.Tag}   ·   {rm.fighters.Count} players   ·   First to {rm.toWin} kills" : $"Round 1   ·   {Rules.Tag}   ·   First to {rm.toWin}", big, new Color(1, 1, 1, .85f));
                    break;
                case RoundManager.State.Countdown:
                    Shadowed(R(0, H / 2 - 120, W, 100), Mathf.CeilToInt(rm.stateT).ToString(), huge, Color.white);
                    Shadowed(R(0, H / 2 - 30, W, 40), rm.Deathmatch ? "Fight!" : "Round " + rm.round, big, new Color(1, 1, 1, .9f));
                    break;
                case RoundManager.State.RoundEnd:
                    {
                        string other = rm.Deathmatch && rm.LastWinnerFighter ? rm.LastWinnerFighter.displayName : Rules.TwoVTwo ? "the other team" : rm.bot ? rm.bot.displayName : "the bot";
                        string msg = rm.lastWinner < 0 ? (rm.timer <= 0 ? "Draw: time ran out" : "Draw") : rm.lastWinner == fighter.team ? (Rules.TwoVTwo ? "Round to your team" : "Round to you") : "Round to " + other;
                        if (rm.timer <= 0 && rm.lastWinner >= 0) msg += " (more health left)";
                        if (rm.matchOver) msg = rm.lastWinner == fighter.team ? "Match point won!" : rm.Deathmatch ? other + " wins" : "Match lost";
                        float ry = HudLayout.RoundBarY;   // centred under the score (and the finisher banner), not across the minimap
                        float rw = HudLayout.StackW(520), rx = HudLayout.StackX - rw / 2;
                        Fill(R(rx, ry, rw, 44), new Color(0, 0, 0, .55f));
                        Shadowed(R(rx, ry, rw, 44), msg, big, rm.matchOver || rm.lastWinner < 0 ? Color.white : rm.lastWinner == fighter.team ? new Color(.5f, 1f, .55f) : new Color(1f, .5f, .45f));
                    }
                    break;
                case RoundManager.State.MatchEnd:
                    {
                        bool won = rm.PlayerWonMatch;
                        Fill(R(0, 0, W, H), new Color(0, 0, 0, Crowning.Active ? .3f : .55f));   // lighter over the throne room
                        bool cr = Crowning.Active; float c0 = cr ? (HudLayout.Touch ? 380 : 210) : H / 2;   // over the throne room: up top, the thrones below
                        Shadowed(R(0, c0 - 190, W, 100), won ? "You caught the W!" : "Took the L this time :(", huge, won ? Color.white : new Color(1, 1, 1, .7f));
                        int oth = 0; foreach (var o in rm.fighters) if (o.team != fighter.team) oth = Mathf.Max(oth, rm.score[o.team]);
                        Shadowed(R(0, c0 - 80, W, 50), rm.Deathmatch ? $"{fighter.kills} kills   ·   {(rm.LastWinnerFighter ? rm.LastWinnerFighter.displayName : "")} wins" : $"{rm.score[fighter.team]}  -  {oth}", big, Color.white);
                        var f = fighter;
                        int acc = f.shots > 0 ? Mathf.RoundToInt(100f * f.hits / f.shots) : 0;
                        string[] k = { "Kills", "Damage", "Accuracy", "Headshots" };
                        string[] v = { f.kills.ToString(), Mathf.RoundToInt(f.damageDealt).ToString(), acc + "%", f.headshots.ToString() };
                        float x0 = W / 2 - 300;
                        for (int i = 0; i < 4 && !cr; i++)   // (the kills and damage are on the thrones)
                        {
                            Shadowed(R(x0 + i * 150, H / 2 - 10, 150, 50), v[i], big, Color.white);
                            Shadowed(R(x0 + i * 150, H / 2 + 36, 150, 24), k[i], center, new Color(1, 1, 1, .8f));
                        }
                        Shadowed(R(0, cr ? c0 - 25 : H / 2 + 100, W, 30), $"{(TouchControls.I && TouchControls.I.Showing ? "Tap Rematch" : "Enter: rematch")}   (starts by itself in {Mathf.CeilToInt(rm.stateT)} s)", center, new Color(1, 1, 1, .85f));
                    }
                    break;
            }
            if (!fighter.alive && rm.state == RoundManager.State.Fight) Shadowed(R(0, H / 2 + 60, W, 30), rm.Deathmatch ? "Respawning in " + Mathf.CeilToInt(rm.RespawnIn(fighter)) : Rules.TwoVTwo ? "You're out: your team-mate is still in" : "You're out", big, new Color(1, .5f, .45f));
            if (Time.time < flashUntil) { float a = Mathf.Clamp01((flashUntil - Time.time) / .4f); Shadowed(R(0, H / 2 - 160, W, 60), flashTitle, huge, new Color(1f, .85f, .3f, a)); Shadowed(R(0, H / 2 - 95, W, 30), flashSub, big, new Color(1, 1, 1, a)); }
        }
    }
}
