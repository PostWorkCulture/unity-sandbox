// Maps chat (Pete, 13 Nov: "feels like I'm playing mobile graphics on a desktop"): the PC quality level was excluded from Android
// and WebGL, so with the editor's build target on Android (for the S24 builds) the editor and the main menu's "High" graphics
// setting could only ever get the Mobile level. This makes PC available on every platform (phones still default to Mobile: the
// menu's Graphics setting defaults to Smooth on phones and High elsewhere), and turns realtime reflection probes on in both
// levels, because WorldLook's street reflections are a realtime probe rendered once per round (without it, glossy surfaces and
// characters lose their reflected light and go dark). Runs once per version; menu: Street Life > Maps > Re-apply quality levels.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    static class MapsQuality
    {
        const int Version = 1;
        const string Stamp = "Assets/StreetLife/Settings/maps-quality-version.txt";

        static MapsQuality() { EditorApplication.delayCall += Auto; }

        static void Auto()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Auto; return; }
            if (File.Exists(Stamp) && File.ReadAllText(Stamp).Trim() == Version.ToString()) return;
            Apply();
        }

        [MenuItem("Street Life/Maps/Re-apply quality levels")]
        public static void Apply()
        {
            var all = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (all == null || all.Length == 0) { Debug.LogWarning("Street Life maps: QualitySettings.asset not found"); return; }
            var so = new SerializedObject(all[0]);
            var levels = so.FindProperty("m_QualitySettings");
            int changed = 0;
            for (int i = 0; i < levels.arraySize; i++)
            {
                var lv = levels.GetArrayElementAtIndex(i);
                var rp = lv.FindPropertyRelative("realtimeReflectionProbes");
                if (rp != null && !rp.boolValue) { rp.boolValue = true; changed++; }
                if (lv.FindPropertyRelative("name").stringValue != "PC") continue;
                var ex = lv.FindPropertyRelative("excludedTargetPlatforms");
                if (ex == null) continue;
                for (int k = ex.arraySize - 1; k >= 0; k--)
                {
                    var v = ex.GetArrayElementAtIndex(k).stringValue;
                    if (v == "Android" || v == "iPhone" || v == "WebGL") { ex.DeleteArrayElementAtIndex(k); changed++; }
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(Stamp));
            File.WriteAllText(Stamp, Version.ToString());
            AssetDatabase.ImportAsset(Stamp);
            Debug.Log("Street Life maps: quality levels updated (" + changed + " changes): PC available on every platform, realtime reflection probes on.");
        }
    }
}
