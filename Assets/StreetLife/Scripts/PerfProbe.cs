// Numbers for the web-vs-Unity comparison: frames per second (average and 1% low over the last 10 s), frame time,
// when the match became playable, and the device. F8 shows them; in the WebGL build they're also handed to the page
// (window.slPerf) so the side-by-side comparison page can read them.
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Profiling;

namespace StreetLife
{
    public class PerfProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<PerfProbe>()) return;
            var go = new GameObject("PerfProbe"); DontDestroyOnLoad(go); go.AddComponent<PerfProbe>();
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
            // phones default to 30 fps: run at the chosen rate (Settings.Fps, 60 by default), and don't dim mid-match
            if (Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer)
            {
                Settings.ApplyFps();   // the Frame rate setting: 60 / 90 / 120, capped at the screen's rate
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SL_Perf(string json);
        [DllImport("__Internal")] static extern void SL_Ready(float secs);
#else
        static void SL_Perf(string json) { }
        static void SL_Ready(float secs) { }
#endif

        const int N = 600;                       // 10 s at 60 fps
        readonly float[] ft = new float[N]; int n, head;
        float sendT; bool ready;
        public static bool Show;   // F8, the settings screen, or a three-finger tap
        public static float ReadyAt = -1;        // seconds since the engine started, when the first match frame ran
        public static float Avg, Low1, Ms;
        GUIStyle st;
        ProfilerRecorder dc, bt, sp, tri, gc, mem;
        float fileT = 3;

        void OnEnable()
        {
            dc = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            bt = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            sp = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            tri = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            mem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
        }
        void OnDisable() { dc.Dispose(); bt.Dispose(); sp.Dispose(); tri.Dispose(); gc.Dispose(); mem.Dispose(); }
        string RenderStats() => $"{dc.LastValue} draws  {bt.LastValue} batches  {sp.LastValue} set-pass  {tri.LastValue / 1000}k tris  {gc.LastValue / 1024} KB GC/frame  {mem.LastValue / 1048576} MB";

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            ft[head] = dt; head = (head + 1) % N; if (n < N) n++;
            if (!ready && BoxWorld.Loaded && Time.frameCount > 2) { ready = true; ReadyAt = Time.realtimeSinceStartup; SL_Ready(ReadyAt); }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) Show = !Show;
            // phones: a three-finger tap
            if (!UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled) UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
            var ts = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
            if (ts.Count == 3) { bool began = false; foreach (var t in ts) if (t.phase == UnityEngine.InputSystem.TouchPhase.Began) began = true; if (began) Show = !Show; }
            // in the editor, leave the numbers where the Cowork chats can read them
            if (Application.isEditor && (fileT -= dt) <= 0)
            {
                fileT = 5; Calc();
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs", "streetlife-perf.txt"),
                    $"{System.DateTime.Now:HH:mm:ss} {Avg:F0} fps (1% low {Low1:F0}) {Ms:F1} ms  {RenderStats()}  {Screen.width}x{Screen.height}\n"); } catch { }
            }
            sendT -= dt;
            if (sendT <= 0)
            {
                sendT = .5f; Calc();
                SL_Perf("{\"fps\":" + Avg.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"low1\":" + Low1.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"ms\":" + Ms.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"ready\":" + ReadyAt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"w\":" + Screen.width + ",\"h\":" + Screen.height + "}");
            }
        }

        void Calc()
        {
            if (n == 0) return;
            var a = new float[n]; float sum = 0;
            for (int i = 0; i < n; i++) { a[i] = ft[i]; sum += ft[i]; }
            System.Array.Sort(a);
            int k = Mathf.Max(1, n / 100); float worst = 0; for (int i = n - k; i < n; i++) worst += a[i];
            Avg = n / Mathf.Max(1e-4f, sum); Low1 = k / Mathf.Max(1e-4f, worst); Ms = sum / n * 1000f;
        }

        void OnGUI()
        {
            if (!Show) return;
            if (st == null) st = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14 };
            GUI.Box(HudLayout.PerfBox(240, 136),
                $"Unity {Application.unityVersion}  {SystemInfo.graphicsDeviceType}\n" +
                $"{Avg:F0} fps avg   {Low1:F0} fps 1% low\n{Ms:F1} ms a frame (last 10 s)\n" +
                $"playable after {ReadyAt:F1} s (engine start)\n{Screen.width}x{Screen.height}  {SystemInfo.deviceModel}\n{dc.LastValue} draws  {bt.LastValue} batches  {tri.LastValue / 1000}k tris", st);
        }
    }
}
