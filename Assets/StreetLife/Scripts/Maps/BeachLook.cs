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
//  - Props: MapProps rebuilds the loungers, umbrellas and drums.
//  - The coloured steps up to the terrace: each step's plain block is clad in painted tongue-and-groove boards in its own
//    colour, with white corner boards, a white nosing on every tread edge and a kickboard at the sand (Steps).
//  - The Tiki Bar is refitted and filled with things to smash (TikiBar.cs).
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
            if (set) { Surfaces(w, set); Sand(w, set); MapProps.Beach(w, set); TikiBar.Build(w, set); }
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
            var rainbow = new List<(Vector3[] verts, Color colour)>();
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
                string rule = RuleFor(src);
                if (rule == null)
                {   // keeps its own material; still loses whatever the new Tiki Bar replaces (the lamp shades)
                    if (TikiBar.Touches(wj.meshes[i]))
                    {
                        var own = MeshIO.Build(bin, wj.meshes[i], nm); int cut = TikiBar.Cut(own);
                        if (cut == 2) { var was = mf.sharedMesh; mf.sharedMesh = own; if (was) Object.Destroy(was); own.UploadMeshData(true); }
                        else { Object.Destroy(own); if (cut == 0) t.gameObject.SetActive(false); }
                    }
                    continue;
                }
                var s = set.Get(rule); if (s == null || !s.material) continue;
                if (!made.TryGetValue(src, out var m)) { m = RailLook.Make(s, src); made[src] = m; }
                var mesh = MeshIO.Build(bin, wj.meshes[i], nm);
                if (TikiBar.Cut(mesh) == 0) { Object.Destroy(mesh); t.gameObject.SetActive(false); continue; }   // old Tiki Bar fittings that TikiBar replaces
                RailLook.WorldUV(mesh, Mathf.Max(.1f, s.metres), rule == "grass", s.upright);
                if (rule == "paint" && Rainbow.Contains(ColorUtility.ToHtmlStringRGB(src.GetColor("_BaseColor")).ToLowerInvariant())) rainbow.Add((mesh.vertices, src.GetColor("_BaseColor")));
                var old = mf.sharedMesh; mf.sharedMesh = mesh; if (old) Object.Destroy(old);
                mesh.UploadMeshData(true);
                mr.sharedMaterial = m;
                n++;
            }
            int steps = Steps(w, set, wj, rainbow);
            Debug.Log("Beach look: " + n + " meshes on " + made.Count + " new surfaces, " + hidden + " glow cards hidden, " + steps + " steps clad");
        }

        // ------------------------------------------------------------------ the coloured steps
        static readonly HashSet<string> Rainbow = new HashSet<string> { "2f7fe0", "f4c22b", "e8463a", "3bb54a", "f08a24", "1f9a9a", "a05ad6", "ff8fab" };

        // A step is a collision box a metre wide standing on the sand whose top front corners are corners of one of the rainbow
        // meshes: that also tells its colour. The new cladding sits a centimetre outside the old block, so the block is hidden
        // and nothing about how you walk the steps changes.
        static int Steps(MapWorld w, RailLookSet set, WorldJson wj, List<(Vector3[] verts, Color colour)> rainbow)
        {
            var boards = set.Get("boards"); var paint = set.Get("paint");
            if (boards == null || !boards.material || paint == null || !paint.material || wj.boxes == null || rainbow.Count == 0) return 0;
            var steps = new List<(WBox b, Color c)>();
            foreach (var b in wj.boxes)
            {
                if (b.y0 > .5f || Mathf.Abs(b.z1 - b.z0 - 1f) > .03f || b.y1 - b.y0 < .3f) continue;
                foreach (var r in rainbow)
                    if (Has(r.verts, b.x1, b.y1, b.z0) && Has(r.verts, b.x1, b.y1, b.z1)) { steps.Add((b, r.colour)); break; }
            }
            if (steps.Count == 0) return 0;
            const float d = .01f;
            var bodies = new Dictionary<Color, MapProps.Mb>(); var trim = new MapProps.Mb();
            foreach (var (b, c) in steps)
            {
                if (!bodies.TryGetValue(c, out var mb)) { mb = new MapProps.Mb(); bodies[c] = mb; }
                mb.Box(new Vector3((b.x0 + b.x1 + d) / 2, (b.y0 + b.y1 + d) / 2, (b.z0 + b.z1) / 2), new Vector3(b.x1 + d - b.x0, b.y1 + d - b.y0, b.z1 - b.z0 + d * 2));
                float zc = (b.z0 + b.z1) / 2, top = b.y1 + d;
                // nosing along the front of the tread, and along each side that drops to a lower step or to the sand
                trim.Box(new Vector3(b.x1 - .005f, top - .02f, zc), new Vector3(.08f, .065f, b.z1 - b.z0 + .03f), .014f, Quaternion.identity);
                foreach (float ze in new[] { b.z0, b.z1 })
                {
                    float beyond = ze == b.z0 ? ze - .5f : ze + .5f;
                    if (TopAt(wj, b.x1 - .6f, beyond, b.y1 + .2f) > b.y1 - .1f) continue;
                    trim.Box(new Vector3((b.x0 + b.x1) / 2 + .015f, top - .02f, ze), new Vector3(b.x1 - b.x0 + .03f, .065f, .08f), .014f, Quaternion.identity);
                }
                // corner boards up the front, and a kickboard along the sand
                foreach (float ze in new[] { b.z0 + .03f, b.z1 - .03f })
                    trim.Box(new Vector3(b.x1 + d + .008f, (b.y0 + top - .05f) / 2, ze), new Vector3(.02f, top - .05f - b.y0, .06f));
                trim.Box(new Vector3(b.x1 + d + .01f, b.y0 + .07f, zc), new Vector3(.025f, .14f, b.z1 - b.z0));
            }
            var root = new GameObject("Coloured steps").transform; root.SetParent(w.transform, false);
            foreach (var kv in bodies)
            {
                var mesh = kv.Value.Build("steps"); RailLook.WorldUV(mesh, Mathf.Max(.1f, boards.metres), false, boards.upright);
                var m = new Material(boards.material) { name = "step boards" }; var c = kv.Key; c.a = 1; m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 1f);
                Piece(root, "steps", mesh, m);
            }
            var tm = trim.Build("step trim"); RailLook.WorldUV(tm, Mathf.Max(.1f, paint.metres), false, false);
            var white = new Material(paint.material) { name = "step trim" }; white.SetColor("_BaseColor", Hex.ToColor("#f4f1ea")); white.SetFloat("_Smoothness", 1f);
            Piece(root, "trim", tm, white);
            return steps.Count;
        }

        static bool Has(Vector3[] verts, float x, float y, float z)
        {
            foreach (var v in verts) if (Mathf.Abs(v.x - x) < .03f && Mathf.Abs(v.y - y) < .03f && Mathf.Abs(v.z - z) < .03f) return true;
            return false;
        }

        // how high the solid ground stands at a spot (the tallest collision box under it that starts below the given height)
        static float TopAt(WorldJson wj, float x, float z, float below)
        {
            float top = 0;
            foreach (var b in wj.boxes) if (x > b.x0 && x < b.x1 && z > b.z0 && z < b.z1 && b.y0 < below && b.y1 > top) top = b.y1;
            return top;
        }

        static void Piece(Transform root, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            mesh.RecalculateBounds(); mesh.UploadMeshData(true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
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
