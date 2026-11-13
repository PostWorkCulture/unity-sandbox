// Railed's own look, on top of what MapBuilder and MapWorld build for every map (two hook lines in MapWorld.Start):
//  - Env: the yard at golden hour. A low sun down the line from the south-west, a warm horizon, cool sky light in the shade.
//  - Surfaces: the flat-colour box materials are swapped for proper surfaces (brick, cast concrete, yard slabs, ballast,
//    ribbed steel, painted steel, roofing, timber, grass) with normal and smoothness maps. The boxes have no usable UVs,
//    so each face is mapped in world space: the textures run at real size and line up across neighbouring boxes.
//    Which surface a material gets is decided by its exported colour (and whether it had a texture), so a re-export
//    that renumbers the materials still lands on the right ones.
//  - Reflections: a box-projected probe inside each shed, and a sharper one for the yard.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetLife
{
    public class RailLook : MonoBehaviour
    {
        // direction to the sun: 22 degrees up, coming down the line from the south and a little from the west
        public static readonly Vector3 Sun = new Vector3(-.317f, .375f, -.871f);

        public static void Env(EnvJson e)
        {
            if (e == null) return;
            if (e.env != null)
            {
                e.env.top = "#2c5cab"; e.env.mid = "#f2b47c"; e.env.bot = "#c78d6a"; e.env.ex = .5f;
                e.env.glow = "#ffc070"; e.env.glowA = .85f; e.env.disc = "#fff2c8"; e.env.discCos = .99945f;
                e.env.sd = new[] { Sun.x, Sun.y, Sun.z };
            }
            if (e.fog != null) { e.fog.color = "#e6b78f"; e.fog.near = 60; e.fog.far = 420; }
            if (e.global == null) return;
            bool first = true;
            foreach (var g in e.global)
            {
                if (g == null) continue;
                if (g.type == "HemisphereLight") { g.color = "#c9d6ff"; g.ground = "#b08d6e"; g.intensity = 1.4f; }
                else if (g.type == "DirectionalLight")
                {
                    if (first) { g.color = "#ffc58a"; g.intensity = 3.1f; g.pos = new[] { Sun.x * 80, Sun.y * 80, Sun.z * 80 }; g.target = new[] { 0f, 0f, 0f }; first = false; }
                    else { g.color = "#a8c0ff"; g.intensity = .4f; g.pos = new[] { 30f, 25f, 40f }; g.target = new[] { 0f, 0f, 0f }; }
                }
            }
        }

        public static void Apply(MapWorld w)
        {
            if (!w || !Application.isPlaying) return;
            var set = Resources.Load<RailLookSet>("RailLook");
            if (set) Surfaces(w, set);
            w.gameObject.AddComponent<RailLook>();
        }

        // ------------------------------------------------------------------ surfaces
        // exported colour (+ had a texture, - had none) -> surface
        static readonly Dictionary<string, string> Rules = new Dictionary<string, string>
        {
            { "86a85a", "grass" }, { "c4beb2", "slabs" }, { "8f877b", "ballast" },
            { "a39e94", "concrete" }, { "bdb5a6", "concrete" }, { "a9a196", "concrete" }, { "9a9286", "concrete" }, { "d6cfc2", "concrete" }, { "8d8a83", "concrete" }, { "5b6070+", "concrete" },
            { "a2583f", "brick" }, { "6a4a3c", "brick" }, { "a5543a", "brick" }, { "8a6a55", "brick" },
            { "9d978b", "floor" }, { "7d776c", "floor" },
            { "7a7f88", "roofing" }, { "6a6f79", "roofing" },
            { "6e737c", "corrugated" }, { "7d838c", "corrugated" }, { "e8463a+", "corrugated" }, { "2f7fe0+", "corrugated" }, { "f4c22b+", "corrugated" }, { "3bb54a+", "corrugated" },
            { "5b6070-", "metal" }, { "3a3d45", "metal" }, { "1c1d22", "metal" }, { "9aa0a8", "metal" }, { "2b2d33", "metal" }, { "8f959c", "metal" }, { "b8bec6", "metal" },
            { "eef0f3", "metal" }, { "16171c", "metal" }, { "f4c22b-", "metal" }, { "8a8f99", "metal" }, { "3bb54a-", "metal" },
            { "6b4a33", "wood" }, { "5b4a3a", "wood" }, { "5e4236", "wood" },
        };

        static string RuleFor(Material src)
        {
            if (!src || !src.shader || src.shader.name.Contains("Unlit") || src.renderQueue >= 2450 || !src.HasProperty("_BaseColor")) return null;
            string hex = ColorUtility.ToHtmlStringRGB(src.GetColor("_BaseColor")).ToLowerInvariant();
            bool tex = src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap");
            if (Rules.TryGetValue(hex + (tex ? "+" : "-"), out var s)) return s;
            return Rules.TryGetValue(hex, out s) ? s : null;
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
            int n = 0;
            foreach (Transform t in geo)
            {
                string nm = t.name; int us = nm.IndexOf("_m", System.StringComparison.Ordinal);
                if (!nm.StartsWith("mesh") || us < 5) continue;
                if (!int.TryParse(nm.Substring(4, us - 4), out int i) || i < 0 || i >= wj.meshes.Length) continue;
                var mr = t.GetComponent<MeshRenderer>(); var mf = t.GetComponent<MeshFilter>();
                var src = mr ? mr.sharedMaterial : null;
                if (!src || !mf) continue;
                string rule = RuleFor(src); if (rule == null) continue;
                var s = set.Get(rule); if (s == null || !s.material) continue;
                if (!made.TryGetValue(src, out var m)) { m = Make(s, src); made[src] = m; }
                var mesh = MeshIO.Build(bin, wj.meshes[i], nm);
                WorldUV(mesh, Mathf.Max(.1f, s.metres), rule == "grass");
                var old = mf.sharedMesh; mf.sharedMesh = mesh; if (old) Destroy(old);
                mesh.UploadMeshData(true);
                mr.sharedMaterial = m;
                n++;
            }
            Debug.Log("Railed look: " + n + " meshes on " + made.Count + " new surfaces");
        }

        static Material Make(RailLookSet.Surface s, Material src)
        {
            var m = new Material(s.material) { name = s.name + " (" + src.name + ")" };
            Color o = src.GetColor("_BaseColor"), tint = o;
            if (!s.tinted)
            {
                Color ol = o.linear, rl = s.reference.linear;
                tint = new Color(Mathf.Min(ol.r / Mathf.Max(rl.r, .01f), 1.6f), Mathf.Min(ol.g / Mathf.Max(rl.g, .01f), 1.6f), Mathf.Min(ol.b / Mathf.Max(rl.b, .01f), 1.6f), 1).gamma;
            }
            tint.a = 1; m.SetColor("_BaseColor", tint);
            if (src.HasProperty("_Metallic")) m.SetFloat("_Metallic", src.GetFloat("_Metallic"));
            float gloss = src.HasProperty("_Smoothness") ? src.GetFloat("_Smoothness") : .28f;
            m.SetFloat("_Smoothness", s.keepGloss ? Mathf.Clamp01(.55f + gloss * .7f) : 1f);   // times the texture's own smoothness (albedo alpha)
            if (src.HasProperty("_Cull")) m.SetFloat("_Cull", src.GetFloat("_Cull"));
            m.enableInstancing = true;
            return m;
        }

        // every vertex is mapped along the axis its face looks down; the tangent follows the mapping so the normal map lights correctly
        static void WorldUV(Mesh mesh, float metres, bool fromAbove)
        {
            var v = mesh.vertices; var nr = mesh.normals;
            var uv = new Vector2[v.Length]; var tan = new Vector4[v.Length];
            float k = 1f / metres;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = v[i], q = nr[i];
                float ax = Mathf.Abs(q.x), ay = Mathf.Abs(q.y), az = Mathf.Abs(q.z);
                if (fromAbove || (ay >= ax && ay >= az)) { uv[i] = new Vector2(p.x * k, p.z * k); tan[i] = new Vector4(1, 0, 0, q.y >= 0 ? -1 : 1); }
                else if (ax >= az) { uv[i] = new Vector2(p.z * k, p.y * k); tan[i] = new Vector4(0, 0, 1, q.x >= 0 ? -1 : 1); }
                else { uv[i] = new Vector2(p.x * k, p.y * k); tan[i] = new Vector4(1, 0, 0, q.z >= 0 ? 1 : -1); }
            }
            mesh.SetUVs(0, uv); mesh.SetTangents(tan);
        }

        // ------------------------------------------------------------------ reflections
        readonly List<ReflectionProbe> probes = new List<ReflectionProbe>();
        int wait = 5;

        void Start()
        {
            // the two sheds (their walls, from the exported map): what a floor or a window reflects indoors is the room, not the sky
            Shed("Goods shed reflections", new Vector3(-22.2f, 0, -16.2f), new Vector3(-8.8f, 6.6f, 10.2f));
            Shed("Engine shed reflections", new Vector3(7.8f, 0, -14.2f), new Vector3(22.1f, 7f, 8.2f));
            RoundManager.RoundStarted += OnRound;
        }

        void OnDestroy() { RoundManager.RoundStarted -= OnRound; }
        void OnRound(int r) { wait = 3; }

        void Shed(string name, Vector3 min, Vector3 max)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            Vector3 c = (min + max) / 2;
            go.transform.position = new Vector3(c.x, min.y + 1.9f, c.z);
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime; p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            p.resolution = Application.isMobilePlatform ? 128 : 512; p.hdr = true;
            p.center = c - go.transform.position; p.size = max - min + new Vector3(.4f, .4f, .4f);
            p.boxProjection = true; p.blendDistance = 1.2f; p.importance = 2;
            p.clearFlags = ReflectionProbeClearFlags.Skybox; p.nearClipPlane = .1f; p.farClipPlane = 300; p.shadowDistance = 50;
            probes.Add(p);
        }

        void Update()
        {
            if (wait <= 0 || --wait > 0) return;
            if (!Application.isMobilePlatform)
            {
                var yard = GameObject.Find("Street reflections");   // WorldLook's probe over the whole map
                var yp = yard ? yard.GetComponent<ReflectionProbe>() : null;
                if (yp && yp.resolution < 512) { yp.resolution = 512; yp.RenderProbe(); }
            }
            foreach (var p in probes) if (p) p.RenderProbe();
        }
    }
}
