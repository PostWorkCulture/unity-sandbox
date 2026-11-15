// Phone controls, laid out and tuned like the web game's touch HUD (S24 in landscape):
// a stick bottom left (132 px; it floats to wherever the left thumb lands), Fire / Aim / Jump / Crouch bottom right,
// drag anywhere else to look (with the web game's acceleration curve on fast swipes), and dragging on Fire aims while you shoot.
// Sizes are in the web game's CSS pixels and scale with the screen (360 CSS px tall on an S24), inside Screen.safeArea.
// Touch-only help (13 Nov, mobile pass): aim assist (slow-down near the enemy, a gentle pull while you look, move or fire,
// and a snap when you toggle Aim on), optional gyro aim, vibration, a Shop button, a Rematch button, and every button
// hides while you're downed or in a finishing move.
// The weapons chat can add its own buttons (Reload, Swap, lethal...) with TouchControls.AddButton.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using Gyro = UnityEngine.InputSystem.Gyroscope;

namespace StreetLife
{
    [DefaultExecutionOrder(-9)]   // after PlayerController (-10): touch input wins while a finger is on it
    public class TouchControls : MonoBehaviour
    {
        public static TouchControls I;
        public static bool Preview;   // editor: Street Life > Touch controls preview draws the layout without a touch screen
        [Tooltip("Look sensitivity (the web game's camera slider, 1 = default)")]
        [Range(.1f, 6f)] public float sensitivity = 1f;
        [Tooltip("Aim assist on touch: 0 off, 1 normal, 1.6 strong")]
        [Range(0f, 2f)] public float aimAssist = 1f;
        [Tooltip("Gyro aim: off, aim (only while aiming) or always")]
        public string gyro = "off";
        [Tooltip("Gyro turn per radian the phone turns")]
        public float gyroScale = 1f;
        public bool forceShow;

        public class Button
        {
            public string id, label; public float x, y, size; public bool toggle, on, held;
            public Action<bool> onPress; public int finger = -1; public Color color = new Color(1, 1, 1, .22f);
        }
        readonly List<Button> buttons = new List<Button>();
        // for the layout editor (TouchLayout) and settings
        public IReadOnlyList<Button> Buttons => buttons;
        public Vector2 CentreOf(Button b) => Centre(b);
        public float CssPx => S;
        public Vector2 StickHomePos => StickHome;
        public float StickRadius => StickR;
        [NonSerialized] public bool autoAds; [NonSerialized] public float aimSens = 1, snipeSens = 1;
        bool autoR; Weapons wpn;
        Fighter f;
        int stickFinger = -1, lookFinger = -1; Vector2 stick, stickOrigin;
        bool seenTouch, wasDown, wasDowned;
        float lookMoved;
        Fighter assistTarget; float assistCone, assistOff; Vector3 assistDir;
        Gyro gyroDev;
        Texture2D disc, white;
        GUIStyle label;

