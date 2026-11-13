// Maps chat: turns each exported map folder (Assets/StreetLife/Maps/<id>: map.*, dyn.*, extras.*, env.json, materials.json,
// Textures) into URP materials and a MaterialLibrary, and lists every map in Resources/StreetLifeMaps.asset for MapSet.
// Runs by itself after a script reload when a map's material table is new or changed; menu: Street Life > Maps > Rebuild maps.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    public static class MapsSetup
    {
        const string Root = "Assets/StreetLife", MapsDir = Root + "/Maps", SetPath = Root + "/Resources/StreetLifeMaps.asset";
        const string Version = "maps-v1";   // bump when the material setup itself changes
        // menu order and which web game menu each map comes from
        static readonly string[] Order = { "street", "rail", "wreck", "arena", "skyhigh", "lagoon", "house", "yard" };
        static readonly Dictionary<string, string> Mode = new Dictionary<string, string> {
            { "street", "Duels" }, { "rail", "Duels" }, { "wreck", "Duels" }, { "arena", "1v1" }, { "skyhigh", "1v1" }, { "lagoon", "1v1" }, { "house", "Rumble" }, { "yard", "Rumble" } };

        static MapsSetup() { EditorApplication.delayCall += Auto; }

        static void Auto()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Auto; return; }
            if (!Directory.Exists(MapsDir)) return;
            bool need = !File.Exists(SetPath);
            foreach (var id in MapIds())
            {
                var lib = AssetDatabase.LoadAssetAtPath<MaterialLibrary>(MapsDir + "/" + id + "/MaterialLibrary.asset");
                if (!lib || lib.dataStamp != Stamp(id)) { need = true; break; }
            }
            if (need) Rebuild();
        }

        static IEnumerable<string> MapIds()
        {
            if (!Directory.Exists(MapsDir)) yield break;
            foreach (var d in Directory.GetDirectories(MapsDir))
            {
                var id = Path.GetFileName(d);
                if (File.Exists(d + "/materials.json") && File.Exists(d + "/map.json")) yield return id;
            }
        }

        static string Stamp(string id) => Hash128.Compute(Version + File.ReadAllText(MapsDir + "/" + id + "/materials.json")).ToString();

        [MenuItem("Street Life/Maps/Rebuild maps")]
        public static void Rebuild()
        {
            try
            {
                AssetDatabase.Refresh();
                var main = AssetDatabase.LoadAssetAtPath<MaterialLibrary>(Root + "/Materials/MaterialLibrary.asset");
                var ids = new List<string>(MapIds());
                for (int i = 0; i < ids.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Street Life maps", "Materials for " + ids[i], (float)i / Mathf.Max(1, ids.Count));
                    var lib = AssetDatabase.LoadAssetAtPath<MaterialLibrary>(MapsDir + "/" + ids[i] + "/MaterialLibrary.asset");
                    if (!lib || lib.dataStamp != Stamp(ids[i])) BuildMap(ids[i], main);
                }
                BuildSet(ids, main);
                Debug.Log("Street Life maps: " + ids.Count + " maps ready (F4 in play mode picks one).");
            }
            catch (System.Exception e) { Debug.LogError("Street Life maps: " + e); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static void BuildMap(string id, MaterialLibrary main)
        {
            var dir = MapsDir + "/" + id;
            var mj = JsonUtility.FromJson<MatJson>(File.ReadAllText(dir + "/materials.json"));
            var tex = ImportTextures(mj, dir + "/Textures");
            Directory.CreateDirectory(dir + "/Materials");
            var lib = AssetDatabase.LoadAssetAtPath<MaterialLibrary>(dir + "/MaterialLibrary.asset");
            if (!lib) { lib = ScriptableObject.CreateInstance<MaterialLibrary>(); AssetDatabase.CreateAsset(lib, dir + "/MaterialLibrary.asset"); }
            lib.map = MakeMats(mj, tex, dir + "/Materials/m");
            if (main) { lib.unlitBase = main.unlitBase; lib.lineBase = main.lineBase; }
            lib.dataStamp = Stamp(id);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
        }

        static void BuildSet(List<string> ids, MaterialLibrary main)
        {
            Directory.CreateDirectory(Root + "/Resources");
            var set = AssetDatabase.LoadAssetAtPath<MapSet>(SetPath);
            if (!set) { set = ScriptableObject.CreateInstance<MapSet>(); AssetDatabase.CreateAsset(set, SetPath); }
            var list = new List<MapSet.Entry>();
            var street = new MapSet.Entry
            {
                id = "street", title = "Street Life", mode = "Duels",
                desc = "A suburban street: houses you can climb and go into, a corner shop, a garage, cars that drive up and down.",
                swatch = new[] { Hex.ToColor("#5aae46"), Hex.ToColor("#e8e1d3"), Hex.ToColor("#e8463a") },
                mapJson = Load(Root + "/Data/map.json"), mapBin = Load(Root + "/Data/map.bytes"), dynJson = Load(Root + "/Data/dyn.json"), dynBin = Load(Root + "/Data/dyn.bytes"),
                library = main
            };
            var all = new Dictionary<string, MapSet.Entry> { { "street", street } };
            foreach (var id in ids)
            {
                var dir = MapsDir + "/" + id;
                var e = new MapSet.Entry
                {
                    id = id, title = id, mode = Mode.TryGetValue(id, out var md) ? md : "Duels",
                    mapJson = Load(dir + "/map.json"), mapBin = Load(dir + "/map.bytes"), dynJson = Load(dir + "/dyn.json"), dynBin = Load(dir + "/dyn.bytes"),
                    extrasJson = Load(dir + "/extras.json"), extrasBin = Load(dir + "/extras.bytes"), envJson = Load(dir + "/env.json"),
                    library = AssetDatabase.LoadAssetAtPath<MaterialLibrary>(dir + "/MaterialLibrary.asset")
                };
                if (e.envJson)
                {
                    var env = JsonUtility.FromJson<EnvJson>(e.envJson.text);
                    if (env != null && env.info != null)
                    {
                        if (!string.IsNullOrEmpty(env.info.name)) e.title = env.info.name;
                        e.desc = env.info.desc;
                        if (env.info.sw != null) { e.swatch = new Color[env.info.sw.Length]; for (int i = 0; i < e.swatch.Length; i++) e.swatch[i] = Hex.ToColor(env.info.sw[i]); }
                    }
                }
                all[id] = e;
            }
            foreach (var id in Order) if (all.TryGetValue(id, out var e)) { list.Add(e); all.Remove(id); }
            list.AddRange(all.Values);
            set.maps = list.ToArray();
            set.sky = Shader.Find("StreetLife/Map Sky"); set.cloud = Shader.Find("StreetLife/Map Cloud");
            set.ground = Shader.Find("StreetLife/Map Ground"); set.water = Shader.Find("StreetLife/Map Water");
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
        }

        static TextAsset Load(string path) => AssetDatabase.LoadAssetAtPath<TextAsset>(path);

        // ------------------------------------------------------------------ textures and materials (as StreetLifeSetup does for Street Life)
        static Texture2D[] ImportTextures(MatJson mj, string folder)
        {
            var arr = new Texture2D[mj.textures.Length];
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var t in mj.textures)
                {
                    var path = folder + "/t" + t.id + ".png";
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                    ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.alphaIsTransparency = true;
                    ti.wrapModeU = t.wrapS == "clamp" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                    ti.wrapModeV = t.wrapT == "clamp" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                    ti.filterMode = t.point ? FilterMode.Point : FilterMode.Trilinear; ti.anisoLevel = t.point ? 0 : 8;
                    ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    ti.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (var t in mj.textures) if (t.id >= 0 && t.id < arr.Length) arr[t.id] = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/t" + t.id + ".png");
            return arr;
        }

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
                bool transparent = w.transparent || w.transmission > 0;
                if (transparent) MakeTransparent(m, w.depthWrite && w.opacity >= .999f);
                else if (w.alphaTest > 0) { m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", w.alphaTest); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; }
                if (w.doubleSided) { m.SetFloat("_Cull", 0); m.doubleSidedGI = true; }
                else if (w.backSide) m.SetFloat("_Cull", 1);
                m.enableInstancing = true;
                if (w.id >= 0 && w.id < outArr.Length) outArr[w.id] = SaveMat(m, pathPrefix + w.id + ".mat");
            }
            return outArr;
        }

        static void MakeTransparent(Material m, bool zwrite)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", zwrite ? 1 : 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
        }

        static Material SaveMat(Material m, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old)
            {
                old.shader = m.shader; old.CopyPropertiesFromMaterial(m); old.shaderKeywords = m.shaderKeywords; old.renderQueue = m.renderQueue;
                old.SetOverrideTag("RenderType", m.GetTag("RenderType", false)); old.globalIlluminationFlags = m.globalIlluminationFlags; old.enableInstancing = m.enableInstancing;
                EditorUtility.SetDirty(old); Object.DestroyImmediate(m); return old;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
