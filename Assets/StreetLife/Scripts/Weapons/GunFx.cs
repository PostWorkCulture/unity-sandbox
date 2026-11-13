// Weapon effects, ported from the web game: particle blocks (emit / updParts), tracers, mist puffs, explosion fireballs,
// the flamethrower jet and burning licks, and camera shake. Everything is drawn from three dynamic meshes, so the whole
// lot costs three draw calls.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class EmitOpts
    {
        public int n = 8; public float speed = 4, up, size = .12f, life = .5f, grav = 12, vx, vz;
        public Color[] colors;
    }

    [DefaultExecutionOrder(50)]
    public class GunFx : MonoBehaviour
    {
        public static GunFx I;
        public static Shader FxShader;
        public static float Shake;          // camera shake amount (S.shake): ShoulderCamera reads and decays it

        const int PMAX = 900, BMAX = 1400, TMAX = 40;
        struct Part { public bool a; public Vector3 p, v, r; public float life, max, size, g; public Color c; }
        struct Bill { public bool a, add; public Vector3 p, v; public float t, life, s0, s1, op, grav, drag, stop, dist; public Color c; public int kind; }
        struct TracerFx { public bool a; public Vector3 a0, b0; public float t, w; public Color c; }
        readonly Part[] parts = new Part[PMAX]; int pI;
        readonly Bill[] bills = new Bill[BMAX]; int bI;
        readonly TracerFx[] tracers = new TracerFx[TMAX]; int tI;

        Mesh cubeMesh, addMesh, alphaMesh;
        Material cubeMat, addMat, alphaMat;
        readonly List<Vector3> vb = new List<Vector3>(); readonly List<Color> cb = new List<Color>(); readonly List<Vector2> ub = new List<Vector2>(); readonly List<int> ib = new List<int>();
        Light boomLight, flameLight; float boomL, flameL; Vector3 flameLp;

        public static Material MakeMat(bool additive, Texture tex, bool opaque = false)
        {
            var sh = FxShader ? FxShader : Shader.Find("StreetLife/Fx");
            var m = new Material(sh) { name = additive ? "fx add" : opaque ? "fx opaque" : "fx alpha" };
            if (tex) m.SetTexture("_MainTex", tex);
            if (opaque) { m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero); m.SetFloat("_ZWrite", 1); m.renderQueue = 2450; }
            else if (additive) { m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_ZWrite", 0); m.renderQueue = 3100; }
            else { m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0); m.renderQueue = 3050; }
            return m;
        }

        static Texture2D softTex;
        public static Texture2D SoftTex
        {
            get
            {
                if (softTex) return softTex;
                int n = 64; softTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "soft puff" };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    {
                        float r = Mathf.Sqrt((x + .5f - 32) * (x + .5f - 32) + (y + .5f - 32) * (y + .5f - 32)) / 32f;
                        float a = r < .5f ? Mathf.Lerp(1, .45f, r / .5f) : r < 1 ? Mathf.Lerp(.45f, 0, (r - .5f) / .5f) : 0;
                        px[y * n + x] = new Color(1, 1, 1, a);
                    }
                softTex.SetPixels(px); softTex.Apply(); return softTex;
            }
        }

        MeshRenderer Layer(string nm, Material m, out Mesh mesh)
        {
            var go = new GameObject(nm); go.transform.SetParent(transform, false);
            mesh = new Mesh { name = nm, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return mr;
        }

        void Awake()
        {
            I = this;
            cubeMat = MakeMat(false, null, true); addMat = MakeMat(true, SoftTex); alphaMat = MakeMat(false, SoftTex);
            Layer("particles", cubeMat, out cubeMesh); Layer("glow", addMat, out addMesh); Layer("smoke", alphaMat, out alphaMesh);
            boomLight = new GameObject("blast light").AddComponent<Light>(); boomLight.transform.SetParent(transform); boomLight.type = LightType.Point; boomLight.range = 14; boomLight.color = new Color(1, .7f, .35f); boomLight.intensity = 0; boomLight.shadows = LightShadows.None;
            flameLight = new GameObject("flame light").AddComponent<Light>(); flameLight.transform.SetParent(transform); flameLight.type = LightType.Point; flameLight.range = 9; flameLight.color = new Color(1, .55f, .2f); flameLight.intensity = 0; flameLight.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------ spawning (static helpers, safe if no Fx yet)
        public static void Emit(Vector3 at, EmitOpts o) { if (I) I.DoEmit(at, o); }
        public static EmitOpts E(int n, float speed, float size, float life, float grav, params string[] cols)
        { var o = new EmitOpts { n = n, speed = speed, size = size, life = life, grav = grav, colors = new Color[cols.Length] }; for (int i = 0; i < cols.Length; i++) o.colors[i] = Web.Hex(cols[i]); return o; }

        void DoEmit(Vector3 at, EmitOpts o)
        {
            var cols = o.colors != null && o.colors.Length > 0 ? o.colors : new[] { Color.white };
            for (int k = 0; k < o.n; k++)
            {
                ref var p = ref parts[pI]; pI = (pI + 1) % PMAX;
                float a = Random.value * Mathf.PI * 2, u = Random.value * 2 - 1, s = Mathf.Sqrt(1 - u * u), sp = o.speed * (.4f + Random.value * .8f);
                p.a = true; p.p = at;
                p.v = new Vector3(Mathf.Cos(a) * s * sp + o.vx, u * sp + o.up, Mathf.Sin(a) * s * sp + o.vz);
                p.life = 0; p.max = o.life * (.7f + Random.value * .6f); p.size = o.size * (.6f + Random.value * .8f); p.g = o.grav;
                p.r = new Vector3(Random.value * 6, Random.value * 6, 0); p.c = cols[Random.Range(0, cols.Length)];
            }
        }

        public static void Tracer(Vector3 a, Vector3 b, Color c, float w = .045f)
        {
            if (!I) return; ref var t = ref I.tracers[I.tI]; I.tI = (I.tI + 1) % TMAX;
            t.a = true; t.a0 = a; t.b0 = b; t.t = .07f; t.w = w; t.c = c;
        }

        static void Bb(Vector3 p, Vector3 v, Color c, float s0, float s1, float op, float life, bool add, int kind = 0, float grav = 0, float drag = 0, float stop = 1e9f)
        {
            if (!I) return; ref var b = ref I.bills[I.bI]; I.bI = (I.bI + 1) % BMAX;
            b.a = true; b.p = p; b.v = v; b.c = c; b.s0 = s0; b.s1 = s1; b.op = op; b.life = life; b.t = 0; b.add = add; b.kind = kind; b.grav = grav; b.drag = drag; b.stop = stop; b.dist = 0;
        }

        /// soft mist puff that swells and fades (hit spray, armour hits)
        public static void Puff(Vector3 p, Color c, float s0, float s1, float op, float life) => Bb(p, Vector3.zero, c, s0, s1, op, life, false);

        static readonly Color[] SprayRed = { Web.Hex("#c8141e"), Web.Hex("#e8303a"), Web.Hex("#8e0d14") };
        public static void HitSpray(Vector3 at, Vector3? dir, int n = 6, float k = 1, Color[] cols = null, bool puff = true, string mist = "#c4161f")
        {
            float bx = dir.HasValue ? -dir.Value.x * .6f : 0, bz = dir.HasValue ? -dir.Value.z * .6f : 0;
            Emit(at, new EmitOpts { n = n, speed = 3.4f * k, up = 1, size = .045f * k, life = .38f, grav = 14, vx = bx, vz = bz, colors = cols ?? SprayRed });
            if (puff) Puff(at - (dir.HasValue ? new Vector3(dir.Value.x, 0, dir.Value.z) * .1f : Vector3.zero), Web.Hex(mist), .2f * k, .85f * k, .85f, .35f);
        }

        /// explosion: white flash, fireball, smoke column, sparks and a light (a lighter version of the web game's boomFX)
        public static void Boom(Vector3 at, float radius, Color[] cols)
        {
            if (!I) return;
            float k = Mathf.Clamp(radius / 4.5f, .35f, 1.3f);
            Bb(at, Vector3.zero, new Color(1, .97f, .85f), .5f * k, 5.5f * k, 1, .16f, true);
            for (int i = 0; i < 14; i++)
            {
                var d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) * .8f + .1f;
                Bb(at + d * .3f * k, d * Random.Range(2.5f, 6f) * k, Color.Lerp(new Color(1, .75f, .3f), new Color(1, .35f, .08f), Random.value), .8f * k, 2.4f * k, .9f, Random.Range(.35f, .6f), true, 0, -1.5f, 2.5f);
            }
            for (int i = 0; i < 12; i++)
            {
                var d = Random.insideUnitSphere; d.y = Mathf.Abs(d.y) + .3f;
                Bb(at + d * .5f * k, d * Random.Range(1f, 2.5f) * k, Color.Lerp(new Color(.2f, .19f, .18f), new Color(.42f, .4f, .38f), Random.value), 1f * k, 3.4f * k, .75f, Random.Range(1.2f, 2.2f), false, 0, -1.2f, 1.2f);
            }
            Emit(at + Vector3.up * .2f, new EmitOpts { n = Mathf.RoundToInt(70 * k), speed = 12 * k, up = 3, size = .3f, life = .8f, grav = 6, colors = cols });
            I.boomLight.transform.position = at + Vector3.up * .6f; I.boomL = 1; I.boomLight.range = 10 + 8 * k;
        }

        /// one burst of the flamethrower jet: from the nozzle along dir until the first wall (stop) - the web game's FLAME.jet
        public static void FlameJet(Vector3 from, Vector3 dir, float stop, int n = 16, float speed = 25)
        {
            if (!I) return;
            var u = Mathf.Abs(dir.y) > .9f ? Vector3.right : Vector3.up; var w = Vector3.Cross(dir, u).normalized; u = Vector3.Cross(w, dir).normalized;
            for (int k = 0; k < n; k++)
            {
                float sp = speed * (.82f + Random.value * .3f), a = Random.value * Mathf.PI * 2, r = Random.value * .1f, age = Random.value * .05f;
                var v = dir * sp + (w * Mathf.Cos(a) + u * Mathf.Sin(a)) * r * sp;
                Bb(from + dir * (sp * age * .9f), v, Color.white, .05f + Random.value * .05f, 1.5f + Random.value * 1.1f, 1, .55f + Random.value * .3f, true, 1, 0, 2.6f, stop);
                I.bills[(I.bI - 1 + BMAX) % BMAX].t = age;
            }
            I.flameLp = from + dir * Mathf.Min(stop, 7) * .55f; I.flameL = 1;
        }

        /// a small lick of flame (someone burning)
        public static void Lick(Vector3 p, float s = 1) => Bb(p, new Vector3((Random.value - .5f) * .8f, 1.2f + Random.value * 1.2f, (Random.value - .5f) * .8f), Color.white, .14f * s, .5f * s + .14f * s, 1, .35f + Random.value * .25f, true, 1, 0, 2.6f);

        // ------------------------------------------------------------------ simulate and draw
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            var cam = Camera.main; if (!cam) return;
            Shake = Mathf.Max(0, Shake - dt * 1.5f);
            boomL = Mathf.Max(0, boomL - dt * 5); boomLight.intensity = boomL * 30;
            flameL = Mathf.Max(0, flameL - dt * 4); flameLight.transform.position = Vector3.Lerp(flameLight.transform.position, flameLp, Mathf.Min(1, dt * 12)); flameLight.intensity = flameL * (9 + 5 * Random.value) / Mathf.PI;

            // particle blocks (spinning cubes that shrink as they die)
            vb.Clear(); cb.Clear(); ib.Clear(); ub.Clear();
            for (int i = 0; i < PMAX; i++)
            {
                ref var p = ref parts[i]; if (!p.a) continue;
                p.life += dt; if (p.life >= p.max) { p.a = false; continue; }
                float d = Mathf.Exp(-1.6f * dt); p.v.x *= d; p.v.z *= d; p.v.y = p.v.y * d - p.g * dt; p.p += p.v * dt;
                if (p.p.y < .03f) { p.p.y = .03f; p.v.y *= -.3f; }
                p.r.x += dt * 6;
                float k = Mathf.Sqrt(1 - p.life / p.max) * p.size;
                Cube(p.p, Quaternion.Euler(p.r.x * Mathf.Rad2Deg, p.r.y * Mathf.Rad2Deg, 0), k, p.c);
            }
            Apply(cubeMesh);

            // billboards: glow (additive) and smoke (alpha)
            Vector3 cr = cam.transform.right, cu = cam.transform.up;
            for (int pass = 0; pass < 2; pass++)
            {
                bool add = pass == 0; vb.Clear(); cb.Clear(); ib.Clear(); ub.Clear();
                if (add)
                    for (int i = 0; i < TMAX; i++)
                    {
                        ref var t = ref tracers[i]; if (!t.a) continue; t.t -= dt * (pass == 0 ? 1 : 0); if (t.t <= 0) { t.a = false; continue; }
                        var c = t.c * (Mathf.Max(0, t.t / .07f) * .95f); c.a = 1;
                        Ribbon(t.a0, t.b0, t.w, cam.transform.position, c);
                    }
                for (int i = 0; i < BMAX; i++)
                {
                    ref var b = ref bills[i]; if (!b.a || b.add != add) continue;
                    if (pass == (add ? 0 : 1)) { b.t += dt; }
                    float u = b.t / b.life; if (u >= 1) { b.a = false; continue; }
                    var op = b.p;
                    if (b.drag > 0) { float kd = Mathf.Exp(-b.drag * dt); b.v.x *= kd; b.v.z *= kd; b.v.y *= kd; }
                    if (b.kind == 1)
                    {   // flame puff: sinks then rises, turbulence, splashes sideways off walls
                        b.v.y += (u < .15f ? -2 : 7 * u) * dt;
                        float tb = (3 + 18 * u * u) * dt; b.v += new Vector3(Random.value - .5f, Random.value - .5f, Random.value - .5f) * tb * 2;
                    }
                    else b.v.y -= b.grav * dt;
                    b.p += b.v * dt; b.dist += (b.p - op).magnitude;
                    if (b.dist > b.stop) { float s = b.v.magnitude, a = Random.value * Mathf.PI * 2; b.v = new Vector3(Mathf.Cos(a) * s * .35f, Mathf.Abs(b.v.y) * .3f + s * .18f, Mathf.Sin(a) * s * .35f); b.p = op; b.stop = 1e9f; b.s1 *= 1.25f; }
                    if (b.p.y < .05f) { b.p.y = .05f; b.v.y = Mathf.Abs(b.v.y) * .2f; }
                    float size; Color c;
                    if (b.kind == 1)
                    {
                        size = b.s0 + b.s1 * Mathf.Pow(u, .85f);
                        c = u < .15f ? Color.Lerp(new Color(1, .95f, .8f), new Color(1, .75f, .3f), u / .15f) : u < .55f ? Color.Lerp(new Color(1, .75f, .3f), new Color(1, .35f, .08f), (u - .15f) / .4f) : Color.Lerp(new Color(1, .35f, .08f), new Color(.25f, .05f, .02f), (u - .55f) / .45f);
                        c *= (1 - u) * .8f;
                    }
                    else
                    {
                        size = b.s0 + (b.s1 - b.s0) * (1 - (1 - u) * (1 - u));
                        float a = b.op * (1 - u);
                        c = add ? b.c * a : new Color(b.c.r, b.c.g, b.c.b, a);
                    }
                    if (add) c.a = 1;
                    Quad(b.p, cr, cu, size, c);
                }
                Apply(add ? addMesh : alphaMesh);
            }
        }

        void Apply(Mesh m)
        {
            m.Clear();
            if (vb.Count == 0) return;
            m.SetVertices(vb); m.SetColors(cb); m.SetUVs(0, ub); m.SetTriangles(ib, 0, false);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4000);
        }

        void Quad(Vector3 p, Vector3 r, Vector3 u, float s, Color c)
        {
            int b = vb.Count; float h = s * .5f;
            vb.Add(p - r * h - u * h); vb.Add(p + r * h - u * h); vb.Add(p + r * h + u * h); vb.Add(p - r * h + u * h);
            ub.Add(new Vector2(0, 0)); ub.Add(new Vector2(1, 0)); ub.Add(new Vector2(1, 1)); ub.Add(new Vector2(0, 1));
            for (int i = 0; i < 4; i++) cb.Add(c);
            ib.Add(b); ib.Add(b + 2); ib.Add(b + 1); ib.Add(b); ib.Add(b + 3); ib.Add(b + 2);
        }

        void Ribbon(Vector3 a, Vector3 b, float w, Vector3 eye, Color c)
        {
            var d = b - a; var side = Vector3.Cross(d, eye - (a + b) * .5f).normalized * w * .5f;
            int k = vb.Count;
            vb.Add(a - side); vb.Add(a + side); vb.Add(b + side); vb.Add(b - side);
            // sample the middle of the soft texture so the ribbon is solid along its length
            for (int i = 0; i < 4; i++) { cb.Add(c); ub.Add(new Vector2(.5f, i == 0 || i == 3 ? .3f : .7f)); }
            ib.Add(k); ib.Add(k + 1); ib.Add(k + 2); ib.Add(k); ib.Add(k + 2); ib.Add(k + 3);
        }

        static readonly Vector3[] cubeV =
        {
            new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(-1,1,-1),
            new Vector3(-1,-1,1), new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1)
        };
        static readonly int[] cubeI = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };
        void Cube(Vector3 p, Quaternion q, float s, Color c)
        {
            int b = vb.Count;
            for (int i = 0; i < 8; i++) { vb.Add(p + q * (cubeV[i] * (s * .5f))); cb.Add(c); ub.Add(new Vector2(.5f, .5f)); }
            for (int i = 0; i < cubeI.Length; i++) ib.Add(b + cubeI[i]);
        }
    }
}
