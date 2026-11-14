// Touch button layout editor (match chat), from the web game's HUD editor (hudEdit / hudSelect / saveHud):
// Settings > Phone controls > Edit button layout. Drag anything (any button, or the move stick) to move it; tap one to
// pick it, then change its size and opacity, or change them for everything at once ("All"). Reset puts the web game's
// layout back; Done saves it (PlayerPrefs "StreetLife.touchLayout") and goes back to the settings.
// TouchControls reads Dx / Dy / Scale / Alpha for every button and the stick.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace StreetLife
{
    public class TouchLayout : MonoBehaviour
    {
        // ------------------------------------------------------------------ the saved layout
        [Serializable] class Item { public string id; public float dx, dy, scale = 1, alpha = 1; }
        [Serializable] class Data { public List<Item> items = new List<Item>(); public float all = 1, allAlpha = 1; }
        const string Key = "StreetLife.touchLayout";
        static Data d;
        static Data D
        {
            get
            {
                if (d != null) return d;
                try { d = JsonUtility.FromJson<Data>(PlayerPrefs.GetString(Key, "")); } catch { d = null; }
                if (d == null) d = new Data();
                return d;
            }
        }
        static Item Of(string id, bool make = false)
        {
            foreach (var it in D.items) if (it.id == id) return it;
            if (!make) return null;
            var n = new Item { id = id }; D.items.Add(n); return n;
        }
        /// CSS px the button has been moved right / up
        public static float Dx(string id) { var it = Of(id); return it != null ? it.dx : 0; }
        public static float Dy(string id) { var it = Of(id); return it != null ? it.dy : 0; }
        public static float Scale(string id) { var it = Of(id); return (it != null ? it.scale : 1) * D.all; }
        public static float Alpha(string id) { var it = Of(id); return Mathf.Clamp01((it != null ? it.alpha : 1) * D.allAlpha); }
        static void Save() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(D)); PlayerPrefs.Save(); }

        // ------------------------------------------------------------------ editing
        public static bool Editing => I;
        static TouchLayout I; static Action done;
        public static void BeginEdit(Action onDone)
        {
            if (I) return;
            done = onDone;
            I = new GameObject("Touch layout editor").AddComponent<TouchLayout>();
        }
        void OnDestroy() { if (I == this) I = null; }

        string sel;               // the picked button ("stick" for the stick), or null for all
        string dragId; Vector2 dragLast; bool dragMoved; Vector2 downAt;
        bool barMin;

        TouchControls TC => TouchControls.I;
        float S => TC ? TC.CssPx : Mathf.Max(1f, Screen.height / 360f);

        void Update()
        {
            if (!TC) { Finish(false); return; }
            // one pointer: the first touch, or the mouse (for the editor and desktop)
            Vector2 p = default; bool began = false, held = false, ended = false;
            if (ETouch.activeTouches.Count > 0)
            {
                var t = ETouch.activeTouches[0]; p = t.screenPosition;
                began = t.phase == UnityEngine.InputSystem.TouchPhase.Began; ended = t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled; held = !ended;
            }
            else if (Mouse.current != null)
            {
                var m = Mouse.current; p = m.position.ReadValue();
                began = m.leftButton.wasPressedThisFrame; held = m.leftButton.isPressed; ended = m.leftButton.wasReleasedThisFrame;
            }
            if (began && !OverBar(p))
            {
                dragId = Pick(p); dragLast = p; downAt = p; dragMoved = false;
            }
            if (held && dragId != null)
            {
                var dl = p - dragLast; dragLast = p;
                if ((p - downAt).magnitude > 8) dragMoved = true;
                if (dragMoved) { var it = Of(dragId, true); it.dx += dl.x / S; it.dy += dl.y / S; Clamp(it); }
            }
            if (ended && dragId != null) { if (!dragMoved) sel = sel == dragId ? null : dragId; dragId = null; }
        }

        // keep everything on screen
        void Clamp(Item it)
        {
            var sa = Screen.safeArea; Vector2 c; float r;
            if (it.id == "stick") { c = TC.StickHomePos; r = TC.StickRadius; }
            else { var b = Find(it.id); if (b == null) return; c = TC.CentreOf(b); r = b.size * Scale(b.id) * .5f * S; }
            float ox = Mathf.Clamp(c.x, sa.xMin + r, sa.xMax - r) - c.x, oy = Mathf.Clamp(c.y, sa.yMin + r, sa.yMax - r) - c.y;
            it.dx += ox / S; it.dy += oy / S;
        }

        TouchControls.Button Find(string id) { foreach (var b in TC.Buttons) if (b.id == id) return b; return null; }

        string Pick(Vector2 p)
        {
            string best = null; float bd = 1e9f;
            foreach (var b in TC.Buttons)
            {
                if (b.size <= 0) continue;
                float dist = Vector2.Distance(p, TC.CentreOf(b)), r = b.size * Scale(b.id) * .5f * S * 1.15f;
                if (dist <= r && dist < bd) { bd = dist; best = b.id; }
            }
            if (best == null && Vector2.Distance(p, TC.StickHomePos) <= TC.StickRadius * 1.1f) best = "stick";
            return best;
        }

        // ------------------------------------------------------------------ drawing: every button where it will be, and the bar
        Texture2D disc; GUIStyle lab, bar, btn;
        Rect barRect;
        bool OverBar(Vector2 screenP) => barRect.Contains(new Vector2(screenP.x, Screen.height - screenP.y));

        void OnGUI()
        {
            if (!TC) return;
            GUI.depth = -40;
            float s = S;
            if (!disc)
            {
                disc = new Texture2D(128, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int py = 0; py < 128; py++) for (int px = 0; px < 128; px++) { float dd = Vector2.Distance(new Vector2(px + .5f, py + .5f), new Vector2(64, 64)); disc.SetPixel(px, py, new Color(1, 1, 1, Mathf.Clamp01(64 - dd))); }
                disc.Apply();
            }
            if (lab == null)
            {
                lab = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                bar = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = true };
                btn = HudSkin.Button(new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });   // rounded menu buttons
            }
            lab.fontSize = Mathf.RoundToInt(13 * s); bar.fontSize = Mathf.RoundToInt(12 * s); btn.fontSize = Mathf.RoundToInt(13 * s);
            var o = GUI.color;
            GUI.color = new Color(0, 0, 0, .35f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            // the stick
            {
                var c = TC.StickHomePos; float r = TC.StickRadius, a = Alpha("stick");
                if (sel == "stick") Ring(c, r * 2 + 8 * s);
                Disc(c, r * 2, new Color(0, 0, 0, .3f * a)); Disc(c, 56 * s * Scale("stick"), new Color(1, 1, 1, .6f * a));
            }
            foreach (var b in TC.Buttons)
            {
                if (b.size <= 0) continue;
                var c = TC.CentreOf(b); float sz = b.size * Scale(b.id) * s, a = Alpha(b.id);
                var col = b.color; col.a = Mathf.Max(.25f, col.a) * a;
                if (sel == b.id) Ring(c, sz + 8 * s);
                Disc(c, sz, col);
                GUI.color = new Color(1, 1, 1, Mathf.Max(.35f, a)); GUI.Label(new Rect(c.x - sz / 2, Screen.height - c.y - sz / 2, sz, sz), b.label, lab);
            }
            GUI.color = o;

            // the bar along the top
            float bw = Mathf.Min(Screen.width - 24 * s, 620 * s), bh = barMin ? 36 * s : 96 * s, bx = (Screen.width - bw) / 2, by = 10 * s;
            barRect = new Rect(bx, by, bw, bh);
            GUI.color = new Color(.06f, .07f, .09f, .92f); GUI.DrawTexture(barRect, Texture2D.whiteTexture); GUI.color = o;
            string who = sel == null ? "All" : sel == "stick" ? "Stick" : (Find(sel)?.label ?? sel);
            if (GUI.Button(new Rect(bx + bw - 86 * s, by + 4 * s, 80 * s, 28 * s), barMin ? "Show" : "Hide", btn)) barMin = !barMin;
            GUI.Label(new Rect(bx + 10 * s, by + 4 * s, bw - 110 * s, 28 * s), sel == null ? "Drag anything to move it; tap it to change its size and opacity" : "Size and opacity: " + who, bar);
            if (barMin) return;
            float y2 = by + 40 * s, x = bx + 10 * s;
            GUI.Label(new Rect(x, y2, 60 * s, 30 * s), "Size", bar); x += 60 * s;
            if (GUI.Button(new Rect(x, y2, 36 * s, 30 * s), "-", btn)) Size(-.1f); x += 40 * s;
            GUI.Label(new Rect(x, y2, 54 * s, 30 * s), Mathf.RoundToInt(CurSize() * 100) + "%", lab); x += 56 * s;
            if (GUI.Button(new Rect(x, y2, 36 * s, 30 * s), "+", btn)) Size(.1f); x += 54 * s;
            GUI.Label(new Rect(x, y2, 70 * s, 30 * s), "Opacity", bar); x += 70 * s;
            if (GUI.Button(new Rect(x, y2, 36 * s, 30 * s), "-", btn)) Fade(-.1f); x += 40 * s;
            GUI.Label(new Rect(x, y2, 54 * s, 30 * s), Mathf.RoundToInt(CurAlpha() * 100) + "%", lab); x += 56 * s;
            if (GUI.Button(new Rect(x, y2, 36 * s, 30 * s), "+", btn)) Fade(.1f); x += 50 * s;
            if (GUI.Button(new Rect(bx + bw - 176 * s, y2, 80 * s, 30 * s), "Reset", btn)) { D.items.Clear(); D.all = 1; D.allAlpha = 1; sel = null; }
            if (GUI.Button(new Rect(bx + bw - 90 * s, y2, 80 * s, 30 * s), "Done", btn)) Finish(true);
        }

        float CurSize() => sel == null ? D.all : (Of(sel)?.scale ?? 1);
        float CurAlpha() => sel == null ? D.allAlpha : (Of(sel)?.alpha ?? 1);
        void Size(float k) { if (sel == null) D.all = Mathf.Clamp(D.all + k, .6f, 1.6f); else { var it = Of(sel, true); it.scale = Mathf.Clamp(it.scale + k, .6f, 1.8f); Clamp(it); } }
        void Fade(float k) { if (sel == null) D.allAlpha = Mathf.Clamp(D.allAlpha + k, .2f, 1f); else { var it = Of(sel, true); it.alpha = Mathf.Clamp(it.alpha + k, .2f, 1f); } }

        void Finish(bool save)
        {
            if (save) Save(); else d = null;
            var cb = done; done = null; Destroy(gameObject); cb?.Invoke();
        }

        void Disc(Vector2 c, float size, Color col)
        {
            var o = GUI.color; GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - size / 2, Screen.height - c.y - size / 2, size, size), disc); GUI.color = o;
        }
        void Ring(Vector2 c, float size)
        {
            var o = GUI.color; GUI.color = new Color(1f, .82f, .2f, .9f);
            GUI.DrawTexture(new Rect(c.x - size / 2, Screen.height - c.y - size / 2, size, size), disc); GUI.color = o;
        }

        /// remote test: call TouchLayout.Test [open|done|state]
        public static string Test(string a)
        {
            if (a == "open") { BeginEdit(null); return "editing"; }
            if (a == "done") { if (I) I.Finish(true); return "saved"; }
            return $"editing {Editing}, {D.items.Count} moved, all {D.all:F1} / {D.allAlpha:F1}";
        }
    }
}
