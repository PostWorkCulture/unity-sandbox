// HUD: vitals panel (health, armour, stamina) bottom left like the web game, the weapon panel bottom right,
// a crosshair that opens with the spread and turns red over an enemy in range, hit markers, damage numbers,
// name tags, the flash-bang white-out, and the loadout picker (Tab): every character and weapon from the web game.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class Hud : MonoBehaviour
    {
        public Fighter fighter;
        public Arsenal arsenal;
        public CharacterRig rig;
        public Armory armory;
        public PracticeRange range;
        public bool showHelp = true;
        float fps, fpsT; int frames;
        Texture2D white;
        GUIStyle label, small, big, btn, btnOn, head, wrap;

        public static bool PickerOpen;
        static float hitT, hitBig, flashA;
        struct Num { public Vector3 pos; public int dmg; public bool head; public float t; }
        static readonly List<Num> nums = new List<Num>();

        public static void DamageNumber(Vector3 at, int dmg, bool head) { if (dmg > 0) nums.Add(new Num { pos = at, dmg = dmg, head = head, t = 0 }); if (nums.Count > 30) nums.RemoveAt(0); }
        public static void HitMarker(bool big) { hitT = .18f; hitBig = big ? 1 : 0; }
        public static void Flash(float secs) { flashA = Mathf.Max(flashA, secs); }

        void Update()
        {
            frames++; fpsT += Time.unscaledDeltaTime;
            if (fpsT >= .5f) { fps = frames / fpsT; frames = 0; fpsT = 0; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) showHelp = !showHelp;
            if (kb != null && kb.tabKey.wasPressedThisFrame) SetPicker(!PickerOpen);
            if (PickerOpen && kb != null && kb.escapeKey.wasPressedThisFrame) SetPicker(false);
            hitT -= Time.deltaTime; flashA = Mathf.Max(0, flashA - Time.deltaTime);
            for (int i = nums.Count - 1; i >= 0; i--) { var n = nums[i]; n.t += Time.deltaTime; n.pos += Vector3.up * Time.deltaTime * .8f; nums[i] = n; if (n.t > .9f) nums.RemoveAt(i); }
        }

        void SetPicker(bool on)
        {
            PickerOpen = on;
            Cursor.lockState = on ? CursorLockMode.None : Cursor.lockState; Cursor.visible = on || Cursor.lockState != CursorLockMode.Locked;
        }

        void Bar(float x, float y, float w, float h, float v, Color c)
        {
            GUI.color = new Color(0, 0, 0, .55f); GUI.DrawTexture(new Rect(x, y, w, h), white);
            GUI.color = c; GUI.DrawTexture(new Rect(x + 2, y + 2, (w - 4) * Mathf.Clamp01(v), h - 4), white);
            GUI.color = Color.white;
        }
        void Box(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, white); GUI.color = Color.white; }

        void Styles()
        {
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            wrap = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            head = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1, .82f, .3f) } };
            btn = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 4, 3, 3) };
            btnOn = new GUIStyle(btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.black, background = white }, hover = { textColor = Color.black, background = white } };
        }

        void OnGUI()
        {
            if (!fighter) return;
            Styles();
            float s = Screen.height / 1080f * 1.4f; var m = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = Screen.height / s;
            var cam = Camera.main;

            // name tags over the dummies (name in the character's colour, health underneath while practising)
            if (cam)
                foreach (var o in Fighter.All)
                {
                    if (o == fighter || !o.alive) continue;
                    var sp = cam.WorldToScreenPoint(o.VisualPos + Vector3.up * 2.35f);
                    if (sp.z <= 0 || sp.z > 60) continue;
                    var ci = armory ? armory.Char(o.characterId) : null;
                    var st = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = ci != null ? ci.Colour : Color.white } };
                    float x = sp.x / s, y = (Screen.height - sp.y) / s;
                    GUI.Label(new Rect(x - 80, y - 22, 160, 20), o.characterName, st);
                    Bar(x - 30, y - 2, 60, 6, o.hp / o.hpMax, new Color(.27f, .85f, .38f));
                }

            // damage numbers
            if (cam)
                foreach (var n in nums)
                {
                    var sp = cam.WorldToScreenPoint(n.pos); if (sp.z <= 0) continue;
                    var st = new GUIStyle(label) { fontSize = n.head ? 20 : 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(n.head ? 1 : 1, n.head ? .25f : .95f, n.head ? .2f : .85f, 1 - n.t / .9f) } };
                    GUI.Label(new Rect(sp.x / s - 40, (Screen.height - sp.y) / s - 12, 80, 24), n.dmg.ToString(), st);
                }

            // vitals: health (green), armour (blue), stamina (purple)
            float x0 = 18, y0 = H - 78;
            Bar(x0, y0, 230, 18, fighter.hp / fighter.hpMax, new Color(.27f, .85f, .38f));
            Bar(x0, y0 + 22, 230, 12, fighter.armor / 100f, new Color(.25f, .6f, 1f));
            Bar(x0, y0 + 38, 230, 12, fighter.stam / Fighter.STAM_MAX, fighter.stamLock ? new Color(.45f, .3f, .55f) : new Color(.62f, .38f, 1f));
            GUI.Label(new Rect(x0, y0 - 22, 300, 20), fighter.characterName + "  " + Mathf.CeilToInt(fighter.hp), label);

            // weapon panel
            if (arsenal && armory)
            {
                var g = arsenal.Info;
                if (g != null)
                {
                    float wx = W - 290, wy = H - 92;
                    Box(new Rect(wx, wy, 272, 78), new Color(0, 0, 0, .45f));
                    GUI.Label(new Rect(wx + 10, wy + 4, 180, 20), g.name, label);
                    GUI.Label(new Rect(wx + 190, wy + 6, 72, 18), g.cls, new GUIStyle(small) { alignment = TextAnchor.UpperRight });
                    int a = arsenal.Ammo(arsenal.Current);
                    GUI.Label(new Rect(wx + 10, wy + 22, 252, 30), a < 0 ? "∞" : a + " / " + Mathf.RoundToInt(g.mag), big);
                    if (arsenal.reloadT > 0) Bar(wx + 10, wy + 54, 252, 8, 1 - arsenal.reloadT / arsenal.reloadMax, new Color(1, .82f, .3f));
                    var p = armory.Weapon(arsenal.primary); var sc = armory.Weapon(arsenal.secondary); var l = armory.Weapon(arsenal.lethal);
                    GUI.Label(new Rect(wx + 10, wy + 58, 260, 18),
                        (arsenal.slot == 0 ? "[1 " : " 1 ") + (p != null ? p.name : "-") + (arsenal.slot == 0 ? "]" : " ") + "   " +
                        (arsenal.slot == 1 ? "[2 " : " 2 ") + (sc != null ? sc.name : "-") + (arsenal.slot == 1 ? "]" : " ") +
                        (l != null ? "   G " + l.name + " x" + arsenal.lethalLeft : ""), small);
                }
            }

            // crosshair: opens with the spread, red over an enemy within the gun's reach
            float cx = W / 2, cy = H / 2;
            if (!(arsenal && arsenal.Scoped))
            {
                float gap = 3 + (arsenal ? Mathf.Min(40, arsenal.SpreadNow * 4.5f) : 0);
                bool onT = OnTarget(cam);
                GUI.color = onT ? new Color(1, .25f, .2f, .95f) : new Color(1, 1, 1, .9f);
                GUI.DrawTexture(new Rect(cx - 1, cy - gap - 6, 2, 6), white); GUI.DrawTexture(new Rect(cx - 1, cy + gap, 2, 6), white);
                GUI.DrawTexture(new Rect(cx - gap - 6, cy - 1, 6, 2), white); GUI.DrawTexture(new Rect(cx + gap, cy - 1, 6, 2), white);
            }
            else
            {
                // sniper scope: dark surround, thin cross and a red centre dot
                float R = H * .42f;
                GUI.color = new Color(0, 0, 0, .85f);
                GUI.DrawTexture(new Rect(0, 0, cx - R, H), white); GUI.DrawTexture(new Rect(cx + R, 0, W - cx - R, H), white);
                GUI.DrawTexture(new Rect(cx - R, 0, 2 * R, cy - R), white); GUI.DrawTexture(new Rect(cx - R, cy + R, 2 * R, H - cy - R), white);
                GUI.color = new Color(0, 0, 0, .9f); GUI.DrawTexture(new Rect(cx - R, cy - .5f, 2 * R, 1), white); GUI.DrawTexture(new Rect(cx - .5f, cy - R, 1, 2 * R), white);
                GUI.color = new Color(1, .15f, .1f); GUI.DrawTexture(new Rect(cx - 2, cy - 2, 4, 4), white);
            }
            if (hitT > 0)
            {
                // an X of four short diagonal strokes (red for a headshot or a down)
                GUI.color = hitBig > 0 ? new Color(1, .2f, .15f) : Color.white; float d = 6 + hitBig * 3;
                for (int k = 0; k < 6; k++)
                    foreach (var sg in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
                        GUI.DrawTexture(new Rect(cx + sg.x * (d + k) - 1, cy + sg.y * (d + k) - 1, 2, 2), white);
            }
            GUI.color = Color.white;

            GUI.Label(new Rect(W - 110, 10, 100, 20), Mathf.RoundToInt(fps) + " fps", label);
            if (showHelp && !PickerOpen)
                GUI.Label(new Rect(18, 14, 640, 140),
                    "Street Life (Unity)  -  click to lock the mouse, Esc to free it\n" +
                    "WASD move   Mouse look   Space jump (again in the air: double jump)   C crouch   Walk into a ladder to climb\n" +
                    "Left mouse fire (snipers: hold to scope, let go to fire)   Right mouse aim   R reload   1 / 2 / wheel switch   G lethal\n" +
                    "Tab characters and weapons   T dummies strafe   Backspace new spawn   F1 hide this", small);

            if (flashA > 0) { GUI.color = new Color(1, 1, 1, Mathf.Clamp01(flashA / .4f)); GUI.DrawTexture(new Rect(0, 0, W, H), white); GUI.color = Color.white; }
            if (PickerOpen) Picker(W, H);
            GUI.matrix = m;
        }

        bool OnTarget(Camera cam)
        {
            if (!cam || !arsenal || arsenal.Info == null) return false;
            var g = arsenal.Info; var o = cam.transform.position; var d = cam.transform.forward;
            float reach = g.type == "melee" ? g.reach + .6f : g.id == "rocket" ? 60 : g.id == "swarm" ? 45 : g.id == "crossbow" ? 65 : g.type == "proj" ? 30 : g.type == "flame" ? 13 : g.HasFall ? g.fall[1] : 160;
            if (!Combat.RayFoes(o, d, fighter, out var h)) return false;
            float wall = BoxWorld.Ray(o, d, h.t); if (wall >= 0) return false;
            return Vector3.Distance(Combat.Eye(fighter), o + d * h.t) <= reach;
        }

        // ------------------------------------------------------------------ loadout picker
        Vector2 scrollL, scrollR; string hoverW;
        void Picker(float W, float H)
        {
            Box(new Rect(0, 0, W, H), new Color(0, 0, 0, .72f));
            float pw = Mathf.Min(W - 40, 1180), ph = H - 60, px = (W - pw) / 2, py = 30;
            Box(new Rect(px, py, pw, ph), new Color(.08f, .09f, .11f, .96f));
            GUI.Label(new Rect(px + 16, py + 10, 600, 24), "Characters and weapons from imok 1v1  (Tab to close)", new GUIStyle(label) { fontSize = 16 });

            // characters
            float cw = pw * .46f;
            GUILayout.BeginArea(new Rect(px + 16, py + 42, cw - 24, ph - 54));
            GUILayout.Label("CHARACTER", head);
            var cat = armory ? armory.Catalog : null;
            if (cat != null && cat.characters != null)
            {
                int cols = 3, i = 0;
                GUILayout.BeginVertical();
                while (i < cat.characters.Length)
                {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < cols && i < cat.characters.Length; c++, i++)
                    {
                        var ch = cat.characters[i]; bool on = fighter.characterId == ch.id;
                        var st = on ? btnOn : btn;
                        GUI.backgroundColor = on ? ch.Colour : Color.Lerp(ch.Colour, Color.gray, .55f);
                        if (GUILayout.Button(ch.name, st, GUILayout.Height(26), GUILayout.Width((cw - 40) / cols)) && !on && rig) { rig.Build(ch.id); }
                        GUI.backgroundColor = Color.white;
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
                var cur = armory.Char(fighter.characterId);
                if (cur != null)
                {
                    GUILayout.Space(10);
                    GUILayout.Label(cur.name, new GUIStyle(label) { fontSize = 18, normal = { textColor = cur.Colour } });
                    GUILayout.Label(cur.desc, wrap);
                    GUILayout.Label("Ability (E): " + cur.ability + ", " + cur.cooldown + " s cooldown  -  not wired up in Unity yet", wrap);
                    GUILayout.Label("Speed x" + cur.speed.ToString("0.##") + "   Health " + cur.hp + (cur.hasModel ? "" : "   (classic blocky look)"), small);
                    if (cur.killFx != null && cur.killFx.Length > 0) GUILayout.Label("Kill effects: " + string.Join(", ", cur.killFx), small);
                }
                GUILayout.Space(14);
                if (range)
                {
                    GUILayout.Label("PRACTICE DUMMIES", head);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("New dummies", GUILayout.Width(120))) range.Shuffle();
                    if (GUILayout.Button(range.strafe ? "Stop strafing (T)" : "Strafe (T)", GUILayout.Width(140))) range.strafe = !range.strafe;
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("How many: " + range.count, small, GUILayout.Width(90));
                    int n = Mathf.RoundToInt(GUILayout.HorizontalSlider(range.count, 0, 8, GUILayout.Width(160)));
                    if (n != range.count) { range.count = n; range.Shuffle(); }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndArea();

            // weapons
            float wx = px + cw, ww = pw - cw - 16;
            GUILayout.BeginArea(new Rect(wx, py + 42, ww, ph - 54));
            scrollR = GUILayout.BeginScrollView(scrollR);
            hoverW = null;
            if (arsenal && armory)
            {
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(ww / 2 - 10));
                WeaponList("PRIMARY", armory.Slot("primary"), arsenal.primary, id => arsenal.SetLoadout(id, null, null));
                GUILayout.Space(8);
                WeaponList("LETHAL (G)", armory.Slot("lethal"), arsenal.lethal, id => arsenal.SetLoadout(null, null, id));
                GUILayout.EndVertical();
                GUILayout.BeginVertical(GUILayout.Width(ww / 2 - 10));
                var sec = armory.Slot("secondary"); foreach (var x in new[] { "flamer", "fists", "stars" }) if (armory.Weapon(x) != null && !sec.Contains(x)) sec.Add(x);
                WeaponList("SECONDARY", sec, arsenal.secondary, id => { if (id == "flamer" && arsenal.secondary != "flamer") arsenal.prevSecondary = arsenal.secondary; arsenal.SetLoadout(null, id, null); arsenal.slot = 1; });
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                var show = armory.Weapon(hoverW ?? arsenal.Current);
                if (show != null)
                {
                    GUILayout.Space(10);
                    GUILayout.Label(show.name + "   " + show.cls, new GUIStyle(label) { fontSize = 16, normal = { textColor = show.Colour } });
                    if (!string.IsNullOrEmpty(show.note)) GUILayout.Label(show.note, wrap);
                    GUILayout.Label(Stats(show), wrap);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void WeaponList(string title, List<string> ids, string current, System.Action<string> pick)
        {
            GUILayout.Label(title, head);
            foreach (var id in ids)
            {
                var w = armory.Weapon(id); if (w == null) continue;
                bool on = id == current;
                GUI.backgroundColor = on ? w.Colour : Color.Lerp(w.Colour, Color.gray, .6f);
                string extra = id == "flamer" ? "  (shop)" : id == "fists" || id == "stars" ? "  (extra)" : "";
                if (GUILayout.Button(w.name + "   " + w.cls + extra, on ? btnOn : btn, GUILayout.Height(24))) pick(id);
                if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) hoverW = id;
                GUI.backgroundColor = Color.white;
            }
        }

        static string Stats(WeaponInfo w)
        {
            var sb = new System.Text.StringBuilder();
            if (w.type == "melee") sb.Append("Damage " + w.dmg + " a hit, reach " + w.reach + " m, " + (1 / Mathf.Max(.01f, w.rate)).ToString("0.#") + " swings a second");
            else if (w.slot == "lethal") sb.Append((w.dmg > 0 ? "Damage " + w.dmg + ", " : "") + "radius " + w.radius + " m" + (w.fuse > 0 ? ", fuse " + w.fuse + " s" : "") + ", 2 per round");
            else
            {
                sb.Append("Damage " + w.dmg + (w.pellets > 1 ? " x " + w.pellets + " pellets" : "") + (w.burst > 1 ? " x " + w.burst + " missiles" : ""));
                if (w.direct > 0) sb.Append(" (direct hit " + w.direct + ")");
                if (w.head > 1) sb.Append(", head x" + w.head);
                if (w.rate > 0) sb.Append(", " + (1 / w.rate).ToString("0.#") + " shots a second");
                sb.Append(w.Infinite ? "" : ", " + w.mag + " rounds");
                if (w.reload > 0) sb.Append(", reload " + w.reload.ToString("0.##") + " s");
                if (w.HasFall) sb.Append(", full damage to " + w.fall[0] + " m, 45% by " + w.fall[1] + " m");
                if (w.radius > 0) sb.Append(", splash " + w.radius + " m");
                sb.Append(". Spread " + w.spread + "° hip, " + w.adsSpread + "° aimed" + (w.auto ? ", full auto" : "") + ".");
            }
            return sb.ToString();
        }
    }
}
