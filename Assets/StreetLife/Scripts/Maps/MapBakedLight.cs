// A map's baked bounce light and its indoor reflections, for maps that have a look pass of their own (Beach Please; Railed
// keeps the same logic inside RailLook):
//  - loads the map's light scene (Adaptive Probe Volumes baked by Editor/RailBake.cs) on top of the game's scene, where the
//    platform and the Graphics setting have probe volumes (not WebGL);
//  - while the baked light is in, turns the cool fill light down (it reaches indoors unshadowed) and back up if it goes;
//  - keeps box-projected reflection probes for rooms (Room), sharpens the map-wide probe, and takes them all again once the
//    baked light has arrived and at each round start.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace StreetLife
{
    public class MapBakedLight : MonoBehaviour
    {
        public float fill = .4f;
        [Tooltip("How much of the baked light is used (0..1): bakes are made a little bright so this can only turn them down")] public float strength = 1f;
        readonly List<ReflectionProbe> probes = new List<ReflectionProbe>();
        readonly List<(Light light, float intensity)> fills = new List<(Light, float)>();
        int wait = 5; bool baked; Volume volume;

        public static bool Available(string scenePath)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            var a = UniversalRenderPipeline.asset;
            return a && a.lightProbeSystem == LightProbeSystem.ProbeVolumes && SceneUtility.GetBuildIndexByScenePath(scenePath) >= 0;
#endif
        }

        public static MapBakedLight Add(GameObject host, string scenePath)
        {
            var b = host.AddComponent<MapBakedLight>();
            if (Available(scenePath)) SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            return b;
        }

        /// a room, by its walls: what a floor or a window reflects indoors is the room, not the sky
        public void Room(string name, Vector3 min, Vector3 max)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            Vector3 c = (min + max) / 2;
            go.transform.position = new Vector3(c.x, min.y + 1.9f, c.z);
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime; p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            p.resolution = Application.isMobilePlatform ? 128 : 512; p.hdr = true;
            p.center = c - go.transform.position; p.size = max - min + new Vector3(.4f, .4f, .4f);
            p.boxProjection = true; p.blendDistance = 1.2f; p.importance = 2;
            p.clearFlags = ReflectionProbeClearFlags.Skybox; p.nearClipPlane = .1f; p.farClipPlane = 300; p.shadowDistance = 50;
            probes.Add(p);
        }

        void Start() { RoundManager.RoundStarted += OnRound; }
        void OnDestroy() { RoundManager.RoundStarted -= OnRound; }
        void OnRound(int r) { wait = 3; }

        void Update()
        {
            var prv = ProbeReferenceVolume.instance; var rp = UniversalRenderPipeline.asset;
            bool on = prv != null && prv.isInitialized && prv.DataHasBeenLoaded() && rp && rp.lightProbeSystem == LightProbeSystem.ProbeVolumes;
            if (on != baked) Switch(on);
            if (wait <= 0 || --wait > 0) return;
            var yard = GameObject.Find("Street reflections");   // WorldLook's probe over the whole map
            var yp = yard ? yard.GetComponent<ReflectionProbe>() : null;
            if (yp) { if (!Application.isMobilePlatform && yp.resolution < 512) yp.resolution = 512; yp.RenderProbe(); }
            foreach (var p in probes) if (p) p.RenderProbe();
        }

        void Switch(bool on)
        {
            baked = on;
            if (!volume)
            {
                var prof = ScriptableObject.CreateInstance<VolumeProfile>(); prof.name = "Baked light";
                prof.Add<ProbeVolumesOptions>(true).intensityMultiplier.Override(Mathf.Clamp01(strength));
                volume = new GameObject("Baked light").AddComponent<Volume>(); volume.transform.SetParent(transform, false);
                volume.isGlobal = true; volume.priority = 6; volume.sharedProfile = prof;
                var sun = RenderSettings.sun;
                foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                    if (l.type == LightType.Directional && l != sun && l.shadows == LightShadows.None) fills.Add((l, l.intensity));
            }
            volume.enabled = on;
            foreach (var f in fills) if (f.light) f.light.intensity = f.intensity * (on ? fill : 1f);
            wait = 4;
            Debug.Log("Map look: baked light " + (on ? "on" : "off"));
        }
    }
}
