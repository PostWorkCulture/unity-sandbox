// Phone controls, laid out and tuned like the web game's touch HUD (S24 in landscape):
// a stick bottom left (132 px), Fire / Aim / Jump / Crouch bottom right, drag anywhere else to look
// (with the web game's acceleration curve on fast swipes), and dragging on Fire aims while you shoot.
// Sizes are in the web game's CSS pixels and scale with the screen (360 CSS px tall on an S24).
// The weapons chat can add its own buttons (Reload, Swap, lethal...) with TouchControls.AddButton.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace StreetLife
{
    [DefaultExecutionOrder(-9)]   // after PlayerController (-10): touch input wins while a finger is on it
    public class TouchControls : MonoBehaviour
    {
        public static TouchControls I;
        [Tooltip("Look sensitivity (the web game's camera slider, 1 = default)")]
        [Range(.1f, 6f)] public float sensitivity = 1f;
        public bool forceShow;

        public class Button
        {
            public string id, label; public float x, y, size; public bool toggle, on, held;
            public Action<bool> onPress; public int finger = -1; public Color color = new Color(1, 1, 1, .22f);
        }
        readonly List<Button> buttons = new List<Button>();
        Fighter f;
        int stickFinger = -1, lookFinger = -1; Vector2 stick;
        bool seenTouch;
        Texture2D disc;
        GUIStyle label;

        float S => Mathf.Max(1f, Mathf.Min(Screen.height / 360f, Screen.width / 640f));   // CSS px -> screen px
        public bool Showing => forceShow || Application.isMobilePlatform || seenTouch;

        public static void Ensure(Fighter player)
        {
            if (!player || player.GetComponent<TouchControls>()) return;
            player.gameObject.AddComponent<TouchControls>();
        }

        /// Add a round button. x, y: its centre in CSS px from the bottom-right corner; onPress(true) on touch, (false) on release.
        public static Button AddButton(string id, float x, float y, float size, string label, Action<bool> onPress, bool toggle = false)
        {
            if (!I) return null;
            var b = I.buttons.Find(q => q.id == id);
            if (b == null) { b = new Button { id = id }; I.buttons.Add(b); }
            b.x = x; b.y = y; b.size = size; b.label = label; b.onPress = onPress; b.toggle = toggle;
            return b;
        }

        void Awake()
        {
            I = this; f = GetComponent<Fighter>();
            EnhancedTouchSupport.Enable();
            // the web game's positions (right / bottom of each button's centre, size)
            buttons.Add(new Button { id = "fire", label = "Fire", x = 78, y = 128, size = 82, color = new Color(1f, .78f, .1f, .55f) });
            buttons.Add(new Button { id = "aim", label = "Aim", x = 64, y = 222, size = 54, toggle = true });
            buttons.Add(new Button { id = "jump", label = "Jump", x = 152, y = 48, size = 62 });
            buttons.Add(new Button { id = "crouch", label = "Crouch", x = 42, y = 40, size = 50 });
            disc = new Texture2D(128, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(64, 64));
                    px[x + y * 128] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(64 - d)));
                }
            disc.SetPixels32(px); disc.Apply();
        }

        void OnDestroy() { if (I == this) I = null; }

        Vector2 StickCentre => new Vector2((24 + 66) * S, (22 + 66) * S);
        float StickR => 66 * S;
        Vector2 Centre(Button b) => new Vector2(Screen.width - (10 + b.x) * S, b.y * S);

        Button Hit(Vector2 p)
        {
            foreach (var b in buttons) if (Vector2.Distance(p, Centre(b)) <= b.size * .5f * S * 1.15f) return b;
            return null;
        }

        void Look(Vector2 deltaPx)
        {
            var d = deltaPx / S;                                   // CSS px, like the web game
            float sp = d.magnitude, acc = 1 + .7f * Mathf.Clamp01((sp - 6) / 34f);
            float k = .0055f * sensitivity * (f.ads ? .65f : 1f) * acc;
            f.yaw += d.x * k;
            f.pitch = Mathf.Clamp(f.pitch + d.y * k, -1.3f, 1.3f);
        }

        void Press(Button b, bool down)
        {
            switch (b.id)
            {
                case "fire": f.inFire = down; break;
                case "jump": f.inJump = down; if (down) f.inJumpPress = true; break;
                case "crouch": if (down) f.crouchOn = !f.crouchOn; break;
                case "aim": break;
            }
            b.onPress?.Invoke(down);
        }

        void Update()
        {
            if (!f) return;
            var touches = ETouch.activeTouches;
            if (touches.Count > 0) seenTouch = true;
            if (!Showing) return;

            foreach (var t in touches)
            {
                int id = t.touchId; var p = t.screenPosition;
                switch (t.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        if (Vector2.Distance(p, StickCentre) <= StickR * 1.3f && stickFinger < 0) { stickFinger = id; break; }
                        var b = Hit(p);
                        if (b != null && b.finger < 0)
                        {
                            b.finger = id;
                            if (b.toggle) { b.on = !b.on; Press(b, b.on); } else { b.held = true; Press(b, true); }
                            break;
                        }
                        if (lookFinger < 0) lookFinger = id;
                        break;
                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                        if (id == lookFinger) Look(t.delta);
                        foreach (var q in buttons) if (q.finger == id && q.id == "fire") Look(t.delta);   // aim while firing
                        break;
                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        if (id == stickFinger) { stickFinger = -1; stick = Vector2.zero; }
                        if (id == lookFinger) lookFinger = -1;
                        foreach (var q in buttons) if (q.finger == id) { q.finger = -1; if (!q.toggle) { q.held = false; Press(q, false); } }
                        break;
                }
                if (id == stickFinger)
                {
                    var v = (p - StickCentre) / StickR; if (v.magnitude > 1) v.Normalize();
                    stick = v;
                }
            }

            if (stickFinger >= 0) { f.inFwd = stick.y; f.inSide = stick.x; }
            var aim = buttons.Find(q => q.id == "aim");
            if (aim != null && (aim.on || Application.isMobilePlatform)) f.ads = aim.on && f.onLadder == null;
            var fire = buttons.Find(q => q.id == "fire");
            if (fire != null && fire.held) f.inFire = true;
            var jump = buttons.Find(q => q.id == "jump");
            if (jump != null && jump.held) f.inJump = true;
        }

        void Disc(Vector2 centreBL, float diameter, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(centreBL.x - diameter / 2, Screen.height - centreBL.y - diameter / 2, diameter, diameter), disc);
        }

        void OnGUI()
        {
            if (!Showing || !f || !f.alive) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            float s = S;
            Disc(StickCentre, 132 * s, new Color(0, 0, 0, .28f));
            Disc(StickCentre + stick * 38 * s, 56 * s, new Color(1, 1, 1, .8f));
            label.fontSize = Mathf.RoundToInt(13 * s);
            foreach (var b in buttons)
            {
                var c = Centre(b); bool lit = b.held || b.on;
                Disc(c, b.size * s, lit ? new Color(1, 1, 1, .55f) : b.color);
                GUI.color = lit ? Color.black : Color.white;
                GUI.Label(new Rect(c.x - b.size * s / 2, Screen.height - c.y - b.size * s / 2, b.size * s, b.size * s), b.label, label);
            }
            GUI.color = Color.white;
        }
    }
}
