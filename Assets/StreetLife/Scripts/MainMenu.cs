// The front end: monochrome (black panels, white type and selections), one red-orange accent on Play:
//   1  Mode & map: bot difficulty, 1v1 rules, the map (all eight, from the maps chat's MapSet), with the live map slowly
//      circling behind the menu.
//   2  Character: every character (characters chat), dressed on your own fighter live in front of the camera.
//   3  Loadout: primary, secondary and lethal (Normal rules; Simple shows the fixed kit), put on your fighter at Play.
//   Play: starts the match (a different map reloads the scene there first and goes straight in).
// Esc in a match pauses: Resume, Settings, Main menu. Settings (gear): look and aim sensitivity, music and effects volume,
// graphics (High = the PC quality level, Smooth = the lighter one phones and the browser use), frame-rate readout.
// Built with UI Toolkit from code, so there are no assets to keep in step.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace StreetLife
{
    [DefaultExecutionOrder(-60)]
    public class MainMenu : MonoBehaviour
    {
        public static bool Open { get; private set; }
        public static bool Paused { get; private set; }
        static bool skipOnce;   // set before a reload for a new map: go straight into the match afterwards

        // ---- palette (the web game's)
        // Monochrome: black panels, white type and selections, one accent (Accent) used sparingly: Play, the "1v1" mark, the map underline.
        static readonly Color Bg = new Color32(6, 6, 6, 235), Panel = new Color32(10, 10, 10, 238), Line = new Color32(70, 70, 70, 255),
            Chip = new Color32(32, 32, 32, 255), Text = new Color32(245, 245, 245, 255), Dim = new Color32(138, 138, 138, 255),
            Sel = new Color32(255, 255, 255, 255), Accent = new Color32(255, 59, 48, 255), AccentDeep = new Color32(214, 34, 26, 255), Ink = new Color32(0, 0, 0, 255);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Settings.Apply();
            if (FindAnyObjectByType<MainMenu>() || !FindAnyObjectByType<MapBuilder>()) return;
            new GameObject("Main menu").AddComponent<MainMenu>();
        }

        UIDocument doc; VisualElement root, menu, pause, settings, mapCard, charGrid, charInfo, stepA, stepB, stepC, loadBox;
        Label mapTitle, mapDesc, mapTag;
        int step; string selMap; float orbit;
        readonly List<Behaviour> hidden = new List<Behaviour>();
        ShoulderCamera shoulder; Fighter player;

        void Awake()
        {
            RoundManager.HoldAtMenu = !skipOnce;
            Open = !skipOnce; skipOnce = false;
            if (!FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>())
            {
                var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize; ps.referenceResolution = new Vector2Int(1280, 720);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; ps.match = .5f; ps.sortingOrder = 100;
            doc = gameObject.AddComponent<UIDocument>(); doc.panelSettings = ps;
        }

        void Start()
        {
            root = doc.rootVisualElement;
            // every button in the menus gives a soft tick (16 Nov; Pete chose it). Caught once here on the way down, so new buttons get it too.
            root.RegisterCallback<ClickEvent>(e => { for (var v = e.target as VisualElement; v != null; v = v.parent) if (v is Button) { Sfx.Ui("uiclick", .55f); break; } }, TrickleDown.TrickleDown);
            root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            root.pickingMode = PickingMode.Ignore;
            // no UI theme: the document root has no size of its own, so pin it to the screen
            root.style.position = Position.Absolute; root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            selMap = Settings.RandomMap ? RandomId : MapSet.Current;
            { var cur = MapSet.Load() ? MapSet.Load().Get(selMap) : null; Settings.Mode = ModeKey(cur); }
            MenuPortraits.Ready += OnPortrait;
            BuildMenu(); BuildPause(); BuildSettings();
            // hover: every button gets the white edge while the pointer is on it (no fading); it goes back to its own edge after
            root.RegisterCallback<PointerOverEvent>(e => HoverOn(ButtonOf(e.target)), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerOutEvent>(e => HoverOff(ButtonOf(e.target)), TrickleDown.TrickleDown);
            Show(Open ? menu : null);
            if (Open) Enter(); else { player = FindPlayer(); ApplyLoadout(); }
        }

        // ================================================================== building blocks
        static void Radius(VisualElement e, float r) { e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r; }
        static void Border(VisualElement e, float w, Color c)
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = w;
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c;
        }
        static void Pad(VisualElement e, float v, float h) { e.style.paddingTop = e.style.paddingBottom = v; e.style.paddingLeft = e.style.paddingRight = h; }
        static Label L(string t, int size, Color c, bool bold = false)
        {
            var l = new Label(t); l.style.fontSize = size; l.style.color = c; l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            l.style.marginBottom = l.style.marginTop = 0; Pad(l, 0, 0); l.pickingMode = PickingMode.Ignore; return l;
        }
        static Label Caps(string t) { var l = L(t.ToUpperInvariant(), 11, Dim, true); l.style.letterSpacing = 1.5f; l.style.marginTop = 14; l.style.marginBottom = 6; return l; }

        // ---- hover edge. The hover white is one step off pure white (Sel), so a click that selects the button while it's
        // hovered (its refresh sets Sel) is told apart from the hover itself and isn't undone when the pointer leaves.
        static readonly Color HoverEdge = new Color32(255, 255, 255, 254);
        class EdgeWas { public Color t, b, l, r; public float tw, bw, lw, rw; }
        static Button ButtonOf(IEventHandler target) { for (var v = target as VisualElement; v != null; v = v.parent) if (v is Button btn) return btn; return null; }
        static bool IsHover(Color c) => Mathf.Abs(c.a - HoverEdge.a) < .002f && c.r > .99f && c.g > .99f && c.b > .99f;
        static void HoverOn(Button b)
        {
            if (b == null || b.userData is EdgeWas) return;
            var st = b.resolvedStyle;
            b.userData = new EdgeWas { t = st.borderTopColor, b = st.borderBottomColor, l = st.borderLeftColor, r = st.borderRightColor, tw = st.borderTopWidth, bw = st.borderBottomWidth, lw = st.borderLeftWidth, rw = st.borderRightWidth };
            // colour only: the edge widths never change, so nothing around the button moves
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = HoverEdge;
        }
        static void HoverOff(Button b)
        {
            if (b == null || !(b.userData is EdgeWas was)) return;
            b.userData = null;
            if (!IsHover(b.style.borderTopColor.value)) return;   // selected while hovered: keep what its refresh set
            b.style.borderTopColor = was.t; b.style.borderBottomColor = was.b; b.style.borderLeftColor = was.l; b.style.borderRightColor = was.r;
            b.style.borderTopWidth = was.tw; b.style.borderBottomWidth = was.bw; b.style.borderLeftWidth = was.lw; b.style.borderRightWidth = was.rw;
        }

        static Texture2D gradient;
        static Texture2D Gradient()
        {
            if (gradient) return gradient;
            gradient = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < 256; i++) gradient.SetPixel(i, 0, Color.Lerp(Accent, AccentDeep, i / 255f));
            gradient.Apply(); return gradient;
        }

        Button Btn(string text, Action click, bool primary = false, int size = 15)
        {
            var b = new Button(click) { text = text };
            b.style.fontSize = size; b.style.color = Text; b.style.unityFontStyleAndWeight = FontStyle.Bold; b.style.unityTextAlign = TextAnchor.MiddleCenter;
            Radius(b, primary ? 10 : 8); Border(b, primary ? 2 : 1, primary ? new Color(0, 0, 0, .35f) : Line); Pad(b, primary ? 12 : 8, primary ? 40 : 16);
            b.style.marginLeft = b.style.marginRight = 0; b.style.marginTop = b.style.marginBottom = 0;
            b.style.flexShrink = 0; b.style.whiteSpace = WhiteSpace.NoWrap;
            if (primary) { b.style.backgroundImage = new StyleBackground(Gradient()); b.style.backgroundColor = Accent; b.style.fontSize = 20; }
            else b.style.backgroundColor = Chip;
            return b;
        }

        /// The web game's segmented control: one bar, the picked option filled cyan.
        // ---- step 1's description: explains exactly what the picked mode, players, rules and bots mean
        Label modeHint;
        void RefreshHint()
        {
            if (modeHint == null) return;
            var sb = new System.Text.StringBuilder();
            if (Rules.Rumble) sb.Append("<color=#F5F5F5><b>Rumble</b></color>: melee only. You start on fists, and each kill without dying moves you up to the knife, the bat, then the sword. Hit someone while you're falling for an air strike.");
            else sb.Append("<color=#F5F5F5><b>Duels</b></color>: guns and a lethal of your choice (step 3), walls you can build (Q) and the shop (Tab).");
            sb.Append("\n\n");
            if (Rules.Ffa) sb.Append($"<color=#F5F5F5><b>Free for all, {Rules.FfaN} fighters</b></color>: you and {Rules.FfaN - 1} bot{(Rules.FfaN - 1 == 1 ? "" : "s")}, everyone for themselves. You respawn after each death; first to {Rules.DM_KILLS} kills wins ({Mathf.RoundToInt(Rules.RoundTime / 60)} min limit).");
            else if (Rules.TwoVTwo) sb.Append($"<color=#F5F5F5><b>2v2</b></color>: you and a bot team-mate against two bots. Rounds of {Mathf.RoundToInt(Rules.RoundTime)} s; first team to {Rules.ROUNDS_TO_WIN} rounds wins.");
            else sb.Append($"<color=#F5F5F5><b>1v1</b></color>: you against one bot. Rounds of {Mathf.RoundToInt(Rules.RoundTime)} s; first to {Rules.ROUNDS_TO_WIN} rounds wins.");
            sb.Append("\n\n");
            if (Rules.Rumble || !Rules.OneVOne) sb.Append("<color=#F5F5F5><b>Rules</b></color>: Simple only applies to 1v1 Duels, so this match uses Normal rules.");
            else if (Settings.Rules == "simple") sb.Append("<color=#F5F5F5><b>Simple rules</b></color>: everyone gets the M4, sword and grenade, and no sniper bots.");
            else sb.Append("<color=#F5F5F5><b>Normal rules</b></color>: you pick your own loadout, and bots can play any style, snipers included.");
            sb.Append("\n\n");
            switch (Settings.Difficulty)
            {
                case "easy": sb.Append("<color=#F5F5F5><b>Easy bots</b></color>: about 1.5 s to react, loose aim, almost never go for your head."); break;
                case "hard": sb.Append("<color=#F5F5F5><b>Hard bots</b></color>: about 0.4 s to react, tight aim, strafe a lot and often go for your head."); break;
                case "extreme": sb.Append("<color=#F5F5F5><b>Extreme bots</b></color>: about 0.3 s to react, near-perfect tracking and frequent headshots."); break;
                default: sb.Append("<color=#F5F5F5><b>Normal bots</b></color>: about 0.8 s to react, decent aim and the odd headshot."); break;
            }
            modeHint.text = sb.ToString();
        }

        readonly List<Action> segRefresh = new List<Action>();   // every segmented control, refreshed when a setting changes elsewhere
        VisualElement Segmented(string[] labels, string[] values, Func<string> get, Action<string> set)
        {
            var bar = new VisualElement(); bar.style.flexDirection = FlexDirection.Row; bar.style.backgroundColor = Chip; Radius(bar, 9); Pad(bar, 3, 3);
            var buttons = new List<Button>();
            segRefresh.Add(() => Refresh());
            void Refresh() { for (int i = 0; i < buttons.Count; i++) { bool on = values[i] == get(); buttons[i].style.backgroundColor = on ? Sel : new Color(0, 0, 0, 0); buttons[i].style.color = on ? Ink : Dim; } }
            for (int i = 0; i < labels.Length; i++)
            {
                var v = values[i];
                var b = new Button(() => { set(v); Refresh(); RefreshHint(); }) { text = labels[i] };
                b.style.flexGrow = 1; b.style.unityTextAlign = TextAnchor.MiddleCenter; b.style.fontSize = 14; b.style.unityFontStyleAndWeight = FontStyle.Bold; Radius(b, 7); Border(b, 1, new Color(0, 0, 0, 0)); Pad(b, 7, 6);
                b.style.marginLeft = b.style.marginRight = b.style.marginTop = b.style.marginBottom = 0;
                buttons.Add(b); bar.Add(b);
            }
            Refresh(); return bar;
        }

        /// ‹ value › stepper (settings): works with no UI theme at all.
        VisualElement Stepper(string name, float[] steps, Func<float> get, Action<float> set, Func<float, string> fmt, int valW = 70)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.justifyContent = Justify.SpaceBetween; row.style.marginBottom = 10;
            row.Add(L(name, 15, Text));
            var box = new VisualElement(); box.style.flexDirection = FlexDirection.Row; box.style.alignItems = Align.Center;
            var val = L("", 15, Sel, true); val.style.width = valW; val.style.unityTextAlign = TextAnchor.MiddleCenter;
            int Index() { float v = get(); int best = 0; for (int i = 1; i < steps.Length; i++) if (Mathf.Abs(steps[i] - v) < Mathf.Abs(steps[best] - v)) best = i; return best; }
            void Refresh() => val.text = fmt(steps[Index()]);
            // one rounded pill: − value +
            box.style.backgroundColor = Chip; Radius(box, 10); Border(box, 2, Line); Pad(box, 2, 2);
            Button Step(string g, int d)
            {
                var sb = new Button(() => { set(steps[Mathf.Clamp(Index() + d, 0, steps.Length - 1)]); Refresh(); }) { text = g };
                sb.style.width = 34; sb.style.height = 30; sb.style.fontSize = 18; sb.style.color = Text; sb.style.unityFontStyleAndWeight = FontStyle.Bold;
                sb.style.unityTextAlign = TextAnchor.MiddleCenter; sb.style.backgroundColor = new Color(0, 0, 0, 0); Radius(sb, 8); Border(sb, 1, new Color(0, 0, 0, 0)); Pad(sb, 0, 0);
                sb.style.marginLeft = sb.style.marginRight = sb.style.marginTop = sb.style.marginBottom = 0;
                return sb;
            }
            box.Add(Step("-", -1)); box.Add(val); box.Add(Step("+", 1));
            row.Add(box); Refresh(); return row;
        }

        VisualElement Card(float w)
        {
            var c = new VisualElement(); c.style.backgroundColor = Panel; Radius(c, 14); Border(c, 1, Line); Pad(c, 18, 22);
            if (w > 0) c.style.width = w; return c;
        }

        VisualElement Overlay()
        {
            var o = new VisualElement(); o.style.position = Position.Absolute; o.style.left = o.style.top = o.style.right = o.style.bottom = 0;
            o.style.alignItems = Align.Center; o.style.justifyContent = Justify.Center; root.Add(o); return o;
        }

        // ================================================================== main menu
        void BuildMenu()
        {
            menu = Overlay(); menu.style.justifyContent = Justify.FlexStart; menu.style.alignItems = Align.Stretch; Pad(menu, 18, 22);
            menu.pickingMode = PickingMode.Ignore;

            // top bar: imok 1v1 + settings
            var top = new VisualElement(); top.style.flexDirection = FlexDirection.Row; top.style.justifyContent = Justify.SpaceBetween; top.style.alignItems = Align.Center; top.style.marginBottom = 12;
            var title = new VisualElement(); title.style.flexDirection = FlexDirection.Row; title.style.alignItems = Align.FlexEnd;
            var imok = L("imok", 38, Text); imok.style.marginRight = 10; title.Add(imok); title.Add(L("1v1", 38, Accent));
            top.Add(title);
            top.Add(Btn("Settings", () => Show(settings, menu)));
            if (CanQuit) { var qb = Btn("Quit", QuitGame); qb.style.marginLeft = 10; top.Add(qb); }   // after Settings (ProfileMenu finds Settings by its text)
            menu.Add(top);

            // the two steps share one panel; the middle stays open so the live map / your fighter show through
            var body = new VisualElement(); body.style.flexGrow = 1; body.style.flexDirection = FlexDirection.Row; body.pickingMode = PickingMode.Ignore;
            menu.Add(body);

            // ---- step 1: mode & map
            stepA = new VisualElement(); stepA.style.flexGrow = 1; stepA.style.flexDirection = FlexDirection.Row; stepA.pickingMode = PickingMode.Ignore;
            var left = Card(330); left.style.marginRight = 16; left.style.alignSelf = Align.FlexStart;
            left.Add(StepHead(1, "Mode & map"));
            left.Add(Caps("Mode")); left.Add(Segmented(new[] { "Duels", "Rumble" }, new[] { "duels", "rumble" }, () => Rules.Mode, v => { Rules.Mode = v; RefreshMap(); }));
            left.Add(Caps("Players")); left.Add(Segmented(new[] { "1v1", "2v2", "FFA 4", "FFA 6" }, new[] { "1v1", "2v2", "ffa4", "ffa6" }, () => Rules.Ffa ? "ffa" + Rules.FfaN : Rules.Players, v => { if (v.StartsWith("ffa")) { Rules.Players = "ffa"; Rules.FfaN = v[3] - '0'; } else Rules.Players = v; RefreshMap(); }));
            left.Add(Caps("Rules")); left.Add(Segmented(new[] { "Normal", "Simple" }, new[] { "normal", "simple" }, () => Settings.Rules, v => Settings.Rules = v));
            left.Add(Caps("Bots")); left.Add(Segmented(new[] { "Easy", "Normal", "Hard", "Extreme" }, new[] { "easy", "normal", "hard", "extreme" }, () => Settings.Difficulty, v => Settings.Difficulty = v));
            var hint = L("", 12, Dim); hint.style.marginTop = 12; hint.style.whiteSpace = WhiteSpace.Normal; modeHint = hint; RefreshHint();
            left.Add(hint);
            stepA.Add(left);

            var right = new VisualElement(); right.style.flexGrow = 1; right.pickingMode = PickingMode.Ignore;
            mapCard = new VisualElement(); mapCard.style.flexGrow = 1; Radius(mapCard, 16); Border(mapCard, 2, Sel); mapCard.pickingMode = PickingMode.Ignore;
            mapCard.style.justifyContent = Justify.FlexEnd; Pad(mapCard, 26, 30);
            var arrows = new VisualElement(); arrows.style.position = Position.Absolute; arrows.style.left = 14; arrows.style.right = 14; arrows.style.top = new Length(45, LengthUnit.Percent);
            arrows.style.flexDirection = FlexDirection.Row; arrows.style.justifyContent = Justify.SpaceBetween; arrows.pickingMode = PickingMode.Ignore;
            arrows.Add(Round("<", () => StepMap(-1))); arrows.Add(Round(">", () => StepMap(1)));
            mapCard.Add(arrows);
            mapTag = L("", 12, Text, true); mapTag.style.alignSelf = Align.FlexStart; mapTag.style.letterSpacing = 1.5f; Radius(mapTag, 5); Pad(mapTag, 3, 8);
            mapTag.style.backgroundColor = Sel; mapTag.style.color = Ink;
            mapTitle = L("", 64, Text); mapTitle.style.marginTop = 6;
            var bar = new VisualElement(); bar.style.height = 4; bar.style.width = 300; Radius(bar, 2); bar.style.backgroundImage = new StyleBackground(Gradient()); bar.style.marginTop = 2; bar.style.marginBottom = 10;
            mapDesc = L("", 15, Text, true); mapDesc.style.letterSpacing = 2;
            mapDesc.style.alignSelf = Align.FlexStart; mapDesc.style.backgroundColor = new Color(0, 0, 0, .55f); Radius(mapDesc, 6); Pad(mapDesc, 5, 10); mapDesc.style.marginTop = 8; mapDesc.style.whiteSpace = WhiteSpace.Normal;
            foreach (var e in new VisualElement[] { mapTag, mapTitle, bar, mapDesc }) mapCard.Add(e);
            right.Add(mapCard);
            var chips = new VisualElement(); chips.style.flexDirection = FlexDirection.Row; chips.style.flexWrap = Wrap.Wrap; chips.style.marginTop = 10;
            var bigChips = new VisualElement(); bigChips.style.flexDirection = FlexDirection.Row; bigChips.style.flexWrap = Wrap.Wrap;
            var smallChips = new VisualElement(); smallChips.style.flexDirection = FlexDirection.Row; smallChips.style.flexWrap = Wrap.Wrap;
            var set = MapSet.Load();
            if (set && set.maps != null)
                foreach (var m in set.maps)
                {
                    if (m == null || (!m.IsStreet && (!m.mapJson || !m.library))) continue;   // left out of this build
                    var id = m.id;
                    var c = Btn(m.title, () => { selMap = id; RefreshMap(); }, false, 13); Border(c, 2, Line); c.name = "map-" + id; c.style.marginRight = 8; c.style.marginBottom = 6;
                    (IsBig(id) ? bigChips : smallChips).Add(c);
                }
            chips.style.flexDirection = FlexDirection.Column;
            { var rnd = Btn("Random", () => { selMap = RandomId; RefreshMap(); }, false, 13); Border(rnd, 2, Line); rnd.name = "map-" + RandomId; rnd.style.alignSelf = Align.FlexStart; rnd.style.marginBottom = 6; chips.Add(rnd); }
            foreach (var (cap, row) in new[] { ("Big maps", bigChips), ("Small maps", smallChips) }) { if (row.childCount == 0) continue; var lab = Caps(cap); lab.style.color = Text; lab.style.marginTop = 4; chips.Add(lab); chips.Add(row); }
            if (bigChips.childCount + smallChips.childCount <= 1) { chips.style.display = DisplayStyle.None; arrows.style.display = DisplayStyle.None; }   // a one-map build (the Street Life WebGL)
            right.Add(chips);
            stepA.Add(right);
            body.Add(stepA);

            // ---- step 2: character
            stepB = new VisualElement(); stepB.style.flexGrow = 1; stepB.style.flexDirection = FlexDirection.Row; stepB.pickingMode = PickingMode.Ignore;
            var grid = Card(460); grid.style.alignSelf = Align.FlexStart; grid.style.overflow = Overflow.Hidden;
            grid.Add(StepHead(2, "Character"));
            charGrid = new VisualElement(); charGrid.style.flexDirection = FlexDirection.Row; charGrid.style.flexWrap = Wrap.Wrap; charGrid.style.marginTop = 12;
            // the picked character's name and line first, then the boxes, so nothing runs off the bottom of the panel
            charInfo = new VisualElement(); charInfo.style.marginTop = 10; charInfo.style.minHeight = 84; grid.Add(charInfo);
            grid.Add(charGrid);
            stepB.Add(grid);
            var gap = new VisualElement(); gap.style.flexGrow = 1; gap.pickingMode = PickingMode.Ignore; stepB.Add(gap);   // your fighter, live
            body.Add(stepB);
            BuildCharacters();

            // ---- step 3: loadout (Normal rules; Simple shows the fixed kit)
            stepC = new VisualElement(); stepC.style.flexGrow = 1; stepC.style.flexDirection = FlexDirection.Row; stepC.pickingMode = PickingMode.Ignore;
            var lc = Card(640); lc.style.alignSelf = Align.FlexStart; lc.style.overflow = Overflow.Hidden;
            lc.Add(StepHead(3, "Loadout"));
            loadBox = new VisualElement(); lc.Add(loadBox);
            stepC.Add(lc);
            var gap3 = new VisualElement(); gap3.style.flexGrow = 1; gap3.pickingMode = PickingMode.Ignore; stepC.Add(gap3);
            stepC.Add(WeaponShowcase.Panel());   // the weapon you pick, under studio lights; drag to turn it
            body.Add(stepC);

            // ---- bottom: back / next or play
            var bottom = new VisualElement(); bottom.style.flexDirection = FlexDirection.Row; bottom.style.justifyContent = Justify.Center; bottom.style.alignItems = Align.Center; bottom.style.marginTop = 14;
            var back = Btn("Back", () => SetStep(Mathf.Max(0, step - 1))); back.name = "back"; back.style.marginRight = 14;
            var next = Btn("Character", () => { if (step < 2) SetStep(step + 1); else Play(); }, true); next.name = "next";
            bottom.Add(back); bottom.Add(next);
            menu.Add(bottom);
            SetStep(0); RefreshMap();
        }

        VisualElement StepHead(int n, string text)
        {
            var h = new VisualElement(); h.style.flexDirection = FlexDirection.Row; h.style.alignItems = Align.Center;
            var dot = L(n.ToString(), 14, Ink, true); dot.style.width = dot.style.height = 26; Radius(dot, 13); dot.style.backgroundColor = Sel; dot.style.unityTextAlign = TextAnchor.MiddleCenter; dot.style.marginRight = 10;
            h.Add(dot); h.Add(L(text, 20, Text)); return h;
        }

        Button Round(string t, Action a)
        {
            var b = Btn(t, a, false, 26); b.style.width = b.style.height = 52; Radius(b, 26); Pad(b, 0, 0); b.style.backgroundColor = new Color(0, 0, 0, .45f); Border(b, 2, Line);
            return b;
        }

        void SetStep(int s)
        {
            step = s;
            stepA.style.display = s == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            stepB.style.display = s == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            stepC.style.display = s == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            if (s == 2) BuildLoadout();
            WeaponShowcase.SetActive(s == 2, s == 2 ? (Settings.Rules == "simple" ? "gar" : Settings.Primary) : null);
            var back = menu.Q<Button>("back"); if (back != null) back.style.visibility = s == 0 ? Visibility.Hidden : Visibility.Visible;
            var next = menu.Q<Button>("next"); if (next != null) next.text = s == 0 ? "Character" : s == 1 ? "Loadout" : "Play";
        }

        void StepMap(int d)
        {
            var set = MapSet.Load(); if (!set || set.maps == null || set.maps.Length == 0) return;
            int i = Array.FindIndex(set.maps, m => m != null && m.id == selMap); if (i < 0) i = 0;
            for (int k = 0; k < set.maps.Length; k++)
            {
                i = (i + d + set.maps.Length) % set.maps.Length; var m = set.maps[i];
                if (Usable(m)) break;   // every map in every mode
            }
            selMap = set.maps[i].id; RefreshMap();
        }

        /// the four big maps; the rest (Sky High, Lagoon, The House, Backyard) are the small ones
        static bool IsBig(string id) => id == "street" || id == "wreck" || id == "arena" || id == "rail";

        static bool Usable(MapSet.Entry m) => m != null && (m.IsStreet || (m.mapJson && m.library));
        /// Duels maps vs everything else (1v1, plus the Rumble maps, which are played here as 1v1)
        static string ModeKey(MapSet.Entry m) => m == null || string.IsNullOrEmpty(m.mode) || m.mode.IndexOf("duel", StringComparison.OrdinalIgnoreCase) >= 0 ? "duels" : "1v1";

        void SetMode(string v)
        {
            Settings.Mode = v;
            var set = MapSet.Load(); if (!set || set.maps == null) { RefreshMap(); return; }
            var cur = set.Get(selMap);
            if (cur == null || ModeKey(cur) != v)
            {
                var live = set.Get(MapSet.Current);
                if (Usable(live) && ModeKey(live) == v) selMap = live.id;
                else foreach (var m in set.maps) if (Usable(m) && ModeKey(m) == v) { selMap = m.id; break; }
            }
            RefreshMap();
        }

        void RefreshMap()
        {
            var set = MapSet.Load(); var m = set ? set.Get(selMap) : null;
            Settings.RandomMap = selMap == RandomId;
            mapTitle.text = Settings.RandomMap ? "Random" : m != null ? m.title : "Street Life";
            mapDesc.text = (Settings.RandomMap ? "A different map every match" : m != null ? m.desc : "Suburban road, cars and front yards").ToUpperInvariant();
            var md = m != null && !string.IsNullOrEmpty(m.mode) ? m.mode : "Duels";
            mapTag.text = ((IsBig(selMap) ? "Big map" : "Small map") + "  ·  " + (Rules.Rumble ? "Rumble" : "Duels") + "  ·  " + (Rules.Ffa ? "FFA " + Rules.FfaN : Rules.Players)).ToUpperInvariant();
            // the live map shows through when it's the one picked; another map gets its own colours until you play it
            bool live = selMap == MapSet.Current || Settings.RandomMap;
            var sw = m != null && m.swatch != null && m.swatch.Length > 0 ? m.swatch[0] : new Color(.2f, .3f, .25f);
            var thumb = live ? null : Thumb(selMap);
            mapCard.style.backgroundImage = thumb ? new StyleBackground(thumb) : new StyleBackground((Texture2D)null);
            mapCard.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
            mapCard.style.backgroundColor = live ? new Color(0, 0, 0, .08f) : thumb ? Color.white : new Color(sw.r * .6f, sw.g * .6f, sw.b * .6f, .92f);
            if (set && set.maps != null)
                foreach (var e in set.maps) { if (e == null) continue; var b = menu.Q<Button>("map-" + e.id); if (b == null) continue; bool on = e.id == selMap; Border(b, 2, on ? Sel : Line); b.style.color = on ? Text : Dim; }
            { var rb = menu.Q<Button>("map-" + RandomId); if (rb != null) { bool on = selMap == RandomId; Border(rb, 2, on ? Sel : Line); rb.style.color = on ? Text : Dim; } }
        }

        // ---- map pictures for the carousel: taken from the menu's own view the first time each map is shown live.
        // In the editor they're saved into the project (Resources/StreetLife/MapThumbs), so builds ship with them;
        // in a build, any missing one is saved on the device instead.
        static readonly Dictionary<string, Texture2D> thumbs = new Dictionary<string, Texture2D>();
        static string DeviceThumb(string id) => System.IO.Path.Combine(Application.persistentDataPath, "mapthumb-" + id + ".png");
        static Texture2D Thumb(string id)
        {
            if (thumbs.TryGetValue(id, out var t) && t) return t;
            t = Resources.Load<Texture2D>("StreetLife/MapThumbs/" + id);
            if (!t)
            {
                try { var f = DeviceThumb(id); if (System.IO.File.Exists(f)) { t = new Texture2D(2, 2); t.LoadImage(System.IO.File.ReadAllBytes(f)); } } catch { }
            }
            if (t) thumbs[id] = t;
            return t;
        }

        const int ThumbW = 1280, ThumbH = 720;   // Editor/MenuThumbImport.cs keeps them unscaled and without mipmaps

        // ---- map pictures follow the maps (16 Nov). In the editor each picture is saved with a signature of the files its
        // map is made from (Editor/MapThumbs/<id>.sig). Whenever a map is shown in the menu and its signature has changed,
        // the picture is taken again. `call MenuShots.Thumbs` (and every build) retakes all that are out of date.
        public static string ThumbSigPath(string id) => System.IO.Path.Combine(Application.dataPath, "StreetLife/Editor/MapThumbs/" + id + ".sig");
        public static string ThumbSig(string id)
        {
            var root = Application.dataPath + "/StreetLife/"; var files = new List<string>();
            void Dir(string d, string pat) { try { if (System.IO.Directory.Exists(root + d)) files.AddRange(System.IO.Directory.GetFiles(root + d, pat, System.IO.SearchOption.AllDirectories)); } catch { } }
            if (id == "street") { Dir("Data", "map.*"); Dir("Data", "dyn.*"); Dir("Data", "materials.json"); }
            Dir("Maps/" + id, "*"); Dir("Maps/Shared", "*"); Dir("Scripts/Maps", "*.cs"); Dir("Resources", "*Look.asset");
            foreach (var one in new[] { "Scripts/MapBuilder.cs", "Scripts/GlassLook.cs", "Scripts/WorldLook.cs", "Scripts/Destruction.cs", "Scripts/TownLife.cs" }) if (System.IO.File.Exists(root + one)) files.Add(root + one);
            files.Sort(string.CompareOrdinal); ulong h = 1469598103934665603UL;
            foreach (var p in files)
            {
                if (p.EndsWith(".meta")) continue;
                var fi = new System.IO.FileInfo(p); string line = p.Substring(root.Length).Replace('\\', '/') + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
                foreach (char ch in line) { h ^= ch; h *= 1099511628211UL; }
            }
            return h.ToString("x16");
        }
        public static bool ThumbStale(string id)
        {
            try { var p = ThumbSigPath(id); return !System.IO.File.Exists(p) || System.IO.File.ReadAllText(p).Trim() != ThumbSig(id); } catch { return true; }
        }
        float thumbT = 3.5f;
        void MaybeTakeThumb()
        {
            if (!Open || step != 0 || thumbT <= 0 || (thumbT -= Time.unscaledDeltaTime) > 0) return;
            var id = MapSet.Current;
            var have = Resources.Load<Texture2D>("StreetLife/MapThumbs/" + id);
            if (have && (Application.isEditor ? !ThumbStale(id) : true)) return;   // editor: retaken whenever the map's files have changed
            if (!Application.isEditor) { try { if (System.IO.File.Exists(DeviceThumb(id))) return; } catch { return; } }
            var cam = Camera.main; if (!cam) return;
            var rt = RenderTexture.GetTemporary(ThumbW, ThumbH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
            var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(ThumbW, ThumbH, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, ThumbW, ThumbH), 0, 0); tex.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var png = tex.EncodeToPNG();
            try
            {
                if (Application.isEditor)
                {
                    var dir = System.IO.Path.Combine(Application.dataPath, "StreetLife/Resources/StreetLife/MapThumbs");
                    System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, id + ".png"), png);
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ThumbSigPath(id))); System.IO.File.WriteAllText(ThumbSigPath(id), ThumbSig(id));
                }
                else System.IO.File.WriteAllBytes(DeviceThumb(id), png);
            }
            catch (Exception e) { Debug.LogWarning("Street Life: couldn't save a map picture: " + e.Message); }
            thumbs[id] = tex;
        }

        // a general line about each character (abilities are gone, so no ability text)
        static readonly Dictionary<string, string> bios = new Dictionary<string, string>
        {
            { "wednesday", "The original. Quick, cool-headed and always first off the mark." },
            { "crusty", "A grizzled old salt who has seen every street fight going." },
            { "angel", "Light on her feet, with a halo she earned the hard way." },
            { "antihero", "Plays by their own rules and is never quite the good guy." },
            { "jet", "A thrill-seeker who lives for speed and big air." },
            { "ghost", "Here one second, gone the next. Nobody sees Starscreen coming." },
            { "dracula", "Centuries old, impeccably dressed and always hungry." },
            { "astronaut", "Back from orbit and still not used to gravity." },
            { "lilpink", "Sweet on the outside, ruthless the moment the round starts." },
            { "brute", "A giant bruiser: a step slower, but very hard to put down." },
            { "blaze", "Hot-headed and loud. Leaves scorch marks wherever they go." },
            { "blink", "Twitchy, unpredictable and impossible to pin down." },
            { "tank", "Armoured up, a little slower, and fond of anything that burns." },
            { "frost", "Cold, calm and precise. She keeps her distance and waits." },
            { "patch", "Patches up friends and takes out everyone else." },
            { "thorn", "Spiked, stubborn and painful to hit." },
            { "thor", "A thunder god who took up street fighting." },
            { "isaac", "The blocky classic. No frills, just a steady hand on the trigger." },
        };
        const string RandomId = "random";
        static string Pick(List<string> ids) => ids == null || ids.Count == 0 ? null : ids[UnityEngine.Random.Range(0, ids.Count)];
        /// what the menu's weapons rack shows for a slot: the Simple kit under Simple rules, nothing in Rumble
        static string RackId(string picked, string simpleKit) => Rules.Rumble ? null : Settings.Rules == "simple" && Rules.OneVOne ? simpleKit : picked;

        public static string Bio(string id) => id != null && bios.TryGetValue(id, out var b) ? b : "";

        void BuildCharacters()
        {
            charGrid.Clear();
            var cs = CharacterSet.Load(); if (!cs) { charGrid.Add(L("Characters aren't set up in this project yet.", 13, Dim)); return; }
            charGrid.Add(CharTile(RandomId, "Random"));
            foreach (var c in cs.Playable) { if (cs.Has(c.id)) charGrid.Add(CharTile(c.id, c.name)); }   // characters chat: bot-only ones (Dida, Lia) left out
            RefreshCharacter();
        }

        // a character box: its picture (MenuPortraits, filled in as each one is made) over the name
        Button CharTile(string id, string name)
        {
            var t = new Button(() => PickCharacter(id)) { text = "" }; t.name = "ch-" + id;
            t.style.width = 72; t.style.height = 76; Radius(t, 8); Border(t, 2, Line); t.style.marginLeft = 0; t.style.marginTop = 0; t.style.marginRight = 7; t.style.marginBottom = 7; Pad(t, 0, 0);
            t.style.backgroundColor = Chip; t.style.overflow = Overflow.Hidden;
            var pic = new VisualElement(); pic.name = "pic"; pic.pickingMode = PickingMode.Ignore; pic.style.height = 54; pic.style.flexShrink = 0;
            pic.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
            if (id == RandomId) { var q = L("?", 30, Text, true); q.style.unityTextAlign = TextAnchor.MiddleCenter; q.style.flexGrow = 1; pic.Add(q); }
            else { var tex = MenuPortraits.Get(id); if (tex) pic.style.backgroundImage = new StyleBackground(tex); }
            t.Add(pic);
            var n = L(name, 11, Text, true); n.style.unityTextAlign = TextAnchor.MiddleCenter; n.style.flexGrow = 1; n.style.whiteSpace = WhiteSpace.NoWrap; n.style.overflow = Overflow.Hidden; t.Add(n);
            return t;
        }

        void OnPortrait(string id, Texture2D tex)
        {
            var pic = charGrid?.Q<Button>("ch-" + id)?.Q<VisualElement>("pic");
            if (pic != null && tex) pic.style.backgroundImage = new StyleBackground(tex);
        }

        void PickCharacter(string id)
        {
            Settings.RandomChar = id == RandomId;
            if (Settings.RandomChar) { RefreshCharacter(); return; }
            CharacterSkin.PlayerChoice = id;
            var p = FindPlayer(); var rig = p ? p.GetComponent<CharacterRig>() : null;
            if (rig) CharacterSkin.Apply(rig, id);
            RefreshCharacter();
        }

        void RefreshCharacter()
        {
            var cs = CharacterSet.Load(); if (!cs) return;
            var mine = Settings.RandomChar ? RandomId : CharacterSkin.PlayerChoice;
            foreach (var b in charGrid.Query<Button>().ToList()) { bool on = b.name == "ch-" + mine; Border(b, 2, on ? Sel : Line); }
            charInfo.Clear();
            if (mine == RandomId)
            {
                charInfo.Add(L("Random", 26, Text, true));
                var rd = L("A different character every match.", 13, Dim); rd.style.whiteSpace = WhiteSpace.Normal; rd.style.marginTop = 4; charInfo.Add(rd);
                return;
            }
            var i = cs.Get(mine); if (i == null) return;
            charInfo.Add(L(i.name, 26, Text, true));
            var d = L(Bio(i.id), 13, Dim); d.style.whiteSpace = WhiteSpace.Normal; d.style.marginTop = 4; charInfo.Add(d);
            var s = L($"Speed x{(i.speed > 0 ? i.speed : 1):0.##}   ·   {(i.hp > 0 ? i.hp : 100):0} HP", 13, Dim); s.style.marginTop = 4; charInfo.Add(s);
        }

        // ---- step 3: loadout (saved in Settings; given to your fighter when the match starts)
        void BuildLoadout()
        {
            loadBox.Clear();
            if (!GunsDb.Loaded) { loadBox.Add(L("Weapons aren't set up in this project yet.", 13, Dim)); return; }
            bool simple = Settings.Rules == "simple" && Rules.OneVOne;
            if (simple) { var n = L("Simple rules: everyone gets the M4, sword and grenade. Pick any weapon to switch to Normal rules and choose your own.", 12, Dim); n.style.whiteSpace = WhiteSpace.Normal; n.style.marginTop = 8; loadBox.Add(n); }
            Section("Primary", GunsDb.Primaries, simple ? "gar" : Settings.Primary, id => Settings.Primary = id, simple);
            Section("Secondary", GunsDb.Secondaries, simple ? "sword" : Settings.Secondary, id => Settings.Secondary = id, simple);
            var leth = new List<string>(); if (GunsDb.Cons("grenade") != null) leth.Add("grenade");
            foreach (var id in GunsDb.Lethals) if (GunsDb.Cons(id) != null) leth.Add(id);
            Section("Lethal", leth, simple ? "grenade" : Settings.Lethal, id => Settings.Lethal = id, simple);
        }

        void Section(string title, List<string> ids, string sel, Action<string> pick, bool locked)
        {
            loadBox.Add(Caps(title));
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
            var all = new List<string> { RandomId }; all.AddRange(ids);
            foreach (var id in all)
            {
                var g = GunsDb.Gun(id); var c = GunsDb.Cons(id);
                string name = id == RandomId ? "Random" : c != null ? c.name : g != null ? g.name : id;
                string stat = id == RandomId ? "Each match" : c != null ? (g != null && g.dmg > 0 ? Mathf.RoundToInt(g.dmg) + " dmg · " : "") + c.meta : g != null ? GunStatLine(g) : "";
                bool on = id == sel; var pid = id;
                var b = new Button(() =>
                {
                    WeaponShowcase.Show(pid);
                    // under Simple rules the kit is fixed: picking a weapon switches to Normal rules, so the pick takes and only it is selected
                    if (locked) { Settings.Rules = "normal"; Settings.Apply(); foreach (var r in segRefresh) r(); RefreshHint(); }
                    pick(pid); PlayerPrefs.Save(); BuildLoadout();
                }) { text = "" };
                b.style.width = 140; b.style.height = 46; Radius(b, 9); Border(b, 2, on ? Sel : Line); Pad(b, 4, 10);
                b.style.marginLeft = b.style.marginTop = 0; b.style.marginRight = b.style.marginBottom = 7;
                b.style.backgroundColor = on ? Sel : Chip; b.style.justifyContent = Justify.Center; b.style.alignItems = Align.FlexStart;
                if (locked && !on) b.style.opacity = .35f;
                var nl = L(name, 13, on ? Ink : Text, true); OneLine(nl); b.Add(nl);
                var sl = L(stat, 11, on ? new Color(0, 0, 0, .65f) : Dim); OneLine(sl); sl.style.marginTop = 2; b.Add(sl);
                row.Add(b);
            }
            loadBox.Add(row);
        }

        /// one line of text, cut with "…" if it doesn't fit (no wrapping into the line below)
        static void OneLine(Label l) { l.style.whiteSpace = WhiteSpace.NoWrap; l.style.overflow = Overflow.Hidden; l.style.textOverflow = TextOverflow.Ellipsis; l.style.maxWidth = Length.Percent(100); }

        static string GunStatLine(GunStat g) => g.IsMelee ? Mathf.RoundToInt(g.dmg) + " dmg · " + g.reach + " m reach"
            : g.IsProj ? Mathf.RoundToInt(g.direct > 0 ? g.direct : g.dmg) + " dmg · " + Mathf.RoundToInt(g.mag) + " rnd"
            : Mathf.RoundToInt(g.dmg) + (g.Pellets > 1 ? "x" + g.Pellets : "") + " dmg · " + Mathf.RoundToInt(60f / Mathf.Max(.01f, g.rate)) + " rpm";

        /// put the saved loadout (or the Simple kit) on your fighter; RoundStarted's ResetKit then fills the ammo
        void ApplyLoadout()
        {
            var p = player ? player : FindPlayer(); if (!p) return;
            bool simple = Settings.Rules == "simple" && Rules.OneVOne;
            string pri = simple ? "gar" : Settings.Primary, sec = simple ? "sword" : Settings.Secondary, leth = simple ? "grenade" : Settings.Lethal;
            if (GunsDb.Loaded)
            {
                if (pri == RandomId) pri = Pick(GunsDb.Primaries);
                if (sec == RandomId) sec = Pick(GunsDb.Secondaries);
                if (leth == RandomId) { var ls = new List<string>(); if (GunsDb.Cons("grenade") != null) ls.Add("grenade"); foreach (var x in GunsDb.Lethals) if (GunsDb.Cons(x) != null) ls.Add(x); leth = Pick(ls); }
            }
            if (Rules.Rumble) { pri = "fists"; sec = "fists"; }   // Rumble: fists only
            if (GunsDb.Loaded) { if (GunsDb.Gun(pri) == null) pri = "gar"; if (GunsDb.Gun(sec) == null) sec = "sword"; if (GunsDb.Cons(leth) == null) leth = "grenade"; }
            p.primary = pri; p.secondary = sec; p.lethal = leth;
            var w = p.GetComponent<Weapons>(); if (w && GunsDb.Loaded) w.ResetKit();
        }

        // ---- soft studio lights on your fighter for the character and loadout steps (the map's own light can leave them in shadow)
        Light keyL, fillL, rimL;
        Light MakeLight(string n, float intensity, Color c)
        {
            var go = new GameObject(n); go.transform.SetParent(transform);
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 9; l.intensity = intensity; l.color = c; l.shadows = LightShadows.None; l.enabled = false;
            return l;
        }

        void UpdateLights()
        {
            var cam = Camera.main;
            bool on = Open && step >= 1 && player && cam;
            // maps chat: key 8 -> 4.5 and fill 3 -> 1.8; at 8 the face went past the bloom threshold and glowed (HDR ~1.9 on white skin)
            if (!keyL) { keyL = MakeLight("Menu key light", 4.5f, new Color(1f, .97f, .93f)); fillL = MakeLight("Menu fill light", 1.8f, new Color(.88f, .92f, 1f)); rimL = MakeLight("Menu rim light", 6f, Color.white); }
            keyL.enabled = fillL.enabled = rimL.enabled = on; if (!on) return;
            var c = player.pos + Vector3.up * 1.2f;
            var toCam = cam.transform.position - c; toCam.y = 0; if (toCam.sqrMagnitude < 1e-4f) toCam = Vector3.forward; toCam.Normalize();
            var side = Vector3.Cross(Vector3.up, toCam);
            keyL.transform.position = c + toCam * 2.2f + side * 1.4f + Vector3.up * 1f;
            fillL.transform.position = c + toCam * 2.4f - side * 1.8f + Vector3.up * .2f;
            rimL.transform.position = c - toCam * 1.6f + Vector3.up * 1.6f;
        }

        // ================================================================== pause and settings
        void BuildPause()
        {
            pause = Overlay(); pause.style.backgroundColor = new Color(0, 0, 0, .55f);
            var c = Card(340); c.style.alignItems = Align.Stretch;
            var t = L("Paused", 30, Text, true); t.style.unityTextAlign = TextAnchor.MiddleCenter; t.style.marginBottom = 16; c.Add(t);
            foreach (var (label, act, primary) in new (string, Action, bool)[] {
                ("Resume", new Action(Resume), true), ("Settings", new Action(() => Show(settings, pause)), false), ("Main menu", new Action(ToMenu), false),
#if !UNITY_WEBGL
                ("Quit game", new Action(QuitGame), false),   // in the editor it stops play mode
#endif
            })
            { var b = Btn(label, act, primary); b.style.marginBottom = 10; c.Add(b); }
            pause.Add(c);
            // phones have no Esc: a pause button under the minimap
            pauseBtn = Btn("II", Pause, false, 18); pauseBtn.style.position = Position.Absolute; pauseBtn.style.right = 14; pauseBtn.style.top = 200;
            pauseBtn.style.width = pauseBtn.style.height = 46; Radius(pauseBtn, 23); Pad(pauseBtn, 0, 0); pauseBtn.style.backgroundColor = new Color(0, 0, 0, .45f);
            pauseBtn.style.display = DisplayStyle.None; root.Add(pauseBtn);
        }

        VisualElement backTo;
        void BuildSettings()
        {
            settings = Overlay(); settings.style.backgroundColor = new Color(0, 0, 0, .55f);
            var c = Card(470);
            var t = L("Settings", 28, Text, true); t.style.marginBottom = 16; c.Add(t);
            var f2 = new float[] { .25f, .5f, .75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f, 3.5f, 4f };
            c.Add(Stepper("Look sensitivity", f2, () => Settings.Sens, v => { Settings.Sens = v; Settings.Apply(); }, v => v.ToString("0.##")));
            c.Add(Stepper("Aim sensitivity", new[] { .05f, .1f, .15f, .2f, .25f, .3f, .4f, .5f, .6f, .7f, .8f, .9f, 1f }, () => Settings.AimSens, v => { Settings.AimSens = v; Settings.Apply(); }, v => v.ToString("0.0#")));   // down to 0.05 (Pete, 15 Nov)
            c.Add(Stepper("Sniper scope sensitivity", new[] { .05f, .1f, .15f, .2f, .25f, .3f, .4f, .5f, .6f, .7f, .8f, .9f, 1f, 1.25f, 1.5f }, () => Settings.SnipeSens, v => { Settings.SnipeSens = v; Settings.Apply(); }, v => v.ToString("0.0#")));
            c.Add(Stepper("Camera distance", new[] { .8f, .9f, 1f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.8f, 2f }, () => Settings.CamDist, v => { Settings.CamDist = v; Settings.Apply(); }, v => Mathf.RoundToInt(v * 100) + "%"));   // third person: 100% = the web game's
            var vol = new float[] { 0, .1f, .2f, .3f, .4f, .5f, .6f, .7f, .8f, .9f, 1f };
            c.Add(Stepper("Music", vol, () => Settings.Music, v => { Settings.Music = v; Settings.Apply(); }, v => Mathf.RoundToInt(v * 100) + "%"));
            c.Add(Stepper("Effects", vol, () => Settings.Effects, v => { Settings.Effects = v; Settings.Apply(); }, v => Mathf.RoundToInt(v * 100) + "%"));
            // guns have their own level (17 Nov): each step fires one rifle shot at the new level so you can hear it from the menu
            c.Add(Stepper("Guns", vol, () => Settings.Guns, v => { Settings.Guns = v; Settings.Apply(); Sfx.Ui("m4", 1, 1, true); }, v => Mathf.RoundToInt(v * 100) + "%"));
            c.Add(Caps("Graphics"));
            c.Add(Segmented(new[] { "Auto", "High", "Smooth" }, new[] { "auto", "high", "smooth" }, () => Settings.Graphics, v => { Settings.Graphics = v; Settings.Apply(); }));
            var gh = L("Auto picks from your hardware and drops to Smooth if the game can't keep up. High: 4K shadows, full ambient occlusion, 4x anti-aliasing. Smooth: the lighter settings phones use.", 12, Dim); gh.style.whiteSpace = WhiteSpace.Normal; gh.style.marginTop = 6; c.Add(gh);
            autoNow = L("", 12, Text); autoNow.style.whiteSpace = WhiteSpace.Normal; autoNow.style.marginTop = 4; c.Add(autoNow);   // what Auto is doing now (Update)
            c.Add(Caps("Touch controls"));
            c.Add(Segmented(new[] { "Auto", "On", "Off" }, new[] { "auto", "on", "off" }, () => Settings.Touch, v => { Settings.Touch = v; Settings.Apply(); }));
            var th = L("Auto: on phones, or once you touch the screen. Off hides the on-screen buttons and stick.", 12, Dim); th.style.whiteSpace = WhiteSpace.Normal; th.style.marginTop = 6; c.Add(th);
            c.Add(Caps("Frame rate readout"));
            c.Add(Segmented(new[] { "Off", "On" }, new[] { "off", "on" }, () => PerfProbe.Show ? "on" : "off", v => PerfProbe.Show = v == "on"));
            var done = Btn("Done", () => Show(backTo), true); done.style.marginTop = 18; c.Add(done);
            Scrolling(c, true);
            if (Application.isMobilePlatform || Application.isEditor)
            {
                // phone controls, in a second card beside the first (a phone screen is too short for one long list)
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.FlexStart;
                var c2 = Card(400); c2.style.marginLeft = 16;
                var t2 = L("Phone controls", 28, Text, true); t2.style.marginBottom = 16; c2.Add(t2);
                c2.Add(Caps("Aim assist"));
                c2.Add(Segmented(new[] { "Off", "Normal", "Strong" }, new[] { "off", "normal", "strong" },
                    () => Settings.AimAssist <= 0 ? "off" : Settings.AimAssist > 1.3f ? "strong" : "normal",
                    v => { Settings.AimAssist = v == "off" ? 0f : v == "strong" ? 1.6f : 1f; Settings.Apply(); }));
                c2.Add(Caps("Gyro aim"));
                c2.Add(Segmented(new[] { "Off", "When aiming", "Always" }, new[] { "off", "aim", "always" }, () => Settings.Gyro, v => { Settings.Gyro = v; Settings.Apply(); }));
                c2.Add(Caps("Aim when firing"));
                c2.Add(Segmented(new[] { "Off", "On" }, new[] { "off", "on" }, () => Settings.AutoAds ? "on" : "off", v => { Settings.AutoAds = v == "on"; Settings.Apply(); }));
                c2.Add(Caps("Vibration"));
                c2.Add(Segmented(new[] { "Off", "On" }, new[] { "off", "on" }, () => Settings.Vibration ? "on" : "off", v => { Settings.Vibration = v == "on"; Settings.Apply(); }));
                c2.Add(Caps("Frame rate"));
                c2.Add(Segmented(new[] { "Auto", "60", "90", "120" }, new[] { "0", "60", "90", "120" }, () => Settings.Fps <= 0 ? "0" : Settings.Fps.ToString(), v => { Settings.Fps = int.Parse(v); Settings.Apply(); }));
                var fh = L("Auto runs at the screen's own rate and steps down to 90 or 60 if the phone can't hold it. 60 keeps the phone cooler over long sessions.", 12, Dim); fh.style.whiteSpace = WhiteSpace.Normal; fh.style.marginTop = 6; c2.Add(fh);
                var ed = Btn("Edit button layout", () => { var back = settings; Show(null); TouchLayout.BeginEdit(() => Show(back)); }); ed.style.marginTop = 14; c2.Add(ed);
                Scrolling(c2, false);
                row.Add(c); row.Add(c2);
                if (Settings.IsDesktop) row.Add(BuildDisplayCard());
                settings.Add(row);
            }
            else if (Settings.IsDesktop) { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.FlexStart; row.Add(c); row.Add(BuildDisplayCard()); settings.Add(row); }
            else settings.Add(c);
        }

        /// A settings card that never outgrows the screen: its title (and, with keepLast, its last button) stay put and
        /// everything between scrolls. Rows keep their own height instead of being squashed into each other.
        readonly List<VisualElement> fitCards = new List<VisualElement>();
        void Scrolling(VisualElement card, bool keepLast)
        {
            var kids = new List<VisualElement>(card.Children()); if (kids.Count < 3) return;
            // This panel has no theme style sheet, so a ScrollView neither clips nor scrolls here. A plain clipped box with
            // the rows on a strip inside it does the job: the wheel moves the strip, and so does a finger (or mouse) drag.
            var clip = new VisualElement(); clip.style.overflow = Overflow.Hidden; clip.style.flexShrink = 1; clip.style.flexGrow = 1; clip.style.minHeight = 0;
            var strip = new VisualElement(); strip.style.flexShrink = 0;
            int last = keepLast ? kids.Count - 1 : kids.Count;
            for (int i = 1; i < last; i++) { kids[i].style.flexShrink = 0; strip.Add(kids[i]); }
            clip.Add(strip); card.Insert(1, clip); kids[0].style.flexShrink = 0; if (keepLast) kids[kids.Count - 1].style.flexShrink = 0;
            float off = 0, off0 = 0; Vector2 down = default; int pid = -1; bool dragging = false;
            void Set(float v) { off = Mathf.Clamp(v, 0, Mathf.Max(0, strip.resolvedStyle.height - clip.resolvedStyle.height)); strip.style.top = -off; }
            clip.RegisterCallback<WheelEvent>(e => { Set(off + e.delta.y * 36); e.StopPropagation(); });
            clip.RegisterCallback<PointerDownEvent>(e => { pid = e.pointerType == UnityEngine.UIElements.PointerType.touch ? e.pointerId : -1; down = e.position; off0 = off; dragging = false; }, TrickleDown.TrickleDown);   // drag-scrolling is for fingers only; a mouse uses the wheel, so a click is always a click
            clip.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != pid) return;
                // 16 Nov: only while the button (or finger) is still down. A mouse keeps one pointer id for ever, so without this a
                // click on a row followed by moving the mouse counted as a drag, the list took the pointer and Done stopped working.
                if ((e.pressedButtons & 1) == 0) { if (clip.HasPointerCapture(pid)) clip.ReleasePointer(pid); pid = -1; dragging = false; return; }
                float dy = e.position.y - down.y;
                if (!dragging && Mathf.Abs(dy) > 10) { dragging = true; clip.CapturePointer(pid); }   // past 10 px it's a scroll, not a tap
                if (dragging) Set(off0 - dy);
            }, TrickleDown.TrickleDown);
            clip.RegisterCallback<PointerUpEvent>(e => { if (e.pointerId != pid) return; if (dragging && clip.HasPointerCapture(pid)) clip.ReleasePointer(pid); pid = -1; dragging = false; }, TrickleDown.TrickleDown);
            clip.RegisterCallback<PointerCancelEvent>(e => { if (pid >= 0 && clip.HasPointerCapture(pid)) clip.ReleasePointer(pid); pid = -1; dragging = false; }, TrickleDown.TrickleDown);
            clip.RegisterCallback<PointerCaptureOutEvent>(e => { pid = -1; dragging = false; });
            clip.RegisterCallback<GeometryChangedEvent>(e => Set(off));
            if (fitCards.Count == 0) settings.RegisterCallback<GeometryChangedEvent>(e => { float h = settings.resolvedStyle.height - 24; if (h > 100) foreach (var fc in fitCards) fc.style.maxHeight = h; });
            fitCards.Add(card);
        }

        // desktop only: how the game takes the screen (match chat)
        VisualElement BuildDisplayCard()
        {
            var c3 = Card(400); c3.style.marginLeft = 16;
            var t3 = L("Display", 28, Text, true); t3.style.marginBottom = 16; c3.Add(t3);
            c3.Add(Caps("Screen"));
            c3.Add(Segmented(new[] { "Full screen", "Windowed" }, new[] { "borderless", "windowed" }, () => Settings.ScreenMode, v => { Settings.ScreenMode = v; Settings.ApplyScreen(); }));
            var sh = L("Full screen fills the whole screen (as a borderless window, so Alt+Tab is instant and nothing freezes when you switch). Windowed puts the game in a normal window.", 12, Dim); sh.style.whiteSpace = WhiteSpace.Normal; sh.style.marginTop = 6; sh.style.marginBottom = 10; c3.Add(sh);
            var res = Settings.Resolutions(); var idx = new float[res.Count]; for (int i = 0; i < idx.Length; i++) idx[i] = i;
            if (res.Count > 0)
                c3.Add(Stepper("Resolution", idx, () => Settings.ResolutionIndex(res), v => { var r = res[Mathf.Clamp(Mathf.RoundToInt(v), 0, res.Count - 1)]; Settings.ResW = r.x; Settings.ResH = r.y; Settings.ApplyScreen(); }, v => { var r = res[Mathf.Clamp(Mathf.RoundToInt(v), 0, res.Count - 1)]; return r.x + " x " + r.y; }, 124));
            c3.Add(Caps("V-sync"));
            c3.Add(Segmented(new[] { "Auto", "Off", "On" }, new[] { "auto", "off", "on" }, () => Settings.VSyncAuto ? "auto" : Settings.VSync ? "on" : "off", v => { Settings.VSyncAuto = v == "auto"; if (v != "auto") Settings.VSync = v == "on"; Settings.Apply(); }));
            c3.Add(Caps("Frame rate cap (V-sync off)"));
            c3.Add(Segmented(new[] { "None", "60", "120", "144", "240" }, new[] { "0", "60", "120", "144", "240" }, () => Settings.FpsCap.ToString(), v => { Settings.FpsCap = int.Parse(v); Settings.Apply(); }));
            var vh = L("Auto: no v-sync; the frame rate is capped at the monitor's rate (up to 144) and steps down to 120, 90 or 60 if the PC can't hold it, before the graphics are lowered. On locks to the monitor and stops tearing (on some laptops it stutters badly). Off: as fast as the PC can go, up to the cap.", 12, Dim); vh.style.whiteSpace = WhiteSpace.Normal; vh.style.marginTop = 6; c3.Add(vh);
            Scrolling(c3, false);
            return c3;
        }

        void Show(VisualElement v, VisualElement returnTo = null)
        {
            if (returnTo != null) backTo = returnTo;
            foreach (var p in new[] { menu, pause, settings }) if (p != null) p.style.display = p == v ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ================================================================== state
        static Fighter FindPlayer() { foreach (var p in FindObjectsByType<PlayerController>()) return p.GetComponent<Fighter>(); return null; }

        static readonly HashSet<string> hudTypes = new HashSet<string> { "Hud", "WeaponHud", "FinisherHud", "TouchControls", "CharacterPicker", "MapPicker", "WeaponInput", "PlayerController" };

        void HideGame(bool hide)
        {
            if (hide)
            {
                hidden.Clear();
                foreach (var b in FindObjectsByType<Behaviour>())
                    if (b && b.enabled && hudTypes.Contains(b.GetType().Name)) { b.enabled = false; hidden.Add(b); }
                shoulder = FindAnyObjectByType<ShoulderCamera>(); if (shoulder) shoulder.enabled = false;
                UnityEngine.Cursor.lockState = CursorLockMode.None; UnityEngine.Cursor.visible = true;
            }
            else
            {
                foreach (var b in hidden) if (b) b.enabled = true;
                hidden.Clear();
                if (shoulder) shoulder.enabled = true;
            }
        }

        void Enter()
        {
            player = FindPlayer();
            HideGame(true);
            if (player) { player.Frozen = true; player.inFwd = player.inSide = 0; player.inFire = false; player.ads = false; }
            Sfx.Music("menu");
        }

        void Play()
        {
            WeaponShowcase.SetActive(false); MenuWeaponRack.Hide();
            if (Settings.RandomChar)
            {
                var cs = CharacterSet.Load(); var ids = new List<string>();
                if (cs) foreach (var c in cs.Playable) if (cs.Has(c.id)) ids.Add(c.id);
                if (ids.Count > 0)
                {
                    var id = Pick(ids); CharacterSkin.PlayerChoice = id;
                    var p = FindPlayer(); var rig = p ? p.GetComponent<CharacterRig>() : null; if (rig) CharacterSkin.Apply(rig, id);
                }
            }
            if (selMap == RandomId)
            {
                var set = MapSet.Load(); var ids = new List<string>();
                if (set && set.maps != null) foreach (var m in set.maps) if (Usable(m)) ids.Add(m.id);
                selMap = ids.Count > 0 ? Pick(ids) : MapSet.Current;
            }
            if (selMap != MapSet.Current && MapSet.Load() && MapSet.Load().Get(selMap) != null)
            {
                MapSet.Choice = selMap; skipOnce = true;
                Loading.ReloadScene();   // behind the loading screen, not a frozen picture
                return;
            }
            ApplyLoadout();
            Open = false; Show(null); HideGame(false);
            RoundManager.HoldAtMenu = false;
            if (RoundManager.Instance) RoundManager.Instance.BeginFromMenu();
        }

        Button pauseBtn;
        void Pause()
        {
            if (Paused || Open || !RoundManager.Instance || RoundManager.Instance.state == RoundManager.State.Boot) return;
            Paused = true; Time.timeScale = 0; HideGame(true); Show(pause);
        }

        void Resume() { Paused = false; Time.timeScale = 1; Show(null); HideGame(false); }
        // a match pauses itself when the window loses focus (Alt+Tab, another tab in the browser) or the phone puts the app in
        // the background. Not in the editor (the remote control runs play mode unfocused) and not in unattended perf runs.
        void OnApplicationFocus(bool focus) { if (!focus) AutoPause(); }
        void OnApplicationPause(bool paused) { if (paused) AutoPause(); }
        void AutoPause()
        {
            if (Application.isEditor || HitchLog.Unattended) return;
            var rm = RoundManager.Instance; if (!rm || rm.matchOver) return;
            if (WeaponHud.MenuOpen) { var h = FindAnyObjectByType<WeaponHud>(); if (h) h.ToggleMenu(); }
            Pause();
        }
        // ---- quitting, and the after-match buttons (MatchEndMenu)
        static bool CanQuit => Application.platform != RuntimePlatform.WebGLPlayer;   // a browser tab can't be closed from the game
        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// back to the main menu (the scene reloads with the menu up)
        public static void GoHome()
        {
            Time.timeScale = 1; Paused = false; skipOnce = false;
            Loading.ReloadScene();   // behind the loading screen, not a frozen picture
        }

        /// straight into a new match on a random map other than this one (same mode, players, character and loadout)
        public static void PlayRandomMap()
        {
            var set = MapSet.Load(); var ids = new List<string>();
            if (set && set.maps != null) foreach (var m in set.maps) if (Usable(m) && m.id != MapSet.Current) ids.Add(m.id);
            if (ids.Count == 0) { var rm = RoundManager.Instance; if (rm) rm.StartMatch(); return; }
            MapSet.Choice = Pick(ids); skipOnce = true; Time.timeScale = 1;
            Loading.ReloadScene();   // behind the loading screen, not a frozen picture
        }

        void ToMenu()
        {
            Time.timeScale = 1; Paused = false;
            Loading.ReloadScene();   // behind the loading screen, not a frozen picture
        }

        // ---- the view of your fighter on the Character and Loadout steps. The fighter stands at a spawn, which can be up
        // against a window, a wall or a prop; they turn to face whichever way gives the camera a clear line, and the camera
        // comes closer when no direction is clear all the way out.
        const float VIEW_DIST = 4.2f;
        float viewDist = VIEW_DIST; bool viewSet; Vector3 viewAt;
        void PickView()
        {
            var c = player.pos + Vector3.up * 1.25f; float best = -1, bestYaw = player.yaw;
            for (int i = 0; i < 16; i++)
            {
                float yaw = player.yaw + i * Mathf.PI / 8, clear = ViewClear(c, yaw);
                if (clear > best + .01f) { best = clear; bestYaw = yaw; }
                if (clear >= VIEW_DIST) break;
            }
            player.yaw = bestYaw; viewDist = Mathf.Clamp(best - .3f, 1.7f, VIEW_DIST); viewSet = true; viewAt = player.pos;
        }
        /// how far out (up to VIEW_DIST) the camera's places are clear of every solid box, looking back at the fighter
        static float ViewClear(Vector3 c, float yaw)
        {
            var fwd = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)); var side = new Vector3(fwd.z, 0, -fwd.x);
            float min = VIEW_DIST + .5f;
            for (int i = 0; i < 5; i++)
            {   // the Loadout place, the Character place either side of it, and a wider pair so the camera isn't hard against a wall
                float lat = i == 0 ? -1.3f : i == 1 ? -1.85f : i == 2 ? -.75f : i == 3 ? -2.5f : 0f;
                var off = fwd * (VIEW_DIST + .5f) + Vector3.up * .35f + side * lat; float d = off.magnitude;
                float h = BoxWorld.Ray(c, off / d, d);
                if (h >= 0) min = Mathf.Min(min, h * (VIEW_DIST + .5f) / d);
                // and from the shins, so a table or a counter in front of them doesn't hide their legs
                var low = c + Vector3.down * .85f; var off2 = c + off - low; float d2 = off2.magnitude;
                h = BoxWorld.Ray(low, off2 / d2, d2);
                if (h >= 0) min = Mathf.Min(min, h * (VIEW_DIST + .5f) / d2);
            }
            return min - .5f;
        }

        Label autoNow; float autoNowT;
        void Update()
        {
            if (autoNow != null && settings != null && settings.style.display == DisplayStyle.Flex && (autoNowT -= Time.unscaledDeltaTime) <= 0)
            { autoNowT = .5f; autoNow.text = AutoQuality.GraphicsAuto || AutoQuality.FpsAuto ? AutoQuality.Describe() : ""; }
            MaybeTakeThumb();
            if (pauseBtn != null) pauseBtn.style.display = !Open && !Paused && TouchControls.I && TouchControls.I.Showing ? DisplayStyle.Flex : DisplayStyle.None;
            var kb = Keyboard.current;
            if (!Open && kb != null && kb.escapeKey.wasPressedThisFrame && !MapPicker.Open && !CharacterPicker.Open)
            {
                if (Paused) { if (settings.style.display == DisplayStyle.Flex) Show(pause); else Resume(); }
                else Pause();
            }
        }

        // the camera while the menu is up: a slow circle round the map, or in front of your fighter on the character step
        void LateUpdate()
        {
            UpdateLights();
            WeaponPose.MenuUnarmed = Open && step >= 1;   // no gun in your fighter's hands while you pick characters and weapons
            if (Open && step == 1 && player) MenuWeaponRack.Show(player, RackId(Settings.Primary, "gar"), RackId(Settings.Secondary, "sword"), RackId(Settings.Lethal, "grenade"));
            else MenuWeaponRack.Hide();
            if (!Open) return;
            var cam = Camera.main; if (!cam) return;
            orbit += Time.unscaledDeltaTime * .05f;
            if (step >= 1 && player)
            {
                if (!viewSet || (player.pos - viewAt).sqrMagnitude > .25f) PickView();
                float yaw = player.yaw;
                var fwd = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
                var c = player.pos + Vector3.up * 1.25f;
                float k = viewDist / VIEW_DIST;   // pulled in when nothing further out is clear
                var want = c + fwd * viewDist + Vector3.up * .35f * k + new Vector3(fwd.z, 0, -fwd.x) * -1.3f * k;   // a little to the side so the panel doesn't cover them
                // character step: slide the view left so your fighter and the weapons rack sit in the right-hand part of the
                // screen, clear of the character panel (MenuWeaponRack works out the same right-hand side)
                var look = c;
                if (step >= 1)
                {   // Loadout: a little further, into the gap between the loadout panel and the weapon card
                    var toCam = want - c; toCam.y = 0; toCam.Normalize();
                    var shift = -Vector3.Cross(Vector3.up, -toCam).normalized * (step == 1 ? .55f : .7f) * k;
                    want += shift; look += shift;
                }
                cam.transform.position = Vector3.Lerp(cam.transform.position, want, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 4));
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(look - cam.transform.position), 1 - Mathf.Exp(-Time.unscaledDeltaTime * 6));
                cam.fieldOfView = 40;
            }
            else
            {
                var bd = BoxWorld.Bounds; var c = new Vector3((bd.x0 + bd.x1) / 2, 2, (bd.z0 + bd.z1) / 2);
                float r = Mathf.Max(bd.x1 - bd.x0, bd.z1 - bd.z0) * .55f;
                var want = c + new Vector3(Mathf.Sin(orbit), 0, Mathf.Cos(orbit)) * r + Vector3.up * r * .55f;
                cam.transform.position = Vector3.Lerp(cam.transform.position, want, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 2));
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(c - cam.transform.position), 1 - Mathf.Exp(-Time.unscaledDeltaTime * 3));
                cam.fieldOfView = 55;
            }
        }

        void OnDestroy() { WeaponPose.MenuUnarmed = false; MenuPortraits.Ready -= OnPortrait; MenuWeaponRack.Hide(); WeaponShowcase.SetActive(false); if (Paused) Time.timeScale = 1; Open = false; Paused = false; }
    }

    /// Saved player settings (PlayerPrefs), applied at every scene load and whenever they change.
    public static class Settings
    {
        const string P = "StreetLife.";
        public static float Sens { get => PlayerPrefs.GetFloat(P + "sens", 1f); set => PlayerPrefs.SetFloat(P + "sens", value); }
        public static float AimSens { get => PlayerPrefs.GetFloat(P + "aimSens", 1f); set => PlayerPrefs.SetFloat(P + "aimSens", value); }
        /// Third-person camera distance (ShoulderCamera), as a share of the web game's: 1.2 by default, a little further out.
        public static float CamDist { get => PlayerPrefs.GetFloat(P + "camDist", 1.2f); set => PlayerPrefs.SetFloat(P + "camDist", value); }
        public static float SnipeSens { get => PlayerPrefs.GetFloat(P + "snipeSens", AimSens); set => PlayerPrefs.SetFloat(P + "snipeSens", value); }
        public static bool AutoAds { get => PlayerPrefs.GetInt(P + "autoAds", 0) == 1; set => PlayerPrefs.SetInt(P + "autoAds", value ? 1 : 0); }
        public static float Music { get => PlayerPrefs.GetFloat(P + "music", .5f); set => PlayerPrefs.SetFloat(P + "music", value); }
        public static float Effects { get => PlayerPrefs.GetFloat(P + "sfx", 1f); set => PlayerPrefs.SetFloat(P + "sfx", value); }
        public static float Guns { get => PlayerPrefs.GetFloat(P + "guns", 1f); set => PlayerPrefs.SetFloat(P + "guns", value); }
        public static string Difficulty { get => PlayerPrefs.GetString(P + "difficulty", "normal"); set => PlayerPrefs.SetString(P + "difficulty", value); }
        public static string Mode { get => PlayerPrefs.GetString(P + "mode", "duels"); set => PlayerPrefs.SetString(P + "mode", value); }
        public static string Primary { get => PlayerPrefs.GetString(P + "primary", "gar"); set => PlayerPrefs.SetString(P + "primary", value); }
        public static string Secondary { get => PlayerPrefs.GetString(P + "secondary", "sword"); set => PlayerPrefs.SetString(P + "secondary", value); }
        public static string Lethal { get => PlayerPrefs.GetString(P + "lethal", "grenade"); set => PlayerPrefs.SetString(P + "lethal", value); }
        public static string Rules { get => PlayerPrefs.GetString(P + "rules2", "normal"); set => PlayerPrefs.SetString(P + "rules2", value); }
        // phone controls (mobile pass, 13 Nov)
        public static float AimAssist { get => PlayerPrefs.GetFloat(P + "assist", 1f); set => PlayerPrefs.SetFloat(P + "assist", value); }
        public static string Gyro { get => PlayerPrefs.GetString(P + "gyro", "off"); set => PlayerPrefs.SetString(P + "gyro", value); }
        public static bool Vibration { get => PlayerPrefs.GetInt(P + "vibe", 1) == 1; set => PlayerPrefs.SetInt(P + "vibe", value ? 1 : 0); }
        public static int Fps { get => PlayerPrefs.GetInt(P + "fps2", 0); set => PlayerPrefs.SetInt(P + "fps2", value); }   // 0 = Auto (AutoQuality)
        /// phones: run at the chosen rate, capped at the screen's own (60 by default: cooler and steadier over long sessions)
        public static void ApplyFps()
        {
            if (!Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer) return;
            int hz = Mathf.Max(60, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));
            Application.targetFrameRate = Fps <= 0 ? AutoQuality.PhoneTarget : Mathf.Min(Fps, hz);
        }
        public static bool RandomMap { get => PlayerPrefs.GetInt(P + "randomMap", 0) == 1; set => PlayerPrefs.SetInt(P + "randomMap", value ? 1 : 0); }
        public static bool RandomChar { get => PlayerPrefs.GetInt(P + "randomChar", 0) == 1; set => PlayerPrefs.SetInt(P + "randomChar", value ? 1 : 0); }
        public static string Touch { get => PlayerPrefs.GetString(P + "touch", "auto"); set => PlayerPrefs.SetString(P + "touch", value); }
        public static string Graphics
        {
            get => PlayerPrefs.GetString(P + "gfx2", "auto");   // "auto" (AutoQuality), "high" or "smooth"
            set => PlayerPrefs.SetString(P + "gfx2", value);
        }

        // ---- display (desktop builds and the editor)
        public static bool IsDesktop => !Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer;
        /// "borderless" (full screen) or "windowed". Exclusive full screen is gone: on Pete's laptop it froze for seconds and locked to 30 fps (street-life-performance.md)
        public static string ScreenMode { get => PlayerPrefs.GetString(P + "screenMode", "borderless") == "windowed" ? "windowed" : "borderless"; set => PlayerPrefs.SetString(P + "screenMode", value); }
        public static int ResW { get => PlayerPrefs.GetInt(P + "resW", 0); set => PlayerPrefs.SetInt(P + "resW", value); }
        public static int ResH { get => PlayerPrefs.GetInt(P + "resH", 0); set => PlayerPrefs.SetInt(P + "resH", value); }
        public static bool VSyncAuto { get => PlayerPrefs.GetInt(P + "vsyncAuto", 1) == 1; set => PlayerPrefs.SetInt(P + "vsyncAuto", value ? 1 : 0); }
        public static bool VSync { get => PlayerPrefs.GetInt(P + "vsync", 1) == 1; set => PlayerPrefs.SetInt(P + "vsync", value ? 1 : 0); }
        public static int FpsCap { get => PlayerPrefs.GetInt(P + "fpsCap", 0); set => PlayerPrefs.SetInt(P + "fpsCap", value); }
        /// the monitor's sizes, biggest last, each once (refresh rates folded together)
        public static List<Vector2Int> Resolutions()
        {
            var l = new List<Vector2Int>();
            foreach (var r in Screen.resolutions) { var v = new Vector2Int(r.width, r.height); if (v.x >= 1024 && !l.Contains(v)) l.Add(v); }
            l.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return l;
        }
        public static int ResolutionIndex(List<Vector2Int> l)
        {
            int w = ResW > 0 ? ResW : Screen.currentResolution.width, h = ResH > 0 ? ResH : Screen.currentResolution.height;
            int best = l.Count - 1; for (int i = 0; i < l.Count; i++) if (l[i].x == w && l[i].y == h) best = i; return Mathf.Max(0, best);
        }
        public static void ApplyScreen()
        {
            if (!IsDesktop || Application.isEditor) return;
            var mode = ScreenMode == "windowed" ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            int w = ResW > 0 ? ResW : Display.main.systemWidth, h = ResH > 0 ? ResH : Display.main.systemHeight;
            if (mode == FullScreenMode.Windowed && w >= Display.main.systemWidth) { w = Mathf.RoundToInt(w * .8f); h = Mathf.RoundToInt(h * .8f); }   // a window you can see the edges of
            if (Screen.fullScreenMode == mode && Screen.width == w && Screen.height == h) return;   // already right: a mode switch costs a hitch
            Screen.SetResolution(w, h, mode, Screen.currentResolution.refreshRateRatio);
            PlayerPrefs.Save();
        }
        // Unity starts in whatever mode the last run used (it keeps it in the registry), so a copy that last ran in exclusive full
        // screen would start that way and then switch: move it to borderless before the first frame instead
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void EarlyScreen()
        {
            if (!IsDesktop || Application.isEditor) return;
            if (Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen || (ScreenMode == "borderless" && Screen.fullScreenMode == FullScreenMode.MaximizedWindow))
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
        }
        static bool screenApplied;
        static void ApplyDisplay()
        {
            if (!IsDesktop) return;
            if (VSyncAuto) { int cap = AutoQuality.PcCap; QualitySettings.vSyncCount = 0; Application.targetFrameRate = cap > 0 ? cap : AutoQuality.PcStart; }   // Auto never uses v-sync: see AutoQuality
            else
            {
                QualitySettings.vSyncCount = VSync ? 1 : 0;   // after the quality level, which sets its own
                Application.targetFrameRate = VSync || FpsCap <= 0 ? -1 : FpsCap;
            }
            if (!screenApplied) { screenApplied = true; ApplyScreen(); }   // once a run: Unity remembers the rest
        }

        public static void Apply()
        {
            foreach (var pc in UnityEngine.Object.FindObjectsByType<PlayerController>()) { pc.sensitivity = Sens; pc.adsSensitivity = AimSens; pc.snipeSensitivity = SnipeSens; }
            TouchControls.Mode = Touch;
            if (TouchControls.I) { TouchControls.I.sensitivity = Sens; TouchControls.I.aimAssist = AimAssist; TouchControls.I.gyro = Gyro; TouchControls.I.autoAds = AutoAds; TouchControls.I.aimSens = AimSens; TouchControls.I.snipeSens = SnipeSens; }
            Haptics.On = Vibration; ApplyFps();
            Sfx.MusicVolume = Music; Sfx.SfxVolume = Effects; Sfx.GunVolume = Guns;
            var names = QualitySettings.names; string want = Graphics == "auto" ? AutoQuality.Level : Graphics == "smooth" ? "Mobile" : "PC";
            for (int i = 0; i < names.Length; i++) if (names[i] == want && QualitySettings.GetQualityLevel() != i) { QualitySettings.SetQualityLevel(i, true); break; }
            ApplyDisplay();
            var rm = RoundManager.Instance; if (rm) { rm.difficulty = Difficulty; rm.simpleRules = Rules == "simple"; }
            PlayerPrefs.Save();
        }
    }
}
