// F3: pick your character (and the bot's) from every character in the web game.
// Picking one restarts the match with them, the same way the web game's menu does before a match.
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StreetLife
{
    public class CharacterPicker : MonoBehaviour
    {
        public static bool Open { get; private set; }
        string sel, botSel;
        bool bot;   // editing the bot's pick
        Texture2D white;
        GUIStyle title, nameSt, body, small, btn, btnOn, head;

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.f3Key.wasPressedThisFrame) Toggle(!Open);
            else if (Open && kb.escapeKey.wasPressedThisFrame) Toggle(false);
        }

        void LateUpdate()
        {
            // keep the mouse free while the picker is up (a click on it would otherwise grab the mouse for the game)
            if (Open && Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        void OnDestroy() { if (Open) Open = false; }

        void Toggle(bool on)
        {
            Open = on;
            if (on) { sel = CharacterSkin.PlayerChoice; botSel = CharacterSkin.BotChoice; bot = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        void Styles(float s)
        {
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            System.Func<float, int> fs = px => Mathf.Max(8, Mathf.RoundToInt(px * s));
            title = new GUIStyle(GUI.skin.label) { fontSize = fs(22), fontStyle = FontStyle.Bold };
            nameSt = new GUIStyle(GUI.skin.label) { fontSize = fs(28), fontStyle = FontStyle.Bold };
            body = new GUIStyle(GUI.skin.label) { fontSize = fs(14), wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = fs(12), wordWrap = true };
            head = new GUIStyle(GUI.skin.label) { fontSize = fs(13), fontStyle = FontStyle.Bold, normal = { textColor = HudSkin.Dim } };   // menu look: grey section heads
            btn = HudSkin.Button(new GUIStyle(GUI.skin.button) { fontSize = fs(14), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true });   // rounded menu buttons (HudRound.cs)
            btnOn = HudSkin.Button(btn, true);
        }

        GUIStyle tileSt;
        void Fill(Rect r, Color c) => HudRound.Fill(r, c, white);   // rounded corners (HudRound.cs)

        void OnGUI()
        {
            var set = CharacterSet.Load();
            float s = Screen.height / 1080f * 1.4f;
            if (!Open)
            {
                if (set) { Styles(s); GUI.Label(new Rect(Screen.width - 230 * s, Screen.height - 50 * s, 220 * s, 20 * s), "F3: characters", new GUIStyle(small) { alignment = TextAnchor.UpperRight }); }
                return;
            }
            Styles(s);
            GUI.depth = -10;
            float W = Screen.width, H = Screen.height;
            Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, .7f));
            int nAll = set ? (bot ? set.All : set.Playable).Length : 0, rowsN = (nAll + 5) / 6; float chh = 112 * s;
            float pw = Mathf.Min(W - 40 * s, 1250 * s), ph = Mathf.Min(H - 40 * s, 60 * s + rowsN * (chh + 8 * s) + (bot ? 52 * s : 0) + 86 * s), px = (W - pw) / 2, py = (H - ph) / 2;
            HudDraw.PanelBox(new Rect(px, py, pw, ph), s);
            if (!set) { GUI.Label(new Rect(px + 20 * s, py + 20 * s, pw, 40 * s), "No characters yet: run Street Life > Rebuild characters in the Editor.", title); return; }

            GUI.Label(new Rect(px + 20 * s, py + 14 * s, pw, 32 * s), "Characters", title);
            // you / bot tabs
            float tx = px + 180 * s;
            if (GUI.Button(new Rect(tx, py + 14 * s, 130 * s, 30 * s), "You", bot ? btn : btnOn)) bot = false;
            if (GUI.Button(new Rect(tx + 140 * s, py + 14 * s, 130 * s, 30 * s), "Bot", bot ? btnOn : btn)) bot = true;
            GUI.Label(new Rect(tx + 290 * s, py + 18 * s, pw - 480 * s, 30 * s), bot ? "The bot plays a random character each match unless you pick one." : "Picking restarts the match with your new character.", small);

            // grid of characters, in the web game's menu order
            // bot-only characters (Dida, Lia) only on the Bot tab
            var all = bot ? set.All : set.Playable; int cols = 6; float gx = px + 20 * s, gy = py + 60 * s, cw = (pw * .62f - 30 * s) / cols;
            if (tileSt == null) tileSt = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
            tileSt.fontSize = Mathf.RoundToInt(12 * s); tileSt.normal.textColor = Color.white;
            string cur = bot ? botSel : sel;
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i]; var r = new Rect(gx + (i % cols) * cw, gy + (i / cols) * (chh + 8 * s), cw - 8 * s, chh);
                bool on = c.id == cur;
                // the menu's character box: picture (MenuPortraits) over the name, white edge when picked or hovered
                HudDraw.Card(r, on, r.Contains(Event.current.mousePosition), s);
                HudDraw.Image(new Rect(r.x + 3 * s, r.y + 3 * s, r.width - 6 * s, r.height - 30 * s), MenuPortraits.Get(c.id), 8 * s);
                GUI.Label(new Rect(r.x + 2 * s, r.yMax - 27 * s, r.width - 4 * s, 24 * s), c.name, tileSt);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { if (bot) botSel = c.id; else sel = c.id; }
            }
            if (bot)
            {
                int rows = (all.Length + cols - 1) / cols;
                var rr = new Rect(gx, gy + rows * (chh + 8 * s), cw * 2 - 8 * s, 40 * s);
                if (GUI.Button(rr, "Random each match", string.IsNullOrEmpty(botSel) ? btnOn : btn)) botSel = "";
            }

            // the selected character: name, what they do, stats
            float ix = px + pw * .62f + 10 * s, iw = pw * .38f - 30 * s, iy = py + 60 * s;
            var info = string.IsNullOrEmpty(cur) ? null : set.Get(cur);
            if (info != null)
            {
                GUI.Label(new Rect(ix, iy, iw, 40 * s), info.name, nameSt);
                GUI.Label(new Rect(ix, iy + 44 * s, iw, 50 * s), MainMenu.Bio(info.id), body);
                GUI.Label(new Rect(ix, iy + 104 * s, iw, 18 * s), "STATS", head);
                GUI.Label(new Rect(ix, iy + 124 * s, iw, 40 * s), "Speed x" + info.speed.ToString("0.##") + (info.speed > 1 ? " (fastest runner)" : info.speed < 1 ? " (slower)" : "") + "    Health " + info.hp, body);
                if (!info.hasModel) GUI.Label(new Rect(ix, iy + 156 * s, iw, 40 * s), "The classic blocky look.", small);
            }
            else if (bot) GUI.Label(new Rect(ix, iy, iw, 60 * s), "Random each match: any character but yours.", body);

            // apply / close
            bool changed = sel != CharacterSkin.PlayerChoice || botSel != CharacterSkin.BotChoice;
            var br = new Rect(px + pw - 330 * s, py + ph - 60 * s, 180 * s, 44 * s);
            GUI.enabled = changed;
            if (GUI.Button(br, "Play", btnOn))
            {
                CharacterSkin.PlayerChoice = sel; CharacterSkin.BotChoice = botSel;
                Open = false; Cursor.lockState = CursorLockMode.None;
                var sc = SceneManager.GetActiveScene();
                if (sc.buildIndex >= 0) SceneManager.LoadScene(sc.buildIndex); else SceneManager.LoadScene(sc.name);
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(br.xMax + 10 * s, br.y, 130 * s, 44 * s), "Close (F3)", btn)) Toggle(false);
        }
    }
}
