// Diagnostic helper (15 Nov): time a method and log it when it takes over 100 ms, only when the game runs with -perf (HitchLog).
//   void Update() { long t = SlowWatch.T(); Work(); SlowWatch.Done(t, "Name"); }
// It found the wall-slot freezes (BoxWorld.Set) by wrapping every Update for one run; nothing uses it by default.
namespace StreetLife
{
    public static class SlowWatch
    {
        public static long T() => HitchLog.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        public static void Done(long t, string who)
        {
            if (t == 0) return;
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms > 100) UnityEngine.Debug.Log($"[perf]   slow Update: {who} {ms:F0} ms at {UnityEngine.Time.realtimeSinceStartup:F1}s");
        }
    }
}
