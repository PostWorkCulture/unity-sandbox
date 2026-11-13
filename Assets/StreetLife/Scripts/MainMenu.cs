// The front end, styled after the web game's menu (dark panels, cyan selections, pink-to-purple call to action):
//   1  Mode & map: bot difficulty, 1v1 rules, the map (all eight, from the maps chat's MapSet), with the live map slowly
//      circling behind the menu.
//   2  Character: every character (characters chat), dressed on your own fighter live in front of the camera.
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
        static readonly Color Bg = new Color32(17, 18, 24, 235), Panel = new Color32(27, 30, 38, 240), Line = new Color32(46, 51, 63, 255),
            Chip = new Color32(36, 40, 51, 255), Text = new Color32(236, 238, 245, 255), Dim = new Color32(150, 156, 176, 255),
            Cyan = new Color32(63, 214, 245, 255), Pink = new Color32(255, 61, 127, 255), Purple = new Color32(124, 92, 255, 255), Ink = new Color32(16, 20, 28, 255);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Settings.Apply();
            if (FindAnyObjectByType<MainMenu>() || !FindAnyObjectByType<MapBuilder>()) return;
            new GameObject("Main menu").AddComponent<MainMenu>();
        }

        UIDocument doc; VisualElement root, menu, pause, settings, mapCard, charGrid, charInfo, stepA, stepB;
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
            root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            root.pickingMode = PickingMode.Ignore;
            // no UI theme: the document root has no size of its own, so pin it to the screen
            root.style.position = Position.Absolute; root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            selMap = MapSet.Current;
            BuildMenu(); BuildPause(); BuildSettings();
            Show(Open ? menu : null);
            if (Open) Enter(); else player = FindPlayer();
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

        static Texture2D gradient;
        static Texture2D Gradient()
        {
            if (gradient) return gradient;
            gradient = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < 256; i++) gradient.SetPixel(i, 0, Color.Lerp(Pink, Purple, i / 255f));
            gradient.Apply(); return gradient;
        }

        Button Btn(string text, Action click, bool primary = false, int size = 15)
        {
            var b = new Button(click) { text = text };
            b.style.fontSize = size; b.style.color = Text; b.style.unityFontStyleAndWeight = FontStyle.Bold; b.style.unityTextAlign = TextAnchor.MiddleCenter;
            Radius(b, primary ? 10 : 8); Border(b, primary ? 2 : 1, primary ? new Color(0, 0, 0, .35f) : Line); Pad(b, primary ? 12 : 8, primary ? 40 : 16);
            b.style.marginLeft = b.style.marginRight = 0; b.style.marginTop = b.style.marginBottom = 0;
            b.style.flexShrink = 0; b.style.whiteSpace = WhiteSpace.NoWrap;
            if (primary) { b.style.backgroundImage = new StyleBackground(Gradient()); b.style.backgroundColor = Pink; b.style.fontSize = 20; }
            else b.style.backgroundColor = Chip;
            b.RegisterCallback<PointerEnterEvent>(_ => b.style.opacity = .85f);
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.opacity = 1f);
            return b;
        }

        /// The web game's segmented control: one bar, the picked option filled cyan.
        VisualElement Segmented(string[] labels, string[] values, Func<string> get, Action<string> set)
        {
            var bar = new VisualElement(); bar.style.flexDirection = FlexDirection.Row; bar.style.backgroundColor = Chip; Radius(bar, 9); Pad(bar, 3, 3);
            var buttons = new List<Button>();
            void Refresh() { for (int i = 0; i < buttons.Count; i++) { bool on = values[i] == get(); buttons[i].style.backgroundColor = on ? Cyan : new Color(0, 0, 0, 0); buttons[i].style.color = on ? Ink : Dim; } }
            for (int i = 0; i < labels.Length; i++)
            {
                var v = values[i];
                var b = new Button(() => { set(v); Refresh(); }) { text = labels[i] };
                b.style.flexGrow = 1; b.style.unityTextAlign = TextAnchor.MiddleCenter; b.style.fontSize = 14; b.style.unityFontStyleAndWeight = FontStyle.Bold; Radius(b, 7); Border(b, 0, Line); Pad(b, 7, 6);
                b.style.marginLeft = b.style.marginRight = b.style.marginTop = b.style.marginBottom = 0;
                buttons.Add(b); bar.Add(b);
            }
            Refresh(); return bar;
        }

        /// ‹ value › stepper (settings): works with no UI theme at all.
        VisualElement Stepper(string name, float[] steps, Func<float> get, Action<float> set, Func<float, string> fmt)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.justifyContent = Justify.SpaceBetween; row.style.marginBottom = 10;
            row.Add(L(name, 15, Text));
            var box = new VisualElement(); box.style.flexDirection = FlexDirection.Row; box.style.alignItems = Align.Center;
            var val = L("", 15, Cyan, true); val.style.width = 70; val.style.unityTextAlign = TextAnchor.MiddleCenter;
            int Index() { float v = get(); int best = 0; for (int i = 1; i < steps.Length; i++) if (Mathf.Abs(steps[i] - v) < Mathf.Abs(steps[best] - v)) best = i; return best; }
            void Refresh() => val.text = fmt(steps[Index()]);
            box.Add(Btn("<", () => { set(steps[Mathf.Max(0, Index() - 1)]); Refresh(); }));
            box.Add(val);
            box.Add(Btn(">", () => { set(steps[Mathf.Min(steps.Length - 1, Index() + 1)]); Refresh(); }));
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
            var imok = L("imok", 38, Text); imok.style.marginRight = 10; title.Add(imok); title.Add(L("1v1", 38, Pink));
            var unity = L("  Unity edition", 14, Dim); unity.style.marginBottom = 8; title.Add(unity);
            top.Add(title);
            top.Add(Btn("Settings", () => Show(settings, menu)));
            menu.Add(top);

            // the two steps share one panel; the middle stays open so the live map / your fighter show through
            var body = new VisualElement(); body.style.flexGrow = 1; body.style.flexDirection = FlexDirection.Row; body.pickingMode = PickingMode.Ignore;
            menu.Add(body);

            // ---- step 1: mode & map
            stepA = new VisualElement(); stepA.style.flexGrow = 1; stepA.style.flexDirection = FlexDirection.Row; stepA.pickingMode = PickingMode.Ignore;
            var left = Card(330); left.style.marginRight = 16; left.style.alignSelf = Align.FlexStart;
            left.Add(StepHead(1, "Mode & map"));
            left.Add(Caps("Mode")); left.Add(Segmented(new[] { "Duels  ·  1v1" }, new[] { "duel" }, () => "duel", _ => { }));
            left.Add(Caps("1v1 rules")); left.Add(Segmented(new[] { "Normal", "Simple" }, new[] { "normal", "simple" }, () => Settings.Rules, v => Settings.Rules = v));
            left.Add(Caps("Bots")); left.Add(Segmented(new[] { "Easy", "Normal", "Hard", "Extreme" }, new[] { "easy", "normal", "hard", "extreme" }, () => Settings.Difficulty, v => Settings.Difficulty = v));
            var hint = L("Normal rules: any loadout (Tab in a match) and every bot style.\nSimple: M4, sword and grenade; no sniper bots.", 12, Dim); hint.style.marginTop = 12; hint.style.whiteSpace = WhiteSpace.Normal;
            left.Add(hint);
            stepA.Add(left);

            var right = new VisualElement(); right.style.flexGrow = 1; right.pickingMode = PickingMode.Ignore;
            mapCard = new VisualElement(); mapCard.style.flexGrow = 1; Radius(mapCard, 16); Border(mapCard, 2, Cyan); mapCard.pickingMode = PickingMode.Ignore;
            mapCard.style.justifyContent = Justify.FlexEnd; Pad(mapCard, 26, 30);
            var arrows = new VisualElement(); arrows.style.position = Position.Absolute; arrows.style.left = 14; arrows.style.right = 14; arrows.style.top = new Length(45, LengthUnit.Percent);
            arrows.style.flexDirection = FlexDirection.Row; arrows.style.justifyContent = Justify.SpaceBetween; arrows.pickingMode = PickingMode.Ignore;
            arrows.Add(Round("<", () => StepMap(-1))); arrows.Add(Round(">", () => StepMap(1)));
            mapCard.Add(arrows);
            mapTag = L("", 12, Text, true); mapTag.style.alignSelf = Align.FlexStart; mapTag.style.letterSpacing = 1.5f; Radius(mapTag, 5); Pad(mapTag, 3, 8);
            mapTag.style.backgroundImage = new StyleBackground(Gradient());
            mapTitle = L("", 64, Text); mapTitle.style.marginTop = 6;
            var bar = new VisualElement(); bar.style.height = 4; bar.style.width = 300; Radius(bar, 2); bar.style.backgroundImage = new StyleBackground(Gradient()); bar.style.marginTop = 2; bar.style.marginBottom = 10;
            mapDesc = L("", 15, Text, true); mapDesc.style.letterSpacing = 2;
            foreach (var e in new VisualElement[] { mapTag, mapTitle, bar, mapDesc }) mapCard.Add(e);
            right.Add(mapCard);
            var chips = new VisualElement(); chips.style.flexDirection = FlexDirection.Row; chips.style.flexWrap = Wrap.Wrap; chips.style.marginTop = 10;
            var set = MapSet.Load();
            if (set && set.maps != null)
                foreach (var m in set.maps)
                {
                    if (m == null || (!m.IsStreet && (!m.mapJson || !m.library))) continue;   // left out of this build
                    var id = m.id;
                    var c = Btn(m.title, () => { selMap = id; RefreshMap(); }, false, 13); c.name = "map-" + id; c.style.marginRight = 8; c.style.marginBottom = 6;
                    chips.Add(c);
                }
            right.Add(chips);
            stepA.Add(right);
            body.Add(stepA);

            // ---- step 2: character
            stepB = new VisualElement(); stepB.style.flexGrow = 1; stepB.style.flexDirection = FlexDirection.Row; stepB.pickingMode = PickingMode.Ignore;
            var grid = Card(420); grid.style.alignSelf = Align.Stretch;
            grid.Add(StepHead(2, "Character"));
            charGrid = new VisualElement(); charGrid.style.flexDirection = FlexDirection.Row; charGrid.style.flexWrap = Wrap.Wrap; charGrid.style.marginTop = 12;
            grid.Add(charGrid);
            charInfo = new VisualElement(); charInfo.style.marginTop = 10; grid.Add(charInfo);
            stepB.Add(grid);
            var gap = new VisualElement(); gap.style.flexGrow = 1; gap.pickingMode = PickingMode.Ignore; stepB.Add(gap);   // your fighter, live
            body.Add(stepB);
            BuildCharacters();

            // ---- bottom: back / next or play
            var bottom = new VisualElement(); bottom.style.flexDirection = FlexDirection.Row; bottom.style.justifyContent = Justify.Center; bottom.style.alignItems = Align.Center; bottom.style.marginTop = 14;
            var back = Btn("Back", () => SetStep(0)); back.name = "back"; back.style.marginRight = 14;
            var next = Btn("Character", () => { if (step == 0) SetStep(1); else Play(); }, true); next.name = "next";
            bottom.Add(back); bottom.Add(next);
            menu.Add(bottom);
            SetStep(0); RefreshMap();
        }

        VisualElement StepHead(int n, string text)
        {
            var h = new VisualElement(); h.style.flexDirection = FlexDirection.Row; h.style.alignItems = Align.Center;
            var dot = L(n.ToString(), 14, Ink, true); dot.style.width = dot.style.height = 26; Radius(dot, 13); dot.style.backgroundColor = Cyan; dot.style.unityTextAlign = TextAnchor.MiddleCenter; dot.style.marginRight = 10;
            h.Add(dot); h.Add(L(text, 20, Text)); return h;
        }

        Button Round(string t, Action a)
        {
            var b = Btn(t, a, false, 26); b.style.width = b.style.height = 52; Radius(b, 26); Pad(b, 0, 0); b.style.backgroundColor = new Color(0, 0, 0, .45f); Border(b, 2, new Color(1, 1, 1, .8f));
            return b;
        }

        void SetStep(int s)
        {
            step = s;
            stepA.style.display = s == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            stepB.style.display = s == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            var back = menu.Q<Button>("back"); if (back != null) back.style.visibility = s == 0 ? Visibility.Hidden : Visibility.Visible;
            var next = menu.Q<Button>("next"); if (next != null) next.text = s == 0 ? "Character" : "Play";
        }

        void StepMap(int d)
        {
            var set = MapSet.Load(); if (!set || set.maps == null || set.maps.Length == 0) return;
            int i = Array.FindIndex(set.maps, m => m != null && m.id == selMap); if (i < 0) i = 0;
            for (int k = 0; k < set.maps.Length; k++)
            {
                i = (i + d + set.maps.Length) % set.maps.Length; var m = set.maps[i];
                if (m != null && (m.IsStreet || (m.mapJson && m.library))) break;
            }
            selMap = set.maps[i].id; RefreshMap();
        }

        void RefreshMap()
        {
            var set = MapSet.Load(); var m = set ? set.Get(selMap) : null;
            mapTitle.text = m != null ? m.title : "Street Life";
            mapDesc.text = (m != null ? m.desc : "Suburban road, cars and front yards").ToUpperInvariant();
            mapTag.text = ((m != null && !string.IsNullOrEmpty(m.mode) ? m.mode : "Duels") + "  ·  1v1").ToUpperInvariant();
            // the live map shows through when it's the one picked; another map gets its own colours until you play it
            bool live = selMap == MapSet.Current;
            var sw = m != null && m.swatch != null && m.swatch.Length > 0 ? m.swatch[0] : new Color(.2f, .3f, .25f);
            var thumb = live ? null : Thumb(selMap);
            mapCard.style.backgroundImage = thumb ? new StyleBackground(thumb) : new StyleBackground((Texture2D)null);
            mapCard.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
            mapCard.style.backgroundColor = live ? new Color(0, 0, 0, .08f) : thumb ? Color.white : new Color(sw.r * .6f, sw.g * .6f, sw.b * .6f, .92f);
            if (set && set.maps != null)
                foreach (var e in set.maps) { if (e == null) continue; var b = menu.Q<Button>("map-" + e.id); if (b == null) continue; bool on = e.id == selMap; Border(b, on ? 2 : 1, on ? Cyan : Line); b.style.color = on ? Text : Dim; }
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

        float thumbT = 3.5f;
        void MaybeTakeThumb()
        {
            if (!Open || step != 0 || thumbT <= 0 || (thumbT -= Time.unscaledDeltaTime) > 0) return;
            var id = MapSet.Current;
            if (Resources.Load<Texture2D>("StreetLife/MapThumbs/" + id)) return;
            if (!Application.isEditor) { try { if (System.IO.File.Exists(DeviceThumb(id))) return; } catch { return; } }
            var cam = Camera.main; if (!cam) return;
            var rt = RenderTexture.GetTemporary(960, 540, 24, RenderTextureFormat.ARGB32);
            var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var png = tex.EncodeToPNG();
            try
            {
                if (Application.isEditor)
                {
                    var dir = System.IO.Path.Combine(Application.dataPath, "StreetLife/Resources/StreetLife/MapThumbs");
                    System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, id + ".png"), png);
                }
                else System.IO.File.WriteAllBytes(DeviceThumb(id), png);
            }
            catch (Exception e) { Debug.LogWarning("Street Life: couldn't save a map picture: " + e.Message); }
            thumbs[id] = tex;
        }

        void BuildCharacters()
        {
            charGrid.Clear();
            var cs = CharacterSet.Load(); if (!cs) { charGrid.Add(L("Characters aren't set up in this project yet.", 13, Dim)); return; }
            foreach (var c in cs.All)
            {
                if (!cs.Has(c.id)) continue; var id = c.id;
                var t = new Button(() => PickCharacter(id)) { text = "" }; t.name = "ch-" + id;
                t.style.width = 88; t.style.height = 58; Radius(t, 9); Border(t, 1, Line); t.style.marginRight = 7; t.style.marginBottom = 7; Pad(t, 4, 4);
                t.style.backgroundColor = Color.Lerp(Chip, c.Colour, .55f); t.style.justifyContent = Justify.FlexEnd;
                var n = L(c.name, 12, Text, true); n.style.unityTextAlign = TextAnchor.LowerCenter; n.style.whiteSpace = WhiteSpace.Normal; t.Add(n);
                charGrid.Add(t);
            }
            RefreshCharacter();
        }

        void PickCharacter(string id)
        {
            CharacterSkin.PlayerChoice = id;
            var p = FindPlayer(); var rig = p ? p.GetComponent<CharacterRig>() : null;
            if (rig) CharacterSkin.Apply(rig, id);
            RefreshCharacter();
        }

        void RefreshCharacter()
        {
            var cs = CharacterSet.Load(); if (!cs) return;
            var mine = CharacterSkin.PlayerChoice;
            foreach (var c in cs.All) { var b = charGrid.Q<Button>("ch-" + c.id); if (b == null) continue; bool on = c.id == mine; Border(b, on ? 3 : 1, on ? Cyan : Line); }
            charInfo.Clear();
            var i = cs.Get(mine); if (i == null) return;
            charInfo.Add(L(i.name, 26, Text, true));
            var d = L(i.desc ?? "", 13, Dim); d.style.whiteSpace = WhiteSpace.Normal; d.style.marginTop = 4; charInfo.Add(d);
            if (!string.IsNullOrEmpty(i.ability)) { var a = L("Ability (E): " + i.ability + (i.cooldown > 0 ? $"  ·  {i.cooldown:0} s" : ""), 13, Cyan); a.style.marginTop = 8; charInfo.Add(a); }
            var s = L($"Speed x{(i.speed > 0 ? i.speed : 1):0.##}   ·   {(i.hp > 0 ? i.hp : 100):0} HP", 13, Dim); s.style.marginTop = 4; charInfo.Add(s);
        }

        // ================================================================== pause and settings
        void BuildPause()
        {
            pause = Overlay(); pause.style.backgroundColor = new Color(0, 0, 0, .55f);
            var c = Card(340); c.style.alignItems = Align.Stretch;
            var t = L("Paused", 30, Text, true); t.style.unityTextAlign = TextAnchor.MiddleCenter; t.style.marginBottom = 16; c.Add(t);
            foreach (var (label, act, primary) in new (string, Action, bool)[] {
                ("Resume", new Action(Resume), true), ("Settings", new Action(() => Show(settings, pause)), false), ("Main menu", new Action(ToMenu), false),
#if !UNITY_WEBGL && !UNITY_EDITOR
                ("Quit", new Action(Application.Quit), false),
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
            c.Add(Stepper("Aim sensitivity", new[] { .3f, .4f, .5f, .6f, .7f, .8f, .9f, 1f }, () => Settings.AimSens, v => { Settings.AimSens = v; Settings.Apply(); }, v => v.ToString("0.0")));
            var vol = new float[] { 0, .1f, .2f, .3f, .4f, .5f, .6f, .7f, .8f, .9f, 1f };
            c.Add(Stepper("Music", vol, () => Settings.Music, v => { Settings.Music = v; Settings.Apply(); }, v => Mathf.RoundToInt(v * 100) + "%"));
            c.Add(Stepper("Effects", vol, () => Settings.Effects, v => { Settings.Effects = v; Settings.Apply(); }, v => Mathf.RoundToInt(v * 100) + "%"));
            c.Add(Caps("Graphics"));
            c.Add(Segmented(new[] { "High", "Smooth" }, new[] { "high", "smooth" }, () => Settings.Graphics, v => { Settings.Graphics = v; Settings.Apply(); }));
            var gh = L("High: 4K shadows, full ambient occlusion, 4x anti-aliasing. Smooth: the lighter settings phones use.", 12, Dim); gh.style.whiteSpace = WhiteSpace.Normal; gh.style.marginTop = 6; c.Add(gh);
            c.Add(Caps("Frame rate readout"));
            c.Add(Segmented(new[] { "Off", "On" }, new[] { "off", "on" }, () => PerfProbe.Show ? "on" : "off", v => PerfProbe.Show = v == "on"));
            var done = Btn("Done", () => Show(backTo), true); done.style.marginTop = 18; c.Add(done);
            settings.Add(c);
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
            if (selMap != MapSet.Current && MapSet.Load() && MapSet.Load().Get(selMap) != null)
            {
                MapSet.Choice = selMap; skipOnce = true;
                var sc = SceneManager.GetActiveScene();
                if (sc.buildIndex >= 0) SceneManager.LoadScene(sc.buildIndex); else SceneManager.LoadScene(sc.name);
                return;
            }
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
        void ToMenu()
        {
            Time.timeScale = 1; Paused = false;
            var sc = SceneManager.GetActiveScene();
            if (sc.buildIndex >= 0) SceneManager.LoadScene(sc.buildIndex); else SceneManager.LoadScene(sc.name);
        }

        void Update()
        {
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
            if (!Open) return;
            var cam = Camera.main; if (!cam) return;
            orbit += Time.unscaledDeltaTime * .05f;
            if (step == 1 && player)
            {
                float yaw = player.yaw;
                var fwd = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
                var c = player.pos + Vector3.up * 1.25f;
                var want = c + fwd * 4.2f + Vector3.up * .35f + new Vector3(fwd.z, 0, -fwd.x) * -1.3f;   // a little to the side so the panel doesn't cover them
                cam.transform.position = Vector3.Lerp(cam.transform.position, want, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 4));
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(c - cam.transform.position), 1 - Mathf.Exp(-Time.unscaledDeltaTime * 6));
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

        void OnDestroy() { if (Paused) Time.timeScale = 1; Open = false; Paused = false; }
    }

    /// Saved player settings (PlayerPrefs), applied at every scene load and whenever they change.
    public static class Settings
    {
        const string P = "StreetLife.";
        public static float Sens { get => PlayerPrefs.GetFloat(P + "sens", 1f); set => PlayerPrefs.SetFloat(P + "sens", value); }
        public static float AimSens { get => PlayerPrefs.GetFloat(P + "aimSens", 1f); set => PlayerPrefs.SetFloat(P + "aimSens", value); }
        public static float Music { get => PlayerPrefs.GetFloat(P + "music", .5f); set => PlayerPrefs.SetFloat(P + "music", value); }
        public static float Effects { get => PlayerPrefs.GetFloat(P + "sfx", 1f); set => PlayerPrefs.SetFloat(P + "sfx", value); }
        public static string Difficulty { get => PlayerPrefs.GetString(P + "difficulty", "normal"); set => PlayerPrefs.SetString(P + "difficulty", value); }
        public static string Rules { get => PlayerPrefs.GetString(P + "rules", "simple"); set => PlayerPrefs.SetString(P + "rules", value); }
        public static string Graphics
        {
            get => PlayerPrefs.GetString(P + "gfx", Application.isMobilePlatform ? "smooth" : "high");
            set => PlayerPrefs.SetString(P + "gfx", value);
        }

        public static void Apply()
        {
            foreach (var pc in UnityEngine.Object.FindObjectsByType<PlayerController>()) { pc.sensitivity = Sens; pc.adsSensitivity = AimSens; }
            if (TouchControls.I) TouchControls.I.sensitivity = Sens;
            Sfx.MusicVolume = Music; Sfx.SfxVolume = Effects;
            var names = QualitySettings.names; string want = Graphics == "smooth" ? "Mobile" : "PC";
            for (int i = 0; i < names.Length; i++) if (names[i] == want && QualitySettings.GetQualityLevel() != i) { QualitySettings.SetQualityLevel(i, true); break; }
            var rm = RoundManager.Instance; if (rm) { rm.difficulty = Difficulty; rm.simpleRules = Rules == "simple"; }
            PlayerPrefs.Save();
        }
    }
}
