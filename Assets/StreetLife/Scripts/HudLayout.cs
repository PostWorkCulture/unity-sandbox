// One place that decides where the in-game panels go, so none of them overlap (Pete, 13 Nov: "ensure no pop ups or in game
// info overlap with each other"). Units are the HUD's own (Screen.height / 1080 * 1.4 screen px each), the same ones Hud,
// WeaponHud, FinisherHud and ProgressHud draw in; H is always ~771 of them, W depends on the screen's shape.
//
//   Desktop                                              Phone (touch controls showing)
//   top left:     leaderboard (bot info F2 under it)     leaderboard, then vitals, items, frame-rate readout
//   top centre:   score, finisher banner, round result,  the same, plus the shop / HeartBeat / downed message
//                 kill feed (stacked, never on top of     (the bottom centre is where the Finish button sits)
//                 each other)
//   top right:    frame-rate readout, minimap            ammo panel, minimap, pause button under it
//   bottom left:  items, then vitals                     (the move stick)
//   bottom centre:message, finisher note, help lines     (free for the thumbs)
//   bottom right: F4 / Tab hints, ammo panel             (the fire, jump, aim... buttons)
//
// Panels set what they used this frame (the leaderboard's height, the items panel's top), and the others read it; a value is
// at most one frame old.
using UnityEngine;

namespace StreetLife
{
    public static class HudLayout
    {
        public static float Scale => Screen.height / 1080f * 1.4f;
        public static float W => Screen.width / Scale;
        public static float H => Screen.height / Scale;
        public static bool Touch => TouchControls.I && TouchControls.I.Showing;

        // ---- set by the panels each frame
        public static float LeaderboardBottom = 92;   // Hud.Leaderboard
        public static float ItemsTop = 9999, ItemsBottom = 0;   // WeaponHud's items panel (0 / 9999 when it isn't drawn)
        public static bool MessageShown;               // Hud's centre message (shop, HeartBeat, downed)
        public static bool RoundBarShown;              // Hud's round-result bar

        // ---- left column
        /// Top of the vitals block (name line, then the health / armour / stamina bars 22 below it; 72 tall in all).
        public static float VitalsTop => Touch ? LeaderboardBottom + 10 : H - 106;
        public static float VitalsBottom => VitalsTop + 72;
        /// Items panel: on phones it hangs under the vitals; on desktop it sits above them (its bottom at H - 116).
        public static float ItemsTopFor(int lines) => Touch ? VitalsBottom + 8 : H - 126 - lines * 20;
        /// The lowest thing in the top-left column, for debug readouts that go under it.
        public static float LeftColumnBottom => Touch ? Mathf.Max(VitalsBottom, ItemsBottom) : LeaderboardBottom;

        // ---- top centre: one stack, top to bottom
        public static float RoundBarY => FinisherHud.BannerUp ? 176 : 70;
        public static float MessageY => (FinisherHud.BannerUp ? 176 : 70) + (RoundBarShown ? 50 : 0);   // phones only
        public static float FeedTop => MessageY + (Touch && MessageShown ? 36 : 0);

        // ---- right side
        /// Ammo panel's top-left corner for a panel w x h: bottom right on desktop, left of the minimap on phones.
        public static Vector2 AmmoPanel(float w, float h) => Touch ? new Vector2(W - 218 - w, 14) : new Vector2(W - w - 18, H - h - 18);
        /// Frame-rate readout (screen px, w x h px): left of the minimap on desktop, under the left column on phones.
        public static Rect PerfBox(float w, float h)
        {
            float s = Scale;
            return Touch ? new Rect(18 * s, (LeftColumnBottom + 8) * s, w, h) : new Rect(Screen.width - 218 * s - w, 10, w, h);
        }

        // ---- bottom centre (desktop) and the finisher note
        public static float FinisherNoteY => Touch ? H * .62f - 30 : H - 120;
        public static float FinisherHelpY => H - 78;
        public static float HelpWidth => Mathf.Min(940, W - 600);   // clear of the vitals (left) and the ammo panel (right)

        // ---- end screen
        /// The XP panel: under the match stats on desktop; above the title on phones (the Rematch button is at the bottom).
        public static float XpPanelY => Touch ? 92 : H / 2 + 140;
    }
}
