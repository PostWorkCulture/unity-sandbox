// One-time setup for the Street Life test scene: imports the exported textures with the right settings, makes URP
// materials from the web game's material table, a post-processing profile and a sky, and builds the scene.
// It runs by itself the first time (when the scene doesn't exist yet); afterwards use the Street Life menu to rebuild.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    public static class StreetLifeSetup
    {
        const string Root = "Assets/StreetLife";
        const string ScenePath = Root + "/Scenes/StreetLife.unity";

        const float WebExposure = 0.807f;   // log2(1.05 / 0.6): three.js's ACES divides by 0.6, Unity's doesn't

        static StreetLifeSetup() { EditorApplication.delayCall += AutoRun; EditorApplication.delayCall += MatchWebExposure; EditorApplication.delayCall += QualityTweaks; }

        // PC quality: 4x MSAA (clean edges and outlines) and shadows across the whole 92 m map
        static void QualityTweaks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += QualityTweaks; return; }
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (!urp || (urp.msaaSampleCount >= 4 && urp.shadowDistance >= 100)) return;
            urp.msaaSampleCount = 4;
            urp.shadowDistance = Mathf.Max(urp.shadowDistance, 100);
            EditorUtility.SetDirty(urp); AssetDatabase.SaveAssets();
            Debug.Log("Street Life: PC quality set to 4x MSAA and 100 m shadows.");
        }

        static void MatchWebExposure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += MatchWebExposure; return; }
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/Settings/StreetLifeVolume.asset");
            if (!p || !p.TryGet<ColorAdjustments>(out var ca) || Mathf.Abs(ca.postExposure.value - WebExposure) < .001f) return;
            ca.postExposure.Override(WebExposure);
            EditorUtility.SetDirty(ca); EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            Debug.Log("Street Life: exposure matched to the web game (+0.807 stops).");
        }

        static void AutoRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += AutoRun; return; }
            if (!File.Exists(Root + "/Data/map.json")) return;
            if (File.Exists(ScenePath))
            {
                // a fresh export (new material tables) rebuilds the materials; the scene itself is kept
                var lib0 = AssetDatabase.LoadAssetAtPath<MaterialLibrary>(Root + "/Materials/MaterialLibrary.asset");
                if (lib0 && lib0.dataStamp != DataStamp()) { BuildMaterials(); Debug.Log("Street Life: new export found, materials rebuilt."); }
                WireDynamic();
                return;
            }
            if (SessionState.GetBool("StreetLifeAutoRan", false)) return;
            SessionState.SetBool("StreetLifeAutoRan", true);
            Setup();
        }

        // Scenes made before destruction existed: hand the map builder the dyn.json / dyn.bytes it needs (once).
        static void WireDynamic()
        {
            var dj = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/dyn.json");
            var db = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/dyn.bytes");
            if (!dj || !db) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool opened = false;
            if (scene.path != ScenePath)
            {
                if (EditorSceneManager.GetActiveScene().isDirty) return;   // don't disturb unsaved work in another scene
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true;
            }
            bool changed = false;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var mb in root.GetComponentsInChildren<MapBuilder>(true))
                    if (mb.dynJson != dj || mb.dynBin != db)
                    {
                        mb.dynJson = dj; mb.dynBin = db; EditorUtility.SetDirty(mb); changed = true;
                        mb.enabled = false; mb.enabled = true;   // rebuild now so the props show in the Scene view
                    }
            if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); Debug.Log("Street Life: breakable and moving things added to the scene."); }
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }

        [MenuItem("Street Life/Rebuild materials only")]
        public static void RebuildMaterials() { BuildMaterials(); }

        [MenuItem("Street Life/Rebuild materials and scene")]
        public static void Setup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            try
            {
                var lib = BuildMaterials();
                EditorUtility.DisplayProgressBar("Street Life", "Building the scene", .7f);
                var profile = MakeProfile();
                var sky = MakeSky();
                BuildScene(lib, profile, sky);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static string DataStamp()
        {
            var a = File.ReadAllText(Root + "/Data/materials.json") + File.ReadAllText(Root + "/Data/wednesday_materials.json");
            return Hash128.Compute("v2" + a).ToString();   // bump the version when the material setup itself changes
        }

        static MaterialLibrary BuildMaterials()
        {
            AssetDatabase.Refresh();
            try
            {
                EditorUtility.DisplayProgressBar("Street Life", "Importing textures", .1f);
                var mapMats = JsonUtility.FromJson<MatJson>(File.ReadAllText(Root + "/Data/materials.json"));
                var chMats = JsonUtility.FromJson<MatJson>(File.ReadAllText(Root + "/Data/wednesday_materials.json"));
                var mapTex = ImportTextures(mapMats, Root + "/Textures", "t");
                var chTex = ImportTextures(chMats, Root + "/Textures/Char", "c");

                EditorUtility.DisplayProgressBar("Street Life", "Making materials", .4f);
                Directory.CreateDirectory(Root + "/Materials/Map"); Directory.CreateDirectory(Root + "/Materials/Wednesday");
                var lib = AssetDatabase.LoadAssetAtPath<MaterialLibrary>(Root + "/Materials/MaterialLibrary.asset");
                if (!lib) { lib = ScriptableObject.CreateInstance<MaterialLibrary>(); AssetDatabase.CreateAsset(lib, Root + "/Materials/MaterialLibrary.asset"); }
                lib.unlitBase = SaveMat(new Material(Shader.Find("Universal Render Pipeline/Unlit")), Root + "/Materials/UnlitBase.mat");
                var edge = Shader.Find("StreetLife/Edge Lines");
                if (edge) lib.lineBase = SaveMat(new Material(edge), Root + "/Materials/EdgeLines.mat");
                lib.map = MakeMats(mapMats, mapTex, Root + "/Materials/Map/m");
                lib.character = MakeMats(chMats, chTex, Root + "/Materials/Wednesday/w");
                lib.dataStamp = DataStamp();
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
                return lib;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // ------------------------------------------------------------------ textures
        static Texture2D[] ImportTextures(MatJson mj, string folder, string prefix)
        {
            var arr = new Texture2D[mj.textures.Length];
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var t in mj.textures)
                {
                    var path = folder + "/" + prefix + t.id + ".png";
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = true;
                    ti.mipmapEnabled = true;
                    ti.alphaIsTransparency = true;
                    ti.wrapModeU = t.wrapS == "clamp" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                    ti.wrapModeV = t.wrapT == "clamp" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                    ti.filterMode = t.point ? FilterMode.Point : FilterMode.Trilinear;
                    ti.anisoLevel = t.point ? 0 : 8;
                    ti.maxTextureSize = 2048;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    ti.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (var t in mj.textures) arr[t.id] = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + prefix + t.id + ".png");
            return arr;
        }

        // ------------------------------------------------------------------ materials
        static Material[] MakeMats(MatJson mj, Texture2D[] tex, string pathPrefix)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var outArr = new Material[mj.materials.Length];
            foreach (var w in mj.materials)
            {
                bool isUnlit = w.kind == "unlit";
                var m = new Material(isUnlit ? unlit : lit);
                var col = Hex.ToColor(w.color, w.opacity);
                m.SetColor("_BaseColor", col);
                Texture2D map = w.map >= 0 && w.map < tex.Length ? tex[w.map] : null;
                if (map) m.SetTexture("_BaseMap", map);
                if (!isUnlit)
                {
                    m.SetFloat("_Smoothness", Mathf.Clamp01(1 - w.roughness) * .92f);
                    m.SetFloat("_Metallic", Mathf.Clamp01(w.metalness));
                    var e = Hex.ToColor(w.emissive) * Mathf.Max(0, w.emissiveIntensity);
                    var emap = w.emissiveMap >= 0 && w.emissiveMap < tex.Length ? tex[w.emissiveMap] : null;
                    if (e.maxColorComponent > .002f || emap)
                    {
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", emap && e.maxColorComponent < .002f ? Color.white : new Color(e.r, e.g, e.b, 1));
                        if (emap) m.SetTexture("_EmissionMap", emap);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                }
                bool transparent = w.transparent || w.transmission > 0;
                if (transparent) MakeTransparent(m, w.depthWrite && w.opacity >= .999f);
                else if (w.alphaTest > 0)
                {
                    m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", w.alphaTest); m.EnableKeyword("_ALPHATEST_ON");
                    m.renderQueue = (int)RenderQueue.AlphaTest;
                }
                if (w.doubleSided) { m.SetFloat("_Cull", 0); m.doubleSidedGI = true; }
                else if (w.backSide) m.SetFloat("_Cull", 1);
                outArr[w.id] = SaveMat(m, pathPrefix + w.id + ".mat");
            }
            return outArr;
        }

        static void MakeTransparent(Material m, bool zwrite)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", zwrite ? 1 : 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        static Material SaveMat(Material m, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old) { old.shader = m.shader; old.CopyPropertiesFromMaterial(m); old.shaderKeywords = m.shaderKeywords; old.renderQueue = m.renderQueue; old.SetOverrideTag("RenderType", m.GetTag("RenderType", false)); old.globalIlluminationFlags = m.globalIlluminationFlags; EditorUtility.SetDirty(old); Object.DestroyImmediate(m); return old; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------ post-processing and sky
        static VolumeProfile MakeProfile()
        {
            var path = Root + "/Settings/StreetLifeVolume.asset";
            Directory.CreateDirectory(Root + "/Settings");
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (p) return p;
            p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var ca = p.Add<ColorAdjustments>(true); ca.postExposure.Override(WebExposure);
            var bl = p.Add<Bloom>(true); bl.intensity.Override(.25f); bl.threshold.Override(1.1f);
            foreach (var c in p.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, p); }
            EditorUtility.SetDirty(p); AssetDatabase.SaveAssets();
            return p;
        }

        static Material MakeSky()
        {
            var path = Root + "/Settings/StreetLifeSky.mat";
            var m = new Material(Shader.Find("Skybox/Procedural"));
            m.SetColor("_SkyTint", new Color(.45f, .62f, .78f));
            m.SetColor("_GroundColor", new Color(.55f, .62f, .55f));
            m.SetFloat("_AtmosphereThickness", .85f);
            m.SetFloat("_Exposure", 1.15f);
            m.SetFloat("_SunSize", .03f);
            return SaveMat(m, path);
        }

        // ------------------------------------------------------------------ scene
        static void BuildScene(MaterialLibrary lib, VolumeProfile profile, Material sky)
        {
            var world = JsonUtility.FromJson<WorldJson>(File.ReadAllText(Root + "/Data/map.json"));
            Directory.CreateDirectory(Root + "/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // lighting: three.js intensities divided by pi (URP has no 1/pi in its diffuse term)
            WLight hemi = null, sun = null, fill = null;
            foreach (var g in world.globalLights)
            {
                if (g.type == "HemisphereLight") hemi = g;
                else if (g.type == "DirectionalLight") { if (sun == null) sun = g; else if (fill == null) fill = g; }
            }
            var sunGo = new GameObject("Sun");
            var sl = sunGo.AddComponent<Light>(); sl.type = LightType.Directional;
            sl.color = Hex.ToColor(sun != null ? sun.color : "#fffaf0");
            sl.intensity = (sun != null ? sun.intensity : 2.3f) / Mathf.PI;
            sl.shadows = LightShadows.Soft; sl.shadowStrength = .85f;
            sunGo.transform.rotation = LookDir(sun, new Vector3(-30, -60, 26));
            RenderSettings.sun = sl;
            if (fill != null)
            {
                var fg = new GameObject("Fill light"); var fl = fg.AddComponent<Light>(); fl.type = LightType.Directional;
                fl.color = Hex.ToColor(fill.color); fl.intensity = fill.intensity / Mathf.PI; fl.shadows = LightShadows.None;
                fg.transform.rotation = LookDir(fill, new Vector3(-1, -1, 1));
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            float hi = (hemi != null ? hemi.intensity : 1.45f) / Mathf.PI;
            Color skyC = Hex.ToColor(hemi != null ? hemi.color : "#ffffff"), gnd = Hex.ToColor(hemi != null ? hemi.ground : "#9aa3ad");
            RenderSettings.ambientSkyColor = skyC * hi;
            RenderSettings.ambientEquatorColor = Color.Lerp(skyC, gnd, .5f) * hi;
            RenderSettings.ambientGroundColor = gnd * hi;
            if (world.fog != null)
            {
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = Hex.ToColor(world.fog.color);
                RenderSettings.fogStartDistance = world.fog.near; RenderSettings.fogEndDistance = world.fog.far;
            }
            RenderSettings.skybox = sky;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true; vol.sharedProfile = profile;

            var map = new GameObject("Street Life").AddComponent<MapBuilder>();
            map.library = lib;
            map.mapJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/map.json");
            map.mapBin = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/map.bytes");
            map.dynJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/dyn.json");
            map.dynBin = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/dyn.bytes");

            var player = new GameObject("Wednesday (player)");
            var f = player.AddComponent<Fighter>();
            player.AddComponent<PlayerController>();
            var rig = player.AddComponent<CharacterRig>();
            rig.fighter = f; rig.library = lib;
            rig.charJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/wednesday.json");
            rig.charBin = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/wednesday.bytes");
            player.AddComponent<Hud>().fighter = f;
            if (world.spawns != null && world.spawns.Length > 0) player.transform.position = new Vector3(world.spawns[0].x, world.spawns[0].y, world.spawns[0].z);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = .05f; cam.farClipPlane = 1500f; cam.fieldOfView = 70;
            var acd = camGo.AddComponent<UniversalAdditionalCameraData>();
            acd.renderPostProcessing = true;
            acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();
            var sc = camGo.AddComponent<ShoulderCamera>(); sc.target = f; sc.rig = rig;
            camGo.transform.position = player.transform.position + new Vector3(0, 3, -5);

            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes) if (s.path != ScenePath) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("Street Life: scene built at " + ScenePath + ". Press Play, click in the Game view to lock the mouse.");
        }

        static Quaternion LookDir(WLight l, Vector3 fallback)
        {
            if (l != null && l.pos != null && l.target != null)
            {
                var d = new Vector3(l.target[0] - l.pos[0], l.target[1] - l.pos[1], l.target[2] - l.pos[2]);
                if (d.sqrMagnitude > 1e-6f) return Quaternion.LookRotation(d.normalized);
            }
            return Quaternion.LookRotation(fallback.normalized);
        }
    }
}
