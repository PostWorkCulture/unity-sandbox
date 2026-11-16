// Remote helpers for screenshotting every menu (menu chat): open a screen, then `shot <name>`.
//   call MenuShots.Main 0|1|2   main menu step      call MenuShots.Settings      call MenuShots.Pause (in a match)
//   call MenuShots.Tab          weapons and shop    call MenuShots.Picker f3|f4  call MenuShots.Close
//   call MenuShots.End          end the match now (end screen)
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace StreetLife.EditorTools
{
    public static class MenuShots
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        static object Call(object o, string m, params object[] a) { var mi = o.GetType().GetMethod(m, F); return mi != null ? mi.Invoke(o, a) : null; }
        static T Field<T>(object o, string f) { var fi = o.GetType().GetField(f, F); return fi != null ? (T)fi.GetValue(o) : default; }
        static MainMenu MM => Object.FindAnyObjectByType<MainMenu>();

        public static string Main(string a) { var m = MM; if (!m) return "no menu"; int s = int.TryParse(a, out var n) ? n : 0; Call(m, "SetStep", s); return "step " + s; }
        public static string Settings(string a) { var m = MM; if (!m) return "no menu"; var st = Field<VisualElement>(m, "settings"); var me = Field<VisualElement>(m, "menu"); Call(m, "Show", st, me); return "settings"; }
        public static string Pause(string a) { var m = MM; if (!m) return "no menu"; Call(m, "Pause"); return "paused " + MainMenu.Paused; }
        public static string Tab(string a) { var h = Object.FindAnyObjectByType<WeaponHud>(); if (!h) return "no weapon hud"; Call(h, "SetMenu", a.Trim() != "off"); return "tab " + WeaponHud.MenuOpen; }
        public static string Picker(string a)
        {
            if (a.Trim() == "f4") { var p = Object.FindAnyObjectByType<MapPicker>(); if (!p) return "no map picker"; Call(p, "Toggle", true); return "map picker"; }
            var c = Object.FindAnyObjectByType<CharacterPicker>(); if (!c) return "no character picker"; Call(c, "Toggle", true); return "character picker";
        }
        public static string Close(string a)
        {
            var c = Object.FindAnyObjectByType<CharacterPicker>(); if (c && CharacterPicker.Open) Call(c, "Toggle", false);
            var p = Object.FindAnyObjectByType<MapPicker>(); if (p && MapPicker.Open) Call(p, "Toggle", false);
            var h = Object.FindAnyObjectByType<WeaponHud>(); if (h && WeaponHud.MenuOpen) Call(h, "SetMenu", false);
            var m = MM; if (m && MainMenu.Paused) Call(m, "Resume");
            return "closed";
        }
        /// call MenuShots.Wheel open [slot] | pick | close   (gives the player 3 grenades and a Sentry first)
        public static string Wheel(string a)
        {
            var tw = ThrowWheel.I; if (!tw) return "no wheel";
            var w = tw.GetComponent<Weapons>(); var parts = a.Trim().Split(' ');
            if (parts[0] == "open")
            {
                w.nades = 3; w.turretKit = 1;
                Call(tw, "OpenWheel"); typeof(ThrowWheel).GetField("holding", F).SetValue(tw, true); typeof(ThrowWheel).GetField("holdT", F).SetValue(tw, 1f);
                int slot = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 1; typeof(ThrowWheel).GetField("hover", F).SetValue(tw, slot);
                typeof(ThrowWheel).GetField("pick", F).SetValue(tw, new Vector2(Mathf.Sin(slot * 120 * Mathf.Deg2Rad), Mathf.Cos(slot * 120 * Mathf.Deg2Rad)));
                return "wheel open, hover " + slot;
            }
            if (parts[0] == "pick") { typeof(ThrowWheel).GetField("holding", F).SetValue(tw, false); Call(tw, "Close", true); return "picked " + tw.sel + " ready " + tw.ready; }
            Call(tw, "Close", false); return "closed";
        }

        // ---- map pictures that follow the maps: call MenuShots.Thumbs [all]
        // Retakes every map picture whose map files have changed since it was taken (MainMenu.ThumbStale), one map at a
        // time: play, wait for the menu to take the picture, stop. Every build runs it first when anything is out of date.
        const string ThumbKey = "StreetLife.thumbQueue", ThumbBack = "StreetLife.thumbBack";
        static int thumbPhase; static double thumbDeadline;
        public static string StaleThumbs()
        {
            var set = MapSet.Load(); if (!set || set.maps == null) return "";
            var q = new System.Collections.Generic.List<string>(); foreach (var m in set.maps) if (MainMenu.ThumbStale(m.id)) q.Add(m.id);
            return string.Join(",", q);
        }
        public static string Thumbs(string a)
        {
            if (UnityEditor.SessionState.GetString(ThumbKey, "") != "") return "already retaking: " + UnityEditor.SessionState.GetString(ThumbKey, "");
            bool all = (a ?? "").Trim() == "all"; var set = MapSet.Load(); if (!set || set.maps == null) return "no maps";
            if (all) foreach (var m in set.maps) { try { System.IO.File.Delete(MainMenu.ThumbSigPath(m.id)); } catch { } }
            string q = StaleThumbs(); if (q == "") return "every map picture is up to date";
            UnityEditor.SessionState.SetString(ThumbKey, q); UnityEditor.SessionState.SetString(ThumbBack, PlayerPrefs.GetString("StreetLife.map", "street"));
            thumbPhase = 0; UnityEditor.EditorApplication.update -= ThumbTick; UnityEditor.EditorApplication.update += ThumbTick;
            return "retaking " + q;
        }
        [UnityEditor.InitializeOnLoadMethod]
        static void ThumbResume() { if (UnityEditor.SessionState.GetString(ThumbKey, "") != "") { thumbPhase = UnityEditor.EditorApplication.isPlaying ? 1 : 0; thumbDeadline = UnityEditor.EditorApplication.timeSinceStartup + 40; UnityEditor.EditorApplication.update -= ThumbTick; UnityEditor.EditorApplication.update += ThumbTick; } }
        static void ThumbTick()
        {
            string q = UnityEditor.SessionState.GetString(ThumbKey, "");
            if (q == "") { UnityEditor.EditorApplication.update -= ThumbTick; return; }
            string id = q.Split(',')[0];
            void Next() { int i = q.IndexOf(','); UnityEditor.SessionState.SetString(ThumbKey, i < 0 ? "" : q.Substring(i + 1)); thumbPhase = 0; }
            if (thumbPhase == 0)
            {
                if (UnityEditor.EditorApplication.isPlaying) { UnityEditor.EditorApplication.isPlaying = false; return; }
                if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || UnityEditor.EditorApplication.isCompiling) return;
                PlayerPrefs.SetString("StreetLife.map", id); PlayerPrefs.Save();
                UnityEditor.EditorApplication.isPlaying = true; thumbPhase = 1; thumbDeadline = UnityEditor.EditorApplication.timeSinceStartup + 40;
            }
            else
            {
                bool late = UnityEditor.EditorApplication.timeSinceStartup > thumbDeadline;
                if (!UnityEditor.EditorApplication.isPlaying) { if (late) Next(); return; }
                if (!MainMenu.ThumbStale(id) || late)
                {
                    if (late) Debug.LogWarning("[thumbs] no picture taken for " + id);
                    Next(); UnityEditor.EditorApplication.isPlaying = false;
                    if (UnityEditor.SessionState.GetString(ThumbKey, "") == "")
                    {
                        PlayerPrefs.SetString("StreetLife.map", UnityEditor.SessionState.GetString(ThumbBack, "street")); PlayerPrefs.Save();
                        UnityEditor.EditorApplication.delayCall += () => { UnityEditor.AssetDatabase.Refresh(); Debug.Log("[thumbs] done"); };
                    }
                }
            }
        }

        public static string End(string a) { var rm = RoundManager.Instance; if (!rm) return "no rm"; rm.matchOver = true; Call(rm, "EnterMatchEnd"); return "match end"; }
    }
}
