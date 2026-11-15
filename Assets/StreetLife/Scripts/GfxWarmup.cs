// Graphics warm-up (match chat). On D3D12 the first time any material or effect is drawn the driver has to build its
// pipeline state, which froze the game for 0.2-0.6 s mid-fight and ~3 s at the start of a match
// (claude/street-life-performance.md). Unity 6's GraphicsStateCollection fixes that: a recorded list of every pipeline
// state the game used, replayed ahead of time.
//   Playing: StreamingAssets/Warmup/<API>-<quality>.graphicsstate is loaded at boot and warmed a little each frame while
//   the menu is up (it stays responsive); if a match starts first, the rest is finished in one go behind the intro.
//   Recording: start the game with -gfxtrace (optionally -map <id> for one run on that map); every state it draws is
//   recorded and, on quit, merged into Logs/Warmup/<API>-<quality>.graphicsstate next to the project, which
//   StreetLifeBuild copies into StreamingAssets. Run it on each map with HitchLog's -perfrun to cover the effects.
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetLife
{
    public class GfxWarmup : MonoBehaviour
    {
        static GfxWarmup I;
        static bool tracing; static string traceOut, mapWas;
        GraphicsStateCollection play, trace;
        string playLevel; float startT; bool logged;

        public static string FileName => SystemInfo.graphicsDeviceType + "-" + QualitySettings.names[QualitySettings.GetQualityLevel()] + ".graphicsstate";
        public static bool Done => !I || I.play == null || I.play.isWarmedUp;

        // the map for a recording run is set before the scene builds
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Args()
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == "-gfxtrace") tracing = true;
                if (a[i] == "-gfxtraceout" && i + 1 < a.Length) traceOut = a[i + 1];
                if (a[i] == "-map" && i + 1 < a.Length && mapWas == null) { mapWas = MapSet.Choice; MapSet.Choice = a[i + 1]; }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I || !Application.isPlaying) return;
            var go = new GameObject("GfxWarmup"); DontDestroyOnLoad(go); I = go.AddComponent<GfxWarmup>();
        }

        void Start()
        {
            if (tracing)
            {
                trace = new GraphicsStateCollection();
                bool ok = trace.BeginTrace();
                Debug.Log("[warmup] recording pipeline states: " + ok + " (" + FileName + ")");
                return;
            }
            Load();
        }

        void Load()
        {
            playLevel = FileName;
            var path = Path.Combine(Application.streamingAssetsPath, "Warmup", playLevel);
            if (!File.Exists(path)) { play = null; Debug.Log("[warmup] no recording for " + playLevel); return; }
            play = new GraphicsStateCollection();
            if (!play.LoadFromFile(path)) { Debug.LogWarning("[warmup] couldn't read " + path); play = null; return; }
            startT = Time.realtimeSinceStartup; logged = false;
            Debug.Log($"[warmup] {play.totalGraphicsStateCount} pipeline states to prepare ({playLevel})");
        }

        float saveT = 15;
        void Update()
        {
            if (tracing)
            {   // saved every 15 s (at quit the recording has already been torn down in a player)
                if (trace != null && Time.realtimeSinceStartup > saveT) { saveT = Time.realtimeSinceStartup + 15; SaveTrace(); trace = new GraphicsStateCollection(); trace.BeginTrace(); }
                return;
            }
            if (playLevel != null && playLevel != FileName) Load();   // the graphics level changed: its own recording
            if (play == null || play.isWarmedUp) { Report(); return; }
            var rm = RoundManager.Instance;
            bool inMenu = MainMenu.Open || !rm || rm.state == RoundManager.State.Intro;
            if (inMenu && MainMenu.Open) play.WarmUpProgressively(24).Complete();   // a few a frame while the menu is up
            else play.WarmUp().Complete();                                            // a match is starting: the rest now, behind the intro
        }

        void Report()
        {
            if (logged || play == null) return; logged = true;
            Debug.Log($"[warmup] {play.completedWarmupCount}/{play.totalGraphicsStateCount} ready in {Time.realtimeSinceStartup - startT:F1} s");
        }

        static GraphicsStateCollection testTrace;
        /// remote test: call GfxWarmup.Test begin|end|state
        public static string Test(string a)
        {
            a = (a ?? "").Trim();
            if (a == "begin") { testTrace = new GraphicsStateCollection(); return "begin " + testTrace.BeginTrace() + " tracing " + testTrace.isTracing; }
            if (a == "end" && testTrace != null) { testTrace.EndTrace(); return $"end: {testTrace.totalGraphicsStateCount} states, {testTrace.variantCount} variants, {SystemInfo.graphicsDeviceType}"; }
            if (a == "count" && testTrace != null) return $"{testTrace.totalGraphicsStateCount} states, {testTrace.variantCount} variants, tracing {testTrace.isTracing}";
            return I && I.play != null ? $"play {I.play.completedWarmupCount}/{I.play.totalGraphicsStateCount}" : "no play collection";
        }

        void OnApplicationQuit()
        {
            if (mapWas != null) MapSet.Choice = mapWas;   // a -map run doesn't change the player's pick
            if (!tracing || trace == null) return;
            SaveTrace();
        }

        void SaveTrace()
        {
            trace.EndTrace(); int got = trace.totalGraphicsStateCount;
            var dir = traceOut ?? Path.Combine(Path.GetDirectoryName(Application.dataPath), "Warmup");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, FileName);
            if (File.Exists(file)) { var old = new GraphicsStateCollection(); if (old.LoadFromFile(file)) { old.Append(trace); trace = old; } }
            bool ok = trace.SaveToFile(file);
            Debug.Log($"[warmup] recorded {got}, saved {trace.totalGraphicsStateCount} states ({trace.variantCount} variants) to {file}: {ok}");
        }
    }
}