        static Rect Safe => Screen.safeArea;
        float S { get { var sa = Safe; return Mathf.Max(1f, Mathf.Min(sa.height / 360f, sa.width / 640f)); } }   // CSS px -> screen px
        /// Settings > Touch controls: "auto" (phones, or once the screen is touched), "on" (always) or "off" (never)
        public static string Mode = "auto";
        public bool Showing => Mode == "off" ? false : Mode == "on" || forceShow || Preview || Application.isMobilePlatform || seenTouch;
        bool Down => f && (f.downed || f.InFinisher);

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
            // the weapon picker and shop (Tab on a keyboard); shown while the weapon HUD is on
            buttons.Add(new Button { id = "shop", label = "Shop", x = 270, y = 292, size = 40, color = new Color(0, 0, 0, .35f) });
            disc = new Texture2D(128, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(64, 64));
                    px[x + y * 128] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(64 - d)));
                }
            disc.SetPixels32(px); disc.Apply();
            white = Texture2D.whiteTexture;
        }

        void OnEnable()
        {
            Combat.OnFeedback += OnHitFeedback; Fighter.Killed += OnKilled; RoundManager.RoundStarted += OnRound;
            if (f) f.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            Combat.OnFeedback -= OnHitFeedback; Fighter.Killed -= OnKilled; RoundManager.RoundStarted -= OnRound;
            if (f) f.Damaged -= OnDamaged;
        }

        void OnDestroy() { if (I == this) I = null; }

        // ------------------------------------------------------------------ layout (screen px, origin bottom left)
        Vector2 StickHome { get { var sa = Safe; return new Vector2(sa.xMin + (24 + 66 + TouchLayout.Dx("stick")) * S, sa.yMin + (22 + 66 + TouchLayout.Dy("stick")) * S); } }
        Vector2 StickCentre => stickFinger >= 0 ? stickOrigin : StickHome;
        float StickR => 66 * S * TouchLayout.Scale("stick");
        Vector2 Centre(Button b) { var sa = Safe; return new Vector2(sa.xMax - (10 + b.x - TouchLayout.Dx(b.id)) * S, sa.yMin + (b.y + TouchLayout.Dy(b.id)) * S); }
        Rect RematchRect { get { var sa = Safe; float w = 190 * S, h = 50 * S; return new Rect(sa.center.x - w / 2, sa.yMin + 72 * S - h / 2, w, h); } }

        bool InStickZone(Vector2 p)
        {
            var sa = Safe;
            return Vector2.Distance(p, StickHome) <= StickR * 1.3f || (p.x < sa.xMin + sa.width * .42f && p.y < sa.yMin + sa.height * .62f);
        }

        Vector2 StickOriginFor(Vector2 p)
        {
            if (Vector2.Distance(p, StickHome) <= StickR * 1.3f) return StickHome;   // the classic spot stays put
            var sa = Safe; float r = StickR;
            return new Vector2(Mathf.Max(p.x, sa.xMin + r), Mathf.Max(p.y, sa.yMin + r));
        }

        bool Live(Button b, bool menu)
        {
            if (b.size <= 0) return false;
            if (b.id == "shop") return WeaponHud.Active && !Down;
            return !menu && !Down;
        }

        Button Hit(Vector2 p, bool menu)
        {
            foreach (var b in buttons) if (Live(b, menu) && Vector2.Distance(p, Centre(b)) <= b.size * TouchLayout.Scale(b.id) * .5f * S * 1.15f) return b;
            return null;
        }

        // ------------------------------------------------------------------ look, aim assist, gyro
        void Look(Vector2 deltaPx)
        {
            var d = deltaPx / S;                                   // CSS px, like the web game
            float sp = d.magnitude, acc = 1 + .7f * Mathf.Clamp01((sp - 6) / 34f);
            if (!wpn) wpn = f.GetComponent<Weapons>();
            bool scoped = wpn && (wpn.Gun == "sniper" || wpn.Gun == "scout");
            float k = .0055f * sensitivity * (f.ads ? .65f * (scoped ? snipeSens : aimSens) : 1f) * acc;
            // aim assist, part 1: the camera slows down while the crosshair is over the enemy
            if (assistTarget && aimAssist > 0 && assistOff < assistCone)
                k *= 1 - Mathf.Min(.6f, .35f * aimAssist) * (1 - assistOff / assistCone);
            f.yaw += d.x * k;
            f.pitch = Mathf.Clamp(f.pitch + d.y * k, -1.3f, 1.3f);
            lookMoved += sp;
        }

        // the enemy nearest the crosshair inside a small cone (wider when aiming), in plain sight
        void FindTarget()
        {
            assistTarget = null;
            if (aimAssist <= 0 || !f.alive || f.Frozen || Down) return;
            var eye = f.Head; var aim = f.Aim; float best = float.MaxValue;
            foreach (var t in Combat.Fighters)
            {
                if (!t || t == f || !t.alive || t.team == f.team || t.InFinisher) continue;
                var tp = t.downed ? t.pos + Vector3.up * .45f : Vector3.Lerp(t.pos, t.Head, .72f);
                var to = tp - eye; float dist = to.magnitude;
                if (dist < .5f || dist > 70f) continue;
                var dir = to / dist;
                float off = Mathf.Acos(Mathf.Clamp(Vector3.Dot(aim, dir), -1f, 1f));
                float cone = Mathf.Clamp(Mathf.Atan(1.1f / dist), .035f, .2f) * (f.ads ? 1.25f : 1f);
                if (off > cone * 1.6f) continue;
                float r = BoxWorld.Ray(eye, dir, dist);
                if (r >= 0 && r < dist - .4f) continue;
                float score = off / cone;
                if (score < best) { best = score; assistTarget = t; assistCone = cone; assistOff = off; assistDir = dir; }
            }
        }

        // aim assist, part 2: turn a fraction of the way toward the target
        void Pull(float amount, bool wide)
        {
            if (!assistTarget || amount <= 0 || assistOff > assistCone * (wide ? 1.6f : 1f)) return;
            float ty = Mathf.Atan2(assistDir.x, assistDir.z), tp = Mathf.Asin(Mathf.Clamp(assistDir.y, -1f, 1f));
            float dy = Mathf.DeltaAngle(f.yaw * Mathf.Rad2Deg, ty * Mathf.Rad2Deg) * Mathf.Deg2Rad, dp = tp - f.pitch;
            float a = Mathf.Clamp01(amount);
            f.yaw += dy * a;
            f.pitch = Mathf.Clamp(f.pitch + dp * a, -1.3f, 1.3f);
        }

        // gyro: turn the phone to fine-tune aim. Input System rotates the readings to the screen (compensateForScreenOrientation),
        // so x is the screen's right axis and y its up axis. Flip gyroScale's sign if a direction comes out reversed on a phone.
        void GyroLook(float dt)
        {
            if (gyro == "off" || (gyro == "aim" && !f.ads)) return;
            if (gyroDev == null)
            {
                gyroDev = Gyro.current; if (gyroDev == null) return;
                if (!gyroDev.enabled) UnityEngine.InputSystem.InputSystem.EnableDevice(gyroDev);
            }
            var w = gyroDev.angularVelocity.ReadValue();
            float k = gyroScale * (f.ads ? .8f : 1f) * dt;
            f.yaw -= w.y * k;
            f.pitch = Mathf.Clamp(f.pitch + w.x * k, -1.3f, 1.3f);
        }

        // ------------------------------------------------------------------ buttons
        void Press(Button b, bool down)
        {
            switch (b.id)
            {
                case "fire":
                    f.inFire = down;
                    // Settings > Aim when firing: Fire also aims, and lets go of it after (unless you'd aimed yourself)
                    if (down && autoAds && !Rules.Rumble) { var a = buttons.Find(q => q.id == "aim"); if (a != null && !a.on) { SetAim(true); autoR = true; } }
                    else if (!down && autoR) { autoR = false; SetAim(false); }
                    break;
                case "jump": f.inJump = down; if (down) f.inJumpPress = true; break;
                case "crouch": if (down) f.crouchOn = !f.crouchOn; break;
                case "aim": autoR = false; break;
                case "shop":
                    if (down) { var h = FindAnyObjectByType<WeaponHud>(); if (h) h.ToggleMenu(); }
                    break;
            }
            b.onPress?.Invoke(down);
        }

        void SetAim(bool on)
        {
            var a = buttons.Find(q => q.id == "aim");
            if (a != null && a.on != on) { a.on = on; Press(a, on); }
            if (!on && f) f.ads = false;
        }

        // let go of everything (going down, a finishing move): hidden buttons mustn't stay held
        void ReleaseAll()
        {
            foreach (var b in buttons)
            {
                if (b.held) { b.held = false; Press(b, false); }
                b.finger = -1;
            }
            SetAim(false);
            if (f) { f.inFire = false; f.inJump = false; }
        }

        // ------------------------------------------------------------------ vibration
        void OnHitFeedback(Fighter victim, float shown, bool head, bool strong)
        {
            if (!Showing || victim == f) return;
            Haptics.Pulse(head || strong ? 18 : 10, head || strong ? 200 : 110);
        }
        void OnDamaged(float amount, Fighter by) { if (Showing && amount > 0) Haptics.Pulse(amount >= 30 ? 45 : 25, 170); }
        void OnKilled(Fighter victim, Fighter killer, bool head)
        {
            if (!Showing) return;
            if (killer == f && victim != f) Haptics.Pulse(60, 255);
            if (victim == f) SetAim(false);
        }
        void OnRound(int round) { ReleaseAll(); wasDown = wasDowned = false; }

        // ------------------------------------------------------------------ input
        void Update()
        {
            if (!f) return;
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();   // also after a script reload in play mode
            var touches = ETouch.activeTouches;
            if (touches.Count > 0) seenTouch = true;
            if (!Showing || TouchLayout.Editing) return;

            float dt = Time.deltaTime;
            var rm = RoundManager.Instance;
            bool matchEnd = rm && rm.state == RoundManager.State.MatchEnd;
            bool menu = WeaponHud.MenuOpen;
            bool down = Down;
            if (down && !wasDown) ReleaseAll();
            if (f.downed && !wasDowned) Haptics.Pulse(120, 255);
            wasDown = down; wasDowned = f.downed;

            FindTarget();
            lookMoved = 0;

            foreach (var t in touches)
            {
                int id = t.touchId; var p = t.screenPosition;
                switch (t.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        if (matchEnd && !KillClips.Viewing && !MatchEndMenu.Active && RematchRect.Contains(p)) { rm.StartMatch(); break; }   // MatchEndMenu has its own Rematch
                        var b = Hit(p, menu);
                        if (b != null && b.finger < 0)
                        {
                            b.finger = id;
                            if (b.toggle) { b.on = !b.on; Press(b, b.on); } else { b.held = true; Press(b, true); }
                            if (b.id == "aim" && b.on) { FindTarget(); Pull(Mathf.Min(.85f, .55f * aimAssist), true); }   // part 3: snap on aim
                            break;
                        }
                        if (menu || down && f.InFinisher) break;
                        if (stickFinger < 0 && InStickZone(p)) { stickFinger = id; stickOrigin = StickOriginFor(p); break; }
                        if (lookFinger < 0) lookFinger = id;
                        break;
                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                        if (menu) break;
                        if (id == lookFinger) Look(t.delta);
                        foreach (var q in buttons) if (q.finger == id && q.id == "fire") Look(t.delta);   // aim while firing
                        break;
                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        if (id == stickFinger) { stickFinger = -1; stick = Vector2.zero; f.inFwd = f.inSide = 0; }   // nothing else clears these on a phone
                        if (id == lookFinger) lookFinger = -1;
                        foreach (var q in buttons) if (q.finger == id) { q.finger = -1; if (!q.toggle && q.held) { q.held = false; Press(q, false); } }
                        break;
                }
                if (id == stickFinger)
                {
                    var v = (p - stickOrigin) / StickR; if (v.magnitude > 1) v.Normalize();
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

            if (!menu && !down && f.alive && !f.Frozen)
            {
                GyroLook(dt);
                // part 2 only while you're doing something (looking, firing, moving), never on its own
                bool engaged = lookMoved > .5f || f.inFire || stick.sqrMagnitude > .04f;
                if (engaged) Pull(aimAssist * 4f * dt * (1 - .5f * assistOff / Mathf.Max(1e-4f, assistCone)), false);
            }
        }

        // ------------------------------------------------------------------ drawing
        void Disc(Vector2 centreBL, float diameter, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(centreBL.x - diameter / 2, Screen.height - centreBL.y - diameter / 2, diameter, diameter), disc);
        }

        void Pill(Rect bl, Color c)
        {
            float h = bl.height;
            Disc(new Vector2(bl.xMin + h / 2, bl.center.y), h, c);
            Disc(new Vector2(bl.xMax - h / 2, bl.center.y), h, c);
            GUI.color = c;
            GUI.DrawTexture(new Rect(bl.xMin + h / 2, Screen.height - bl.yMax, bl.width - h, h), white);
        }

        void OnGUI()
        {
            if (!Showing || !f) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            float s = S;
            label.fontSize = Mathf.RoundToInt(13 * s);

            var rm = RoundManager.Instance;
            if (rm && rm.state == RoundManager.State.MatchEnd && !KillClips.Viewing && !MatchEndMenu.Active)
            {
                var r = RematchRect;
                Pill(r, new Color(1, 1, 1, .92f));
                GUI.color = Color.black; label.fontSize = Mathf.RoundToInt(16 * s);
                GUI.Label(new Rect(r.x, Screen.height - r.yMax, r.width, r.height), "Rematch", label);
                label.fontSize = Mathf.RoundToInt(13 * s);
            }
            if (Cine.HideHud) { GUI.color = Color.white; return; }   // the throne room or Man Down: no game buttons over it
            if (!f.alive) { GUI.color = Color.white; return; }

            bool menu = WeaponHud.MenuOpen, down = Down;
            if (!menu && !(down && f.InFinisher))
            {
                bool held = stickFinger >= 0;
                Disc(StickCentre, 132 * s, new Color(0, 0, 0, held ? .32f : .2f));
                Disc(StickCentre + stick * 38 * s, 56 * s, new Color(1, 1, 1, held ? .85f : .55f));
            }
            foreach (var b in buttons)
            {
                if (!Live(b, menu)) continue;
                if (b.id == "shop") b.label = menu ? "Close" : "Shop";
                var c = Centre(b); bool lit = b.held || b.on;
                float bs = b.size * TouchLayout.Scale(b.id), ba = TouchLayout.Alpha(b.id); var bc = lit ? new Color(1, 1, 1, .55f) : b.color; bc.a *= ba;
                Disc(c, bs * s, bc);
                GUI.color = lit ? Color.black : Color.white;
                GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, Mathf.Max(.35f, ba));
                GUI.Label(new Rect(c.x - bs * s / 2, Screen.height - c.y - bs * s / 2, bs * s, bs * s), b.label, label);
            }
            GUI.color = Color.white;
        }
    }
}
