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

namespace StreetLife
{
    /// The menus' look for IMGUI screens (F3 / F4 pickers, the Tab weapons and shop, the touch layout editor, the frame-rate
    /// box): rounded dark buttons with a thin grey edge, a white edge on hover, a white fill with black text when selected,
    /// the same as the main menu. Wrap a GUIStyle made from GUI.skin.button: `btn = HudSkin.Button(new GUIStyle(GUI.skin.button) {...})`.
    public static class HudSkin
    {
        public static readonly Color Fill = new Color32(26, 26, 26, 235), Edge = new Color32(70, 70, 70, 255), Text = new Color32(245, 245, 245, 255),
            Dim = new Color32(150, 150, 150, 255), Ink = Color.black, Sel = Color.white;
        static Texture2D normal, hover, on, box;

        static Texture2D Rounded(Color fill, Color edge, float edgeW, int size = 48, float radius = 12)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[size * size]; float h = size * .5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + .5f - h) - (h - radius), qy = Mathf.Abs(y + .5f - h) - (h - radius);
                    float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;   // <0 inside
                    float a = Mathf.Clamp01(.5f - d);                       // shape coverage
                    float e = Mathf.Clamp01(.5f - (-d - edgeW));             // 1 on the edge ring
                    var c = Color.Lerp(fill, edge, edgeW > 0 ? e : 0); c.a *= a;
                    px[x + y * size] = c;
                }
            t.SetPixels(px); t.Apply(false, true); return t;
        }

        static void Make()
        {
            if (normal) return;
            normal = Rounded(Fill, Edge, 1.5f); hover = Rounded(Fill, Sel, 1.5f); on = Rounded(Sel, Sel, 0); box = Rounded(new Color32(10, 10, 10, 225), Edge, 1.5f);
        }

        /// a copy of `src` drawn as a rounded menu button; `selected` gives the white fill
        public static GUIStyle Button(GUIStyle src, bool selected = false)
        {
            Make();
            var s = new GUIStyle(src) { border = new RectOffset(13, 13, 13, 13), overflow = new RectOffset(0, 0, 0, 0) };
            if (s.padding.left < 10) s.padding = new RectOffset(12, 12, Mathf.Max(6, s.padding.top), Mathf.Max(6, s.padding.bottom));
            var bg = selected ? on : normal; var hv = selected ? on : hover; var tc = selected ? Ink : Text;
            s.normal.background = bg; s.normal.textColor = tc;
            s.hover.background = hv; s.hover.textColor = tc;
            s.active.background = on; s.active.textColor = Ink;
            s.focused.background = bg; s.focused.textColor = tc;
            s.onNormal.background = on; s.onNormal.textColor = Ink; s.onHover.background = on; s.onHover.textColor = Ink; s.onActive.background = on; s.onActive.textColor = Ink;
            return s;
        }

        /// a copy of `src` drawn as a rounded dark panel (for GUI.Box / GUI.Label readouts)
        public static GUIStyle Box(GUIStyle src)
        {
            Make();
            var s = new GUIStyle(src) { border = new RectOffset(13, 13, 13, 13) };
            s.normal.background = box; s.normal.textColor = Text;
            if (s.padding.left < 10) s.padding = new RectOffset(12, 12, 8, 8);
            return s;
        }
    }
}

namespace StreetLife
{
    /// drawing helpers for the IMGUI pickers, matching the main menu: panels, tile cards, rounded pictures
    public static class HudDraw
    {
        public static readonly Color Panel = new Color32(10, 10, 10, 246), Tile = new Color32(26, 26, 26, 255), Edge = new Color32(70, 70, 70, 255);

        /// a rounded panel with a thin grey edge
        public static void PanelBox(Rect r, float s)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Panel, 0, 14 * s);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Edge, 1.5f * s, 14 * s);
        }

        /// a tile card: dark fill, grey edge, white edge on hover or when selected (same width, so nothing moves)
        public static void Card(Rect r, bool selected, bool hover, float s)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Tile, 0, 10 * s);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, selected || hover ? Color.white : Edge, 2 * s, 10 * s);
        }

        /// a picture cropped to fill r, with rounded corners
        public static void Image(Rect r, Texture t, float radius)
        {
            if (t) GUI.DrawTexture(r, t, ScaleMode.ScaleAndCrop, false, 0, Color.white, 0, radius);
        }
    }
}
