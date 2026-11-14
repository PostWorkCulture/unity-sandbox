// Maps chat: F4 (or the MAPS button on phones): pick the map. Lists every map from the web game, grouped by the web game's
// menus (Duels, 1v1, Rumble); picking one reloads the scene on it, the same way the character picker restarts the match.
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StreetLife
{
    public class MapPicker : MonoBehaviour
    {
        public static bool Open { get; private set; }
        string sel;
        Texture2D white;
        GUIStyle title, nameSt, body, small, btn, btnOn, head;
        static readonly string[] Groups = { "Duels", "1v1", "Rumble" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<MapPicker>()) return;
            var go = new GameObject("Map picker"); DontDestroyOnLoad(go); go.AddComponent<MapPicker>();
            SceneManager.sceneLoaded -= Reboot; SceneManager.sceneLoaded += Reboot;
        }

        // Unity only runs [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] once, for the first scene. Picking a map (or a character)
        // reloads the scene, which takes the match, weapons and finishers with it: run those boots again for the new scene.
        // Each of them already checks for an existing instance, so nothing is made twice.
        static void Reboot(Scene sc, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            foreach (var t in typeof(MapPicker).Assembly.GetTypes())
            {
                if (t == typeof(MapPicker) || t.Namespace == null || !t.Namespace.StartsWith("StreetLife")) continue;
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (m.GetParameters().Length != 0) continue;
                    foreach (var a in m.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false))
                        if (((RuntimeInitializeOnLoadMethodAttribute)a).loadType == RuntimeInitializeLoadType.AfterSceneLoad)
                        {
                            try { m.Invoke(null, null); } catch (System.Exception e) { Debug.LogException(e); }
                        }
                }
            }
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.f4Key.wasPressedThisFrame) Toggle(!Open);
            else if (Open && kb.escapeKey.wasPressedThisFrame) Toggle(false);
            else if (Open && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) Play();
        }

        void LateUpdate() { if (Open && Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } }
        void OnDestroy() { if (Open) Open = false; }

        void Toggle(bool on)
        {
            Open = on;
            if (on) { sel = MapSet.Current; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            // the player's controls are off while it's up (a click would otherwise grab the mouse for the game mid-click)
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
            {
                pc.enabled = !on;
                var f = pc.GetComponent<Fighter>(); if (on && f) { f.inFwd = f.inSide = 0; f.inJump = false; f.inFire = false; f.ads = false; }
            }
        }

        void Play()
        {
            var set = MapSet.Load(); if (!set || set.Get(sel) == null || sel == MapSet.Current) return;
            MapSet.Choice = sel; Toggle(false);
            var sc = SceneManager.GetActiveScene();
            if (sc.buildIndex >= 0) SceneManager.LoadScene(sc.buildIndex); else SceneManager.LoadScene(sc.name);
        }

        // acts on the press itself, so nothing that grabs the mouse on the same click can swallow it
        bool Press(Rect r, string label, GUIStyle st)
        {
            var e = Event.current;
            if (GUI.enabled && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { e.Use(); GUI.Button(r, label, st); return true; }
            return GUI.Button(r, label, st);
        }

        void Styles(float s)
        {
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            System.Func<float, int> fs = px => Mathf.Max(8, Mathf.RoundToInt(px * s));
            title = new GUIStyle(GUI.skin.label) { fontSize = fs(22), fontStyle = FontStyle.Bold };
            nameSt = new GUIStyle(GUI.skin.label) { fontSize = fs(30), fontStyle = FontStyle.Bold };
            body = new GUIStyle(GUI.skin.label) { fontSize = fs(15), wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = fs(12), wordWrap = true };
            head = new GUIStyle(GUI.skin.label) { fontSize = fs(13), fontStyle = FontStyle.Bold, normal = { textColor = HudSkin.Dim } };   // menu look: grey section heads
            btn = HudSkin.Button(new GUIStyle(GUI.skin.button) { fontSize = fs(15), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true });   // rounded menu buttons (HudRound.cs)
            btnOn = HudSkin.Button(btn, true);
        }

        GUIStyle tileSt;
        static readonly System.Collections.Generic.Dictionary<string, Texture2D> thumbs = new System.Collections.Generic.Dictionary<string, Texture2D>();
        static Texture2D Thumb(string id) { if (!thumbs.TryGetValue(id, out var t)) thumbs[id] = t = Resources.Load<Texture2D>("StreetLife/MapThumbs/" + id); return t; }   // the main menu's map pictures
        void Fill(Rect r, Color c) => HudRound.Fill(r, c, white);   // rounded corners (HudRound.cs)

        void OnGUI()
        {
            var set = MapSet.Load();
            float s = Screen.height / 1080f * 1.4f;
            Styles(s);
            if (!Open)
            {
                if (!set) return;
                if (!HudLayout.Touch) GUI.Label(new Rect(Screen.width - 268 * s, Screen.height - 172 * s, 250 * s, 20 * s), "F4: maps", new GUIStyle(small) { alignment = TextAnchor.UpperRight });   // above the ammo panel's Tab hint (HudLayout)
                if (Application.isMobilePlatform && Press(HudLayout.MapsButton(90 * s, 34 * s), "MAPS", btn)) Toggle(true);
                return;
            }
            GUI.depth = -10;
            float W = Screen.width, H = Screen.height;
            Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, .7f));
            float pw = Mathf.Min(W - 40 * s, 1250 * s), ph = Mathf.Min(H - 40 * s, 720 * s), px = (W - pw) / 2, py = (H - ph) / 2;
            HudDraw.PanelBox(new Rect(px, py, pw, ph), s);
            GUI.Label(new Rect(px + 20 * s, py + 14 * s, pw, 32 * s), "Maps", title);
            if (!set || set.maps == null) { GUI.Label(new Rect(px + 20 * s, py + 60 * s, pw, 40 * s), "No maps yet: run Street Life > Maps > Rebuild maps in the Editor.", body); return; }
            GUI.Label(new Rect(px + 120 * s, py + 20 * s, pw - 300 * s, 30 * s), "Every map from the web game. Picking one restarts the match there.", small);

            float lx = px + 20 * s, lw = pw * .5f - 30 * s, y = py + 60 * s, bh = 92 * s;
            if (tileSt == null) tileSt = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
            tileSt.fontSize = Mathf.RoundToInt(13 * s); tileSt.normal.textColor = Color.white;
            foreach (var g in Groups)
            {
                GUI.Label(new Rect(lx, y, lw, 20 * s), g.ToUpperInvariant(), head); y += 24 * s;
                int col = 0; float cw = (lw - 16 * s) / 3;
                foreach (var e in set.maps)
                {
                    if (e == null || e.mode != g) continue;
                    var r = new Rect(lx + col * (cw + 8 * s), y, cw, bh);
                    // the menu's map card: its picture over the name, white edge when picked or hovered
                    bool on = e.id == sel;
                    HudDraw.Card(r, on, r.Contains(Event.current.mousePosition), s);
                    HudDraw.Image(new Rect(r.x + 3 * s, r.y + 3 * s, r.width - 6 * s, r.height - 30 * s), Thumb(e.id), 8 * s);
                    GUI.Label(new Rect(r.x + 2 * s, r.yMax - 27 * s, r.width - 4 * s, 24 * s), e.title, tileSt);
                    if (Press(r, "", GUIStyle.none)) sel = e.id;
                    if (++col == 3) { col = 0; y += bh + 8 * s; }
                }
                if (col != 0) y += bh + 8 * s;
                y += 10 * s;
            }

            var info = set.Get(sel);
            float ix = px + pw * .5f + 10 * s, iw = pw * .5f - 30 * s, iy = py + 60 * s;
            if (info != null)
            {
                GUI.Label(new Rect(ix, iy, iw, 44 * s), info.title, nameSt);
                { float top = iy + 262 * s, ih = Mathf.Min(iw * 9 / 16f, py + ph - 84 * s - top); if (ih > 60 * s) HudDraw.Image(new Rect(ix, top, ih * 16 / 9f, ih), Thumb(info.id), 12 * s); }
                GUI.Label(new Rect(ix, iy + 70 * s, iw, 90 * s), info.desc, body);
                GUI.Label(new Rect(ix, iy + 168 * s, iw, 18 * s), "FROM THE WEB GAME'S " + info.mode.ToUpperInvariant() + " MENU", head);
                GUI.Label(new Rect(ix, iy + 190 * s, iw, 120 * s), Notes(info.id), small);
            }

            var br = new Rect(px + pw - 330 * s, py + ph - 62 * s, 180 * s, 44 * s);
            GUI.enabled = info != null && sel != MapSet.Current;
            if (Press(br, "Play (Enter)", btnOn)) Play();
            GUI.enabled = true;
            if (Press(new Rect(br.xMax + 10 * s, br.y, 130 * s, 44 * s), "Close (F4)", btn)) Toggle(false);
        }

        static string Notes(string id)
        {
            switch (id)
            {
                case "street": return "Breakable glass, furniture, cars that drive and wreck, trees that fall.";
                case "rail": return "A train runs through every so often: ride on its roof, but don't stand in front of it. The rails go live (and glow red) while it's near.";
                case "wreck": return "Wade into the sea (it slows you down). Glass pavilions, jetties, a lifeguard tower.";
                case "arena": return "Night-time stadium with a crowd. Open floor, pillars and a ramped cross in the middle.";
                case "skyhigh": return "No walls at the edge, only glass: smash through it and you fall. Knock the bot off for the win.";
                case "lagoon": return "Fall off the boardwalks and you sink. Bots won't step off on their own.";
                case "house": return "Room-to-room fights round the furniture (a Rumble map, played here as 1v1).";
                case "yard": return "A small back garden (a Rumble map, played here as 1v1).";
            }
            return "";
        }
    }
}
