// Beach Please's own look, the same pass Railed had (RailLook.cs), through three hook lines in MapWorld.Start:
//  - Env: a clear tropical afternoon. The sun 38 degrees up over the sea, so palms and umbrellas throw shadows up the beach and
//    the water glitters when you look out; a deeper blue overhead, sea haze far off.
//  - Surfaces: bedded rock on the cliffs and boulders, proper deck boards, thatch, bamboo and woven cane on the Tiki Bar,
//    painted and stainless finishes, grass; all mapped in world space and picked by exported colour, as on Railed.
//  - Sand: the map's vertex-coloured sand becomes a standard lit surface (its colours drawn into a texture by
//    Tools/beachtex.py), with wind ripples as a detail normal map and a glossy wet band where the waves run up. Being a
//    standard surface it also takes the baked bounce light and the lamps.
//  - The glow cards (behind the three neon signs, round the kiosk's sign, and the painted light pools under the jetty
//    lamps) are hidden: every one of those has a real light next to it already.
//  - Bounce light and reflections: MapBakedLight (a box-projected probe in the Tiki Bar).
//  - The sea's sun glint is toned down (the water shader breaks it into glitter now).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetLife
{
    public static class BeachLook
    {
        public static readonly Vector3 Sun = new Vector3(.60f, .62f, -.505f);
        public const string SunHex = "#fff0d2";
        public const float SunPower = 3.2f;
        public const string LightScene = "Assets/StreetLife/Maps/wreck/Light/BeachLight.unity";
        // the sand texture covers this rectangle of the map (Tools/beachtex.py)
        const float SandX0 = -60, SandW = 200, SandZ0 = -80, SandH = 160;

        public static void Env(EnvJson e)
        {
            if (e == null) return;
            if (e.env != null)
            {
                e.env.top = "#1b78d8"; e.env.mid = "#8fd0ee"; e.env.bot = "#dff4f6"; e.env.ex = .55f;
                e.env.glow = "#fff6dc"; e.env.glowA = .35f; e.env.disc = "#fffbe8"; e.env.discCos = .9995f;
                e.env.sd = new[] { Sun.x, Sun.y, Sun.z };
            }
            if (e.fog != null) { e.fog.color = "#bfe2f0"; e.fog.near = 120; e.fog.far = 520; }
            if (e.global == null) return;
            bool first = true;
            foreach (var g in e.global)
            {
                if (g == null) continue;
                if (g.type == "HemisphereLight") { g.color = "#cfe4ff"; g.ground = "#d8c39a"; g.intensity = 1.5f; }
                else if (g.type == "DirectionalLight")
                {
                    if (first) { g.color = SunHex; g.intensity = SunPower; g.pos = new[] { Sun.x * 80, Sun.y * 80, Sun.z * 80 }; g.target = new[] { 0f, 0f, 0f }; first = false; }
                    else { g.color = "#cfe0ff"; g.intensity = .3f; g.pos = new[] { -40f, 30f, 30f }; g.target = new[] { 0f, 0f, 0f }; }
                }
            }
        }

        // exported colour (+ had a texture, - had none) -> surface
        static readonly Dictionary<string, string> Rules = new Dictionary<string, string>
        {
            { "b39a7c", "rock" }, { "9a9288", "rock" }, { "a48b6e", "rock" }, { "bfa585", "rock" }, { "9c8a74", "rock" }, { "a8927a", "rock" }, { "857d72", "rock" },
            { "c8a27a", "planks" }, { "b88a5a+", "planks" }, { "a8794c", "planks" }, { "6b4428", "planks" }, { "4a2f1c", "planks" }, { "3a2414", "planks" }, { "b88a5a-", "planks" },
            { "8a5a34", "post" }, { "5b3a22", "post" },
            { "c9a25a", "thatch" }, { "e9c46a+", "thatch" },
            { "ffffff+", "bamboo" },
            { "4f9a42", "grass" }, { "56a546", "leaf" }, { "3f8a3a", "leaf" },
            { "f4f5f7", "paint" }, { "d9dde3", "paint" }, { "f4efe6", "paint" }, { "ffffff-", "paint" }, { "e6e9ee", "paint" }, { "23252b", "paint" }, { "3a3d45", "paint" },
            { "2f7fe0-", "paint" }, { "f4c22b", "paint" }, { "e8463a", "paint" }, { "3bb54a-", "paint" }, { "f08a24", "paint" }, { "1f9a9a", "paint" }, { "a05ad6-", "paint" }, { "ff8fab", "paint" },
            { "e8563a", "paint" }, { "16171c", "paint" }, { "2a9d8f", "paint" }, { "1d3557", "paint" }, { "e9c46a-", "paint" },
        };

        // the two white-tinted textured materials that aren't bamboo: the woven panels, told apart by their texture's name
        static string RuleFor(Material src)
        {
            if (src && src.IsKeywordEnabled("_EMISSION")) return null;   // lit signs and lamps keep their own material
            string r = RailLook.RuleFor(src, Rules);
            if (r != "bamboo") return r;
            var t = src.GetTexture("_BaseMap"); string n = t ? t.name : "";
            if (n == "t16") return "wicker";
            if (n == "t31" || t && t.width > 256) return null;   // the palm fronds and anything else with a picture of its own
            return r;
        }

        public static void Apply(MapWorld w)
        {
            if (!w || !Application.isPlaying) return;
            var set = Resources.Load<RailLookSet>("BeachLook");
            if (set) { Surfaces(w, set); Sand(w, set); }
            foreach (var mr in w.GetComponentsInChildren<MeshRenderer>())
                if (mr.name == "Beach sea" && mr.sharedMaterial) { mr.sharedMaterial.SetFloat("_Gloss", 420); mr.sharedMaterial.SetFloat("_Glint", 3.2f); }
            var light = MapBakedLight.Add(w.gameObject, LightScene);
            light.strength = .8f;   // at full strength the sky's bounce bleaches the sand
            light.Room("Tiki Bar reflections", new Vector3(-30f, .4f, -9f), new Vector3(-4f, 6f, 9f));
        }

        static void Surfaces(MapWorld w, RailLookSet set)
        {
            var mb = w.builder; var e = w.entry;
            if (!mb || e == null || !e.mapJson || !e.mapBin) return;
            var gen = mb.transform.Find("__Generated"); var geo = gen ? gen.Find("Geometry") : null;
            if (!geo) return;
            var wj = JsonUtility.FromJson<WorldJson>(e.mapJson.text);
            if (wj == null || wj.meshes == null) return;
            var bin = e.mapBin.bytes;
            var made = new Dictionary<Material, Material>();
            int n = 0, hidden = 0;
            foreach (Transform t in geo)
            {
                string nm = t.name; int us = nm.IndexOf("_m", System.StringComparison.Ordinal);
                if (!nm.StartsWith("mesh") || us < 5) continue;
                if (!int.TryParse(nm.Substring(4, us - 4), out int i) || i < 0 || i >= wj.meshes.Length) continue;
                var mr = t.GetComponent<MeshRenderer>(); var mf = t.GetComponent<MeshFilter>();
                var src = mr ? mr.sharedMaterial : null;
                if (!src || !mf) continue;
                // glow cards: unlit, see-through and textured
                if (src.shader && src.shader.name.Contains("Unlit") && src.renderQueue >= 3000 && src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap")) { t.gameObject.SetActive(false); hidden++; continue; }
                string rule = RuleFor(src); if (rule == null) continue;
                var s = set.Get(rule); if (s == null || !s.material) continue;
                if (!made.TryGetValue(src, out var m)) { m = RailLook.Make(s, src); made[src] = m; }
                var mesh = MeshIO.Build(bin, wj.meshes[i], nm);
                RailLook.WorldUV(mesh, Mathf.Max(.1f, s.metres), rule == "grass", s.upright);
                var old = mf.sharedMesh; mf.sharedMesh = mesh; if (old) Object.Destroy(old);
                mesh.UploadMeshData(true);
                mr.sharedMaterial = m;
                n++;
            }
            Debug.Log("Beach look: " + n + " meshes on " + made.Count + " new surfaces, " + hidden + " glow cards hidden");
        }

        // the sand MapWorld built from the map's extras: mapped onto the sand texture's rectangle and given the standard lit sand
        static void Sand(MapWorld w, RailLookSet set)
        {
            if (!set.ground) return;
            foreach (var mr in w.GetComponentsInChildren<MeshRenderer>())
            {
                var src = mr.sharedMaterial;
                if (!src || !src.shader || src.shader.name != "StreetLife/Map Ground") continue;
                var mf = mr.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                SandUV(mf.sharedMesh);
                mr.sharedMaterial = set.ground; mr.receiveShadows = true;
            }
        }

        static void SandUV(Mesh mesh)
        {
            var v = mesh.vertices; var uv = new Vector2[v.Length]; var tan = new Vector4[v.Length];
            for (int i = 0; i < v.Length; i++) { uv[i] = new Vector2((v[i].x - SandX0) / SandW, (v[i].z - SandZ0) / SandH); tan[i] = new Vector4(1, 0, 0, -1); }
            mesh.SetUVs(0, uv); mesh.SetTangents(tan);
        }

        /// For the lighting bake (Editor/RailBake.cs): the beach's solid geometry with the surfaces the game shows, and its sand.
        public static void BuildForBake(Transform root, MapSet.Entry e, RailLookSet set)
        {
            RailLook.BuildForBake(root, e, set, RuleFor, "grass", "tree");
            if (!set || !set.ground || !e.extrasJson || !e.extrasBin) return;
            var x = JsonUtility.FromJson<ExtrasJson>(e.extrasJson.text); var bin = e.extrasBin.bytes;
            if (x == null || x.vc == null) return;
            foreach (var v in x.vc)
            {
                if (v.water || v.meshes == null) continue;
                foreach (int id in v.meshes)
                {
                    var mesh = XMeshIO.Build(bin, x.meshes[id], "sand" + id); SandUV(mesh);
                    RailLook.BakePart(root, "sand" + id, mesh, set.ground, Vector3.zero, Quaternion.identity, Vector3.one);
                }
            }
        }
    }
}
