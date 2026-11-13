// Fire (match chat): the web game's FLAME system line for line, replacing the port's simpler flame puffs. Each puff is a
// lumpy textured sprite that turns as it flies, starting as a blue-white core at the nozzle, going yellow, orange, red
// and a dark ember as it ages, sinking a moment then rising with growing turbulence, splashing sideways off walls; one
// in ten leaves a smoke puff behind. Drawn premultiplied (fire) and alpha (smoke) from two dynamic meshes.
// Used by the flamethrower (GunFx.FlameJet), burning fighters (GunFx.Lick) and the Fireball.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class FlameFx : MonoBehaviour
    {
        static FlameFx I; static bool quit;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { I = null; quit = false; }
        static FlameFx Get()
        {
            if (I || quit || !Application.isPlaying) return I;
            I = new GameObject("Fire").AddComponent<FlameFx>(); return I;
        }
        void OnApplicationQuit() { quit = true; }

        const int NF = 1600, NS = 600;
        struct Puff { public bool a; public Vector3 p, v; public float t, max, s0, grow, dist, stop, rot; }
        readonly Puff[] fire = new Puff[NF], smoke = new Puff[NS]; int fI, sI; float fLive, sLive;
        Mesh fireMesh, smokeMesh; Light light; float lightW; Vector3 lp;
        readonly List<Vector3> vb = new List<Vector3>(); readonly List<Color> cb = new List<Color>(); readonly List<Vector2> ub = new List<Vector2>(); readonly List<int> ib = new List<int>();
        static Texture2D puffTex;

        static Texture2D PuffTex()
        {
            if (puffTex) return puffTex;
            int n = 128; var rnd = new System.Random(5); var px = new Color[n * n];
            var blobs = new List<Vector4>();
            for (int i = 0; i < 40; i++) { double a = rnd.NextDouble() * 6.283, r = System.Math.Pow(rnd.NextDouble(), .7) * 34; blobs.Add(new Vector4((float)(64 + System.Math.Cos(a) * r), (float)(64 + System.Math.Sin(a) * r), (float)(8 + rnd.NextDouble() * 22), 0)); }
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float al = 0;
                    foreach (var b in blobs) { float d = Mathf.Sqrt((x - b.x) * (x - b.x) + (y - b.y) * (y - b.y)) / b.z; if (d < 1) al += .3f * (1 - d); }
                    float r = Mathf.Sqrt((x - 64) * (x - 64) + (y - 64) * (y - 64)) / 64f;
                    al += r < .5f ? Mathf.Lerp(.35f, .1f, r / .5f) : r < .94f ? Mathf.Lerp(.1f, 0, (r - .5f) / .44f) : 0;
                    al = Mathf.Min(1, al * (1.15f - r * .4f)) * (r > .95f ? 0 : 1);
                    px[y * n + x] = new Color(1, 1, 1, al);
                }
            puffTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "flame puff", wrapMode = TextureWrapMode.Clamp };
            puffTex.SetPixels(px); puffTex.Apply(true); return puffTex;
        }

        void Awake()
        {
            var fm = GunFx.MakeMat(true, PuffTex()); fm.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); fm.renderQueue = 3095;
            var sm = GunFx.MakeMat(false, PuffTex()); sm.renderQueue = 3065;
            fireMesh = Layer("flames", fm); smokeMesh = Layer("flame smoke", sm);
            light = new GameObject("fire light").AddComponent<Light>(); light.transform.SetParent(transform);
            light.type = LightType.Point; light.color = Web.Hex("#ff7a22"); light.range = 14; light.intensity = 0; light.shadows = LightShadows.None;
        }

        Mesh Layer(string nm, Material m)
        {
            var go = new GameObject(nm); go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = nm, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return mesh;
        }

        void Spawn(bool isFire, Vector3 p, Vector3 v, float max, float s0, float grow, float stop)
        {
            ref var q = ref isFire ? ref fire[fI] : ref smoke[sI];
            if (isFire) { fI = (fI + 1) % NF; fLive = 1.5f; } else { sI = (sI + 1) % NS; sLive = 1.5f; }
            q.a = true; q.p = p; q.v = v; q.t = 0; q.max = max; q.s0 = s0; q.grow = grow; q.dist = 0; q.stop = stop; q.rot = Random.value * 6.283f;
        }

        /// one burst of the jet: from the nozzle along dir until the first wall (stop). False if there's no fire object.
        public static bool Jet(Vector3 from, Vector3 dir, float stop, int n = 16, float speed = 25)
        {
            var I = Get(); if (!I) return false;
            var u = Mathf.Abs(dir.y) > .9f ? Vector3.right : Vector3.up; var w = Vector3.Cross(dir, u).normalized; u = Vector3.Cross(w, dir).normalized;
            for (int k = 0; k < n; k++)
            {
                float sp = speed * (.82f + Random.value * .3f), a = Random.value * Mathf.PI * 2, r = Random.value * .1f, age = Random.value * .05f;
                var v = dir * sp + (w * Mathf.Cos(a) + u * Mathf.Sin(a)) * r * sp; float j = sp * age * .9f;
                I.Spawn(true, from + dir * j, v, .55f + Random.value * .3f, .05f + Random.value * .05f, 1.5f + Random.value * 1.1f, stop);
                I.fire[(I.fI - 1 + NF) % NF].t = age;
            }
            I.lp = from + dir * Mathf.Min(stop, 7) * .55f; I.lightW = 1;
            return true;
        }

        /// a small lick of flame (someone burning, the fireball's tail)
        public static bool Lick(Vector3 p, float s = 1)
        {
            var I = Get(); if (!I) return false;
            I.Spawn(true, p, new Vector3((Random.value - .5f) * .8f, 1.2f + Random.value * 1.2f, (Random.value - .5f) * .8f), .35f + Random.value * .25f, .14f * s, .5f * s, 99);
            return true;
        }

        static readonly Color C0 = new Color(.5f, .7f, 1f), C1 = new Color(1f, .8f, .36f), C2 = new Color(1f, .45f, .06f), C3 = new Color(.8f, .15f, .02f), C4 = new Color(.22f, .03f, .01f);
        static Color Ramp(float t) => t < .04f ? Color.Lerp(C0, C1, t / .04f) : t < .16f ? Color.Lerp(C1, C2, (t - .04f) / .12f) : t < .45f ? Color.Lerp(C2, C3, (t - .16f) / .29f) : Color.Lerp(C3, C4, (t - .45f) / .55f);

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            lightW = Mathf.Max(0, lightW - dt * 4); light.transform.position = Vector3.Lerp(light.transform.position, lp, Mathf.Min(1, dt * 12)); light.intensity = lightW * (9 + 5 * Random.value) / Mathf.PI;
            if (fLive <= 0 && sLive <= 0) return;
            fLive -= dt; sLive -= dt;
            var cam = Camera.main; if (!cam) return;
            Vector3 cr = cam.transform.right, cu = cam.transform.up;
            float kd = Mathf.Exp(-2.6f * dt);

            vb.Clear(); cb.Clear(); ub.Clear(); ib.Clear();
            for (int i = 0; i < NF; i++)
            {
                ref var q = ref fire[i]; if (!q.a) continue;
                q.t += dt; float t = q.t / q.max;
                if (t >= 1) { q.a = false; if (Random.value < .1f) Spawn(false, q.p, new Vector3(q.v.x * .3f, .8f + Random.value * .8f, q.v.z * .3f), 1.2f + Random.value * .8f, .4f, 2.2f, 99); continue; }
                var o = q.p;
                q.v.x *= kd; q.v.z *= kd; q.v.y = q.v.y * kd + (t < .15f ? -2 : 7 * t) * dt;
                float tb = (3 + 18 * t * t) * dt; q.v += new Vector3(Random.value - .5f, Random.value - .5f, Random.value - .5f) * tb * 2;
                q.p += q.v * dt; q.dist += (q.p - o).magnitude;
                if (q.dist > q.stop) { float s = q.v.magnitude, a = Random.value * Mathf.PI * 2; q.v = new Vector3(Mathf.Cos(a) * s * .35f, Mathf.Abs(q.v.y) * .3f + s * .18f, Mathf.Sin(a) * s * .35f); q.p.x = o.x; q.p.z = o.z; q.stop = 1e9f; q.grow *= 1.25f; }
                if (q.p.y < .05f) { q.p.y = .05f; q.v.y = Mathf.Abs(q.v.y) * .2f; }
                float size = q.s0 + q.grow * Mathf.Pow(t, .85f);
                float al = Mathf.SmoothStep(0, 1, t / .03f) * (1 - Mathf.SmoothStep(0, 1, (t - .5f) / .5f)) * .62f;
                var c = Ramp(t).linear;
                Quad(q.p, cr, cu, size, new Color(c.r * al * 1.25f, c.g * al * 1.25f, c.b * al * 1.25f, al * .6f), q.rot + t * 2.4f);
            }
            Apply(fireMesh);

            vb.Clear(); cb.Clear(); ub.Clear(); ib.Clear();
            for (int i = 0; i < NS; i++)
            {
                ref var q = ref smoke[i]; if (!q.a) continue;
                q.t += dt; float t = q.t / q.max; if (t >= 1) { q.a = false; continue; }
                q.v.x *= kd; q.v.z *= kd; q.v.y = q.v.y * .99f + .6f * dt; q.p += q.v * dt;
                float size = q.s0 + q.grow * t;
                float al = Mathf.SmoothStep(0, 1, t / .2f) * (1 - t) * .32f;
                var c = (new Color(.16f, .15f, .15f) + new Color(.18f, .18f, .18f) * t).linear;
                Quad(q.p, cr, cu, size, new Color(c.r, c.g, c.b, al), q.rot + t * 2.4f);
                sLive = Mathf.Max(sLive, .1f);
            }
            Apply(smokeMesh);
        }

        void Apply(Mesh m)
        {
            m.Clear(); if (vb.Count == 0) return;
            m.SetVertices(vb); m.SetColors(cb); m.SetUVs(0, ub); m.SetTriangles(ib, 0, false); m.bounds = new Bounds(Vector3.zero, Vector3.one * 4000);
        }

        void Quad(Vector3 p, Vector3 r, Vector3 u, float s, Color c, float rot)
        {
            float h = s * .5f, cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
            var R = (r * cs + u * sn) * h; var U = (u * cs - r * sn) * h; int b = vb.Count;
            vb.Add(p - R - U); vb.Add(p + R - U); vb.Add(p + R + U); vb.Add(p - R + U);
            ub.Add(new Vector2(0, 0)); ub.Add(new Vector2(1, 0)); ub.Add(new Vector2(1, 1)); ub.Add(new Vector2(0, 1));
            cb.Add(c); cb.Add(c); cb.Add(c); cb.Add(c);
            ib.Add(b); ib.Add(b + 2); ib.Add(b + 1); ib.Add(b); ib.Add(b + 3); ib.Add(b + 2);
        }
    }
}
