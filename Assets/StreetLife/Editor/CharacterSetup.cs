// Builds the character set from the exported data: imports Textures/Characters with the right settings, makes URP materials
// from Data/characters_materials.json (the same conversion the map uses), and writes Resources/StreetLifeCharacters.asset
// listing every character in Data/Characters. Runs by itself whenever a new export lands; menu: Street Life > Rebuild characters.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    public static class CharacterSetup
    {
        const string Root = "Assets/StreetLife";
        const string MatsJson = Root + "/Data/characters_materials.json", Catalog = Root + "/Data/characters.json";
        const string AssetPath = Root + "/Resources/StreetLifeCharacters.asset";

        static CharacterSetup() { EditorApplication.delayCall += Auto; }

        static string Stamp() => Hash128.Compute("c1" + File.ReadAllText(MatsJson) + File.ReadAllText(Catalog) + string.Join(",", Directory.GetFiles(Root + "/Data/Characters", "*.json"))).ToString();

        static void Auto()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Auto; return; }
            if (!File.Exists(MatsJson) || !File.Exists(Catalog) || !Directory.Exists(Root + "/Data/Characters")) return;
            var set = AssetDatabase.LoadAssetAtPath<CharacterSet>(AssetPath);
            string st = Stamp();
            if ((!set || set.stamp != st) && SessionState.GetString("StreetLifeCharStamp", "") != st)
            {
                SessionState.SetString("StreetLifeCharStamp", st);
                Build();
            }
            // the old Wednesday-only materials were cleared out for a moment: let the map setup make them again (once)
            if (!File.Exists(Root + "/Materials/Wednesday/w0.mat") && File.Exists(Root + "/Data/wednesday_materials.json") && !SessionState.GetBool("StreetLifeWedMats", false))
            {
                SessionState.SetBool("StreetLifeWedMats", true);
                EditorApplication.ExecuteMenuItem("Street Life/Rebuild materials only");
            }
        }

        [MenuItem("Street Life/Rebuild characters")]
        public static void Build()
        {
            try
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayProgressBar("Street Life", "Importing character textures", .2f);
                var mj = JsonUtility.FromJson<MatJson>(File.ReadAllText(MatsJson));
                var tex = ImportTextures(mj, Root + "/Textures/Characters", "c");
                EditorUtility.DisplayProgressBar("Street Life", "Making character materials", .6f);
                Directory.CreateDirectory(Root + "/Materials/Characters"); Directory.CreateDirectory(Root + "/Resources");
                var mats = MakeMats(mj, tex, Root + "/Materials/Characters/c");

                var set = AssetDatabase.LoadAssetAtPath<CharacterSet>(AssetPath);
                if (!set) { set = ScriptableObject.CreateInstance<CharacterSet>(); AssetDatabase.CreateAsset(set, AssetPath); }
                set.catalog = AssetDatabase.LoadAssetAtPath<TextAsset>(Catalog);
                set.materials = mats;
                var list = new List<CharacterSet.Entry>();
                var files = Directory.GetFiles(Root + "/Data/Characters", "*.json"); System.Array.Sort(files);
                foreach (var f in files)
                {
                    var id = Path.GetFileNameWithoutExtension(f);
                    list.Add(new CharacterSet.Entry
                    {
                        id = id,
                        json = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/Characters/" + id + ".json"),
                        bin = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/Characters/" + id + ".bytes")
                    });
                }
                set.characters = list.ToArray();
                set.stamp = Stamp();
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();
                Debug.Log("Street Life: " + set.characters.Length + " characters ready (" + mats.Length + " materials). Press Play, then F3 to pick one.");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

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
            foreach (var t in mj.textures) if (t.id >= 0 && t.id < arr.Length) arr[t.id] = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + prefix + t.id + ".png");
            return arr;
        }

        // three.js material -> URP Lit / Unlit, as the map's materials are made
        static Material[] MakeMats(MatJson mj, Texture2D[] tex, string pathPrefix)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var outArr = new Material[mj.materials.Length];
            foreach (var w in mj.materials)
            {
                bool isUnlit = w.kind == "unlit";
                var m = new Material(isUnlit ? unlit : lit);
                m.SetColor("_BaseColor", Hex.ToColor(w.color, w.opacity));
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
                if (w.transparent || w.transmission > 0)
                {
                    m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", w.depthWrite && w.opacity >= .999f ? 1 : 0);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)RenderQueue.Transparent;
                }
                else if (w.alphaTest > 0)
                {
                    m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", w.alphaTest); m.EnableKeyword("_ALPHATEST_ON");
                    m.renderQueue = (int)RenderQueue.AlphaTest;
                }
                if (w.doubleSided) { m.SetFloat("_Cull", 0); m.doubleSidedGI = true; }
                else if (w.backSide) m.SetFloat("_Cull", 1);   // the cartoon outline shells
                outArr[w.id] = Save(m, pathPrefix + w.id + ".mat");
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
                EditorUtility.SetDirty(old); Object.DestroyImmediate(m); return old;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
