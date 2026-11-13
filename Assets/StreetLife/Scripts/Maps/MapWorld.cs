// Maps chat: everything a map needs beyond what MapBuilder builds (created by MapSet.Apply when the map isn't Street Life):
//  - its atmosphere: the web game's sky gradient, sun and fill light, hemisphere ambient, fog, the far box clouds;
//  - its extras: vertex-coloured ground (Map Ground shader), sea or lagoon water (Map Water shader), instanced crowds;
//  - its rules: trains and the live rail (RailYard), wading and sea sounds (Waterside), open edges ("MAN DOWN"),
//    edge glass that smashes when you walk into it (Sky High).
// Runs before RoundManager (order -60) so the trains' boxes are in the world before the bots' nav grid is built.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetLife
{
    [DefaultExecutionOrder(-60)]
    public class MapWorld : MonoBehaviour
    {
        public static MapWorld I { get; private set; }
        public MapSet.Entry entry;
        public MapSet set;
        public MapBuilder builder;
        public ExtrasJson extras;
        public EnvJson env;
        byte[] bin;
        readonly List<(Mesh mesh, Material mat, Matrix4x4[] m, Bounds b, bool cast)> crowd = new List<(Mesh, Material, Matrix4x4[], Bounds, bool)>();
        readonly List<int> touchBoxes = new List<int>();
        float bannerT = -9; string bannerText;
        Fighter[] fighters = new Fighter[0];

        public static void Create(MapBuilder mb, MapSet.Entry e, MapSet set)
        {
            var go = new GameObject("Map: " + e.title);
            var w = go.AddComponent<MapWorld>();
            w.entry = e; w.set = set; w.builder = mb;
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; Fighter.WentOverEdge -= OnOverEdge; Fighter.HitWater -= OnHitWater; RoundManager.RoundStarted -= OnRound; }

        void Start()
        {
            if (entry == null) { enabled = false; return; }
            try { env = entry.envJson ? JsonUtility.FromJson<EnvJson>(entry.envJson.text) : null; } catch (System.Exception ex) { Debug.LogError("Street Life maps: env.json: " + ex.Message); }
            try { extras = entry.extrasJson ? JsonUtility.FromJson<ExtrasJson>(entry.extrasJson.text) : null; } catch (System.Exception ex) { Debug.LogError("Street Life maps: extras.json: " + ex.Message); }
            bin = entry.extrasBin ? entry.extrasBin.bytes : null;
            ApplyEnv();
            if (extras != null && bin != null) { BuildGround(); BuildClouds(); BuildCrowd(); }
            if (extras != null && extras.trains != null && extras.trains.Length > 0 || extras != null && extras.glow != null && extras.glow.Length > 0)
                gameObject.AddComponent<RailYard>().Init(this);
            if (MapRules.Wading || MapRules.DeepWater) gameObject.AddComponent<Waterside>().Init(this);
            FindTouchGlass();
            QuietTownLife();
            Fighter.WentOverEdge += OnOverEdge; Fighter.HitWater += OnHitWater; RoundManager.RoundStarted += OnRound;
        }

        // ------------------------------------------------------------------ atmosphere
        void ApplyEnv()
        {
            if (env == null) return;
            if (env.env != null && set && set.sky)
            {
                var s = env.env; var m = new Material(set.sky) { name = "Sky " + entry.id };
                m.SetColor("_Top", Hex.ToColor(s.top)); m.SetColor("_Mid", Hex.ToColor(s.mid)); m.SetColor("_Bot", Hex.ToColor(s.bot));
                m.SetFloat("_Ex", s.ex); m.SetColor("_Glow", Hex.ToColor(s.glow)); m.SetFloat("_GlowA", s.glowA);
                m.SetColor("_Disc", Hex.ToColor(s.disc)); m.SetFloat("_DiscCos", s.discCos);
                if (s.sd != null && s.sd.Length == 3) m.SetVector("_SunDir", new Vector4(s.sd[0], s.sd[1], s.sd[2], 0));
                RenderSettings.skybox = m;
            }
            WLight hemi = null, sun = null, fill = null;
            if (env.global != null)
                foreach (var g in env.global)
                {
                    if (g.type == "HemisphereLight") hemi = g;
                    else if (g.type == "DirectionalLight") { if (sun == null) sun = g; else if (fill == null) fill = g; }
                }
            var sl = RenderSettings.sun;
            if (!sl) foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.type == LightType.Directional && l.shadows != LightShadows.None) { sl = l; break; }
            if (sl && sun != null)
            {
                sl.color = Hex.ToColor(sun.color); sl.intensity = sun.intensity / Mathf.PI;
                sl.transform.rotation = LookDir(sun, sl.transform.forward);
            }
            if (fill != null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                    if (l.type == LightType.Directional && l != sl) { l.color = Hex.ToColor(fill.color); l.intensity = fill.intensity / Mathf.PI; l.transform.rotation = LookDir(fill, l.transform.forward); break; }
            if (hemi != null)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                float hi = hemi.intensity / Mathf.PI; Color sky = Hex.ToColor(hemi.color), gnd = Hex.ToColor(hemi.ground);
                RenderSettings.ambientSkyColor = sky * hi; RenderSettings.ambientEquatorColor = Color.Lerp(sky, gnd, .5f) * hi; RenderSettings.ambientGroundColor = gnd * hi;
            }
            if (env.fog != null && !string.IsNullOrEmpty(env.fog.color))
            {
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = Hex.ToColor(env.fog.color);
                RenderSettings.fogStartDistance = env.fog.near; RenderSettings.fogEndDistance = env.fog.far;
            }
            DynamicGI.UpdateEnvironment();
        }

        static Quaternion LookDir(WLight l, Vector3 fallback)
        {
            if (l != null && l.pos != null && l.target != null && l.pos.Length == 3 && l.target.Length == 3)
            {
                var d = new Vector3(l.target[0] - l.pos[0], l.target[1] - l.pos[1], l.target[2] - l.pos[2]);
                if (d.sqrMagnitude > 1e-6f) return Quaternion.LookRotation(d.normalized);
            }
            return Quaternion.LookRotation(fallback);
        }

        // ------------------------------------------------------------------ extras
        public Material MapMat(int id) { var lib = builder ? builder.library : null; return lib && id >= 0 && id < lib.map.Length ? lib.map[id] : lib ? lib.unlitBase : null; }

        public GameObject MeshObject(int id, Transform parent, Material mat)
        {
            var xm = extras.meshes[id];
            var go = new GameObject("x" + id); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = XMeshIO.Build(bin, xm, go.name);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat ? mat : MapMat(xm.mat);
            bool transparent = mr.sharedMaterial && mr.sharedMaterial.renderQueue >= 3000;
            mr.shadowCastingMode = xm.cast && !transparent ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        void BuildGround()
        {
            if (extras.vc == null) return;
            var root = new GameObject("Ground and water").transform; root.SetParent(transform, false);
            foreach (var v in extras.vc)
                foreach (var id in v.meshes)
                {
                    var src = MapMat(extras.meshes[id].mat);
                    Material m;
                    if (v.water && set.water) { m = new Material(set.water) { name = "Water " + entry.id }; m.SetFloat("_Mode", entry.id == "lagoon" ? 1 : 0); m.renderQueue = 3000 + v.ro; }
                    else if (set.ground)
                    {
                        m = new Material(set.ground) { name = "Ground " + entry.id };
                        if (src) { if (src.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", src.GetTexture("_BaseMap")); if (src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor")); }
                    }
                    else m = src;
                    var go = MeshObject(id, root, m);
                    var mr = go.GetComponent<MeshRenderer>();
                    if (v.water) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true; go.GetComponent<MeshFilter>().sharedMesh.bounds = Grow(go.GetComponent<MeshFilter>().sharedMesh.bounds); }
                }
        }

        static Bounds Grow(Bounds b) { b.Expand(new Vector3(0, 2, 0)); return b; }   // the waves move the surface up and down

        void BuildClouds()
        {
            if (env == null || env.env == null || env.env.clouds == null || env.env.clouds.Length == 0) return;
            var root = new GameObject("Clouds").transform; root.SetParent(transform, false);
            var mats = new Dictionary<int, Material>();
            foreach (var id in env.env.clouds)
            {
                int mid = extras.meshes[id].mat;
                if (!mats.TryGetValue(mid, out var m))
                {
                    var src = MapMat(mid);
                    m = set.cloud ? new Material(set.cloud) : src;
                    if (set.cloud && src && src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                    mats[mid] = m;
                }
                var go = MeshObject(id, root, m); go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        void BuildCrowd()
        {
            if (extras.inst == null) return;
            foreach (var it in extras.inst)
            {
                if (it.m == null || it.m.Length < 16) continue;
                int n = it.m.Length / 16; var mx = new Matrix4x4[n]; Vector3 mn = Vector3.one * 1e9f, mxv = Vector3.one * -1e9f;
                for (int k = 0; k < n; k++)
                {
                    int o = k * 16; var f = it.m;
                    mx[k] = new Matrix4x4(new Vector4(f[o], f[o + 1], f[o + 2], f[o + 3]), new Vector4(f[o + 4], f[o + 5], f[o + 6], f[o + 7]), new Vector4(f[o + 8], f[o + 9], f[o + 10], f[o + 11]), new Vector4(f[o + 12], f[o + 13], f[o + 14], f[o + 15]));
                    var p = mx[k].GetColumn(3); mn = Vector3.Min(mn, p); mxv = Vector3.Max(mxv, p);
                }
                var b = new Bounds((mn + mxv) / 2, mxv - mn + Vector3.one * 6);
                foreach (var id in it.meshes)
                {
                    var xm = extras.meshes[id]; var src = MapMat(xm.mat); if (!src) continue;
                    var mat = new Material(src) { enableInstancing = true };
                    crowd.Add((XMeshIO.Build(bin, xm, "crowd" + id), mat, mx, b, xm.cast));
                }
            }
        }

        void LateUpdate()
        {
            foreach (var c in crowd)
            {
                var rp = new RenderParams(c.mat) { worldBounds = c.b, shadowCastingMode = c.cast ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
                for (int s = 0; s < c.m.Length; s += 1023) Graphics.RenderMeshInstanced(rp, c.mesh, 0, c.m, Mathf.Min(1023, c.m.Length - s), s);
            }
        }

        // ------------------------------------------------------------------ rules
        // Sky High's edge glass: walking into a pane smashes it (the web game's touchGlass)
        void FindTouchGlass()
        {
            if (!entry.mapJson) return;
            var w = JsonUtility.FromJson<WorldJson>(entry.mapJson.text);
            if (w == null || w.boxes == null) return;
            for (int i = 0; i < w.boxes.Length && i < BoxWorld.Boxes.Count; i++) if (w.boxes[i].touch) touchBoxes.Add(i);
        }

        void Update()
        {
            if (Time.frameCount % 30 == 0 || fighters.Length == 0) fighters = FindObjectsByType<Fighter>(FindObjectsInactive.Exclude);
            float dt = Time.deltaTime;
            foreach (var f in fighters)
            {
                if (!f) continue;
                if (!f.alive && f.EdgeFalling) { f.EdgeFallTick(Mathf.Min(dt, .05f)); FadeFaller(f); }
                else if (f.EdgeFalling) FadeFaller(f);
                if (touchBoxes.Count > 0 && f.alive && !f.Frozen && f.HorizontalSpeed > 1f) TouchGlass(f);
            }
        }

        void FadeFaller(Fighter f)
        {
            bool gone = MapRules.DeepWater ? f.EdgeWetTime > 3.2f : f.pos.y < -60f;
            if (!gone) return;
            var rig = f.GetComponent<CharacterRig>(); if (rig && rig.Visible) rig.SetVisible(false);
        }

        void TouchGlass(Fighter f)
        {
            const float R = Fighter.R + .08f; var B = BoxWorld.Boxes;
            foreach (var i in touchBoxes)
            {
                if (i >= B.Count || BoxWorld.IsDisabled(i)) continue;
                var b = B[i];
                if (b.x0 < f.pos.x + R && b.x1 > f.pos.x - R && b.z0 < f.pos.z + R && b.z1 > f.pos.z - R && b.y1 > f.pos.y + .02f && b.y0 < f.pos.y + f.Hh)
                {
                    var at = new Vector3(Mathf.Clamp(f.pos.x, b.x0, b.x1), f.pos.y + 1.1f, Mathf.Clamp(f.pos.z, b.z0, b.z1));
                    Destruction.Hit(i, 9999, at, new Vector3(f.vel.x, 0, f.vel.z).normalized, f);
                }
            }
        }

        void OnOverEdge(Fighter f)
        {
            bannerT = Time.time; bannerText = MapRules.DeepWater ? "MAN OVERBOARD" : "MAN DOWN";
            Sfx.Play2D("whiz", .9f); Sfx.Play2D("glboom", .6f);
        }

        void OnHitWater(Fighter f)
        {
            Sfx.Play("splash", f.pos, 1f, 40f, .75f);
            var ws = GetComponent<Waterside>(); if (ws) { ws.Splash(f.pos.x, f.pos.z, 2.6f); ws.Splash(f.pos.x + .6f, f.pos.z + .4f, 1.6f); }
        }

        void OnRound(int r)
        {
            fighters = FindObjectsByType<Fighter>(FindObjectsInactive.Exclude);
            foreach (var f in fighters) { if (!f) continue; f.EdgeReset(); var rig = f.GetComponent<CharacterRig>(); if (rig && !rig.Visible) rig.SetVisible(true); }
            bannerT = -9;
        }

        // Street Life's butterflies live in its front gardens; Railed and the beach keep the passing birds (the web game's townLife
        // and wcBirds), the other maps have neither
        void QuietTownLife()
        {
            var tl = FindAnyObjectByType<TownLife>(); if (!tl) return;
            if (entry.id != "rail" && entry.id != "wreck") { tl.gameObject.SetActive(false); return; }
            foreach (Transform c in tl.transform) if (c.name == "butterfly") c.gameObject.SetActive(false);
        }

        GUIStyle bannerSt;
        void OnGUI()
        {
            float a = Time.time - bannerT; if (a < 0 || a > 3.7f) return;
            float s = Screen.height / 1080f;
            if (bannerSt == null) bannerSt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            bannerSt.fontSize = Mathf.RoundToInt(96 * s);
            float k = a < .25f ? a / .25f : a > 3.1f ? (3.7f - a) / .6f : 1f;
            var r = new Rect(0, Screen.height * .28f, Screen.width, 140 * s);
            GUI.color = new Color(0, 0, 0, .6f * k); GUI.Label(new Rect(r.x + 4 * s, r.y + 5 * s, r.width, r.height), bannerText, bannerSt);
            GUI.color = new Color(1f, .25f, .2f, k); GUI.Label(r, bannerText, bannerSt); GUI.color = Color.white;
        }
    }
}
