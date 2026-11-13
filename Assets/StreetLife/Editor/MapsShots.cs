// Maps chat: a hands-off lighting check. Plays every map on the PC and Mobile quality levels and saves two screenshots of each
// (the character close-up from the menu, then the first seconds of the match) to Workspace/Renders/lighting/, with a light log.
// Start it from Street Life > Maps > Lighting shots, or by creating Workspace/Misc/lighting-shots.go (picked up within a second,
// so it works without touching the editor). Leaves play mode when done and restores the Graphics setting.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    static class MapsShots
    {
        const string Trigger = "Workspace/Misc/lighting-shots.go", Out = "Workspace/Renders/lighting";
        const string Key = "StreetLife.MapsShots.queue";   // SessionState survives the domain reload on entering play mode

        static double next, wait;
        static int phase;
        static string only = "";   // trigger file contents: "street" or "pc:street,lagoon" limits the run

        static MapsShots() { EditorApplication.update += Tick; }

        [MenuItem("Street Life/Maps/Lighting shots")]
        static void Begin()
        {
            var set = MapSet.Load(); if (!set) return;
            var q = new List<string>();
            string gsel = only.StartsWith("pc:") ? "high" : only.StartsWith("mobile:") ? "smooth" : "";
            string msel = only.Contains(":") ? only.Substring(only.IndexOf(':') + 1) : only;
            foreach (var g in new[] { "high", "smooth" }) foreach (var m in set.maps)
                if (m != null && (m.IsStreet || (m.mapJson && m.library)) && (gsel == "" || gsel == g) && (msel == "" || Array.IndexOf(msel.Split(','), m.id) >= 0)) q.Add(g + ":" + m.id);
            SessionState.SetString(Key, string.Join(",", q));
            SessionState.SetString(Key + ".gfx", Settings.Graphics);
            SessionState.SetInt(Key + ".phase", 0);
            Directory.CreateDirectory(Out);
            File.WriteAllText(Out + "/log.txt", "Lighting shots " + DateTime.Now + "\n");
            if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < next) return;
            next = now + .25;
            if (File.Exists(Trigger) && !EditorApplication.isCompiling) { only = File.ReadAllText(Trigger).Trim(); File.Delete(Trigger); Begin(); only = ""; return; }
            var queue = SessionState.GetString(Key, "");
            if (queue == "" || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (now < wait) return;
            var items = queue.Split(',');
            var cur = items[0].Split(':'); string gfx = cur[0], map = cur[1];
            phase = SessionState.GetInt(Key + ".phase", 0);
            var menu = UnityEngine.Object.FindAnyObjectByType<MainMenu>();
            try
            {
                switch (phase)
                {
                    case 0:   // set quality, load the map with the menu up
                        Settings.Graphics = gfx; Settings.Apply();
                        MapSet.Choice = map;
                        var sc = SceneManager.GetActiveScene();
                        if (sc.buildIndex >= 0) SceneManager.LoadScene(sc.buildIndex); else SceneManager.LoadScene(sc.name);
                        Step(1, 5); break;
                    case 1:   // character close-up
                        if (!menu) { wait = now + 1; return; }
                        Call(menu, "SetStep", 1);
                        Step(2, 3); break;
                    case 2:
                        Shot(gfx, map, "char");
                        Step(4, 1); break;          // the capture happens at the end of the frame: start the match a tick later
                    case 4:
                        if (!menu) { wait = now + 1; return; }
                        Call(menu, "Play");
                        Step(3, 4); break;
                    case 3:
                        Shot(gfx, map, "play");
                        SessionState.SetString(Key, string.Join(",", items, 1, items.Length - 1));
                        Step(0, .5);
                        if (items.Length == 1) Finish();
                        break;
                }
            }
            catch (Exception e) { Debug.LogException(e); Finish(); }
        }

        static void Step(int p, double secs) { SessionState.SetInt(Key + ".phase", p); wait = EditorApplication.timeSinceStartup + secs; }

        static void Call(object o, string name, params object[] args)
        {
            var m = o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) throw new Exception("MainMenu." + name + " not found");
            m.Invoke(o, args);
        }

        static void Shot(string gfx, string map, string what)
        {
            string file = Out + "/" + map + "-" + (gfx == "high" ? "pc" : "mobile") + "-" + what + ".png";
            ScreenCapture.CaptureScreenshot(file);
            var sb = new StringBuilder();
            sb.Append(Path.GetFileName(file)).Append("  quality=").Append(QualitySettings.names[QualitySettings.GetQualityLevel()]).Append("  map=").Append(MapSet.Current);
            sb.Append("  ambient=").Append(RenderSettings.ambientMode).Append(" sky=").Append(RenderSettings.ambientSkyColor).Append(" eq=").Append(RenderSettings.ambientEquatorColor)
              .Append(" gnd=").Append(RenderSettings.ambientGroundColor).Append(" probe0=").Append(RenderSettings.ambientProbe[0, 0].ToString("0.00")).Append(",").Append(RenderSettings.ambientProbe[1, 0].ToString("0.00")).Append(",").Append(RenderSettings.ambientProbe[2, 0].ToString("0.00"));
            sb.Append("  refl=").Append(RenderSettings.defaultReflectionMode).Append(" x").Append(RenderSettings.reflectionIntensity.ToString("0.00"));
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>())
                if (l.isActiveAndEnabled && l.type == LightType.Directional) sb.Append("\n    dir '").Append(l.name).Append("' ").Append(l.color).Append(" i=").Append(l.intensity.ToString("0.00")).Append(" shadows=").Append(l.shadows).Append(" rot=").Append(l.transform.eulerAngles);
            foreach (var p in UnityEngine.Object.FindObjectsByType<ReflectionProbe>()) sb.Append("\n    probe '").Append(p.name).Append("' mode=").Append(p.mode).Append(" i=").Append(p.intensity.ToString("0.00")).Append(" tex=").Append(p.texture ? "yes" : "none");
            if (what == "char")
                foreach (var rig in UnityEngine.Object.FindObjectsByType<CharacterRig>())
                {
                    var f = rig.GetComponentInParent<Fighter>(); if (f && !f.GetComponent<PlayerController>()) continue;
                    foreach (var r in rig.GetComponentsInChildren<Renderer>())
                    {
                        if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                        sb.Append("\n    rig ").Append(r.name).Append(" ").Append(r.GetType().Name).Append(" b=").Append(r.bounds.center.ToString("0.00")).Append(" size=").Append(r.bounds.size.ToString("0.00"));
                        foreach (var m in r.sharedMaterials)
                        {
                            if (!m) continue;
                            sb.Append("\n       ").Append(m.name).Append(" [").Append(m.shader.name).Append("] q=").Append(m.renderQueue);
                            if (m.HasProperty("_BaseColor")) sb.Append(" base=").Append(m.GetColor("_BaseColor"));
                            if (m.HasProperty("_Surface")) sb.Append(" surf=").Append(m.GetFloat("_Surface"));
                            if (m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor")) sb.Append(" emis=").Append(m.GetColor("_EmissionColor")).Append(" emap=").Append(m.GetTexture("_EmissionMap") ? m.GetTexture("_EmissionMap").name : "none");
                            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap")) sb.Append(" map=").Append(m.GetTexture("_BaseMap").name);
                        }
                    }
                }
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>())
                if (l.isActiveAndEnabled && l.type != LightType.Directional) sb.Append("\n    light '").Append(l.name).Append("' ").Append(l.type).Append(" i=").Append(l.intensity.ToString("0.0")).Append(" range=").Append(l.range.ToString("0.0")).Append(" at ").Append(l.transform.position.ToString("0.0"));
            File.AppendAllText(Out + "/log.txt", sb.ToString() + "\n");
        }

        static void Finish()
        {
            SessionState.EraseString(Key);
            var g = SessionState.GetString(Key + ".gfx", ""); if (g != "") Settings.Graphics = g;
            File.AppendAllText(Out + "/log.txt", "done " + DateTime.Now + "\n");
            EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
        }
    }
}
