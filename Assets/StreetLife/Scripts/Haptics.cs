// Short vibrations for phones (mobile pass, 13 Nov): hits you land, hits you take, kills, going down.
// Android uses the Vibrator service with a strength (API 26+); iOS only has Handheld.Vibrate, kept for the long ones.
using UnityEngine;

namespace StreetLife
{
    public static class Haptics
    {
        public static bool On = true;
        static float last = -1;
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vib; static int sdk; static bool init;
#endif

        /// ms: length, amp: strength 1..255 (Android 8+)
        public static void Pulse(int ms, int amp = 160)
        {
            if (!On || Time.unscaledTime - last < .05f) return;
            last = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!init)
                {
                    init = true;
                    using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                        vib = act.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    using (var v = new AndroidJavaClass("android.os.Build$VERSION")) sdk = v.GetStatic<int>("SDK_INT");
                }
                if (vib == null) return;
                if (sdk >= 26)
                {
                    using (var ve = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (var e = ve.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, Mathf.Clamp(amp, 1, 255)))
                        vib.Call("vibrate", e);
                }
                else vib.Call("vibrate", (long)ms);
            }
            catch { vib = null; }
#elif UNITY_IOS && !UNITY_EDITOR
            if (ms >= 40) Handheld.Vibrate();
#endif
        }

#if UNITY_ANDROID || UNITY_IOS
        // never called: referencing Handheld.Vibrate makes Unity add the VIBRATE permission to the Android build
        static void KeepVibratePermission() { Handheld.Vibrate(); }
#endif
    }
}
