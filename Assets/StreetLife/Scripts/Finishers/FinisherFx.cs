// Finishers chat: the finishing moves' effects, in the web game's blocky style but lit and shadowed:
// particles (small spinning cubes that slow and shrink), debris (cubes that bounce on whatever floor they land on),
// shatter (a fighter bursting into cubes of their own colours, sampled from their textures), scorch decals,
// flash balls with a real light, and electric arcs. Every cube of one colour is drawn in a single instanced call.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetLife
{
    public class FinisherFx : MonoBehaviour
    {
        static FinisherFx inst;
        static FinisherFx I { get { if (!inst) inst = new GameObject("FinisherFx").AddComponent<FinisherFx>(); return inst; } }

        // ------------------------------------------------------------------ cubes
        struct Cube
        {
            public Vector3 p, v, w; public Quaternion r;
            public float s, age, ttl, fl, grav, flT;
            public int mat; public bool debris;
        }
        Cube[] cubes = new Cube[2048]; int nCubes;
        Mesh cubeMesh;
        readonly Dictionary<int, Material> mats = new Dictionary<int, Material>();
        readonly Dictionary<int, Matrix4x4[]> bufs = new Dictionary<int, Matrix4x4[]>();
        readonly Dictionary<int, int> counts = new Dictionary<int, int>();
        readonly List<int> keys = new List<int>();
        static readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
        static Shader litShader, unlitShader;

        public static Color C(string hex)
        {
            if (!colors.TryGetValue(hex, out var c)) { c = Hex.ToColor(hex); colors[hex] = c; }
            return c;
        }

        void Awake() { cubeMesh = MakeCube(); }

        static Mesh MakeCube()
        {
            var m = new Mesh { name = "fin cube" };
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                Vector3 a = d.y != 0 ? Vector3.right : Vector3.up, b = Vector3.Cross(d, a);
                int i = v.Count;
                v.Add((d - a - b) * .5f); v.Add((d + a - b) * .5f); v.Add((d + a + b) * .5f); v.Add((d - a + b) * .5f);
                for (int k = 0; k < 4; k++) n.Add(d);
                t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            // make sure every face is front-facing from outside (Unity: clockwise = front, whose cross product points at the viewer)
            var tris = m.triangles; var vs = m.vertices;
            for (int i = 0; i < tris.Length; i += 3)
            {
                var c = Vector3.Cross(vs[tris[i + 1]] - vs[tris[i]], vs[tris[i + 2]] - vs[tris[i]]);
                var mid = (vs[tris[i]] + vs[tris[i + 1]] + vs[tris[i + 2]]) / 3f;
                if (Vector3.Dot(c, mid) < 0) { var q = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = q; }
            }
            m.triangles = tris;
            return m;
        }

        public static Shader Lit
        {
            get
            {
                if (!litShader) litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (!litShader) { var lib = AnyLibrary(); if (lib && lib.character != null) foreach (var m in lib.character) if (m && m.shader && m.shader.name.Contains("Lit") && !m.shader.name.Contains("Unlit")) { litShader = m.shader; break; } }
                if (!litShader) litShader = Shader.Find("Standard");
                return litShader;
            }
        }
        public static Shader Unlit
        {
            get
            {
                if (!unlitShader) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (!unlitShader) { var lib = AnyLibrary(); if (lib && lib.unlitBase) unlitShader = lib.unlitBase.shader; }
                return unlitShader;
            }
        }
        static MaterialLibrary AnyLibrary() { var r = FindAnyObjectByType<CharacterRig>(); return r ? r.library : null; }

        /// A plain lit material (props, debris). Smoothness from the web game's roughness.
        public static Material LitMat(Color c, float rough = .7f, float metal = 0, Color? emissive = null, float emissiveK = 0)
        {
            var m = new Material(Lit) { enableInstancing = true };
            m.SetColor("_BaseColor", c); m.SetColor("_Color", c);
            m.SetFloat("_Smoothness", Mathf.Clamp01(1 - rough) * .92f); m.SetFloat("_Metallic", metal);
            if (emissive.HasValue && emissiveK > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emissive.Value * emissiveK); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            return m;
        }
        public static Material UnlitMat(Color c)
        {
            var m = new Material(Unlit) { enableInstancing = true };
            m.SetColor("_BaseColor", c); m.SetColor("_Color", c);
            return m;
        }
        /// See-through unlit (alpha blended, no depth write) for flashes, decals and the ice block's glow.
        public static Material FadeMat(Color c, bool additive = false, bool lit = false)
        {
            var m = new Material(lit ? Lit : Unlit);
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
            m.SetColor("_BaseColor", c); m.SetColor("_Color", c);
            return m;
        }
        /// Black inside-out hull for the web game's ink outlines (OUTL: #16171c, back faces, scaled 1.06).
        static Material outline;
        public static Material Outline
        {
            get
            {
                if (!outline) { outline = UnlitMat(C("#16171c")); outline.SetFloat("_Cull", 1); }
                return outline;
            }
        }

        int MatKey(Color c, int kind)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 40), 0, 40), g = Mathf.Clamp(Mathf.RoundToInt(c.g * 40), 0, 40), b = Mathf.Clamp(Mathf.RoundToInt(c.b * 40), 0, 40);
            int key = ((r * 41 + g) * 41 + b) * 4 + kind;
            if (!mats.ContainsKey(key))
            {
                var q = new Color(r / 40f, g / 40f, b / 40f);
                mats[key] = kind == 0 ? LitMat(q) : UnlitMat(q);
                bufs[key] = new Matrix4x4[256]; counts[key] = 0; keys.Add(key);
            }
            return key;
        }

        int NewCube()
        {
            if (nCubes >= cubes.Length) { int oldest = 0; for (int i = 1; i < nCubes; i++) if (cubes[i].age / cubes[i].ttl > cubes[oldest].age / cubes[oldest].ttl) oldest = i; return oldest; }
            return nCubes++;
        }

        // ------------------------------------------------------------------ web: emit (particles)
        /// web emit: n small cubes flung out in random directions at speed*(.4..1.2), plus `up`; slowed by drag, pulled by grav,
        /// shrinking away over life*(.7..1.3) s. Unlit (sparks, fire, dust, smoke).
        public static void Emit(Vector3 p, int n, float speed, float size, float life, float grav, string[] cols, float up = 0, Vector3 vel = default)
        {
            var me = I;
            for (int k = 0; k < n; k++)
            {
                float a = Random.value * Mathf.PI * 2, u = Random.value * 2 - 1, s = Mathf.Sqrt(1 - u * u), sp = speed * (.4f + Random.value * .8f);
                int i = me.NewCube();
                ref var c = ref me.cubes[i];
                c.p = p; c.v = new Vector3(Mathf.Cos(a) * s * sp + vel.x, u * sp + up + vel.y, Mathf.Sin(a) * s * sp + vel.z);
                c.w = new Vector3(6, 0, 0) * Mathf.Rad2Deg; c.r = Quaternion.Euler(Random.value * 340, Random.value * 340, 0);
                c.s = size * (.6f + Random.value * .8f); c.age = 0; c.ttl = life * (.7f + Random.value * .6f); c.grav = grav;
                c.fl = FinGeo.GroundAt(p.x, p.z, p.y) + .03f; c.flT = 0; c.debris = false;
                c.mat = me.MatKey(C(cols[Random.Range(0, cols.Length)]), 1);
            }
        }

        /// web spray: lit cubes of the given colours thrown out from c (bounce on the floor, shrink away after 2.2-3.4 s).
        public static void Spray(Vector3 c, int n, string[] cols, float s0, float s1, Vector3? box, float power, float up, Vector3? vel = null, float? floor = null)
        {
            var me = I; var h = box ?? new Vector3(.3f, .3f, .3f);
            float fl = floor ?? FinGeo.GroundAt(c.x, c.z, c.y);
            for (int i = 0; i < n; i++)
            {
                float s = Random.Range(s0, s1);
                var p = c + new Vector3(Random.Range(-h.x, h.x), Random.Range(-h.y, h.y), Random.Range(-h.z, h.z));
                var d = p - c; d.y = Mathf.Max(d.y, 0) * .5f; if (d.sqrMagnitude < 1e-4f) d = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)); d.Normalize();
                float k = power * Random.Range(.5f, 1.2f);
                var v = new Vector3(d.x * k, up * Random.Range(.5f, 1.3f) + d.y * k, d.z * k); if (vel.HasValue) v += vel.Value;
                me.AddDebris(p, v, s, Random.Range(2.2f, 3.4f), fl + s / 2, C(cols[i % cols.Length]));
            }
        }

        void AddDebris(Vector3 p, Vector3 v, float s, float ttl, float fl, Color col)
        {
            int i = NewCube(); ref var c = ref cubes[i];
            c.p = p; c.v = v; c.r = Quaternion.Euler(Random.value * 170, Random.value * 170, 0);
            c.w = new Vector3(Random.Range(-10f, 10f), Random.Range(-10f, 10f), Random.Range(-10f, 10f)) * Mathf.Rad2Deg;
            c.s = s; c.age = 0; c.ttl = ttl; c.fl = fl; c.flT = .1f; c.grav = 30; c.debris = true; c.mat = MatKey(col, 0);
        }

        /// The web game's hit spray: small dark-red droplets.
        static readonly string[] SprayRed = { "#c8141e", "#e8303a", "#8e0d14" };
        public static void HitSpray(Vector3 p, int n = 6, float k = 1) { Emit(p, n, 3.4f * k, .045f * k, .38f, 14, SprayRed, 1); }

        /// web bloodBits: a few small red blocks round a body on the ground (they stay 30 s).
        public static void BloodBits(Vector3 at, int n)
        {
            var me = I;
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2, r = Random.Range(.35f, 1.1f), x = at.x + Mathf.Cos(a) * r, z = at.z + Mathf.Sin(a) * r, s = Random.Range(.07f, .14f);
                float y = FinGeo.GroundAt(x, z, at.y) + s / 2;
                int j = me.NewCube(); ref var c = ref me.cubes[j];
                c.p = new Vector3(x, y, z); c.v = Vector3.zero; c.w = Vector3.zero; c.r = Quaternion.Euler(0, Random.value * 170, 0);
                c.s = s; c.age = 0; c.ttl = 30; c.fl = y; c.flT = 99; c.grav = 0; c.debris = true; c.mat = me.MatKey(C("#b3121f"), 0);
            }
        }

        // ------------------------------------------------------------------ shatter
        static readonly List<Renderer> partRend = new List<Renderer>();
        public struct ShatterOpts { public float power, up, size, max; public float? floor; public Vector3? from, vel; }
        public static ShatterOpts SO(float power = 8, float up = 4, float? floor = null, Vector3? from = null, Vector3? vel = null, float size = .17f) =>
            new ShatterOpts { power = power, up = up, floor = floor, from = from, vel = vel, size = size, max = 260 };

        /// web shatter: the fighter bursts into cubes of their own colours (each from the part it came from), flung out from `from`.
        /// Half as many, bigger pieces than a full voxel fill, so it reads clearly and stays cheap. Hides the fighter.
        public static void Shatter(Fighter v, ShatterOpts o)
        {
            var pose = v ? v.GetComponent<FinisherPose>() : null; if (!pose) return;
            var me = I;
            var from = o.from ?? v.pos + Vector3.up * 1.2f * Fighter.SC;
            float ps = o.size * 1.41f;
            // every cell on the surface of each part; colours are looked up only for the ones that get used
            var cells = new List<(Vector3 p, int part, float s)>();
            foreach (var r in pose.Renderers)
            {
                // (renderer.enabled isn't a guide: the shoulder camera and the x-ray flicker switch whole rigs off)
                if (!r || !r.gameObject.activeInHierarchy) continue;
                var mat = r.sharedMaterial; if (!mat) continue;
                if (mat.HasProperty("_Cull") && mat.GetFloat("_Cull") == 1) continue;     // ink outline
                var b = r.bounds; var sz = b.size; if (sz.x + sz.y + sz.z < .05f) continue;
                int part = partRend.Count;
                int nx = Mathf.Clamp(Mathf.RoundToInt(sz.x / ps), 1, 6), ny = Mathf.Clamp(Mathf.RoundToInt(sz.y / ps), 1, 8), nz = Mathf.Clamp(Mathf.RoundToInt(sz.z / ps), 1, 6);
                for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) for (int k = 0; k < nz; k++)
                        {
                            if (i > 0 && i < nx - 1 && j > 0 && j < ny - 1 && k > 0 && k < nz - 1) continue;
                            var p = new Vector3(b.min.x + (i + .5f) * sz.x / nx, b.min.y + (j + .5f) * sz.y / ny, b.min.z + (k + .5f) * sz.z / nz);
                            cells.Add((p, part, Mathf.Min(Mathf.Min(sz.x / nx, sz.y / ny), Mathf.Min(sz.z / nz, ps * 1.3f))));
                        }
                partRend.Add(r);
            }
            for (int i = cells.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var t = cells[i]; cells[i] = cells[j]; cells[j] = t; }
            int n = Mathf.Min(Mathf.CeilToInt(cells.Count / 2f), Mathf.RoundToInt(o.max / 4));
            var samplers = new Dictionary<int, (TexSampler smp, Color flat)>();
            float fl = o.floor ?? FinGeo.GroundAt(v.pos.x, v.pos.z, v.pos.y);
            for (int i = 0; i < n; i++)
            {
                var c = cells[i];
                if (!samplers.TryGetValue(c.part, out var sp))
                {
                    var r = partRend[c.part]; var m = r.sharedMaterial;
                    sp = (TexSampler.For(r, r.GetComponent<MeshFilter>(), m), MatColor(m)); samplers[c.part] = sp;
                }
                var col = sp.smp != null ? sp.smp.At(c.p) : sp.flat;
                float s = Mathf.Max(.07f, c.s) * Random.Range(.85f, 1.05f);
                var d = c.p - from; float L = d.magnitude; d = L > 1e-4f ? d / L : Vector3.up;
                float k = o.power * Random.Range(.5f, 1.15f);
                var vel = new Vector3(d.x * k + Random.Range(-1f, 1f), d.y * k * .6f + o.up * Random.Range(.4f, 1.2f), d.z * k + Random.Range(-1f, 1f));
                if (o.vel.HasValue) vel += o.vel.Value;
                me.AddDebris(c.p, vel, s, Random.Range(2.6f, 3.4f), fl + s / 2, col);
            }
            partRend.Clear();
            pose.Hidden = true;
        }

        public static Color MatColor(Material m)
        {
            var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.gray;
            var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            if (tex) c *= TexSampler.Average(tex);
            c.a = 1; return c;
        }

        /// The colours a fighter is made of (web palOf), for the bits the dogs and shocks knock off.
        public static string[] Palette(Fighter v)
        {
            var pose = v ? v.GetComponent<FinisherPose>() : null; var L = new List<string>();
            if (pose) foreach (var r in pose.Renderers)
                {
                    if (!r || !r.sharedMaterial) continue; var m = r.sharedMaterial;
                    if (m.HasProperty("_Cull") && m.GetFloat("_Cull") == 1) continue;
                    var h = "#" + ColorUtility.ToHtmlStringRGB(MatColor(m));
                    if (!L.Contains(h)) L.Add(h);
                }
            if (L.Count == 0) L.Add("#888888");
            return L.ToArray();
        }

        // ------------------------------------------------------------------ decals, flashes, arcs, lights
        class Timed { public GameObject go; public float age, ttl; public System.Action<Timed, float> upd; public Material m; public Light l; }
        readonly List<Timed> timed = new List<Timed>();
        static Mesh disc, ring, sphere;

        static Mesh Disc(float r0, float r1, int seg)
        {
            var m = new Mesh(); var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++) { float a = i / (float)seg * Mathf.PI * 2; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); v.Add(d * r0); v.Add(d * r1); }
            for (int i = 0; i < seg; i++) { int a = i * 2; t.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 }); }
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            // face up
            var n = m.normals; if (n.Length > 0 && n[0].y < 0) { var tr = m.triangles; for (int i = 0; i < tr.Length; i += 3) { var q = tr[i + 1]; tr[i + 1] = tr[i + 2]; tr[i + 2] = q; } m.triangles = tr; m.RecalculateNormals(); }
            return m;
        }
        public static Mesh SphereMesh { get { if (!sphere) { var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g); } return sphere; } }

        static GameObject MeshObj(string name, Mesh mesh, Material m, Vector3 p)
        {
            var go = new GameObject(name); go.transform.position = p;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            go.transform.SetParent(I.transform, true);
            return go;
        }

        /// web decal: a dark scorch or dent on the ground (with an optional lighter rim), fading out over 6 s.
        public static void Decal(float x, float y, float z, float r, string col, string rim = null)
        {
            if (!disc) disc = Disc(0, 1, 28);
            if (!ring) ring = Disc(.85f, 1.15f, 28);
            var cc = C(col); var m = FadeMat(new Color(cc.r, cc.g, cc.b, .8f)); m.renderQueue = 2990;
            var go = MeshObj("scorch", disc, m, new Vector3(x, y + .03f, z)); go.transform.localScale = Vector3.one * r;
            go.transform.rotation = Quaternion.Euler(0, Random.value * 360, 0);
            Material rm = null;
            if (rim != null)
            {
                var rc = C(rim); rm = FadeMat(new Color(rc.r, rc.g, rc.b, .85f)); rm.renderQueue = 2991;
                var rg = MeshObj("rim", ring, rm, new Vector3(x, y + .04f, z)); rg.transform.SetParent(go.transform, true); rg.transform.localScale = Vector3.one;
            }
            I.timed.Add(new Timed
            {
                go = go, ttl = 6, m = m,
                upd = (T, k) =>
                {
                    float a = Mathf.Min(1, (T.ttl - T.age) / 1.2f);
                    var c0 = m.GetColor("_BaseColor"); c0.a = .8f * a; m.SetColor("_BaseColor", c0);
                    if (rm) { var c1 = rm.GetColor("_BaseColor"); c1.a = .85f * a; rm.SetColor("_BaseColor", c1); }
                }
            });
        }

        /// web flashBall: a bright ball that swells and fades in .4 s, plus a real light so the scene flashes with it.
        public static void Flash(Vector3 p, string col, float r)
        {
            var c = C(col); var m = FadeMat(new Color(c.r, c.g, c.b, 1), true);
            var go = MeshObj("flash", SphereMesh, m, p);
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.range = r * 3.5f + 2; l.intensity = 6;
            I.timed.Add(new Timed
            {
                go = go, ttl = .4f, m = m, l = l,
                upd = (T, k) => { float u = T.age / .4f; go.transform.localScale = Vector3.one * (.4f + u * r) * 2; var cc = m.GetColor("_BaseColor"); cc.a = Mathf.Max(0, 1 - u); m.SetColor("_BaseColor", cc); l.intensity = 6 * (1 - u) * (1 - u); }
            });
        }

        /// A coloured light that fades out (fire, sparks).
        public static void Glow(Vector3 p, string col, float intensity, float range, float ttl)
        {
            var go = new GameObject("glow"); go.transform.position = p; go.transform.SetParent(I.transform, true);
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = C(col); l.range = range; l.intensity = intensity;
            I.timed.Add(new Timed { go = go, ttl = ttl, l = l, upd = (T, k) => { float u = T.age / T.ttl; l.intensity = intensity * (1 - u) * (1 - u); } });
        }

        /// web tracer: a bright line for a moment (electric arcs, the harpoon's rope).
        public static void Tracer(Vector3 a, Vector3 b, string col, float w, float ttl = .07f)
        {
            var go = new GameObject("arc"); go.transform.SetParent(I.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            var m = UnlitMat(C(col)); lr.sharedMaterial = m;
            lr.positionCount = 2; lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.startWidth = lr.endWidth = w; lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.numCapVertices = 2;
            I.timed.Add(new Timed { go = go, ttl = ttl, m = m });
        }

        /// A jagged bolt from a to b in `segs` pieces (each kink up to `jag` m off the straight line).
        public static void Bolt(Vector3 a, Vector3 b, string col, float w, int segs = 3, float jag = .25f)
        {
            var p = a;
            for (int s = 1; s <= segs; s++)
            {
                var n = s == segs ? b : Vector3.Lerp(a, b, s / (float)segs) + new Vector3(Random.Range(-jag, jag), Random.Range(-jag, jag), Random.Range(-jag, jag));
                Tracer(p, n, col, w); p = n;
            }
        }

        public static void ClearAll()
        {
            if (!inst) return;
            inst.nCubes = 0;
            foreach (var T in inst.timed) if (T.go) Destroy(T.go);
            inst.timed.Clear();
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, .05f);
            for (int i = nCubes - 1; i >= 0; i--)
            {
                ref var c = ref cubes[i];
                c.age += dt;
                if (c.age >= c.ttl) { cubes[i] = cubes[--nCubes]; continue; }
                if (c.debris)
                {
                    if (c.grav == 0) continue;
                    c.v.y -= c.grav * dt; c.p += c.v * dt;
                    c.r = c.r * Quaternion.Euler(c.w * dt);
                    // the floor under it (so bits that fly off a ledge fall to the ground below)
                    c.flT -= dt; if (c.flT <= 0) { c.flT = .12f; c.fl = FinGeo.GroundAt(c.p.x, c.p.z, c.p.y + .2f) + c.s / 2; }
                    if (c.p.y < c.fl && c.v.y < 0) { c.p.y = c.fl; c.v.y *= -.3f; c.v.x *= .6f; c.v.z *= .6f; c.w *= .5f; if (Mathf.Abs(c.v.y) < .4f) { c.v.y = 0; c.w *= .6f; } }
                }
                else
                {
                    float d = Mathf.Exp(-1.6f * dt);
                    c.v.x *= d; c.v.z *= d; c.v.y = c.v.y * d - c.grav * dt; c.p += c.v * dt;
                    if (c.p.y < c.fl) { c.p.y = c.fl; c.v.y *= -.3f; }
                    c.r = c.r * Quaternion.Euler(c.w * dt);
                }
            }
            for (int i = timed.Count - 1; i >= 0; i--)
            {
                var T = timed[i]; T.age += dt;
                if (!T.go || T.age >= T.ttl) { if (T.go) Destroy(T.go); if (T.m) Destroy(T.m); timed.RemoveAt(i); continue; }
                T.upd?.Invoke(T, dt);
            }
        }

        /// Draw every cube, one instanced call per colour (called after all the poses, from FinisherLate).
        public static void LateFrame() { if (inst) inst.Draw(); }

        void Draw()
        {
            if (nCubes == 0) return;
            foreach (var k in keys) counts[k] = 0;
            Vector3 mn = new Vector3(1e9f, 1e9f, 1e9f), mx = -mn;
            for (int i = 0; i < nCubes; i++)
            {
                ref var c = ref cubes[i];
                float k = c.debris ? (c.ttl - c.age < .5f ? Mathf.Max(.001f, (c.ttl - c.age) / .5f) : 1) : Mathf.Sqrt(Mathf.Max(0, 1 - c.age / c.ttl));
                int n = counts[c.mat]; var buf = bufs[c.mat];
                if (n >= buf.Length) { System.Array.Resize(ref buf, buf.Length * 2); bufs[c.mat] = buf; }
                buf[n] = Matrix4x4.TRS(c.p, c.r, Vector3.one * (c.s * k)); counts[c.mat] = n + 1;
                mn = Vector3.Min(mn, c.p); mx = Vector3.Max(mx, c.p);
            }
            var bounds = new Bounds((mn + mx) / 2, mx - mn + Vector3.one * 2);
            foreach (var key in keys)
            {
                int cnt = counts[key]; if (cnt == 0) continue;
                bool lit = (key & 3) == 0;
                var rp = new RenderParams(mats[key]) { shadowCastingMode = lit ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = lit, worldBounds = bounds };
                var buf = bufs[key];
                for (int s = 0; s < cnt; s += 1023) Graphics.RenderMeshInstanced(rp, cubeMesh, 0, buf, Mathf.Min(1023, cnt - s), s);
            }
        }
    }

    /// Colour of a textured model at a point on its surface: the nearest vertex's UV, looked up in a small copy of the texture
    /// (web texSampler). Textures don't need to be readable: a 64x64 copy is read back once per texture.
    public class TexSampler
    {
        static readonly Dictionary<Texture, Color32[]> small = new Dictionary<Texture, Color32[]>();
        static readonly Dictionary<Texture, Color> avg = new Dictionary<Texture, Color>();
        Vector3[] P; Vector2[] U; Color32[] px; Color tint;

        static Color32[] Small(Texture tex)
        {
            if (small.TryGetValue(tex, out var c)) return c;
            c = null;
            try
            {
                var rt = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                Graphics.Blit(tex, rt); RenderTexture.active = rt;
                var t2 = new Texture2D(64, 64, TextureFormat.RGBA32, false, false);
                t2.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); t2.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                c = t2.GetPixels32(); Object.Destroy(t2);
            }
            catch { c = null; }
            small[tex] = c; return c;
        }

        public static Color Average(Texture tex)
        {
            if (avg.TryGetValue(tex, out var a)) return a;
            var px = Small(tex); a = Color.white;
            if (px != null) { float r = 0, g = 0, b = 0; int n = 0; foreach (var p in px) { if (p.a < 128) continue; r += p.r; g += p.g; b += p.b; n++; } if (n > 0) a = new Color(r / n / 255f, g / n / 255f, b / n / 255f); }
            avg[tex] = a; return a;
        }

        public static TexSampler For(Renderer r, MeshFilter mf, Material m)
        {
            var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            var mesh = mf ? mf.sharedMesh : null;
            if (!tex || !mesh || !mesh.isReadable) return null;
            var uv = mesh.uv; var v = mesh.vertices; if (uv == null || uv.Length != v.Length || v.Length == 0) return null;
            var px = Small(tex); if (px == null) return null;
            int step = Mathf.Max(1, Mathf.CeilToInt(v.Length / 1800f));
            var P = new List<Vector3>(); var U = new List<Vector2>(); var M = r.localToWorldMatrix;
            for (int i = 0; i < v.Length; i += step) { P.Add(M.MultiplyPoint3x4(v[i])); U.Add(uv[i]); }
            return new TexSampler { P = P.ToArray(), U = U.ToArray(), px = px, tint = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white };
        }

        public Color At(Vector3 q)
        {
            int bi = 0; float bd = 1e9f;
            for (int i = 0; i < P.Length; i++) { float d = (P[i] - q).sqrMagnitude; if (d < bd) { bd = d; bi = i; } }
            float u = U[bi].x % 1, v = U[bi].y % 1; if (u < 0) u += 1; if (v < 0) v += 1;
            int tx = Mathf.Min(63, (int)(u * 64)), ty = Mathf.Min(63, (int)(v * 64));
            var c = (Color)px[ty * 64 + tx]; c *= tint; c.a = 1; return c;
        }
    }
}
