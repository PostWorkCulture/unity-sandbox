// Maps chat: everything a map needs beyond what MapBuilder builds (created by MapSet.Apply when the map isn't Street Life):
//  - its atmosphere: the web game's sky gradient, sun and fill light, hemisphere ambient and fog, plus Unity's own drifting
//    cloud layer in the sky shader and a per-map colour mood (Street Life too: it gets a MapWorld for the sky alone);
//  - its extras: vertex-coloured ground (Map Ground shader), sea or lagoon water (Map Water shader; the beach's sea is
//    generated here, dense near the shore and coarse far out), instanced crowds;
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
            Mood();
            if (extras != null && bin != null) { BuildGround(); BuildCrowd(); }
            if (entry.id == "wreck") BuildBeachSea();
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
                var look = Look(entry.id);
                m.SetFloat("_CloudCover", look.cover); m.SetColor("_CloudLit", look.lit); m.SetColor("_CloudShade", look.shade); m.SetFloat("_CloudAlpha", look.alpha);
                if (env.global != null) foreach (var g in env.global) if (g.type == "DirectionalLight") { m.SetColor("_SunCol", Hex.ToColor(g.color)); break; }
                RenderSettings.skybox = m;
            }
            // Lighting is set RELATIVE to the scene, never from the web game's raw numbers: the scene's sun, fill, ambient and fog are
            // what the match chat tuned for Street Life (exposure, character lighting), so each map only scales and tints them by how
            // its web game lights differ from Street Life's. Street Life itself keeps the scene's lighting untouched; and the ambient is
            // kept exactly as it was before the sky changed (a new skybox must never darken the characters).
            var probe = RenderSettings.ambientProbe;
            Color aSky = RenderSettings.ambientSkyColor, aEq = RenderSettings.ambientEquatorColor, aGnd = RenderSettings.ambientGroundColor, aFlat = RenderSettings.ambientLight;
            var mode = RenderSettings.ambientMode;
            if (!entry.IsStreet)
            {
                Split(env, out var hemi, out var sun, out var fill);
                EnvJson street = null;
                var se = set ? set.Get("street") : null;
                if (se != null && se.envJson) try { street = JsonUtility.FromJson<EnvJson>(se.envJson.text); } catch { }
                Split(street, out var sHemi, out var sSun, out var sFill);
                var sl = RenderSettings.sun;
                if (!sl) foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.type == LightType.Directional && l.shadows != LightShadows.None) { sl = l; break; }
                if (sl && sun != null)
                {
                    sl.color = Hex.ToColor(sun.color);
                    if (sSun != null && sSun.intensity > 0) sl.intensity *= sun.intensity / sSun.intensity;
                    sl.transform.rotation = LookDir(sun, sl.transform.forward);
                }
                if (fill != null)
                    foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                        if (l.type == LightType.Directional && l != sl)
                        {
                            l.color = Hex.ToColor(fill.color);
                            if (sFill != null && sFill.intensity > 0) l.intensity *= fill.intensity / sFill.intensity;
                            l.transform.rotation = LookDir(fill, l.transform.forward); break;
                        }
                if (hemi != null && sHemi != null)
                {
                    float k = sHemi.intensity > 0 ? hemi.intensity / sHemi.intensity : 1;
                    Color tSky = Ratio(Hex.ToColor(hemi.color), Hex.ToColor(sHemi.color)) * k, tGnd = Ratio(Hex.ToColor(hemi.ground), Hex.ToColor(sHemi.ground)) * k;
                    Color tMid = Color.Lerp(tSky, tGnd, .5f);
                    aSky *= tSky; aEq *= tMid; aGnd *= tGnd; aFlat *= tMid;
                    for (int c = 0; c < 3; c++) { float t = c == 0 ? tMid.r : c == 1 ? tMid.g : tMid.b; for (int i = 0; i < 9; i++) probe[c, i] *= t; }
                }
                if (env.fog != null && !string.IsNullOrEmpty(env.fog.color) && RenderSettings.fog)
                {
                    RenderSettings.fogColor = Hex.ToColor(env.fog.color);
                    if (RenderSettings.fogMode == FogMode.Linear) { RenderSettings.fogStartDistance = env.fog.near; RenderSettings.fogEndDistance = env.fog.far; }
                }
            }
            RenderSettings.ambientMode = mode;
            RenderSettings.ambientSkyColor = aSky; RenderSettings.ambientEquatorColor = aEq; RenderSettings.ambientGroundColor = aGnd; RenderSettings.ambientLight = aFlat;
            RenderSettings.ambientProbe = probe;   // no DynamicGI.UpdateEnvironment: that would rebuild the ambient from the new sky
        }

        static void Split(EnvJson e, out WLight hemi, out WLight sun, out WLight fill)
        {
            hemi = sun = fill = null;
            if (e == null || e.global == null) return;
            foreach (var g in e.global)
            {
                if (g.type == "HemisphereLight") hemi = g;
                else if (g.type == "DirectionalLight") { if (sun == null) sun = g; else if (fill == null) fill = g; }
            }
        }

        static Color Ratio(Color a, Color b) => new Color(b.r > .01f ? a.r / b.r : 1, b.g > .01f ? a.g / b.g : 1, b.b > .01f ? a.b / b.b : 1, 1);

        static Quaternion LookDir(WLight l, Vector3 fallback)
        {
            if (l != null && l.pos != null && l.target != null && l.pos.Length == 3 && l.target.Length == 3)
            {
                var d = new Vector3(l.target[0] - l.pos[0], l.target[1] - l.pos[1], l.target[2] - l.pos[2]);
                if (d.sqrMagnitude > 1e-6f) return Quaternion.LookRotation(d.normalized);
            }
            return Quaternion.LookRotation(fallback);
        }

        // ------------------------------------------------------------------ per-map look: cloud layer and colour mood
        struct LookSet { public float cover, alpha, temp, contrast, sat, bloom, ev; public Color lit, shade; }
        static LookSet Look(string id)
        {
            // ev: the web game draws with no tone mapping (three r158 default), while the shared profile uses ACES, which puts mid grey
            // about 0.8 stops lower; +0.6 brings the mid tones (and dark clothes such as Wednesday's) back to the web game's brightness
            // and leaves ACES's softer highlights.
            var l = new LookSet { cover = .52f, alpha = .95f, lit = Color.white, shade = new Color(.62f, .68f, .78f), bloom = -1, ev = .6f };
            switch (id)
            {
                case "wreck": l.cover = .45f; l.temp = 3; break;                                                         // bright beach day
                case "rail": l.cover = .55f; break;
                case "skyhigh": l.cover = .55f; l.lit = new Color(1f, .8f, .62f); l.shade = new Color(.55f, .38f, .45f); l.temp = 10; l.sat = 8; break;   // golden hour
                case "lagoon": l.cover = .52f; l.lit = new Color(1f, .82f, .86f); l.shade = new Color(.55f, .42f, .62f); l.temp = 6; l.sat = 10; break; // sunset
                case "arena": l.cover = .22f; l.alpha = .55f; l.lit = new Color(.2f, .23f, .36f); l.shade = new Color(.07f, .08f, .15f); l.contrast = 8; l.bloom = .7f; l.ev = .45f; break; // night
                case "house": case "yard": l.cover = .5f; break;
            }
            return l;
        }

        // a global volume on top of the shared one (priority 5) that nudges the shared look for the map: warmer, more saturated,
        // more contrast or more bloom. Values are the shared profile's plus the map's change, so the match chat's look stays the base.
        void Mood()
        {
            var l = Look(entry.id);
            if (l.temp == 0 && l.sat == 0 && l.contrast == 0 && l.bloom < 0 && l.ev == 0) return;
            VolumeProfile shared = null;
            foreach (var vol in FindObjectsByType<Volume>(FindObjectsInactive.Exclude)) if (vol.isGlobal && vol.sharedProfile) { shared = vol.sharedProfile; break; }
            float baseTemp = 0, baseSat = 0, baseCon = 0, baseEv = 0;
            if (shared)
            {
                if (shared.TryGet<UnityEngine.Rendering.Universal.WhiteBalance>(out var wb0) && wb0.temperature.overrideState) baseTemp = wb0.temperature.value;
                if (shared.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var ca0)) { if (ca0.saturation.overrideState) baseSat = ca0.saturation.value; if (ca0.contrast.overrideState) baseCon = ca0.contrast.value; if (ca0.postExposure.overrideState) baseEv = ca0.postExposure.value; }
            }
            var prof = ScriptableObject.CreateInstance<VolumeProfile>(); prof.name = "Mood " + entry.id;
            if (l.temp != 0) prof.Add<UnityEngine.Rendering.Universal.WhiteBalance>(true).temperature.Override(baseTemp + l.temp);
            if (l.sat != 0 || l.contrast != 0 || l.ev != 0)
            {
                var ca = prof.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
                if (l.ev != 0) ca.postExposure.Override(baseEv + l.ev);
                if (l.sat != 0) ca.saturation.Override(baseSat + l.sat);
                if (l.contrast != 0) ca.contrast.Override(baseCon + l.contrast);
            }
            if (l.bloom >= 0) prof.Add<UnityEngine.Rendering.Universal.Bloom>(true).intensity.Override(l.bloom);
            var v = new GameObject("Map mood").AddComponent<Volume>(); v.transform.SetParent(transform, false);
            v.isGlobal = true; v.priority = 5; v.sharedProfile = prof;
        }

        // Beach Please: the sea as one grid, dense where the waves run up the sand (0.7 m) and coarser further out (up to 40 m),
        // coloured like the web game's (pale turquoise shallows, mid blue, deep blue) and moved by the Map Water shader.
        void BuildBeachSea()
        {
            if (!set || !set.water) return;
            var xs = Steps(8, 700, 60, .7f, 1.08f, 40f); var zs = Sym(320, 70, 1.2f, 1.1f, 40f);
            int nx = xs.Count, nz = zs.Count; var v = new Vector3[nx * nz]; var col = new Color[nx * nz]; var nr = new Vector3[nx * nz];
            Color sh = Hex.ToColor("#7fe6da").linear, mid = Hex.ToColor("#22a9c8").linear, dp = Hex.ToColor("#0f4f86").linear;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = xs[i], z = zs[j], d = x - MapRules.Shore(-z); int k = j * nx + i;
                    v[k] = new Vector3(x, .28f, z); nr[k] = Vector3.up;
                    var c = Color.Lerp(sh, mid, Mathf.Clamp01(d / 18)); if (d > 18) c = Color.Lerp(c, dp, Mathf.Clamp01((d - 18) / 120)); c.a = 1; col[k] = c;
                }
            var idx = new int[(nx - 1) * (nz - 1) * 6]; int t = 0;
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = j * nx + i, b = a + 1, c2 = a + nx, d2 = c2 + 1;
                    idx[t++] = a; idx[t++] = c2; idx[t++] = b; idx[t++] = b; idx[t++] = c2; idx[t++] = d2;
                }
            var mesh = new Mesh { name = "beach sea", indexFormat = v.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = v; mesh.normals = nr; mesh.colors = col; mesh.triangles = idx;
            var b2 = mesh.bounds; b2.Expand(new Vector3(0, 2, 0)); mesh.bounds = b2;
            var go = new GameObject("Beach sea"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.shadowCastingMode = ShadowCastingMode.Off;
            var m = new Material(set.water) { name = "Water wreck" }; m.SetFloat("_Mode", 0); m.renderQueue = 3000; mr.sharedMaterial = m;
        }

        // from a to b: step s0 up to `until`, then growing by `grow` each step up to `max`
        static List<float> Steps(float a, float b, float until, float s0, float grow, float max)
        {
            var l = new List<float>(); float x = a, s = s0;
            while (x < b) { l.Add(x); if (x >= until) s = Mathf.Min(max, s * grow); x += s; }
            l.Add(b); return l;
        }
        static List<float> Sym(float half, float inner, float s0, float grow, float max)
        {
            var pos = Steps(0, half, inner, s0, grow, max); var l = new List<float>();
            for (int i = pos.Count - 1; i > 0; i--) l.Add(-pos[i]);
            l.AddRange(pos); return l;
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
            if (entry.id == "street") return;
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
