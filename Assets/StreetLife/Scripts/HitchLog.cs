// Hitch log (match chat; PerfProbe is the F8 overlay). Off unless the game is started with -perf (or HitchLog.On is set): then every frame over
// 50 ms is logged with what the main thread, render thread and GPU spent, the managed memory allocated that frame and
// whether a garbage collection ran, plus a summary every 10 s (average fps, 1% low, worst frame, GC per second).
// -perfrun also starts a match from the menu after 4 s and quits after -perfsecs (default 70) seconds, for unattended runs.
using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace StreetLife
{
    public class HitchLog : MonoBehaviour
    {
        public static bool On;
        static HitchLog I;
        static bool run; static float runSecs = 70;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();
        static void Ensure()
        {
            if (I) return;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i].ToLowerInvariant();
                if (a == "-perf") On = true;
                if (a == "-perfrun") { On = true; run = true; }
                if (a == "-perfsecs" && i + 1 < args.Length) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out runSecs);
            }
            if (!On) return;
            var go = new GameObject("HitchLog"); DontDestroyOnLoad(go); I = go.AddComponent<HitchLog>();
        }

        ProfilerRecorder gcAlloc, mainT, renderT, gcCount;
        readonly FrameTiming[] ft = new FrameTiming[1];
        readonly List<float> win = new List<float>(1200);
        int gcAtWin, hitches; long allocWin; float winStart, t0; bool started;
        double sumMain, sumRender, sumGpu; int nTiming;

        void OnEnable()
        {
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            gcCount = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            mainT = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            renderT = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Render Thread", 1);
            winStart = t0 = Time.realtimeSinceStartup; gcAtWin = GC.CollectionCount(0);
            Debug.Log($"[perf] on: {Screen.width}x{Screen.height} {Screen.fullScreenMode}, vSync {QualitySettings.vSyncCount}, target {Application.targetFrameRate}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, {SystemInfo.graphicsDeviceName} {SystemInfo.graphicsDeviceType}, recorders gc {gcAlloc.Valid} main {mainT.Valid} render {renderT.Valid}");
        }
        void OnDisable() { gcAlloc.Dispose(); gcCount.Dispose(); mainT.Dispose(); renderT.Dispose(); }

        static string State()
        {
            var rm = RoundManager.Instance;
            return (MainMenu.Open ? "menu" : rm ? rm.state.ToString() : "-");
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup, dt = Time.unscaledDeltaTime * 1000f;
            if (run && !started && now - t0 > 4)
            {
                started = true;
                var mm = FindAnyObjectByType<MainMenu>();
                var play = typeof(MainMenu).GetMethod("Play", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (mm && play != null) play.Invoke(mm, null);
                Debug.Log("[perf] match started");
            }
            if (run && now - t0 > runSecs) { Report(now, true); Application.Quit(); run = false; }

            FrameTimingManager.CaptureFrameTimings();
            double fm = -1, fr = -1, fg = -1;
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0) { fm = ft[0].cpuMainThreadFrameTime; fr = ft[0].cpuRenderThreadFrameTime; fg = ft[0].gpuFrameTime; sumMain += fm; sumRender += fr; sumGpu += fg; nTiming++; }
            long alloc = gcAlloc.Valid ? gcAlloc.LastValue : 0; allocWin += alloc;
            win.Add(dt);
            if (dt > 50 && now - t0 > 3)
            {
                hitches++;
                Debug.Log($"[perf] hitch {dt:F0} ms at {now - t0:F1}s [{State()}] main {fm:F1} render {fr:F1} gpu {fg:F1} ms, alloc {alloc / 1024} KB, gc runs {GC.CollectionCount(0)}, heap {GC.GetTotalMemory(false) / 1048576} MB");
            }
            if (now - winStart >= 10) Report(now, false);
        }

        void Report(float now, bool final)
        {
            if (win.Count == 0) return;
            float secs = now - winStart; var s = new List<float>(win); s.Sort();
            float avg = 0; foreach (var v in s) avg += v; avg /= s.Count;
            float p99 = s[Mathf.Min(s.Count - 1, (int)(s.Count * .99f))], worst = s[s.Count - 1];
            int gcs = GC.CollectionCount(0) - gcAtWin;
            Debug.Log($"[perf] {(final ? "final" : "10s")} [{State()}] {1000 / avg:F0} fps avg, 1% low {1000 / p99:F0} fps, worst {worst:F0} ms, hitches>50ms {hitches}, GC {gcs} runs ({gcs / secs:F1}/s), alloc {allocWin / secs / 1048576:F1} MB/s, main {(nTiming > 0 ? sumMain / nTiming : -1):F1} render {(nTiming > 0 ? sumRender / nTiming : -1):F1} gpu {(nTiming > 0 ? sumGpu / nTiming : -1):F1} ms, heap {GC.GetTotalMemory(false) / 1048576} MB");
            win.Clear(); winStart = now; gcAtWin = GC.CollectionCount(0); hitches = 0; allocWin = 0; sumMain = sumRender = sumGpu = 0; nTiming = 0;
        }
    }
}
