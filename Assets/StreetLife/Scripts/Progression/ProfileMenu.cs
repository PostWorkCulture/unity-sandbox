// Progression (progression chat): the main menu's Profile. A "Lv 4 · Profile" button sits next to Settings in the
// menu's top bar; it opens a panel with your level and XP, your title (pick any you've unlocked), lifetime stats,
// your record on each map and difficulty, and the finishing moves you've landed.
// It adds itself to the match chat's MainMenu tree at runtime (found by the Settings button), so MainMenu.cs is untouched.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StreetLife
{
    public class ProfileMenu : MonoBehaviour
    {
        // the web game's palette (as MainMenu.cs)
        // the main menu's monochrome look (black panels, white text, white for what's picked; "Cyan" and "Gold" are white now)
        static readonly Color Bg = new Color32(6, 6, 6, 235), Panel = new Color32(10, 10, 10, 238), Line = new Color32(70, 70, 70, 255),
            Chip = new Color32(32, 32, 32, 255), Text = new Color32(245, 245, 245, 255), Dim = new Color32(138, 138, 138, 255),
            Cyan = new Color32(255, 255, 255, 255), Ink = new Color32(0, 0, 0, 255), Gold = new Color32(255, 255, 255, 255);

        VisualElement root, overlay, card;
        Button chip;
        bool injected, warned;
        float tryT;

        public static bool Showing { get; private set; }

        void Update()
        {
            if (!injected && Time.unscaledTime >= tryT) { tryT = Time.unscaledTime + .5f; Inject(); }
            if (!injected) return;
            if (chip != null) chip.text = $"Lv {Progress.Level}  ·  Profile";
            if (Showing && (!MainMenu.Open || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))) Close();
        }

        void Inject()
        {
            var mm = FindAnyObjectByType<MainMenu>(); if (!mm) return;
            var doc = mm.GetComponent<UIDocument>(); if (!doc || doc.rootVisualElement == null) return;
            root = doc.rootVisualElement;
            Button settings = null;
            root.Query<Button>().ForEach(b => { if (settings == null && b.text == "Settings") settings = b; });
            if (settings == null || settings.parent == null)
            {
                if (!warned && Time.unscaledTime > 5) { warned = true; Debug.LogWarning("Progression: couldn't find the main menu's Settings button, so Profile isn't in the menu"); }
                return;
            }
            chip = Btn("Profile", Open);
            chip.style.marginLeft = StyleKeyword.Auto; chip.style.marginRight = 10;
            var bar = settings.parent;
            bar.Insert(bar.IndexOf(settings), chip);

            overlay = new VisualElement();
            overlay.style.position = Position.Absolute; overlay.style.left = overlay.style.top = overlay.style.right = overlay.style.bottom = 0;
            overlay.style.alignItems = Align.Center; overlay.style.justifyContent = Justify.Center; overlay.style.backgroundColor = Bg;
            overlay.style.display = DisplayStyle.None;
            root.Add(overlay);
            injected = true;
            Debug.Log("Progression: Profile added to the main menu");
        }

        // ================================================================== building blocks (MainMenu's look)
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
        static VisualElement Row() { var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; r.style.alignItems = Align.Center; return r; }

        static Button Btn(string text, Action click, bool on = false, int size = 15)
        {
            var b = new Button(click) { text = text };
            b.style.fontSize = size; b.style.color = on ? Ink : Text; b.style.unityFontStyleAndWeight = FontStyle.Bold; b.style.unityTextAlign = TextAnchor.MiddleCenter;
            Radius(b, 8); Border(b, 2, on ? Cyan : Line); Pad(b, 8, 16);   // hover edge: MainMenu's handler
            b.style.marginLeft = b.style.marginRight = b.style.marginTop = b.style.marginBottom = 0;
            b.style.flexShrink = 0; b.style.whiteSpace = WhiteSpace.NoWrap;
            b.style.backgroundColor = on ? Cyan : Chip;
            return b;
        }

        // ================================================================== the panel
        public void Open()
        {
            if (!injected) return;
            Showing = true; overlay.style.display = DisplayStyle.Flex;
            Build();
        }

        public void Close()
        {
            Showing = false;
            if (overlay != null) overlay.style.display = DisplayStyle.None;
        }

        void Build()
        {
            overlay.Clear();
            var d = Progress.Data;
            card = new VisualElement(); card.style.backgroundColor = Panel; Radius(card, 14); Border(card, 1, Line); Pad(card, 18, 24);
            card.style.width = 940;
            overlay.Add(card);

            // header
            var head = Row(); head.style.justifyContent = Justify.SpaceBetween;
            var who = Row(); who.style.alignItems = Align.FlexEnd;
            who.Add(L("Profile", 28, Text, true));
            if (!string.IsNullOrEmpty(d.title)) { var t = L("   " + ProgressRewards.NameOf(d.title), 18, Gold, true); t.style.marginBottom = 3; who.Add(t); }
            head.Add(who);
            head.Add(Btn("Close", Close));
            card.Add(head);

            var cols = Row(); cols.style.alignItems = Align.FlexStart; cols.style.marginTop = 8;
            var left = new VisualElement(); left.style.width = 430; left.style.marginRight = 28;
            var right = new VisualElement(); right.style.flexGrow = 1;
            cols.Add(left); cols.Add(right); card.Add(cols);

            // ---- level and XP
            left.Add(Caps("Level"));
            var lvRow = Row(); lvRow.style.justifyContent = Justify.SpaceBetween;
            lvRow.Add(L("Level " + Progress.Level, 22, Text, true));
            lvRow.Add(L($"{Progress.XpIntoLevel} / {Progress.XpPerLevel} XP", 14, Dim));
            left.Add(lvRow);
            var bar = new VisualElement(); bar.style.height = 10; bar.style.marginTop = 6; bar.style.backgroundColor = Chip; Radius(bar, 5);
            var fill = new VisualElement(); fill.style.height = 10; fill.style.width = Length.Percent(100f * Progress.XpIntoLevel / Progress.XpPerLevel); fill.style.backgroundColor = Cyan; Radius(fill, 5);
            bar.Add(fill); left.Add(bar);
            int next = ProgressRewards.NextRewardLevel(Progress.Level);
            var nx = L(next > 0 ? $"{d.xp} XP in all   ·   next reward at level {next}" : $"{d.xp} XP in all", 12, Dim); nx.style.marginTop = 6; left.Add(nx);

            // ---- title
            left.Add(Caps("Title"));
            var wrap = new VisualElement(); wrap.style.flexDirection = FlexDirection.Row; wrap.style.flexWrap = Wrap.Wrap;
            void AddTitle(string id, string name, bool open)
            {
                var b = Btn(open ? name : $"{name}  ·  Lv {LevelFor(id)}", () => { if (!open) return; d.title = id; Progress.Save(); Build(); }, d.title == id, 13);
                b.style.marginRight = 6; b.style.marginBottom = 6; Pad(b, 6, 12);
                if (!open) { b.style.opacity = .4f; b.SetEnabled(false); }
                wrap.Add(b);
            }
            AddTitle("", "None", true);
            foreach (var t in ProgressRewards.Titles) AddTitle(t.id, t.name, Progress.Has(t.id));
            left.Add(wrap);

            // ---- lifetime stats
            left.Add(Caps("Lifetime"));
            var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap;
            string Pct(int a, int b) => b > 0 ? Mathf.RoundToInt(100f * a / b) + "%" : "–";
            void Stat(string k, string v)
            {
                var c = new VisualElement(); c.style.width = 105; c.style.marginBottom = 10;
                c.Add(L(v, 20, Text, true)); c.Add(L(k, 11, Dim));
                grid.Add(c);
            }
            Stat("Matches", d.matches.ToString());
            Stat("Win rate", Pct(d.matchesWon, d.matches));
            Stat("Rounds won", d.roundsWon.ToString());
            Stat("Flawless 5–0", d.flawless.ToString());
            Stat("Kills", d.kills.ToString());
            Stat("K/D", d.deaths > 0 ? (d.kills / (float)d.deaths).ToString("0.00") : d.kills.ToString());
            Stat("Headshots", d.headshots.ToString());
            Stat("Accuracy", Pct(d.hits, d.shots));
            Stat("Damage", Mathf.RoundToInt(d.damage).ToString("N0"));
            Stat("Best streak", d.bestStreak.ToString());
            Stat("Hard+ streak", d.bestHardStreak.ToString());
            Stat("Finishers", Total(d.finishers).ToString());
            left.Add(grid);

            // ---- record per map / difficulty / rules (most played first)
            right.Add(Caps("Record"));
            var recs = new List<ProgressData.Record>(d.record);
            recs.Sort((a, b) => (b.won + b.lost).CompareTo(a.won + a.lost));
            if (recs.Count == 0) right.Add(L("Play a match to start your record.", 14, Dim));
            var set = MapSet.Load();
            for (int i = 0; i < recs.Count && i < 9; i++)
            {
                var r = recs[i];
                var e = set ? set.Get(r.map) : null;
                var row = Row(); row.style.justifyContent = Justify.SpaceBetween; row.style.marginBottom = 4;
                row.Add(L($"{(e != null ? e.title : r.map)}  ·  {Cap(r.difficulty)}  ·  {Cap(r.rules)}", 14, Text));
                row.Add(L($"{r.won} W  –  {r.lost} L", 14, r.won >= r.lost ? Cyan : Dim, true));
                right.Add(row);
            }
            if (recs.Count > 9) right.Add(L($"+ {recs.Count - 9} more", 12, Dim));

            // ---- finishing moves landed
            right.Add(Caps("Finishing moves landed"));
            var fm = new VisualElement(); fm.style.flexDirection = FlexDirection.Row; fm.style.flexWrap = Wrap.Wrap;
            foreach (var mv in Finishers.Moves)
            {
                int n = Progress.CountOf(d.finishers, mv);
                var c = Row(); c.style.width = 150; c.style.marginBottom = 4; c.style.justifyContent = Justify.SpaceBetween; c.style.marginRight = 16;
                c.Add(L(mv, 13, n > 0 ? Text : Dim)); c.Add(L(n.ToString(), 13, n > 0 ? Cyan : Dim, true));
                fm.Add(c);
            }
            right.Add(fm);
        }

        static int Total(List<ProgressData.Count> l) { int n = 0; foreach (var c in l) n += c.n; return n; }
        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        static int LevelFor(string id) { foreach (var t in ProgressRewards.Titles) if (t.id == id) return t.level; return 0; }
    }
}
