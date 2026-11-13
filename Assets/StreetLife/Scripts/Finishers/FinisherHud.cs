// Finishers chat: the finisher bits of the HUD, in the match HUD's style: the "F  Finish" prompt when a downed enemy is
// in reach, your own downed state (a revive bar and a dark red vignette), the "Eliminated" banner at the top for the
// whole of a move you're in (with the move's name), the call-outs ("Pancake!") and "Interrupted!" / "Saved!".
using UnityEngine;

namespace StreetLife
{
    public class FinisherHud : MonoBehaviour
    {
        static FinisherHud inst;
        string bTitle, bSub; bool bWin; float bT, bDur;
        string cTitle, cSub; bool cWin; float cT, cDur;
        string note; float noteT;
        Texture2D white, vignette;
        GUIStyle big, sub, small, key;
        float s;
        Hud hud;

        void Awake() { inst = this; }

        public static bool BannerUp => inst && inst.bTitle != null && Time.unscaledTime - inst.bT < inst.bDur;
        /// The banner at the top ("Eliminated"); dur < 0 keeps the current time left.
        public static void Banner(string title, string subtitle, bool win, float dur)
        {
            if (!inst) return;
            if (dur >= 0 || !BannerUp) { inst.bT = Time.unscaledTime; inst.bDur = dur >= 0 ? dur : 1.2f; }
            inst.bTitle = title; inst.bSub = subtitle; inst.bWin = win;
        }
        public static void ClearBanner() { if (inst) inst.bTitle = null; }
        /// A call-out in the middle of the screen for a moment.
        public static void Center(string title, string subtitle, bool win, float dur)
        {
            if (!inst) return;
            inst.cTitle = title; inst.cSub = subtitle; inst.cWin = win; inst.cT = Time.unscaledTime; inst.cDur = dur;
        }
        public static void Note(string text, float dur) { if (inst) { inst.note = text; inst.noteT = Time.unscaledTime + dur; } }

