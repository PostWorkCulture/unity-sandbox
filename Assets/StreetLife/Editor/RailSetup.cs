// Railed look: turns the texture sets in Maps/rail/Surfaces (<name>_albedo.png with smoothness in alpha, <name>_normal.png)
// into materials and the Resources/RailLook.asset the game reads (RailLook.cs).
// Menu: Street Life > Maps > Rebuild Railed surfaces. Remote: call RailSetup.Build
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StreetLife.EditorTools
{
    public static class RailSetup
    {
        const string Dir = "Assets/StreetLife/Maps/rail/Surfaces", SetPath = "Assets/StreetLife/Resources/RailLook.asset", Stamp = "rail-surface-2";

        struct Def { public string name, reference; public float metres, bump, metallic; public bool tinted, keepGloss; }
        static readonly Def[] Defs =
        {
            new Def { name = "brick", metres = 2.4f, reference = "#a5543a", bump = 1f },
            new Def { name = "slabs", metres = 5f, reference = "#c4beb2", bump = 1f },
            new Def { name = "floor", metres = 3f, reference = "#9d978b", bump = 1f },
            new Def { name = "ballast", metres = 2f, reference = "#8f877b", bump = 1f },
            new Def { name = "concrete", metres = 2.4f, tinted = true, bump = 1f },
            new Def { name = "corrugated", metres = 2f, tinted = true, bump = 1f, metallic = .35f },
            new Def { name = "roofing", metres = 2f, tinted = true, bump = 1f },
            new Def { name = "metal", metres = 1.5f, tinted = true, bump = 1f, keepGloss = true },
            new Def { name = "wood", metres = 1.2f, tinted = true, bump = 1f },
            new Def { name = "grass", metres = 6f, tinted = true, bump = 1f },
        };

        [MenuItem("Street Life/Maps/Rebuild Railed surfaces")]
        static void Menu() { Debug.Log("Railed surfaces: " + Build("")); }

        public static string Build(string arg)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "not in play mode";
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (!lit) return "no URP Lit shader";
            var list = new System.Collections.Generic.List<RailLookSet.Surface>();
            string missing = "";
            foreach (var d in Defs)
            {
                string ap = Dir + "/" + d.name + "_albedo.png", np = Dir + "/" + d.name + "_normal.png";
                if (!File.Exists(ap) || !File.Exists(np)) { missing += " " + d.name; continue; }
                Import(ap, false); Import(np, true);
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ap); var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(np);
                string mp = Dir + "/" + d.name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (!m) { m = new Material(lit); AssetDatabase.CreateAsset(m, mp); }
                m.shader = lit;
                m.SetTexture("_BaseMap", albedo); m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", d.bump); m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_SmoothnessTextureChannel", 1); m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                m.SetFloat("_Smoothness", 1); m.SetFloat("_Metallic", 0); m.SetFloat("_WorkflowMode", 1);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                ColorUtility.TryParseHtmlString(string.IsNullOrEmpty(d.reference) ? "#ffffff" : d.reference, out var rc);
                list.Add(new RailLookSet.Surface { name = d.name, material = m, metres = d.metres, tinted = d.tinted, reference = rc, keepGloss = d.keepGloss, metallic = d.metallic });
            }
            var set = AssetDatabase.LoadAssetAtPath<RailLookSet>(SetPath);
            if (!set) { set = ScriptableObject.CreateInstance<RailLookSet>(); AssetDatabase.CreateAsset(set, SetPath); }
            set.surfaces = list.ToArray();
            string sign = Dir + "/sign_shed.png";
            if (File.Exists(sign)) { Import(sign, false, true); set.shedSign = AssetDatabase.LoadAssetAtPath<Texture2D>(sign); }
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return list.Count + " surfaces" + (missing.Length > 0 ? ", missing:" + missing : "");
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
