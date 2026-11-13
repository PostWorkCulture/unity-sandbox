// Weapon setup (weapons/characters chat): imports the exported gun textures with the right settings, makes URP
// materials from guns_materials.json (including the dragon sword's normal and metal/smoothness maps), and writes
// Resources/StreetLifeWeapons.asset, which the game loads at Play. Runs by itself whenever the gun export changes;
// menu Street Life > Rebuild weapon materials runs it by hand. Kept apart from StreetLifeSetup (the match chat's).
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    public static class StreetLifeWeaponsSetup
    {
        const string Root = "Assets/StreetLife";
        const string MatsJson = Root + "/Data/guns_materials.json";
        const string AssetPath = Root + "/Resources/StreetLifeWeapons.asset";

        [Serializable] class GTex { public int id, w, h; public string wrapS, wrapT; public bool point, normal, linear; }
        [Serializable] class GMat
        {
            public int id; public string kind, color, emissive; public int map = -1, emissiveMap = -1, alphaMap = -1, normalMap = -1, msMap = -1;
            public float opacity = 1, alphaTest, roughness = 1, metalness, emissiveIntensity = 1, transmission;
            public bool transparent, doubleSided, backSide, depthWrite = true;
        }
        [Serializable] class GMatJson { public GTex[] textures; public GMat[] materials; }

        static StreetLifeWeaponsSetup() { EditorApplication.delayCall += AutoRun; }

        static void AutoRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += AutoRun; return; }
            if (!File.Exists(MatsJson)) return;
            var wa = AssetDatabase.LoadAssetAtPath<WeaponAssets>(AssetPath);
            if (wa && wa.dataStamp == Stamp() && wa.gunsJson && wa.gunsBin && wa.materials != null && wa.materials.Length > 0) return;
            Build();
            Debug.Log("Street Life: weapon materials built (" + AssetPath + ").");
        }

        static string Stamp() => Hash128.Compute("w1" + File.ReadAllText(MatsJson)).ToString();

        [MenuItem("Street Life/Rebuild weapon materials")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            try
            {
                EditorUtility.DisplayProgressBar("Street Life", "Importing weapon textures", .2f);
                var mj = JsonUtility.FromJson<GMatJson>(File.ReadAllText(MatsJson));
                var tex = ImportTextures(mj, Root + "/Textures/Guns", "g");
                EditorUtility.DisplayProgressBar("Street Life", "Making weapon materials", .6f);
                Directory.CreateDirectory(Root + "/Materials/Guns"); Directory.CreateDirectory(Root + "/Resources");
                var mats = MakeMats(mj, tex, Root + "/Materials/Guns/g");
                var wa = AssetDatabase.LoadAssetAtPath<WeaponAssets>(AssetPath);
                if (!wa) { wa = ScriptableObject.CreateInstance<WeaponAssets>(); AssetDatabase.CreateAsset(wa, AssetPath); }
                wa.gunsJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/guns.json");
                wa.gunsBin = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/guns.bytes");
                wa.fxShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/Fx.shader");
                wa.materials = mats; wa.dataStamp = Stamp();
                EditorUtility.SetDirty(wa); AssetDatabase.SaveAssets();
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static Texture2D[] ImportTextures(GMatJson mj, string folder, string prefix)
        {
            var arr = new Texture2D[mj.textures.Length];
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var t in mj.textures)
                {
                    if (!(AssetImporter.GetAtPath(folder + "/" + prefix + t.id + ".png") is TextureImporter ti)) continue;
                    ti.textureType = t.normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.sRGBTexture = !t.linear && !t.normal;
                    ti.alphaIsTransparency = !t.linear && !t.normal;
                    ti.mipmapEnabled = true;
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
            foreach (var t in mj.textures) if (t.id >= 0 && t.id < arr.Length) arr[t.id] = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + prefix + t.id + ".png");
            return arr;
        }

        static Texture2D T(Texture2D[] tex, int i) => i >= 0 && i < tex.Length ? tex[i] : null;

        static Material[] MakeMats(GMatJson mj, Texture2D[] tex, string prefix)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit"); var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var outArr = new Material[mj.materials.Length];
            foreach (var w in mj.materials)
            {
                bool isUnlit = w.kind == "unlit";
                var m = new Material(isUnlit ? unlit : lit);
                m.SetColor("_BaseColor", Hex.ToColor(w.color, w.opacity));
                var map = T(tex, w.map); if (map) m.SetTexture("_BaseMap", map);
                if (!isUnlit)
                {
                    m.SetFloat("_Smoothness", Mathf.Clamp01(1 - w.roughness) * .92f);
                    m.SetFloat("_Metallic", Mathf.Clamp01(w.metalness));
                    var e = Hex.ToColor(w.emissive) * Mathf.Max(0, w.emissiveIntensity); var emap = T(tex, w.emissiveMap);
                    if (e.maxColorComponent > .002f || emap)
                    {
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", emap && e.maxColorComponent < .002f ? Color.white : new Color(e.r, e.g, e.b, 1));
                        if (emap) m.SetTexture("_EmissionMap", emap);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                    var nm = T(tex, w.normalMap); if (nm) { m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 1); m.EnableKeyword("_NORMALMAP"); }
                    var ms = T(tex, w.msMap); if (ms) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 1); m.SetFloat("_SmoothnessTextureChannel", 0); }
                }
                if (w.transparent || w.transmission > 0)
                {
                    m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", w.depthWrite && w.opacity >= .999f ? 1 : 0);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
                }
                else if (w.alphaTest > 0) { m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", w.alphaTest); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; }
                if (w.doubleSided) { m.SetFloat("_Cull", 0); m.doubleSidedGI = true; } else if (w.backSide) m.SetFloat("_Cull", 1);
                outArr[w.id] = Save(m, prefix + w.id + ".mat");
            }
            return outArr;
        }

        static Material Save(Material m, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old)
            {
                old.shader = m.shader; old.CopyPropertiesFromMaterial(m); old.shaderKeywords = m.shaderKeywords; old.renderQueue = m.renderQueue;
                old.SetOverrideTag("RenderType", m.GetTag("RenderType", false)); old.globalIlluminationFlags = m.globalIlluminationFlags;
                EditorUtility.SetDirty(old); UnityEngine.Object.DestroyImmediate(m); return old;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
