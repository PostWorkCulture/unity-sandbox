// Progression (progression chat): editor menu for testing progress. Package install for the online services (P3)
// will also live here.
//   Street Life > Progression > Add 100 XP (play mode) / Reset progress / Show save file
using UnityEditor;
using UnityEngine;

namespace StreetLife
{
    public static class ProgressionSetup
    {
        [MenuItem("Street Life/Progression/Add 100 XP (play mode)")]
        static void AddXp()
        {
            if (!Application.isPlaying) { Debug.Log("Progression: enter play mode first"); return; }
            Progress.AddXp(100, "editor test"); Progress.Save();
            Debug.Log($"Progression: now level {Progress.Level} ({Progress.Data.xp} XP)");
        }

        [MenuItem("Street Life/Progression/Reset progress")]
        static void Reset()
        {
            if (!EditorUtility.DisplayDialog("Reset progress", "Wipe the saved XP, record, stats and unlocks on this computer?", "Reset", "Cancel")) return;
            Progress.ResetAll();
        }

        [MenuItem("Street Life/Progression/Show save file")]
        static void Show() => EditorUtility.RevealInFinder(System.IO.Path.Combine(Application.persistentDataPath, "streetlife-progress.json"));
    }
}
