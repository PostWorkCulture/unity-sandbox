// Auto graphics and frame rate (Settings: Graphics "auto", phone Frame rate 0 = Auto, desktop V-sync "auto").
//   Graphics: starts from the hardware (phones, browsers and PCs with under 3 GB of video memory on Smooth, the rest on
//   High), then watches the frame rate in 3 s windows. Two slow windows in a row on High drop to Smooth; after a long run
//   of easy headroom on Smooth it tries High again, at most twice a session, so it never flickers between them.
//   Frame rate: phones aim at the screen's own rate (up to 120) and step down 120 > 90 > 60 when it can't hold;
//   PCs: v-sync stays OFF and the frame rate is capped instead, starting at the monitor's rate (at most 144) and stepping
//   down 120 > 90 > 60 when it can't hold. (15 Nov: on Pete's Nitro laptop, v-sync in a borderless window made every
//   present wait ~250 ms: 4-8 fps until Auto happened to cap the rate, when it ran at a steady 60.) On a PC the frame rate
//   steps down first and the graphics drop to Smooth only if even 60 can't hold; on phones graphics drop first.
//   A graphics change rebuilds the renderer (a visible hitch), so during a fight it waits for the round to end.
using UnityEngine;

namespace StreetLife
{
    public class AutoQuality : MonoBehaviour
    {
        static AutoQuality I;
        // decisions survive scene reloads (a new map), so the game doesn't relearn every match
        static string level;            // "PC" or "Mobile" while Graphics is Auto
        static int phoneTarget;         // phones' frame rate while Frame rate is Auto
        static int pcCap;               // PCs: 0 = v-sync at the monitor's rate, else a capped rate (v-sync off)
        static int ups;                 // tries back up to High this session
        static float settleUntil;
        static string pendingLevel;     // a graphics change waiting for the fight to end

        public static bool GraphicsAuto => Settings.Graphics == "auto";
        public static bool FpsAuto => Settings.IsDesktop ? Settings.VSyncAuto : Settings.Fps <= 0;
        public static int Refresh => Mathf.Clamp(Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value), 30, 360);

        /// the quality level Auto uses now
        public static string Level { get { if (level == null) level = HardwareLevel(); return level; } }
        static string HardwareLevel()
        {
            if (Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer) return "Mobile";
            return SystemInfo.graphicsMemorySize >= 3000 ? "PC" : "Mobile";
        }
        public static int PhoneTarget { get { if (phoneTarget <= 0) phoneTarget = Mathf.Min(120, Mathf.Max(60, Refresh)); return phoneTarget; } }
        public static int PcCap => pcCap;
        /// PCs on Auto: the first cap, the monitor's rate but no more than 144
        public static int PcStart => Mathf.Min(Refresh, 144);

        /// the frame rate everything aims at right now (for the decisions and the Settings line)
        public static int Target
        {
            get
            {
                if (Application.platform == RuntimePlatform.WebGLPlayer) return 60;
                if (Application.isMobilePlatform) return Settings.Fps > 0 ? Mathf.Min(Settings.Fps, Mathf.Max(60, Refresh)) : PhoneTarget;
                if (Settings.VSyncAuto) return pcCap > 0 ? pcCap : PcStart;
                if (Settings.VSync) return Refresh;
                return Settings.FpsCap > 0 ? Settings.FpsCap : Refresh;
            }
        }

        /// what Auto is doing, for the Settings screen
        public static string Describe()
        {
            string g = (GraphicsAuto ? Level : QualitySettings.names[QualitySettings.GetQualityLevel()]) == "PC" ? "High" : "Smooth";
            return "Now: " + g + " graphics, " + Target + " fps target" + (fps > 0 ? ", running at " + Mathf.RoundToInt(fps) : "");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            I = new GameObject("Auto quality") { hideFlags = HideFlags.DontSave }.AddComponent<AutoQuality>();
            DontDestroyOnLoad(I.gameObject);
        }

        static float fps;
        float winT, winFrames; int slow, easy;

        void OnEnable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnLoad; settleUntil = Time.realtimeSinceStartup + 4; }
        void OnDisable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnLoad; }
        void OnLoad(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) { settleUntil = Time.realtimeSinceStartup + 4; winT = winFrames = 0; slow = easy = 0; }

        void Update()
        {
            if (!GraphicsAuto && !FpsAuto) { fps = 0; return; }
            if (Time.realtimeSinceStartup < settleUntil || !Application.isFocused) { winT = winFrames = 0; return; }
            winT += Time.unscaledDeltaTime; winFrames++;
            if (winT < 3f) return;
            fps = winFrames / winT; winT = winFrames = 0;
            int target = Target;
            if (fps < target * .88f) { slow++; easy = 0; } else if (fps > target * .97f) { easy++; slow = 0; } else { slow = 0; easy = 0; }

            if (pendingLevel != null && !InFight) { level = pendingLevel; pendingLevel = null; Changed(level == "PC" ? "graphics to High (waited for the fight to end)" : "graphics to Smooth (waited for the fight to end)"); return; }
            if (slow >= 2)
            {
                slow = 0;
                bool pc = Settings.IsDesktop;
                if (pc && FpsAuto && Target > 60) { StepFpsDown(); return; }   // PCs: a lower cap first, the graphics stay
                if (GraphicsAuto && Level == "PC" && pendingLevel == null) { SetLevel("Mobile"); return; }
                if (FpsAuto) StepFpsDown();
                return;
            }
            // headroom on Smooth (only measurable when the frame rate isn't pinned at the target, i.e. v-sync off on a PC)
            if (GraphicsAuto && Level == "Mobile" && ups < 2 && !Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer
                && SystemInfo.graphicsMemorySize >= 3000 && easy >= 6)
            { easy = 0; ups++; SetLevel("PC"); }
        }

        static bool InFight { get { var rm = RoundManager.Instance; return rm && rm.state == RoundManager.State.Fight && !MainMenu.Open; } }

        /// a graphics change: now, or (mid-fight) when the round ends
        static void SetLevel(string to)
        {
            if (InFight) { pendingLevel = to; Debug.Log("[auto quality] graphics to " + (to == "PC" ? "High" : "Smooth") + " after this fight (running at " + Mathf.RoundToInt(fps) + " fps)"); return; }
            level = to; Changed(to == "PC" ? "graphics back to High (try " + ups + ")" : "graphics to Smooth");
        }

        static void StepFpsDown()
        {
            if (Application.isMobilePlatform)
            {
                int t = PhoneTarget; int next = t > 90 ? 90 : 60;
                if (next < t) { phoneTarget = next; Changed("frame rate to " + next); }
                return;
            }
            if (!Settings.IsDesktop) return;
            int cur = pcCap > 0 ? pcCap : PcStart;
            foreach (var r in new[] { 144, 120, 90, 60 })
                if (r < cur && r <= fps * 1.05f + 1) { pcCap = r; Changed("frame rate capped at " + r); return; }
            if (cur > 60) { pcCap = 60; Changed("frame rate capped at 60"); }
        }

        static void Changed(string what)
        {
            Debug.Log("[auto quality] " + what + " (running at " + Mathf.RoundToInt(fps) + " fps)");
            settleUntil = Time.realtimeSinceStartup + 4;
            Settings.Apply();
        }
    }
}
