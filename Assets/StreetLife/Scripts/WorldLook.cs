// Runtime look (match chat): one reflection probe over the whole street, rendered once when play starts (after the map,
// the breakables and the characters are built), so glass, car paint and polished floors reflect the street and sky
// instead of nothing. Re-rendered at each round start (cheap: one capture, sliced over a few frames).
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetLife
{
    public class WorldLook : MonoBehaviour
    {
        ReflectionProbe probe; int wait = 3;

        public static void Ensure(Transform parent)
        {
            if (!Application.isPlaying || FindAnyObjectByType<WorldLook>()) return;
            var go = new GameObject("WorldLook"); go.transform.SetParent(parent, false);
            go.AddComponent<WorldLook>();
        }

        void Start()
        {
            var bd = BoxWorld.Bounds;
            var c = new Vector3((bd.x0 + bd.x1) / 2, 6f, (bd.z0 + bd.z1) / 2);
            var go = new GameObject("Street reflections"); go.transform.SetParent(transform, false); go.transform.position = c;
            probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = Application.isMobilePlatform ? 128 : 256;
            probe.hdr = true;
            probe.size = new Vector3(bd.x1 - bd.x0 + 60, 120, bd.z1 - bd.z0 + 60);
            probe.boxProjection = false;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.nearClipPlane = .3f; probe.farClipPlane = 400;
            probe.shadowDistance = 60;
            probe.importance = 1;
            RoundManager.RoundStarted += OnRound;
        }

        void OnDestroy() { RoundManager.RoundStarted -= OnRound; }
        void OnRound(int r) { wait = 2; }

        void Update()
        {
            if (wait > 0 && --wait == 0 && probe) probe.RenderProbe();
        }
    }
}
