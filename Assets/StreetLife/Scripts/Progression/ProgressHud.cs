// Progression (progression chat): the XP panel on the end-of-match screen, under the match HUD's "You caught the W!"
// screen (Hud.cs draws that; this only adds below it). The bar fills from where you started the match to where you are
// now, ticking over each level it passes, with this match's XP and anything new it unlocked.
using UnityEngine;

namespace StreetLife
{
    public class ProgressHud : MonoBehaviour
    {
        Texture2D white;
        GUIStyle label, small, center, bigC;
        float s, shownT = -1;

        // black and white like the menus, with their one accent for LEVEL UP
        static readonly Color Cyan = Color.white, Gold = Color.white, Accent = new Color32(255, 59, 48, 255), Dim = new Color(1, 1, 1, .75f);

        Rect R(float x, float y, float w, float h) => new Rect(x * s, y * s, w * s, h * s);
        void Fill(Rect r, Color c) => HudRound.Fill(r, c, white);   // rounded corners (HudRound.cs)

        void Styles()
        {
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            int fs(float px) => Mathf.Max(8, Mathf.RoundToInt(px * s));
            label = new GUIStyle(GUI.skin.label) { fontSize = fs(15), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            small = new GUIStyle(GUI.skin.label) { fontSize = fs(12), alignment = TextAnchor.MiddleRight };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            bigC = new GUIStyle(center) { fontSize = fs(20) };
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
            var rm = RoundManager.Instance; var tr = ProgressTracker.I;
            if (!rm || !tr || rm.state != RoundManager.State.MatchEnd || MainMenu.Open || MainMenu.Paused) { shownT = -1; return; }
            if (shownT < 0) shownT = Time.unscaledTime;
            GUI.depth = -5;   // over the match HUD
            s = Screen.height / 1080f * 1.4f;   // the same scale as Hud.cs
            Styles();
            float W = Screen.width / s, H = Screen.height / s;
            var m = tr.match;

            // the bar runs from the XP you started the match with to now, over 1.4 s
            float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - shownT - .4f) / 1.4f));
            int xpNow = Mathf.RoundToInt(Mathf.Lerp(m.xpBefore, Progress.Data.xp, k));
            int lv = Progress.LevelOf(xpNow), into = xpNow % Progress.XpPerLevel;
            bool levelled = lv > m.levelBefore;

            float w = 560, x = W / 2 - w / 2, y = HudLayout.XpPanelY;   // phones: above the title (the Rematch button is at the bottom)
            Fill(R(x - 16, y - 12, w + 32, m.unlocked.Count > 0 ? 112 : 84), new Color(0, 0, 0, .45f));
            Shadowed(R(x, y, 200, 26), "Level " + lv, label, levelled ? Gold : Color.white);
            Shadowed(R(x + w - 200, y, 200, 26), "+" + m.xp + " XP", new GUIStyle(label) { alignment = TextAnchor.MiddleRight }, Cyan);
            Fill(R(x, y + 30, w, 12), new Color(1, 1, 1, .12f));
            Fill(R(x, y + 30, w * into / (float)Progress.XpPerLevel, 12), levelled ? Gold : Cyan);
            int next = ProgressRewards.NextRewardLevel(lv);
            Shadowed(R(x, y + 46, w, 22), $"{into} / {Progress.XpPerLevel} XP to level {lv + 1}" + (next > 0 ? $"   ·   next reward at level {next}" : ""), small, Dim);
            if (levelled && k >= 1) Shadowed(R(x, y + 46, w, 22), "LEVEL UP!", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold }, Accent);
            if (m.unlocked.Count > 0 && k >= 1)
            {
                var names = new System.Text.StringBuilder();
                foreach (var id in m.unlocked) { if (names.Length > 0) names.Append(", "); names.Append(ProgressRewards.NameOf(id)); }
                Shadowed(R(x, y + 72, w, 26), "Unlocked: " + names + "  (equip it in Profile)", center, Gold);
            }
        }
    }
}
