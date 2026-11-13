// The Unity version's own look, a step up from the web game while keeping its colours (match chat).
// Applied once per LookVersion (and from the menu Street Life > Apply look):
//  PC:     GPU Resident Drawer with GPU occlusion culling; ACES as the web game, a touch more contrast and colour, soft bloom on lamps / flashes / explosions, a light vignette,
//          stronger ambient occlusion so rooms, kerbs and furniture sit in their corners, 4K soft shadows over 100 m.
//  Mobile and WebGL (the "Mobile" quality level): 2x MSAA, 2K shadows in two cascades over 60 m, half-resolution ambient occlusion.
// Reflections come from a probe rendered once at the start of play (WorldLook.cs), so glass and car paint reflect the street.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    static class StreetLifeLook
    {
        const int LookVersion = 3;
        const string Stamp = "Assets/StreetLife/Settings/look-version.txt";

        static StreetLifeLook() { EditorApplication.delayCall += Auto; }

        static void Auto()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Auto; return; }
            EnsureRim(); EnsureMat("Fresnel"); EnsureMat("Outline");
            if (File.Exists(Stamp) && File.ReadAllText(Stamp).Trim() == LookVersion.ToString()) return;
            if (!File.Exists("Assets/StreetLife/Settings/StreetLifeVolume.asset")) return;   // the scene setup hasn't run yet
            Apply();
        }

        [MenuItem("Street Life/Apply look")]
        public static void Apply()
        {
            try
            {
                Post();
                var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                var mob = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
                if (pc)
                {
                    pc.msaaSampleCount = 4; pc.shadowDistance = 100; pc.shadowCascadeCount = 4;
                    pc.supportsHDR = true;
                    SetInt(pc, "m_MainLightShadowmapResolution", 4096);
                    SetInt(pc, "m_SoftShadowQuality", 3);
                    SetInt(pc, "m_AdditionalLightsPerObjectLimit", 8);
                    // GPU Resident Drawer: the ~2,500 draws become instanced GPU draws, and the GPU culls what walls and houses hide
                    SetInt(pc, "m_GPUResidentDrawerMode", 1);
                    SetInt(pc, "m_GPUResidentDrawerEnableOcclusionCullingInCameras", 1);
                    var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
                    if (gs) SetInt(gs, "m_BrgStripping", 2);   // keep the BatchRendererGroup shader variants the drawer needs
                    EditorUtility.SetDirty(pc);
                }
                if (mob)
                {
                    mob.msaaSampleCount = 2; mob.shadowDistance = 60; mob.shadowCascadeCount = 2;
                    SetInt(mob, "m_MainLightShadowmapResolution", 2048);
                    SetInt(mob, "m_UpscalingFilter", 1);   // linear: 'Auto' picks FSR, whose shader WebGL strips, and then post-processing doesn't run at all
                    EditorUtility.SetDirty(mob);
                }
                Ssao("Assets/Settings/PC_Renderer.asset", false);
                Ssao("Assets/Settings/Mobile_Renderer.asset", true);
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory(Path.GetDirectoryName(Stamp));
                File.WriteAllText(Stamp, LookVersion.ToString());
                AssetDatabase.ImportAsset(Stamp);
                Debug.Log($"Street Life: look v{LookVersion} applied (post, ambient occlusion, shadows, per-platform quality).");
            }
            catch (Exception e) { Debug.LogError("Street Life look: " + e); }
        }

        // the material CharacterLook adds to every character (Resources, so builds include the shader)
        static void EnsureRim()
        {
            const string path = "Assets/StreetLife/Resources/StreetLife/CharacterRim.mat";
            if (File.Exists(path)) return;
            var sh = Shader.Find("StreetLife/CharacterRim"); if (!sh) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var m = new Material(sh) { name = "CharacterRim", enableInstancing = true };
            AssetDatabase.CreateAsset(m, path);
            Debug.Log("Street Life: character rim light material made.");
        }

        // materials made at runtime from these shaders live in Resources, so builds include the shaders
        static void EnsureMat(string name)
        {
            string path = "Assets/StreetLife/Resources/StreetLife/" + name + ".mat";
            if (File.Exists(path)) return;
            var sh = Shader.Find("StreetLife/" + name); if (!sh) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(new Material(sh) { name = name }, path);
            Debug.Log("Street Life: " + name + " material made.");
        }

        static void SetInt(UnityEngine.Object o, string prop, int v)
        {
            var so = new SerializedObject(o); var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning("Street Life look: no " + prop + " on " + o.name); return; }
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v != 0; else p.intValue = v;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static T Get<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (p.TryGet<T>(out var c)) return c;
            c = p.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, p); return c;
        }

        static void Post()
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/StreetLife/Settings/StreetLifeVolume.asset");
            if (!p) return;
            Get<Tonemapping>(p).mode.Override(TonemappingMode.ACES);
            var ca = Get<ColorAdjustments>(p); ca.contrast.Override(10f); ca.saturation.Override(10f);   // exposure stays matched to the web game
            var bl = Get<Bloom>(p); bl.intensity.Override(.45f); bl.threshold.Override(1f); bl.scatter.Override(.65f); bl.highQualityFiltering.Override(true);
            var vg = Get<Vignette>(p); vg.intensity.Override(.18f); vg.smoothness.Override(.45f);
            var wb = Get<WhiteBalance>(p); wb.temperature.Override(4f);
            foreach (var c in p.components) EditorUtility.SetDirty(c);
            EditorUtility.SetDirty(p);
        }

        // URP's ambient occlusion feature is internal: find it on the renderer, or add one the way URP's own editor does.
        static void Ssao(string rendererPath, bool mobile)
        {
            var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
            if (!rd) return;
            var type = typeof(ScriptableRendererFeature).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
            if (type == null) { Debug.LogWarning("Street Life look: URP has no ScreenSpaceAmbientOcclusion feature"); return; }
            ScriptableRendererFeature f = null;
            foreach (var x in rd.rendererFeatures) if (x && x.GetType() == type) f = x;
            if (!f)
            {
                f = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                f.name = "ScreenSpaceAmbientOcclusion";
                AssetDatabase.AddObjectToAsset(f, rd);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out _, out long id);
                var so = new SerializedObject(rd);
                var list = so.FindProperty("m_RendererFeatures"); list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = f;
                var map = so.FindProperty("m_RendererFeatureMap"); map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var fs = new SerializedObject(f);
            void F(string n, float v) { var q = fs.FindProperty("m_Settings." + n); if (q != null) q.floatValue = v; }
            void I(string n, int v) { var q = fs.FindProperty("m_Settings." + n); if (q != null) { if (q.propertyType == SerializedPropertyType.Boolean) q.boolValue = v != 0; else q.intValue = v; } }
            F("Intensity", mobile ? .7f : .9f);
            F("Radius", mobile ? .4f : .45f);
            F("DirectLightingStrength", .3f);
            F("Falloff", 60f);
            I("Downsample", mobile ? 1 : 0);
            I("Source", 1);                 // depth + normals
            I("NormalSamples", 1);
            I("Samples", mobile ? 2 : 1);   // low / medium
            I("BlurQuality", mobile ? 1 : 0);
            fs.ApplyModifiedPropertiesWithoutUndo();
            f.SetActive(true);
            EditorUtility.SetDirty(f); EditorUtility.SetDirty(rd);
            rd.SetDirty();
        }
    }
}
