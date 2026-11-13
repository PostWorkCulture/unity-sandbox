// Rounded corners for every in-game panel, bar and box drawn with IMGUI (HUD, weapon HUD, finisher HUD, progression,
// the F3 / F4 pickers). Each HUD's own Fill / Rect helper calls this instead of a plain square DrawTexture.
// Corners round to at most 10 px (half the short side for thin bars, which makes them pills). Hairlines, single
// pixels and anything spanning the whole screen (vignettes, flashes, full-screen dimmers) stay square.
using UnityEngine;

namespace StreetLife
{
    public static class HudRound
    {
        public const float MaxRadius = 10f;

        public static void Fill(Rect r, Color c, Texture white)
        {
            if (!white) return;
            float w = Mathf.Abs(r.width), h = Mathf.Abs(r.height);
            float rr = Mathf.Min(MaxRadius * Mathf.Max(1f, Screen.height / 1080f), Mathf.Min(w, h) * .5f);
            bool fullSpan = w >= Screen.width * .98f || h >= Screen.height * .98f;
            if (rr < 1.5f || fullSpan) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old; return; }
            GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0, c * GUI.color, Vector4.zero, new Vector4(rr, rr, rr, rr));
        }
    }
}
