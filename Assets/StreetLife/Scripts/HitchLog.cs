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
        /// an unattended -perfrun: nothing is saved to the player's profile and the game never pauses itself
        public static bool Unattended => run;

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
        // where a long frame went, by the engine's own player-loop markers (those a release player reports)
        static readonly string[] Markers = {
            "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate",
            "FixedUpdate.PhysicsFixedUpdate", "Update.ScriptRunDelayedDynamicFrameRate", "PostLateUpdate.FinishFrameRendering",
            "PostLateUpdate.PresentAfterDraw", "PostLateUpdate.UpdateAllRenderers", "PostLateUpdate.PlayerSendFrameComplete",
            "PreUpdate.AIUpdate", "Update.DirectorUpdate", "PostLateUpdate.UpdateAudio", "PreLateUpdate.DirectorUpdateAnimationBegin",
            "Initialization.PlayerUpdateTime", "EarlyUpdate.UpdatePreloading", "EarlyUpdate.UpdateTextureStreamingManager",
            "PostLateUpdate.UpdateCustomRenderTextures", "GC.Collect", "Loading.ReadObject", "Loading.AwakeFromLoad",
            "Shader.CreateGPUProgram", "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread", "Inl_RenderCameraStack",
            "OnGUI", "GUI.Repaint", "UIElements.UpdatePanels", "UIR.DrawChain" };
        readonly List<ProfilerRecorder> mrec = new List<ProfilerRecorder>();
        readonly FrameTiming[] ft = new FrameTiming[1], ft8 = new FrameTiming[8];
        int lookBack;   // frames left to look back for a hitch's own timings (FrameTimingManager reports them a few frames late)
        float lookBackDt;
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
            foreach (var m in Markers) { var r = ProfilerRecorder.StartNew(new ProfilerCategory("Scripts"), m, 1); if (!r.Valid) { r.Dispose(); r = ProfilerRecorder.StartNew(ProfilerCategory.Internal, m, 1); } mrec.Add(r); }
            Debug.Log($"[perf] on: {Screen.width}x{Screen.height} {Screen.fullScreenMode}, vSync {QualitySettings.vSyncCount}, target {Application.targetFrameRate}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, {SystemInfo.graphicsDeviceName} {SystemInfo.graphicsDeviceType}, recorders gc {gcAlloc.Valid} main {mainT.Valid} render {renderT.Valid}");
        }
        void OnDisable() { gcAlloc.Dispose(); gcCount.Dispose(); mainT.Dispose(); renderT.Dispose(); foreach (var r in mrec) r.Dispose(); mrec.Clear(); Unhook(); }

        // ---- events, so a hitch can be matched to what was happening (progression chat, 15 Nov)
        bool hooked;
        void Start() { Hook(); }
        void Hook()
        {
            if (hooked) return; hooked = true;
            Fighter.Killed += OnKilled; RoundManager.RoundStarted += OnRoundStart; RoundManager.RoundEnded += OnRoundEnd;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnScene;
        }
        void Unhook()
        {
            if (!hooked) return; hooked = false;
            Fighter.Killed -= OnKilled; RoundManager.RoundStarted -= OnRoundStart; RoundManager.RoundEnded -= OnRoundEnd;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnScene;
        }
        void Ev(string what) => Debug.Log($"[perf] event {what} at {Time.realtimeSinceStartup - t0:F1}s [{State()}]");
        void OnKilled(Fighter v, Fighter k, bool head) => Ev($"kill {(k ? k.displayName : "-")} > {(v ? v.displayName : "-")}");
        void OnRoundStart(int r) => Ev("round " + r + " start");
        void OnRoundEnd(int r, int w) => Ev("round " + r + " end");
        void OnScene(UnityEngine.SceneManagement.Scene sc, UnityEngine.SceneManagement.LoadSceneMode m) => Ev("scene loaded");
        void OnApplicationFocus(bool f) => Ev(f ? "focus gained" : "focus LOST");
        void OnApplicationPause(bool p) => Ev(p ? "paused by the OS" : "resumed by the OS");

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
            // a few frames after a big hitch its own timings arrive: say where the time went (main thread work, waiting to
            // present, render thread, GPU), so a stall outside the game's own work shows up as such
            if (lookBack > 0 && --lookBack == 0)
            {
                uint n = FrameTimingManager.GetLatestTimings(8, ft8); int best = -1;
                for (int i = 0; i < n; i++) if (best < 0 || ft8[i].cpuFrameTime > ft8[best].cpuFrameTime) best = i;
                if (best >= 0) { var b = ft8[best]; Debug.Log($"[perf]   that {lookBackDt:F0} ms frame: cpu {b.cpuFrameTime:F0} ms = main {b.cpuMainThreadFrameTime:F0} (of it waiting to present {b.cpuMainThreadPresentWaitTime:F0}), render {b.cpuRenderThreadFrameTime:F0}, gpu {b.gpuFrameTime:F0} ms"); }
            }
            if (dt > 50 && now - t0 > 3)
            {
                hitches++;
                if (dt > 250) { lookBack = 5; lookBackDt = dt; LogMarkers(dt); }
                Debug.Log($"[perf] hitch {dt:F0} ms at {now - t0:F1}s [{State()}]{(Application.isFocused ? "" : " UNFOCUSED")} mainrec {(mainT.Valid ? mainT.LastValue / 1e6 : -1):F1} main {fm:F1} render {fr:F1} gpu {fg:F1} ms, alloc {alloc / 1024} KB, gc runs {GC.CollectionCount(0)}, heap {GC.GetTotalMemory(false) / 1048576} MB");
            }
            if (now - winStart >= 10) Report(now, false);
        }

        void LogMarkers(float dt)
        {
            var sb = new System.Text.StringBuilder(); int nValid = 0;
            for (int i = 0; i < mrec.Count; i++)
            {
                var r = mrec[i]; if (!r.Valid) continue; nValid++;
                double ms = r.LastValue / 1e6; if (ms >= 15) sb.Append($" {Markers[i]} {ms:F0}");
            }
            Debug.Log($"[perf]   markers ({nValid} of {Markers.Length} readable) over 15 ms:{(sb.Length > 0 ? sb.ToString() : " none")}");
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
