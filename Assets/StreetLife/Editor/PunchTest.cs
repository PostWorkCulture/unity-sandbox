// Remote test for the Rumble punch animation (progression chat, 14 Nov), through the editor's remote control:
//   call PunchTest.Shot bot 0.10 name   make the bot (or "me") throw a punch, then save the Game view to Logs/<name>.png
//                                       that many seconds later (play mode, Rumble, ideally after StreetLifeTests.Face 2)
//   call PunchTest.Shot bot 0.10 name side   the same, but seen from the fighter's side by a camera of its own
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
            bool side = parts.Length > 3 && parts[3] == "side";
            cb = () =>
            {
                if (EditorApplication.timeSinceStartup < at) return; EditorApplication.update -= cb;
                if (!side) { ScreenCapture.CaptureScreenshot(file); return; }
                // a camera of its own: 3.2 m to the fighter's side, looking at the chest, so the arms show in profile
                var fw = f.Forward; var rt = Vector3.Cross(Vector3.up, fw).normalized;
                var chest = f.pos + Vector3.up * 1.35f * Fighter.SC;
                var go = new GameObject("punch test cam"); var cam = go.AddComponent<Camera>();
                cam.transform.position = chest + rt * 3.2f + fw * .9f + Vector3.up * .25f; cam.transform.LookAt(chest + fw * .7f);
                cam.fieldOfView = 45; cam.nearClipPlane = .05f;
                var tex = new RenderTexture(1280, 720, 24); cam.targetTexture = tex; cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = tex;
                var png = new Texture2D(1280, 720, TextureFormat.RGB24, false); png.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); png.Apply();
                RenderTexture.active = prev; cam.targetTexture = null;
                System.IO.File.WriteAllBytes(file, png.EncodeToPNG());
                Object.Destroy(go); Object.Destroy(tex); Object.Destroy(png);
            };
            EditorApplication.update += cb;
            return $"{who} ({w.Gun}) punching; shot in {delay:0.00} s to {file}";
        }
    }
}
