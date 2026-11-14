// Remote test for Rumble's weapon ladder models and swings (14 Nov), through the editor's remote control:
//   call LadderTest.Shot bot knife 0.12 name        give the bot (or "me") that weapon (fists, knife, bat, sword), start a
//                                                   swing, and save a picture from a camera of its own to Logs/<name>.png
//                                                   that many seconds later
//   call LadderTest.Shot bot bat 0.3 name still     the same without the swing (the guard pose)
// The weapon is only lent for the picture and the fighter's loadout is put back after, so it can run inside another
// chat's match in any mode. The ladder itself is checked with: call RumbleLadder.Test (see RumbleLadder.cs).
using UnityEditor;
using UnityEngine;

namespace StreetLife.EditorTools
{
    public static class LadderTest
    {
        public static string Shot(string a)
        {
            if (!Application.isPlaying) return "not in play mode";
            var parts = (a ?? "").Split(' ');
            string who = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : "bot";
            string gun = parts.Length > 1 ? parts[1] : "knife";
            float delay = parts.Length > 2 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : .12f;
            string name = parts.Length > 3 ? parts[3] : "ladder-" + gun;
            bool still = parts.Length > 4 && parts[4] == "still";
            var rm = RoundManager.Instance; if (!rm) return "no round manager";
            var f = who == "me" ? rm.player : rm.bot; if (!f) return "no " + who;
            var w = f.GetComponent<Weapons>(); if (!w) return "no weapons on " + who;
            if (GunsDb.Gun(gun) == null) return "no weapon " + gun;
            string pri = w.primary, sec = w.secondary, leth = w.lethal; int slot = w.slot;
            w.SetLoadout(gun, gun, leth);
            if (!still) { w.swingT = .32f; w.swL = !w.swL; }
            double at = EditorApplication.timeSinceStartup + delay;
            var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs", name + ".png");
            EditorApplication.CallbackFunction cb = null;
            cb = () =>
            {
                if (EditorApplication.timeSinceStartup < at) return; EditorApplication.update -= cb;
                if (!f || !w) return;
                // three-quarter view from the fighter's right front, looking at the chest
                var fw = f.Forward; var rt = Vector3.Cross(Vector3.up, fw).normalized;
                var chest = f.pos + Vector3.up * 1.45f * Fighter.SC;
                var go = new GameObject("ladder test cam"); var cam = go.AddComponent<Camera>();
                cam.transform.position = chest + rt * 2.4f + fw * 2.6f + Vector3.up * .5f; cam.transform.LookAt(chest + fw * .5f + Vector3.up * .15f);
                cam.fieldOfView = 45; cam.nearClipPlane = .05f;
                var tex = new RenderTexture(1280, 720, 24); cam.targetTexture = tex; cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = tex;
                var png = new Texture2D(1280, 720, TextureFormat.RGB24, false); png.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); png.Apply();
                RenderTexture.active = prev; cam.targetTexture = null;
                System.IO.File.WriteAllBytes(file, png.EncodeToPNG());
                Object.Destroy(go); Object.Destroy(tex); Object.Destroy(png);
                w.SetLoadout(pri, sec, leth); w.Equip(slot);
            };
            EditorApplication.update += cb;
            return $"{who} with {gun}{(still ? "" : ", swinging")}; shot in {delay:0.00} s to {file}";
        }
    }
}
