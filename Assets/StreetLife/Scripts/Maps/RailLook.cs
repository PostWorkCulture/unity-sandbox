// Railed's own look, on top of what MapBuilder and MapWorld build for every map (two hook lines in MapWorld.Start):
//  - Env: the yard at golden hour. A low sun down the line from the south-west, a warm horizon, cool sky light in the shade.
//  - Surfaces: the flat-colour box materials are swapped for proper surfaces (brick, cast concrete, yard slabs, ballast,
//    ribbed steel, painted steel, roofing, timber, grass) with normal and smoothness maps. The boxes have no usable UVs,
//    so each face is mapped in world space: the textures run at real size and line up across neighbouring boxes.
//    Which surface a material gets is decided by its exported colour (and whether it had a texture), so a re-export
//    that renumbers the materials still lands on the right ones.
//  - Reflections: a box-projected probe inside each shed, and a sharper one for the yard.
//  - Bounce light (PC): Editor/RailBake.cs bakes the yard's indirect light into Adaptive Probe Volumes, kept in a small scene of
//    its own (LightScene) that is loaded on top of the game's when Railed is played. Not on WebGL, which has no probe volumes,
//    nor on the Mobile quality level: there the hemisphere light from Env stays.
//  - Signs: the flat glow cards behind the two shed signs are gone; each sign is lit by a row of lamps on arms above it,
//    so the light falls on the brick the way a real one does. The goods shed's sign reads SHED.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace StreetLife
{
    public class RailLook : MonoBehaviour
    {
        // direction to the sun: 22 degrees up, coming down the line from the south and a little from the west
        public static readonly Vector3 Sun = new Vector3(-.317f, .375f, -.871f);
        public const string SunHex = "#ffd09a";
        public const float SunPower = 3.0f;   // in the web game's units, like every map's lights (URP: divided by pi)
        public const string LightScene = "Assets/StreetLife/Maps/rail/Light/RailLight.unity";

        public static void Env(EnvJson e)
        {
            if (e == null) return;
            if (e.env != null)
            {
                e.env.top = "#3d7bd0"; e.env.mid = "#f4c8a0"; e.env.bot = "#d4a888"; e.env.ex = .42f;
                e.env.glow = "#ffc070"; e.env.glowA = .6f; e.env.disc = "#fff2c8"; e.env.discCos = .99945f;
                e.env.sd = new[] { Sun.x, Sun.y, Sun.z };
            }
            if (e.fog != null) { e.fog.color = "#dcc0a8"; e.fog.near = 80; e.fog.far = 500; }
            if (e.global == null) return;
            bool first = true;
            foreach (var g in e.global)
            {
                if (g == null) continue;
                if (g.type == "HemisphereLight") { g.color = "#b4c8ff"; g.ground = "#a08a78"; g.intensity = 1.75f; }
                else if (g.type == "DirectionalLight")
                {
                    if (first) { g.color = SunHex; g.intensity = SunPower; g.pos = new[] { Sun.x * 80, Sun.y * 80, Sun.z * 80 }; g.target = new[] { 0f, 0f, 0f }; first = false; }
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
            if (BakedLightAvailable()) SceneManager.LoadSceneAsync(LightScene, LoadSceneMode.Additive);
        }

        static bool BakedLightAvailable()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            var a = UniversalRenderPipeline.asset;
            return a && a.lightProbeSystem == LightProbeSystem.ProbeVolumes && SceneUtility.GetBuildIndexByScenePath(LightScene) >= 0;
#endif
        }

        /// For the lighting bake (Editor/RailBake.cs): the yard's solid geometry with the surfaces the game shows, and the trees.
        public static void BuildForBake(Transform root, MapSet.Entry e, RailLookSet set)
        {
            var lib = e.library;
            var wj = JsonUtility.FromJson<WorldJson>(e.mapJson.text); var bin = e.mapBin.bytes;
            var made = new Dictionary<Material, Material>();
            for (int i = 0; i < wj.meshes.Length; i++)
            {
                var rec = wj.meshes[i];
                var src = rec.mat >= 0 && rec.mat < lib.map.Length ? lib.map[rec.mat] : null;
                if (!src || src.renderQueue >= 2450 || (src.shader && src.shader.name.Contains("Unlit"))) continue;
                var mesh = MeshIO.Build(bin, rec, "bake" + i);
                var m = src; string rule = set ? RuleFor(src) : null; var s = rule != null ? set.Get(rule) : null;
                if (s != null && s.material)
                {
                    if (!made.TryGetValue(src, out m)) { m = Make(s, src); made[src] = m; }
                    WorldUV(mesh, Mathf.Max(.1f, s.metres), rule == "grass");
                }
                BakePart(root, "bake" + i, mesh, m, Vector3.zero, Quaternion.identity, Vector3.one);
            }
            if (!e.dynJson || !e.dynBin) return;
            var dj = JsonUtility.FromJson<DynJson>(e.dynJson.text); var db = e.dynBin.bytes;
            if (dj == null || dj.entities == null) return;
            foreach (var d in dj.entities)
            {
                if (d.kind != "tree" || d.meshes == null || d.pos == null || d.pos.Length < 3) continue;
                var rot = d.rot != null && d.rot.Length >= 4 ? new Quaternion(d.rot[0], d.rot[1], d.rot[2], d.rot[3]) : Quaternion.identity;
                var scl = d.scl != null && d.scl.Length >= 3 ? new Vector3(d.scl[0], d.scl[1], d.scl[2]) : Vector3.one;
                foreach (int mi in d.meshes)
                {
                    if (mi < 0 || mi >= dj.meshes.Length) continue;
                    var rec = dj.meshes[mi];
                    var src = rec.mat >= 0 && rec.mat < lib.map.Length ? lib.map[rec.mat] : null;
                    if (!src || src.renderQueue >= 3000) continue;
                    BakePart(root, "tree" + d.id, MeshIO.Build(db, rec, "tree" + mi), src, new Vector3(d.pos[0], d.pos[1], d.pos[2]), rot, scl);
                }
            }
        }

        static void BakePart(Transform root, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scl)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scl;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
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
            var signs = new List<WMesh>();
            int n = 0;
            foreach (Transform t in geo)
            {
                string nm = t.name; int us = nm.IndexOf("_m", System.StringComparison.Ordinal);
                if (!nm.StartsWith("mesh") || us < 5) continue;
                if (!int.TryParse(nm.Substring(4, us - 4), out int i) || i < 0 || i >= wj.meshes.Length) continue;
                var mr = t.GetComponent<MeshRenderer>(); var mf = t.GetComponent<MeshFilter>();
                var src = mr ? mr.sharedMaterial : null;
                if (!src || !mf) continue;
                var rec = wj.meshes[i];
                // the yellow halo card round the kiosk's SHOP sign goes the same way as the sign glows: the sign itself is bright enough to bloom
                if (src.shader && src.shader.name.Contains("Unlit") && src.renderQueue >= 3000 && rec.bsize != null && rec.bsize.Length == 3 && rec.bmin != null
                    && rec.bsize[1] > 1.5f && rec.bsize[1] < 3f && rec.bsize[2] > 3f && rec.bsize[2] < 5f && src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap") && NearKiosk(rec))
                { t.gameObject.SetActive(false); continue; }
                if (src.shader && src.shader.name.Contains("Unlit") && rec.bsize != null && rec.bsize.Length == 3 && rec.bmin != null && rec.bsize[0] < .02f && rec.bsize[2] > 6f)
                {
                    bool card = src.renderQueue >= 3000;
                    if (card && rec.bsize[1] > 2f) { t.gameObject.SetActive(false); continue; }   // the glow card behind a sign
                    if (!card && rec.bsize[1] <= 1.3f && src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap"))
                    {
                        signs.Add(rec);
                        if (rec.bmin[0] < 0 && set.shedSign) { var sm = new Material(src) { name = "Shed sign" }; sm.SetTexture("_BaseMap", set.shedSign); mr.sharedMaterial = sm; }
                        continue;
                    }
                }
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
            if (signs.Count > 0)
            {
                var root = new GameObject("Sign lamps").transform; root.SetParent(w.transform, false);
                var ms = set.Get("metal"); var lib = mb.library;
                Material arm = ms != null && ms.material ? new Material(ms.material) { name = "Sign lamp arm" } : null;
                if (arm) { arm.SetColor("_BaseColor", Hex.ToColor("#2b2d33")); arm.SetFloat("_Metallic", .6f); }
                Material bulb = lib && lib.unlitBase ? new Material(lib.unlitBase) { name = "Sign lamp bulb" } : null;
                if (bulb) bulb.SetColor("_BaseColor", new Color(4f, 3.1f, 2f, 1));
                foreach (var s in signs) SignLamps(root, s, arm, bulb);
            }
            Debug.Log("Railed look: " + n + " meshes on " + made.Count + " new surfaces, " + signs.Count + " signs");
        }

        static bool NearKiosk(WMesh rec)
        {
            var c = new Vector3(rec.bmin[0] + rec.bsize[0] / 2, 0, rec.bmin[2] + rec.bsize[2] / 2);
            foreach (var k in Kiosk.Here) if (Mathf.Abs(k.x - c.x) < 3f && Mathf.Abs(k.z - c.z) < 3f) return true;
            return false;
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
            tint *= s.shift; tint.a = 1; m.SetColor("_BaseColor", tint);
            m.SetFloat("_Metallic", Mathf.Max(s.metallic, src.HasProperty("_Metallic") ? src.GetFloat("_Metallic") : 0));
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

        // ------------------------------------------------------------------ sign lamps
        // a lamp on an arm every 2.5 m or so above the sign, shining down the wall
        static void SignLamps(Transform root, WMesh sign, Material arm, Material bulb)
        {
            float x = sign.bmin[0], top = sign.bmin[1] + sign.bsize[1], z0 = sign.bmin[2], len = sign.bsize[2];
            float side = x < 0 ? 1 : -1;   // the sign faces the line
            // the Mobile quality level lights each mesh with its four nearest lamps only, and the wall is one mesh: two wider lamps there
            bool few = QualitySettings.names[QualitySettings.GetQualityLevel()].Contains("Mobile");
            int count = few ? 2 : Mathf.Max(2, Mathf.RoundToInt(len / 2.5f));
            for (int k = 0; k < count; k++)
            {
                float z = z0 + len * (k + .5f) / count;
                var head = new Vector3(x + side * .55f, top + .5f, z);
                Block(root, "arm", new Vector3(x + side * .27f, top + .57f, z), new Vector3(.58f, .04f, .04f), arm);
                Block(root, "shade", head, new Vector3(.24f, .1f, .32f), arm);
                Block(root, "bulb", head + Vector3.down * .055f, new Vector3(.17f, .02f, .25f), bulb);
                var go = new GameObject("Sign lamp"); go.transform.SetParent(root, false);
                go.transform.position = head + Vector3.down * .1f;
                go.transform.rotation = Quaternion.LookRotation(new Vector3(-side * .3f, -1, 0).normalized);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.color = new Color(1f, .84f, .64f); l.intensity = few ? 8f : 6f; l.range = 8f;
                l.spotAngle = few ? 120 : 100; l.innerSpotAngle = 40; l.shadows = LightShadows.None;
            }
        }

        static void Block(Transform root, string name, Vector3 at, Vector3 size, Material mat)
        {
            if (!mat) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            go.transform.SetParent(root, false); go.transform.position = at; go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ reflections
        readonly List<ReflectionProbe> probes = new List<ReflectionProbe>();
        int wait = 5;
        bool baked;
        Volume bakedVolume;
        readonly List<(Light light, float intensity)> fills = new List<(Light, float)>();
        /// how much of the baked light is used (0..1): the bake is made a little bright so this can only turn it down
        public static float BakedLight = 1f;

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
            // the baked light comes and goes with its scene and with the Graphics setting (the Mobile level has no probe volumes)
            var prv = ProbeReferenceVolume.instance; var rp = UniversalRenderPipeline.asset;
            bool on = prv != null && prv.isInitialized && prv.DataHasBeenLoaded() && rp && rp.lightProbeSystem == LightProbeSystem.ProbeVolumes;
            if (on != baked) OnBaked(on);
            if (wait <= 0 || --wait > 0) return;
            var yard = GameObject.Find("Street reflections");   // WorldLook's probe over the whole map
            var yp = yard ? yard.GetComponent<ReflectionProbe>() : null;
            if (yp) { if (!Application.isMobilePlatform && yp.resolution < 512) yp.resolution = 512; yp.RenderProbe(); }
            foreach (var p in probes) if (p) p.RenderProbe();
        }

        // the baked bounce light has arrived: it replaces the flat hemisphere light, so the cool fill light (which reaches indoors
        // unshadowed) is turned down, and the reflections are taken again in the new light
        void OnBaked(bool on)
        {
            baked = on;
            if (!bakedVolume)
            {
                var prof = ScriptableObject.CreateInstance<VolumeProfile>(); prof.name = "Railed baked light";
                var o = prof.Add<ProbeVolumesOptions>(true);
                o.intensityMultiplier.Override(Mathf.Clamp01(BakedLight));
                bakedVolume = new GameObject("Baked light").AddComponent<Volume>(); bakedVolume.transform.SetParent(transform, false);
                bakedVolume.isGlobal = true; bakedVolume.priority = 6; bakedVolume.sharedProfile = prof;
                var sun = RenderSettings.sun;
                foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                    if (l.type == LightType.Directional && l != sun && l.shadows == LightShadows.None) fills.Add((l, l.intensity));
            }
            bakedVolume.enabled = on;
            foreach (var f in fills) if (f.light) f.light.intensity = f.intensity * (on ? .4f : 1f);
            wait = 4;
            Debug.Log("Railed look: baked light " + (on ? "on" : "off"));
        }
    }
}
