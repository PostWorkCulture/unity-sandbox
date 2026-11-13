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
            if (kb != null && kb.f2Key.wasPressedThisFrame) showBot = !showBot;
        }

        Rect R(float x, float y, float w, float h) => new Rect(x * s, y * s, w * s, h * s);

        void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, white); GUI.color = Color.white; }

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

        void OnGUI()
        {
            if (!fighter) return;
            s = Screen.height / 1080f * 1.4f;
            Styles();
            float W = Screen.width / s, H = Screen.height / s;
            var rm = RoundManager.Instance;

            Vitals(H);
            if (fighter.alive) { if (drawCrosshair) Crosshair(W, H); HitArc(W, H); }
            Minimap(W);
            if (rm && rm.player && rm.bot)
            {
                ScoreAndFeed(rm, W);
                Leaderboard(rm);
                Banners(rm, W, H);
                if (showBot && rm.botBrain) GUI.Label(R(18, 120, 520, 60), $"Bot: {rm.botBrain.Style.name} ({rm.botBrain.Dif.name}), {rm.botBrain.Mode}", small);
            }
            if (showHelp && !(TouchControls.I && TouchControls.I.Showing))
            {
                Fill(R(W / 2 - 470, H - 50, 940, 44), new Color(0, 0, 0, .35f));
                GUI.Label(R(W / 2 - 470, H - 48, 940, 44),
                    "WASD move · Space jump (again for a double jump) · Left click fire · Right click aim · C crouch · G ability · walk into a ladder to climb · Esc frees the mouse\n" +
                    "Tab weapons and shop · F3 characters · F2 bot's style · F8 frame rate · F1 shows this again", new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
            }
        }

        // ------------------------------------------------------------------ vitals
        void Vitals(float H)
        {
            float x = 18, y = H - 84;
            Bar(x, y, 230, 18, fighter.hp / fighter.hpMax, new Color(.27f, .85f, .38f));
            Bar(x, y + 22, 230, 12, fighter.armor / 100f, new Color(.25f, .6f, 1f));
            Bar(x, y + 38, 230, 12, fighter.stam / Fighter.STAM_MAX, fighter.stamLock ? new Color(.45f, .3f, .55f) : new Color(.62f, .38f, 1f));
            Shadowed(R(x, y - 22, 300, 20), fighter.characterName + "   " + Mathf.CeilToInt(fighter.hp), label, Color.white);
            AbilityChip(x + 242, y - 2, H);
        }

        /// the character ability: name, G, and the cooldown filling up; status timers above it
        void AbilityChip(float x, float y, float H)
        {
            if (!Fighter.AbilitiesOn || !fighter.alive) return;
            float cd = fighter.AbilityCooldown, left = Mathf.Max(0, fighter.abilT); bool ready = left <= 0 && !fighter.downed;
            Fill(R(x, y, 150, 54), new Color(0, 0, 0, .55f));
            if (!ready) Fill(R(x + 2, y + 2, 146 * (1 - left / Mathf.Max(.1f, cd)), 50), new Color(1, 1, 1, .12f));
            else Fill(R(x, y, 3, 54), new Color(1f, .78f, .1f, .95f));
            Shadowed(R(x + 10, y + 4, 140, 22), fighter.AbilityName, label, ready ? Color.white : new Color(1, 1, 1, .55f));
            Shadowed(R(x + 10, y + 27, 140, 22), ready ? (TouchControls.I && TouchControls.I.Showing ? "ready" : "G  ready") : Mathf.CeilToInt(left) + "s", small, ready ? new Color(1f, .82f, .3f) : new Color(1, 1, 1, .6f));
            string st = fighter.shieldT > 0 ? "Shield " + Mathf.CeilToInt(fighter.shieldT) + "s" : fighter.thornT > 0 ? "Thorns " + Mathf.CeilToInt(fighter.thornT) + "s" : fighter.ghostT > 0 ? "Vanished " + Mathf.CeilToInt(fighter.ghostT) + "s" : fighter.zipT > 0 ? "Sprint " + Mathf.CeilToInt(fighter.zipT) + "s" : fighter.boostT > 0 ? "Jetpack" : null;
            if (st != null) Shadowed(R(x, y - 22, 200, 20), st, small, new Color(.75f, .95f, 1f));
            if (fighter.slowT > 0) FrostEdges(Mathf.Clamp01(fighter.slowT / 1.5f));
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

        void Minimap(float W)
        {
            if (!BoxWorld.Loaded) return;
            if (!mapTex) BuildMap();
            var bd = BoxWorld.Bounds; const float k = 2.4f;   // UI px per metre
            float size = 190;
            var area = R(W - size - 14, 14, size, size);
            Fill(area, new Color(0, 0, 0, .45f));
            GUI.BeginGroup(area);
            var m = GUI.matrix;
            var c = new Vector2(area.width / 2, area.height / 2);
            var me = fighter.VisualPos;
            Vector2 ToMap(Vector3 p) => new Vector2(c.x + (p.x - me.x) * k * s, c.y - (p.z - me.z) * k * s);
            GUIUtility.RotateAroundPivot(-fighter.yaw * Mathf.Rad2Deg, c);
            var tl = ToMap(new Vector3(bd.x0, 0, bd.z1));
            GUI.DrawTexture(new Rect(tl.x, tl.y, (bd.x1 - bd.x0) * k * s, (bd.z1 - bd.z0) * k * s), mapTex);
            var rm = RoundManager.Instance;
            if (rm)
            {
                // enemies only show where they fired from (fading over two seconds)
                foreach (var f in new[] { rm.player, rm.bot })
                {
                    if (!f || f == fighter || f.team == fighter.team) continue;
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
            int me = fighter.team, them = 1 - me;
            string t = rm.state == RoundManager.State.Fight || rm.state == RoundManager.State.RoundEnd ? $"{Mathf.FloorToInt(Mathf.Max(0, rm.timer) / 60)}:{Mathf.FloorToInt(Mathf.Max(0, rm.timer) % 60):00}" : "";
            Fill(R(W / 2 - 170, 8, 340, 58), new Color(0, 0, 0, .45f));
            Shadowed(R(W / 2 - 170, 8, 340, 36), $"YOU  {rm.score[me]}  :  {rm.score[them]}  {(rm.bot ? rm.bot.displayName.ToUpperInvariant() : "BOT")}", big, Color.white);
            Shadowed(R(W / 2 - 170, 42, 340, 20), $"{t}   First to {rm.toWin}", center, new Color(1, 1, 1, .85f));
            float y = 62;
            foreach (var it in rm.feed)
            {
                float a = Mathf.Clamp01(6 - (Time.time - it.t)) * .65f;
                Fill(R(W / 2 - 170, y, 340, 20), new Color(0, 0, 0, .4f * a / .65f));
                Shadowed(R(W / 2 - 170, y, 340, 20), it.text, feedStyle, it.mine ? new Color(1, .85f, .3f, a / .65f) : new Color(1, 1, 1, a / .65f));
                y += 22;
            }
        }

        void Leaderboard(RoundManager rm)
        {
            float x = 18, y = 16;
            Fill(R(x - 6, y - 4, 230, 66), new Color(0, 0, 0, .35f));
            GUI.Label(R(x, y, 120, 18), "Player", small); GUI.Label(R(x + 130, y, 90, 18), "K   A   D", small);
            y += 20;
            foreach (var f in new[] { rm.player, rm.bot })
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
            switch (rm.state)
            {
                case RoundManager.State.Intro:
                    Fill(R(0, H / 2 - 90, W, 180), new Color(0, 0, 0, .55f));
                    Shadowed(R(0, H / 2 - 70, W, 90), "Street Life", huge, Color.white);
                    Shadowed(R(0, H / 2 + 20, W, 40), $"Round 1   ·   1v1   ·   First to {rm.toWin}", big, new Color(1, 1, 1, .85f));
                    break;
                case RoundManager.State.Countdown:
                    Shadowed(R(0, H / 2 - 120, W, 100), Mathf.CeilToInt(rm.stateT).ToString(), huge, Color.white);
                    Shadowed(R(0, H / 2 - 30, W, 40), "Round " + rm.round, big, new Color(1, 1, 1, .9f));
                    break;
                case RoundManager.State.RoundEnd:
                    {
                        string msg = rm.lastWinner < 0 ? (rm.timer <= 0 ? "Draw: time ran out" : "Draw") : rm.lastWinner == fighter.team ? "Round to you" : "Round to the bot";
                        if (rm.timer <= 0 && rm.lastWinner >= 0) msg += " (more health left)";
                        if (rm.matchOver) msg = rm.lastWinner == fighter.team ? "Match point won!" : "Match lost";
                        Fill(R(0, 118, W, 44), new Color(0, 0, 0, .55f));
                        Shadowed(R(0, 118, W, 44), msg, big, rm.lastWinner == fighter.team ? new Color(.5f, 1f, .55f) : rm.lastWinner < 0 ? Color.white : new Color(1f, .5f, .45f));
                    }
                    break;
                case RoundManager.State.MatchEnd:
                    {
                        bool won = rm.score[fighter.team] >= rm.toWin;
                        Fill(R(0, 0, W, H), new Color(0, 0, 0, .55f));
                        Shadowed(R(0, H / 2 - 190, W, 100), won ? "You caught the W!" : "Took the L this time :(", huge, won ? new Color(1f, .85f, .3f) : new Color(1f, .55f, .5f));
                        Shadowed(R(0, H / 2 - 80, W, 50), $"{rm.score[fighter.team]}  -  {rm.score[1 - fighter.team]}", big, Color.white);
                        var f = fighter;
                        int acc = f.shots > 0 ? Mathf.RoundToInt(100f * f.hits / f.shots) : 0;
                        string[] k = { "Kills", "Damage", "Accuracy", "Headshots" };
                        string[] v = { f.kills.ToString(), Mathf.RoundToInt(f.damageDealt).ToString(), acc + "%", f.headshots.ToString() };
                        float x0 = W / 2 - 300;
                        for (int i = 0; i < 4; i++)
                        {
                            Shadowed(R(x0 + i * 150, H / 2 - 10, 150, 50), v[i], big, Color.white);
                            Shadowed(R(x0 + i * 150, H / 2 + 36, 150, 24), k[i], center, new Color(1, 1, 1, .8f));
                        }
                        Shadowed(R(0, H / 2 + 100, W, 30), $"{(TouchControls.I && TouchControls.I.Showing ? "Tap Rematch" : "Enter: rematch")}   (starts by itself in {Mathf.CeilToInt(rm.stateT)} s)", center, new Color(1, 1, 1, .85f));
                    }
                    break;
            }
            if (!fighter.alive && rm.state == RoundManager.State.Fight) Shadowed(R(0, H / 2 + 60, W, 30), "You're out", big, new Color(1, .5f, .45f));
        }
    }
}
