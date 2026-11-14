// Look checks from a chat (maps chat): "call MapWorldTest.Test <cmd>" in play mode.
//   mood on|off          the map's mood volume
//   bloom <x>            bloom intensity on the mood volume (adds a Bloom override)
//   ev <x>               post exposure on the mood volume
//   geo <i> on|off       map mesh i (the "meshN_m..." objects)
//   cam x y z yaw pitch  a fixed camera for screenshots; "cam off"
//   sweep x y z yaw pitch  renders that view to Logs/look-<variant>.png for: as is, no mood volume, no beams (mesh 42),
//                        no bloom, no extra exposure (one command, so other chats' play/stop can't split it)
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StreetLife
{
    public static class MapWorldTest
    {
        public static string Test(string a)
        {
            var w = MapWorld.I; if (!w) return "no MapWorld";
            var p = (a ?? "").Trim().Split(' ');
            float F(int i) => float.Parse(p[i], System.Globalization.CultureInfo.InvariantCulture);
            var vol = w.transform.Find("Map mood") ? w.transform.Find("Map mood").GetComponent<Volume>() : null;
            switch (p[0])
            {
                case "sweep": w.StartCoroutine(Sweep(new Vector3(F(1), F(2), F(3)), Quaternion.Euler(F(5), F(4), 0))); return "sweeping";
                case "mood": if (!vol) return "no mood volume"; vol.enabled = p.Length < 2 || p[1] != "off"; return "mood " + vol.enabled;
                case "bloom": { if (!vol) return "no mood"; if (!vol.sharedProfile.TryGet<Bloom>(out var b)) b = vol.sharedProfile.Add<Bloom>(true); b.intensity.Override(F(1)); return "bloom " + F(1); }
                case "ev": { if (!vol) return "no mood"; if (!vol.sharedProfile.TryGet<ColorAdjustments>(out var c)) c = vol.sharedProfile.Add<ColorAdjustments>(true); c.postExposure.Override(F(1)); return "ev " + F(1); }
                case "geo":
                    {
                        int n = 0;
                        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
                            if (r.name.StartsWith("mesh" + p[1] + "_")) { r.enabled = p.Length < 3 || p[2] != "off"; n++; }
                        return "geo " + p[1] + " x" + n;
                    }
                case "cam":
                    {
                        var go = GameObject.Find("Look test camera");
                        if (p.Length > 1 && p[1] == "off") { if (go) Object.Destroy(go); return "camera off"; }
                        if (!go) { go = new GameObject("Look test camera"); var cc = go.AddComponent<Camera>(); cc.depth = 90; cc.fieldOfView = 60; cc.farClipPlane = 800; go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true; }
                        go.transform.position = new Vector3(F(1), F(2), F(3)); go.transform.rotation = Quaternion.Euler(F(5), F(4), 0);
                        return "camera at " + go.transform.position;
                    }
            }
            return Info(vol);
        }

        static string Info(Volume vol)
        {
            var sb = new System.Text.StringBuilder();
            if (vol && vol.sharedProfile) foreach (var c in vol.sharedProfile.components) sb.Append(c.GetType().Name).Append(' ');
            sb.Append("| sun ");
            foreach (var l in Object.FindObjectsByType<Light>()) if (l.isActiveAndEnabled && l.type == LightType.Directional) sb.Append(l.name).Append(' ').Append(l.intensity.ToString("0.00")).Append(' ');
            sb.Append("| ambient ").Append(RenderSettings.ambientSkyColor).Append(" fog ").Append(RenderSettings.fog).Append(' ').Append(RenderSettings.fogMode).Append(' ').Append(RenderSettings.fogColor);
            foreach (var v in Object.FindObjectsByType<Volume>()) sb.Append(" | vol ").Append(v.name).Append(" p").Append(v.priority).Append(v.isGlobal ? " global" : " local").Append(v.enabled ? "" : " off");
            return sb.ToString();
        }
    
        static System.Collections.IEnumerator Sweep(Vector3 at, Quaternion rot)
        {
            var w = MapWorld.I; var vt = w.transform.Find("Map mood"); var vol = vt ? vt.GetComponent<Volume>() : null;
            var go = new GameObject("Look sweep camera"); var cam = go.AddComponent<Camera>(); cam.enabled = false; cam.fieldOfView = 60; cam.farClipPlane = 800;
            var d = go.AddComponent<UniversalAdditionalCameraData>(); d.renderPostProcessing = true;
            var main = Camera.main; if (main) { cam.cullingMask = main.cullingMask; d.volumeLayerMask = main.GetUniversalAdditionalCameraData().volumeLayerMask; }
            go.transform.SetPositionAndRotation(at, rot);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt;
            var beams = new System.Collections.Generic.List<MeshRenderer>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>()) if (r.name.StartsWith("mesh42_")) beams.Add(r);
            Bloom bl = null; float bl0 = 0; bool blOv = false; ColorAdjustments ca = null; float ev0 = 0;
            if (vol && vol.sharedProfile) { if (vol.sharedProfile.TryGet(out bl)) { bl0 = bl.intensity.value; blOv = bl.intensity.overrideState; } vol.sharedProfile.TryGet(out ca); if (ca) ev0 = ca.postExposure.value; }
            var log = new System.Text.StringBuilder(Info(vol)).Append('\n');
            for (int k = 0; k < 5; k++)
            {
                if (k == 1 && vol) vol.enabled = false;
                if (k == 2) { if (vol) vol.enabled = true; foreach (var r in beams) r.enabled = false; }
                if (k == 3) { foreach (var r in beams) r.enabled = true; if (vol && vol.sharedProfile) { if (!bl) bl = vol.sharedProfile.Add<Bloom>(true); bl.intensity.Override(0); } }
                if (k == 4) { if (bl) { bl.intensity.value = bl0; bl.intensity.overrideState = blOv; } if (ca) ca.postExposure.value = 0; }
                yield return null; yield return null;
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = prev;
                float sum = 0; var px = tex.GetPixels(); for (int i = 0; i < px.Length; i += 37) sum += px[i].grayscale; sum /= px.Length / 37f;
                string name = new[] { "asis", "nomood", "nobeams", "nobloom", "noev" }[k];
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath, "../Logs/look-" + name + ".png"), tex.EncodeToPNG());
                log.Append(name).Append(" brightness ").Append(sum.ToString("0.00")).Append('\n');
                Object.Destroy(tex);
            }
            if (ca) ca.postExposure.value = ev0;
            cam.targetTexture = null; rt.Release(); Object.Destroy(rt); Object.Destroy(go);
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/look-sweep.txt"), log.ToString());
        }
}
}