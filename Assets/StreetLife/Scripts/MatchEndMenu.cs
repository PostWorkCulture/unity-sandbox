// After a match (match end screen): three rounded buttons, Rematch, Random map and Home, in the menus' look (HudSkin).
// Desktop: a row under the Clips button. Phones: a row where the touch Rematch pill was (that pill hides while this shows).
// The rematch countdown still runs; any button acts at once.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class MatchEndMenu : MonoBehaviour
    {
        static MatchEndMenu I;
        public static bool Active => I && I.show;
        bool show; GUIStyle btn, btnOn; float lastS;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("Match end menu").AddComponent<MatchEndMenu>();
        }

        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            var rm = RoundManager.Instance;
            show = rm && rm.state == RoundManager.State.MatchEnd && !MainMenu.Open && !MainMenu.Paused;
        }

        void OnGUI()
        {
            if (!show) return;
            GUI.depth = -21;
            bool touch = HudLayout.Touch;
            var sa = Screen.safeArea; float S = Mathf.Max(1f, Mathf.Min(sa.height / 360f, sa.width / 640f));
            float s = touch ? S : Screen.height / 1080f * 1.4f;
            if (btn == null || !Mathf.Approximately(s, lastS))
            {
                lastS = s;
                btn = HudSkin.Button(new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt((touch ? 14 : 15) * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
                btnOn = HudSkin.Button(btn, true);
            }
            float bw, bh, gap, x0, y;
            if (touch)
            {   // the Rematch pill's spot (TouchControls), beside the Clips button
                bw = 120 * S; bh = 50 * S; gap = 10 * S; x0 = sa.center.x - 95 * S;
                y = Screen.height - (sa.yMin + 72 * S) - bh / 2;
            }
            else
            {   // under the results, or at the bottom while the throne scene plays
                bw = 170 * s; bh = 44 * s; gap = 12 * s; x0 = (Screen.width - (3 * bw + 2 * gap)) / 2;
                y = Crowning.Active ? Screen.height - 112 * s : Screen.height / 2f + 262 * s;
            }
            if (GUI.Button(new Rect(x0, y, bw, bh), "Rematch", btnOn)) { var rm = RoundManager.Instance; if (rm) rm.StartMatch(); }
            if (GUI.Button(new Rect(x0 + bw + gap, y, bw, bh), "Random map", btn)) MainMenu.PlayRandomMap();
            if (GUI.Button(new Rect(x0 + 2 * (bw + gap), y, bw, bh), "Home", btn)) MainMenu.GoHome();
        }
    }
}
