// Remote test for the Rumble punch animation (progression chat, 14 Nov), through the editor's remote control:
//   call PunchTest.Shot bot 0.10 name   make the bot (or "me") throw a punch, then save the Game view to Logs/<name>.png
//                                       that many seconds later (play mode, Rumble, ideally after StreetLifeTests.Face 2)
// It only starts the punch animation (as Weapons.Melee does, by setting swingT): no damage, no lunge.
using UnityEditor;
using UnityEngine;

namespace StreetLife.EditorTools
{
    public static class PunchTest
    {
        public static string Shot(string a)
        {
            if (!Application.isPlaying) return "not in play mode";
            var parts = (a ?? "").Split(' ');
            string who = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : "bot";
            float delay = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : .1f;
            string name = parts.Length > 2 ? parts[2] : "punch";
            var rm = RoundManager.Instance; if (!rm) return "no round manager";
            var f = who == "me" ? rm.player : rm.bot; if (!f) return "no " + who;
            var w = f.GetComponent<Weapons>(); if (!w) return "no weapons on " + who;
            w.swingT = .32f;
            double at = EditorApplication.timeSinceStartup + delay;
            var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs", name + ".png");
            EditorApplication.CallbackFunction cb = null;
            cb = () => { if (EditorApplication.timeSinceStartup < at) return; EditorApplication.update -= cb; ScreenCapture.CaptureScreenshot(file); };
            EditorApplication.update += cb;
            return $"{who} ({w.Gun}) punching; shot in {delay:0.00} s to {file}";
        }
    }
}