        Rect R(float x, float y, float w, float h) => new Rect(x * s, y * s, w * s, h * s);
        void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, white); GUI.color = Color.white; }

        void Styles()
        {
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            if (!vignette)
            {
                const int N = 128; vignette = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                    {
                        float dx = (x + .5f) / N * 2 - 1, dy = (y + .5f) / N * 2 - 1, e = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                        px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01((e - .35f) / .65f) * Mathf.Clamp01((e - .35f) / .65f) * 255));
                    }
                vignette.SetPixels32(px); vignette.Apply();
            }
            int fs(float px) => Mathf.Max(8, Mathf.RoundToInt(px * s));
            big = new GUIStyle(GUI.skin.label) { fontSize = fs(34), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            sub = new GUIStyle(GUI.skin.label) { fontSize = fs(15), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            small = new GUIStyle(GUI.skin.label) { fontSize = fs(12), alignment = TextAnchor.MiddleCenter };
            key = new GUIStyle(GUI.skin.label) { fontSize = fs(14), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        void Shadowed(Rect r, string t, GUIStyle st, Color c)
        {
            var o = st.normal.textColor;
            st.normal.textColor = new Color(0, 0, 0, c.a * .8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, st);
            st.normal.textColor = c; GUI.Label(r, t, st);
            st.normal.textColor = o;
        }

        static readonly Color Gold = new Color(1f, .82f, .2f), Red = new Color(1f, .3f, .28f);

        void OnGUI()
        {
            var fin = Finishers.I; if (!fin) return;
            var me = fin.Player; if (!me) return;
            GUI.depth = -10;
            s = Screen.height / 1080f * 1.4f;
            Styles();
            float W = Screen.width / s, H = Screen.height / s, now = Time.unscaledTime;

            // vignette: a hit of dark red when you go down or a move lands, and a steady one while you're down
            float vg = Mathf.Max(Finishers.Vig, me.downed && me.alive && !me.InFinisher ? .45f + .08f * Mathf.Sin(now * 3) : 0);
            if (vg > .01f) { GUI.color = new Color(Finishers.VigColor.r, Finishers.VigColor.g, Finishers.VigColor.b, Mathf.Clamp01(vg)); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette); GUI.color = Color.white; }

            // finish prompt
            if (Finishers.Finishable(me))
            {
                float pulse = .5f + .5f * Mathf.Sin(now * 6), w = 170, x = HudLayout.StackX - w / 2, y = H * .64f;
                Fill(R(x, y, w, 40), new Color(0, 0, 0, .55f));
                Fill(R(x, y + 38, w, 2), new Color(Gold.r, Gold.g, Gold.b, .6f + .4f * pulse));
                Fill(R(x + 10, y + 7, 26, 26), Gold); Shadowed(R(x + 10, y + 7, 26, 26), "F", key, new Color(.08f, .08f, .1f));
                Shadowed(R(x + 40, y, w - 46, 40), "FINISH", sub, Color.white);
            }

            // you're down
            if (me.downed && me.alive && !me.InFinisher)
            {
                float y = H * .62f, w = 280, x = HudLayout.StackX - w / 2, left = Mathf.Max(0, (1 - me.revive) * Fighter.REVIVE_TIME);
                Shadowed(R(x, y, w, 40), "YOU'RE DOWN", big, Red);
                Shadowed(R(x, y + 38, w, 22), "Crawl to cover · back up in " + left.ToString("0.0") + " s", sub, new Color(1, 1, 1, .9f));
                Fill(R(x + 30, y + 64, w - 60, 8), new Color(0, 0, 0, .6f));
                Fill(R(x + 32, y + 66, (w - 64) * Mathf.Clamp01(me.revive), 4), new Color(.95f, .95f, .95f));
                Fill(R(x + 30, y + 76, w - 60, 4), new Color(0, 0, 0, .5f));
                Fill(R(x + 31, y + 77, (w - 62) * Mathf.Clamp01(me.dhp / Fighter.DOWN_HP), 2), Red);
            }

            // banner (top)
            if (bTitle != null && now - bT < bDur)
            {
                float u = now - bT, a = Mathf.Clamp01(u / .15f) * Mathf.Clamp01((bDur - u) / .3f), pop = 1 + .12f * Mathf.Max(0, 1 - u / .2f);
                var c = bWin ? Gold : Red;
                float y = 96, bw = HudLayout.StackW(520), bx = HudLayout.StackX - bw / 2;   // the HUD's centre column (HudLayout)
                Fill(R(bx, y - 4, bw, 76), new Color(0, 0, 0, .45f * a));
                Fill(R(bx, y + 70, bw, 2), new Color(c.r, c.g, c.b, .8f * a));
                var m = GUI.matrix; GUIUtility.ScaleAroundPivot(Vector2.one * pop, new Vector2(HudLayout.StackX * s, (y + 22) * s));
                Shadowed(R(bx, y, bw, 44), bTitle.ToUpper(), big, new Color(c.r, c.g, c.b, a));
                GUI.matrix = m;
                Shadowed(R(bx, y + 42, bw, 24), bSub, sub, new Color(1, 1, 1, a));
            }

            // call-out (centre)
            if (cTitle != null && now - cT < cDur)
            {
                float u = now - cT, a = Mathf.Clamp01(u / .08f) * Mathf.Clamp01((cDur - u) / .25f), pop = 1 + .35f * Mathf.Max(0, 1 - u / .18f);
                var c = cWin ? Gold : Red; float y = H * .36f, cw = HudLayout.StackW(600), cx = HudLayout.StackX - cw / 2;
                var m = GUI.matrix; GUIUtility.ScaleAroundPivot(Vector2.one * pop, new Vector2(HudLayout.StackX * s, (y + 22) * s));
                Shadowed(R(cx, y, cw, 48), cTitle, big, new Color(c.r, c.g, c.b, a));
                GUI.matrix = m;
                Shadowed(R(cx, y + 46, cw, 24), cSub, sub, new Color(1, 1, 1, a));
            }

            // test notes and the test keys (shown with the match HUD's F1 help)
            if (note != null && now < noteT) Shadowed(R(HudLayout.NoteX - HudLayout.NoteW / 2, HudLayout.FinisherNoteY, HudLayout.NoteW, 22), note, sub, Gold);   // phones: above the down / finish prompts, clear of the Finish button
            if (!hud) hud = FindAnyObjectByType<Hud>();
            if (hud && hud.showHelp && !HudLayout.Touch)   // phones show no key help (and the Finish button is there)
                Shadowed(R(W / 2 - HudLayout.CenterWidth(660) / 2, HudLayout.FinisherHelpY, HudLayout.CenterWidth(660), 20), "F finishes a downed enemy (firing stops yours)" + (Application.isEditor ? "   ·   test: F9 downs the bot, Shift+F9 downs you, F10 next move: " + (Finishers.ForceMove == "" ? "Random" : Finishers.ForceMove) : ""), small, new Color(1, 1, 1, .85f));
        }
    }
}
