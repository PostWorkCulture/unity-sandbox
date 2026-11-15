// Map looks (Railed, Beach Please): turns the texture sets in Maps/<id>/Surfaces (<name>_albedo.png with smoothness in alpha,
// <name>_normal.png) into materials and the Resources/RailLook.asset and BeachLook.asset the game reads (RailLook.cs, BeachLook.cs).
// Menu: Street Life > Maps > Rebuild Railed surfaces. Remote: call RailSetup.Build
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StreetLife.EditorTools
{
    public static class RailSetup
    {
        const string Stamp = "rail-surface-2";

        struct Def { public string name, tex, dir, reference, shift; public float metres, bump, metallic; public bool tinted, keepGloss, upright; }
        const string RailDir = "Assets/StreetLife/Maps/rail/Surfaces", BeachDir = "Assets/StreetLife/Maps/wreck/Surfaces";

        static readonly Def[] Rail =
        {
            new Def { name = "brick", metres = 2.4f, reference = "#a5543a" },
            new Def { name = "slabs", metres = 5f, reference = "#c4beb2" },
            new Def { name = "floor", metres = 3f, reference = "#9d978b" },
            new Def { name = "ballast", metres = 2f, reference = "#8f877b" },
            new Def { name = "concrete", metres = 2.4f, tinted = true },
            new Def { name = "corrugated", metres = 2f, tinted = true, metallic = .35f, shift = "#dcf0ff" },
            new Def { name = "roofing", metres = 2f, tinted = true },
            new Def { name = "metal", metres = 1.5f, tinted = true, keepGloss = true },
            new Def { name = "wood", metres = 1.2f, tinted = true },
            new Def { name = "grass", metres = 6f, tinted = true },
        };

        // Beach Please: its own sets, plus Railed's painted steel, timber and grass under the names its rules use
        static readonly Def[] Beach =
        {
            new Def { name = "rock", metres = 4f, tinted = true },
            new Def { name = "planks", metres = 1.68f, tinted = true },
            new Def { name = "thatch", metres = 2f, tinted = true },
            new Def { name = "bamboo", metres = 1.2f, reference = "#ffffff" },
            new Def { name = "wicker", metres = .8f, reference = "#ffffff" },
            new Def { name = "fabric", metres = .4f, tinted = true },
            new Def { name = "boards", metres = 1.2f, tinted = true, upright = true },
            new Def { name = "post", tex = "wood", dir = RailDir, metres = 1.2f, tinted = true, upright = true },
            new Def { name = "paint", tex = "metal", dir = RailDir, metres = 1.5f, tinted = true, keepGloss = true },
            new Def { name = "grass", dir = RailDir, metres = 6f, tinted = true },
            new Def { name = "leaf", tex = "grass", dir = RailDir, metres = 3f, tinted = true },
            new Def { name = "marble", metres = 2.6f, tinted = true, bump = .35f },
        };

        /// remote: call RailSetup.Target [win|webgl|android]   which platform the editor is set to (no argument: just says). After a
        /// browser build the editor is left on WebGL, where the baked bounce light is off: `win` puts it back.
        public static string Target(string arg)
        {
            var now = EditorUserBuildSettings.activeBuildTarget;
            if (string.IsNullOrEmpty(arg)) return now.ToString();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "not in play mode (" + now + ")";
            bool ok = arg.Contains("win") ? EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64)
                : arg.Contains("web") ? EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL)
                : arg.Contains("android") && EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            return (ok ? "now " : "could not switch, still ") + EditorUserBuildSettings.activeBuildTarget;
        }

        // the small maps (MapLooks): mostly Railed's and Beach Please's texture sets under the names their rules use
        static readonly Def[] Sky =
        {
            new Def { name = "slabs", dir = RailDir, metres = 5f, reference = "#c4beb2" },
            new Def { name = "concrete", dir = RailDir, metres = 2.4f, tinted = true },
            new Def { name = "metal", dir = RailDir, metres = 1.5f, tinted = true, keepGloss = true },
            new Def { name = "roofing", dir = RailDir, metres = 2f, tinted = true },
        };

        [MenuItem("Street Life/Maps/Rebuild Railed surfaces")]
        static void Menu() { Debug.Log("Map surfaces: " + Build("")); }

        public static string Build(string arg)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "not in play mode";
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (!lit) return "no URP Lit shader";
            string a = Book(lit, RailDir, "Assets/StreetLife/Resources/RailLook.asset", Rail, out var rail);
            string sign = RailDir + "/sign_shed.png";
            if (rail && File.Exists(sign)) { Import(sign, false, true); rail.shedSign = AssetDatabase.LoadAssetAtPath<Texture2D>(sign); EditorUtility.SetDirty(rail); }
            string b = Book(lit, BeachDir, "Assets/StreetLife/Resources/BeachLook.asset", Beach, out var beach);
            if (beach) { beach.ground = Sand(lit); beach.cushion = Cushion(lit); EditorUtility.SetDirty(beach); }
            if (beach) Bar(lit, beach);
            string label = BeachDir + "/drum_label.png";
            if (File.Exists(label))
            {
                Import(label, false, true);
                var lt = AssetDatabase.LoadAssetAtPath<Texture2D>(label);
                if (rail) { rail.drumLabel = lt; EditorUtility.SetDirty(rail); }
                if (beach) { beach.drumLabel = lt; EditorUtility.SetDirty(beach); }
            }
            string c = "; Sky High " + Book(lit, RailDir, "Assets/StreetLife/Resources/SkyLook.asset", Sky, out _);
            AssetDatabase.SaveAssets();
            return "Railed " + a + "; Beach Please " + b + (beach && beach.ground ? " and the sand" : "") + c;
        }

        static string Book(Shader lit, string dir, string setPath, Def[] defs, out RailLookSet set)
        {
            var list = new System.Collections.Generic.List<RailLookSet.Surface>();
            string missing = "";
            foreach (var d in defs)
            {
                string from = string.IsNullOrEmpty(d.dir) ? dir : d.dir, tex = string.IsNullOrEmpty(d.tex) ? d.name : d.tex;
                string ap = from + "/" + tex + "_albedo.png", np = from + "/" + tex + "_normal.png";
                if (!File.Exists(ap) || !File.Exists(np)) { missing += " " + d.name; continue; }
                Import(ap, false); Import(np, true);
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ap); var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(np);
                string mp = from + "/" + tex + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (!m) { m = new Material(lit); AssetDatabase.CreateAsset(m, mp); }
                m.shader = lit;
                m.SetTexture("_BaseMap", albedo); m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", d.bump > 0 ? d.bump : 1f); m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_SmoothnessTextureChannel", 1); m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                m.SetFloat("_Smoothness", 1); m.SetFloat("_Metallic", 0); m.SetFloat("_WorkflowMode", 1);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                ColorUtility.TryParseHtmlString(string.IsNullOrEmpty(d.reference) ? "#ffffff" : d.reference, out var rc);
                ColorUtility.TryParseHtmlString(string.IsNullOrEmpty(d.shift) ? "#ffffff" : d.shift, out var sc);
                list.Add(new RailLookSet.Surface { shift = sc, name = d.name, material = m, metres = d.metres, tinted = d.tinted, reference = rc, keepGloss = d.keepGloss, metallic = d.metallic, upright = d.upright });
            }
            set = AssetDatabase.LoadAssetAtPath<RailLookSet>(setPath);
            if (!set) { set = ScriptableObject.CreateInstance<RailLookSet>(); AssetDatabase.CreateAsset(set, setPath); }
            set.surfaces = list.ToArray();
            EditorUtility.SetDirty(set);
            return list.Count + " surfaces" + (missing.Length > 0 ? " (missing:" + missing + ")" : "");
        }

        // Beach Please's sand: the map's own colours as the base (smoothness in alpha: the wet band), ripples and grain as detail maps
        static Material Sand(Shader lit)
        {
            string bp = BeachDir + "/sand_base.png", dp = BeachDir + "/sand_detail.png", np = BeachDir + "/sand_normal.png";
            if (!File.Exists(bp) || !File.Exists(dp) || !File.Exists(np)) return null;
            Import(bp, false, true); Import(dp, false); Import(np, true);
            string mp = BeachDir + "/sand.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!m) { m = new Material(lit); AssetDatabase.CreateAsset(m, mp); }
            m.shader = lit;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(bp)); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_SmoothnessTextureChannel", 1); m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat("_Smoothness", 1); m.SetFloat("_Metallic", 0); m.SetFloat("_WorkflowMode", 1);
            m.SetTexture("_DetailAlbedoMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dp)); m.SetTexture("_DetailNormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(np));
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(200f / 3f, 160f / 3f));   // the texture covers 200 x 160 m, one ripple tile 3 m
            m.SetFloat("_DetailAlbedoMapScale", 1); m.SetFloat("_DetailNormalMapScale", 1); m.EnableKeyword("_DETAIL_MULX2");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // the template for striped cushions: MapProps puts each cover's own picture in; the canvas weave is the detail map
        static Material Cushion(Shader lit)
        {
            string dp = BeachDir + "/fabric_detail.png", np = BeachDir + "/fabric_normal.png";
            if (!File.Exists(dp) || !File.Exists(np)) return null;
            Import(dp, false); Import(np, true);
            string mp = BeachDir + "/cushion.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!m) { m = new Material(lit); AssetDatabase.CreateAsset(m, mp); }
            m.shader = lit;
            m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", .16f); m.SetFloat("_Metallic", 0); m.SetFloat("_WorkflowMode", 1);
            m.SetTexture("_DetailAlbedoMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dp)); m.SetTexture("_DetailNormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(np));
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(3.5f, 1.7f));   // a mattress is about 1.4 x 0.7 m, one weave tile 0.4 m
            m.SetFloat("_DetailAlbedoMapScale", 1); m.SetFloat("_DetailNormalMapScale", 1); m.EnableKeyword("_DETAIL_MULX2");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // The Tiki Bar's templates (TikiBar.cs copies and recolours them at play time): clear glass, a plain surface and a plain
        // surface that glows. Kept as assets so every build has the shader variants they need.
        static void Bar(Shader lit, RailLookSet beach)
        {
            Material Mat(string name)
            {
                string mp = BeachDir + "/" + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (!m) { m = new Material(lit); AssetDatabase.CreateAsset(m, mp); }
                m.shader = lit; m.SetFloat("_WorkflowMode", 1); m.SetColor("_BaseColor", Color.white); m.enableInstancing = true;
                return m;
            }
            var glass = Mat("bar_glass");
            glass.SetFloat("_Surface", 1); glass.SetFloat("_Blend", 0); glass.SetFloat("_BlendModePreserveSpecular", 1);
            glass.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One); glass.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One); glass.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_ZWrite", 0); glass.SetFloat("_AlphaClip", 0); glass.SetFloat("_Cull", 2); glass.SetFloat("_ReceiveShadows", 0);
            glass.SetOverrideTag("RenderType", "Transparent"); glass.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); glass.EnableKeyword("_ALPHAPREMULTIPLY_ON"); glass.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            glass.SetShaderPassEnabled("DepthOnly", false); glass.SetShaderPassEnabled("ShadowCaster", false);
            glass.SetColor("_BaseColor", new Color(1, 1, 1, .16f)); glass.SetFloat("_Smoothness", .96f); glass.SetFloat("_Metallic", 0);
            EditorUtility.SetDirty(glass);
            var gloss = Mat("bar_gloss");
            gloss.SetFloat("_Smoothness", .8f); gloss.SetFloat("_Metallic", 0);
            EditorUtility.SetDirty(gloss);
            var glow = Mat("bar_glow");
            glow.SetFloat("_Smoothness", .6f); glow.SetFloat("_Metallic", 0);
            glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", Color.white * 2f);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(glow);
            beach.glass = glass; beach.gloss = gloss; beach.glow = glow;
            string lp = BeachDir + "/bar_labels.png", op = BeachDir + "/onyx.png";
            if (File.Exists(lp)) { Import(lp, false, true); beach.barLabels = AssetDatabase.LoadAssetAtPath<Texture2D>(lp); }
            if (File.Exists(op)) { Import(op, false, true); beach.onyx = AssetDatabase.LoadAssetAtPath<Texture2D>(op); }
            EditorUtility.SetDirty(beach);
        }

        static void Import(string path, bool normal, bool sign = false)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            if (ti.userData == Stamp) return;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal; ti.alphaSource = normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
            ti.mipmapEnabled = true; ti.wrapMode = sign ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8;
            ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.userData = Stamp;
            ti.SaveAndReimport();
        }
    }
}
