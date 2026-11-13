// Explosions (match chat): the web game's boomFX in full, a step up from the port's lighter version. A white-hot flash,
// a ground shockwave ring, a rolling fireball of textured puffs cooling from white to deep red, a smoke column that
// climbs and spreads, a dust ring along the ground, spark streaks, dark chunks, a strong short light, and scorch marks
// left on the floor and walls (with embers that fade). Small blasts (fireball, heart, bomblets, Swarm) get a lighter mix
// in their own colours. Everything is drawn from four dynamic meshes, so any number of blasts costs four draw calls.
// Hooked in by Combat.Explode and GunFx.Boom (weapons chat) and BreakFx.Boom (drums).
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class BoomFx : MonoBehaviour
    {
        static BoomFx I;
        static bool quit;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { I = null; quit = false; }

        static BoomFx Get()
        {
            if (I || quit || !Application.isPlaying) return I;
            I = new GameObject("Explosions").AddComponent<BoomFx>();
            return I;
        }
        void OnApplicationQuit() { quit = true; }

        // kinds: 0 fire (premultiplied), 1 smoke (alpha), 2 glow (additive soft), 3 ring (additive, flat on the ground), 4 spark (additive streak)
        struct P
        {
            public bool a; public int kind; public Vector3 p, v; public float t, life, s0, s1, rot, spin, op, grav, drag, fadeIn;
            public Color c0, c1; public Vector3 n;
        }
        const int MAX = 2400;
        readonly P[] ps = new P[MAX]; int pI, live;
        Mesh[] meshes = new Mesh[4]; Material[] mats = new Material[4];
        readonly List<Vector3> vb = new List<Vector3>(); readonly List<Color> cb = new List<Color>(); readonly List<Vector2> ub = new List<Vector2>(); readonly List<int> ib = new List<int>();
        Light light; float lightT = 9, lightK;
        static bool low;

        // ------------------------------------------------------------------ textures
        static Texture2D fireTex, cloudTex, glowTex, ringTex, scorchTex;
        static Texture2D Tex(string name, System.Func<float, float, Color> f, int n = 128)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f((x + .5f) / n * 2 - 1, (y + .5f) / n * 2 - 1);
            t.SetPixels(px); t.Apply(true); return t;
        }
        /// lumpy puff: 40 soft blobs inside a soft disc (the web game's puffTex)
        static Texture2D Puff(string name, int seed, float blob, float body)
        {
            var rnd = new System.Random(seed); var bl = new List<Vector4>();
            for (int i = 0; i < 40; i++) { double a = rnd.NextDouble() * 6.283, r = System.Math.Pow(rnd.NextDouble(), .7) * .53; bl.Add(new Vector4((float)(System.Math.Cos(a) * r), (float)(System.Math.Sin(a) * r), (float)(.12 + rnd.NextDouble() * .34), 0)); }
            return Tex(name, (x, y) =>
            {
                float al = 0;
                foreach (var b in bl) { float d = Mathf.Sqrt((x - b.x) * (x - b.x) + (y - b.y) * (y - b.y)) / b.z; if (d < 1) al += blob * (1 - d); }
                float r = Mathf.Sqrt(x * x + y * y);
                al += body * Mathf.Clamp01(1 - r);
                al = Mathf.Min(1, al * (1.15f - r * .4f)) * (r > .95f ? 0 : Mathf.Clamp01((.95f - r) * 8));
                return new Color(1, 1, 1, al);
            });
        }
        static void Textures()
        {
            if (fireTex) return;
            fireTex = Puff("fire puff", 7, .3f, .35f);
            cloudTex = Puff("smoke puff", 11, .26f, .45f);
            glowTex = Tex("glow", (x, y) => { float r = Mathf.Sqrt(x * x + y * y); float a = r < .25f ? Mathf.Lerp(1, .45f, r / .25f) : Mathf.Lerp(.45f, 0, Mathf.Clamp01((r - .25f) / .75f)); return new Color(a, a, a, a); }, 64);
            ringTex = Tex("shock ring", (x, y) => { float r = Mathf.Sqrt(x * x + y * y); float a = Mathf.Exp(-Mathf.Pow((r - .78f) / .1f, 2)) + .25f * Mathf.Exp(-Mathf.Pow((r - .62f) / .18f, 2)); a *= r < 1 ? 1 : 0; return new Color(a, a * .85f, a * .6f, a); });
            var rnd = new System.Random(3); var blot = new List<Vector4>();
            for (int i = 0; i < 26; i++) { double a = rnd.NextDouble() * 6.283, r = .3 + rnd.NextDouble() * .55; blot.Add(new Vector4((float)(System.Math.Cos(a) * r), (float)(System.Math.Sin(a) * r), (float)(.05 + rnd.NextDouble() * .11), (float)(.25 + rnd.NextDouble() * .4))); }
            scorchTex = Tex("scorch", (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float a = r < .45f ? Mathf.Lerp(.95f, .85f, r / .45f) : r < .75f ? Mathf.Lerp(.85f, .45f, (r - .45f) / .3f) : Mathf.Lerp(.45f, 0, Mathf.Clamp01((r - .75f) / .25f));
                foreach (var b in blot) { float d = Mathf.Sqrt((x - b.x) * (x - b.x) + (y - b.y) * (y - b.y)); if (d < b.z) a = Mathf.Max(a, b.w); }
                float shade = Mathf.Lerp(.05f, .16f, Mathf.Clamp01(r));
                return new Color(shade, shade * .85f, shade * .75f, Mathf.Clamp01(a));
            });
        }

        void Awake()
        {
            Textures();
            low = Application.isMobilePlatform || QualitySettings.names[QualitySettings.GetQualityLevel()] == "Mobile";
            var fire = GunFx.MakeMat(true, fireTex); fire.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); fire.renderQueue = 3090;
            var smoke = GunFx.MakeMat(false, cloudTex); smoke.renderQueue = 3060;
            var glow = GunFx.MakeMat(true, glowTex); glow.renderQueue = 3110;
            var ring = GunFx.MakeMat(true, ringTex); ring.renderQueue = 3080;
            mats = new[] { fire, smoke, glow, ring };
            string[] names = { "boom fire", "boom smoke", "boom glow", "boom ring" };
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject(names[i]); go.transform.SetParent(transform, false);
                meshes[i] = new Mesh { name = names[i], indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; meshes[i].MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats[i];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            light = new GameObject("blast light").AddComponent<Light>(); light.transform.SetParent(transform);
            light.type = LightType.Point; light.color = Web.Hex("#ffa040"); light.intensity = 0; light.shadows = LightShadows.None;
            RoundManager.RoundStarted += OnRound;
        }
        void OnDestroy() { RoundManager.RoundStarted -= OnRound; ClearScorch(); }
        void OnRound(int r) { ClearScorch(); }

        // ------------------------------------------------------------------ spawning
        int N(int n) => Mathf.Max(3, Mathf.RoundToInt(n * (low ? .55f : 1)));
        static float Rnd(float a, float b) => Random.Range(a, b);

        ref P New(int kind)
        {
            ref var p = ref ps[pI]; pI = (pI + 1) % MAX;
            p = default; p.a = true; p.kind = kind; p.rot = Rnd(0, 6.283f); return ref p;
        }

        /// The big one (grenade, rocket, mine, drum). Returns false if there's no effects object yet.
        public static bool Play(Vector3 at, float R, Color[] cols, bool big, Vector3? n = null)
        {
            var I = Get(); if (!I) return false;
            I.Do(at, R, cols, big, n); return true;
        }

        void Do(Vector3 at, float R, Color[] cols, bool big, Vector3? n)
        {
            float k = Mathf.Clamp(R / 4.5f, .35f, 1.3f);
            var c = at + Vector3.up * .25f;
            float gy = GroundAt(at.x, at.z, at.y + .6f); bool onGround = gy > float.MinValue && at.y - gy < 1.3f;
            Color hotC = big ? Web.Hex("#ffe9a8") : cols != null && cols.Length > 0 ? Color.Lerp(cols[0], Color.white, .35f) : Color.white;
            Color coolC = big ? Web.Hex("#ff3a10") : cols != null && cols.Length > 1 ? cols[1] : Web.Hex("#ff7a1f");
            bool smoky = big || (cols != null && cols.Length > 2 && cols[2].grayscale < .45f);

            // flash
            { ref var p = ref New(2); p.p = c + Vector3.up * .6f; p.life = .14f; float fs = big ? 3 : 1.8f; p.s0 = R * fs * .6f; p.s1 = R * fs; p.op = 1; p.c0 = Color.white; p.c1 = hotC; }
            { ref var p = ref New(2); p.p = c; p.life = .45f; p.s0 = R * (big ? 1.4f : .9f); p.s1 = R * (big ? 2.4f : 1.4f); p.op = big ? .55f : .4f; p.c0 = hotC; p.c1 = coolC * .4f; }
            // shockwave ring on the ground
            if (onGround && big) { ref var p = ref New(3); p.p = new Vector3(at.x, gy + .06f, at.z); p.life = .45f; p.s0 = R * 1; p.s1 = R * 10; p.op = .95f; p.c0 = Web.Hex("#ffd8a0"); p.c1 = Web.Hex("#ff8a3a"); p.n = Vector3.up; }
            // dust ring along the ground
            if (onGround && big)
                for (int i = 0, m = N(14); i < m; i++)
                {
                    ref var p = ref New(1); float a = i / (float)m * 6.283f + Rnd(-.2f, .2f), sp = R * Rnd(1.3f, 1.9f);
                    p.p = new Vector3(at.x + Mathf.Cos(a) * R * .4f, gy + .35f, at.z + Mathf.Sin(a) * R * .4f);
                    p.v = new Vector3(Mathf.Cos(a) * sp, .3f, Mathf.Sin(a) * sp); p.drag = 2.2f;
                    p.life = 1.6f; p.s0 = R * .6f * Rnd(.8f, 1.2f); p.s1 = R * 1.7f; p.op = .7f; p.c0 = Web.Hex("#d8ccb6"); p.c1 = Web.Hex("#b8ab96"); p.spin = Rnd(-.6f, .6f);
                }
            // fireball: puffs flung out and rising, cooling as they go
            for (int i = 0, m = N(big ? 22 : 12); i < m; i++)
            {
                ref var p = ref New(0);
                var d = new Vector3(Rnd(-1, 1), Rnd(-.1f, 1), Rnd(-1, 1)).normalized;
                p.p = c + d * R * .1f; p.v = d * R * Rnd(1.1f, 2.6f) + Vector3.up * R * .6f; p.drag = 4.5f; p.grav = -R * .5f;
                p.life = (big ? .75f : .55f) * Rnd(.7f, 1.15f); p.s0 = R * Rnd(.35f, .55f); p.s1 = R * Rnd(.9f, 1.3f) * (big ? 1 : .8f);
                p.op = 1; p.c0 = hotC; p.c1 = coolC; p.spin = Rnd(-2, 2); p.fadeIn = .02f;
            }
            // smoke column
            if (smoky)
                for (int i = 0, m = N(big ? 24 : 8); i < m; i++)
                {
                    ref var p = ref New(1);
                    var d = new Vector3(Rnd(-1, 1), Rnd(0, 1), Rnd(-1, 1)).normalized;
                    p.p = c + d * R * Rnd(.1f, .4f); p.v = d * R * Rnd(.5f, 1.2f) + Vector3.up * R * Rnd(.5f, 1); p.drag = 1.6f; p.grav = -Rnd(.8f, 1.6f);
                    p.life = (big ? 3.2f : 1.8f) * Rnd(.75f, 1.1f); p.s0 = R * Rnd(.45f, .7f); p.s1 = R * Rnd(1.6f, 2.4f) * (big ? 1 : .6f);
                    p.op = big ? .62f : .4f; p.c0 = Web.Hex("#3a3836"); p.c1 = Web.Hex("#77726c"); p.spin = Rnd(-.5f, .5f); p.fadeIn = .12f;
                    p.t = -Rnd(0, .15f);   // a little later than the fire
                }
            // sparks
            for (int i = 0, m = N(big ? 30 : 12); i < m; i++)
            {
                ref var p = ref New(4);
                float a = Rnd(0, 6.283f), el = Rnd(.15f, 1.2f), v = Rnd(9, 20) * (big ? 1 : .7f);
                p.p = c; p.v = new Vector3(Mathf.Cos(a) * Mathf.Cos(el) * v, Mathf.Sin(el) * v, Mathf.Sin(a) * Mathf.Cos(el) * v);
                p.grav = 22; p.drag = .8f; p.life = Rnd(.5f, 1.1f); p.s0 = .07f; p.op = 1;
                p.c0 = i % 3 != 0 ? Web.Hex("#ffb347") : Web.Hex("#fff1b0"); p.c1 = Web.Hex("#ff5a14");
            }
            // chunks (the weapons chat's spinning blocks)
            if (big) GunFx.Emit(c, new EmitOpts { n = N(10), speed = 8, up = 6, size = .16f, life = 1.6f, grav = 30, colors = new[] { Web.Hex("#2e2f33"), Web.Hex("#4a4038"), Web.Hex("#5a5048") } });
            if (cols != null && cols.Length > 0) GunFx.Emit(c, new EmitOpts { n = N(big ? 24 : 18), speed = 10 * k, up = 3, size = .2f, life = .7f, grav = 6, colors = cols });
            // light
            light.transform.position = c + Vector3.up; light.range = (big ? 26 : 14) * Mathf.Max(.6f, k); lightK = big ? (low ? 30 : 60) : 22; lightT = 0;
            if (cols != null && !big && cols.Length > 0) light.color = Color.Lerp(cols[0], Color.white, .3f); else light.color = Web.Hex("#ffa040");
            // scorch marks (not in water)
            if (big) Scorches(at, n, R, gy);
            live = 4;
        }

        /// a ground-smash dust ring (Gorillaz landing)
        public static void Dust(Vector3 at, float R)
        {
            var I = Get(); if (!I) return;
            float gy = GroundAt(at.x, at.z, at.y + .6f); if (gy == float.MinValue) gy = at.y;
            for (int i = 0, m = I.N(14); i < m; i++)
            {
                ref var p = ref I.New(1); float a = i / (float)m * 6.283f + Rnd(-.2f, .2f), sp = R * Rnd(1.1f, 1.6f);
                p.p = new Vector3(at.x + Mathf.Cos(a) * .6f, gy + .3f, at.z + Mathf.Sin(a) * .6f);
                p.v = new Vector3(Mathf.Cos(a) * sp, .4f, Mathf.Sin(a) * sp); p.drag = 2.6f;
                p.life = 1.2f; p.s0 = .8f; p.s1 = R * .6f; p.op = .65f; p.c0 = Web.Hex("#cfc2ab"); p.c1 = Web.Hex("#a99c88"); p.spin = Rnd(-.6f, .6f);
            }
            { ref var p = ref I.New(3); p.p = new Vector3(at.x, gy + .06f, at.z); p.life = .4f; p.s0 = 1; p.s1 = R * 2.6f; p.op = .6f; p.c0 = Web.Hex("#e8dcc4"); p.c1 = Web.Hex("#a08c70"); p.n = Vector3.up; }
            I.live = 4;
        }

        /// top of the highest box under (x, z) at or below maxY (float.MinValue if none)
        static float GroundAt(float x, float z, float maxY)
        {
            float best = float.MinValue; var B = BoxWorld.Boxes;
            for (int i = 0; i < B.Count; i++) { var b = B[i]; if (x >= b.x0 && x <= b.x1 && z >= b.z0 && z <= b.z1 && b.y1 <= maxY && b.y1 > best) best = b.y1; }
            if (best == float.MinValue && maxY > 0) best = 0;
            return best;
        }

        // ------------------------------------------------------------------ scorch marks
        class Scorch { public GameObject go; public Material m, em; public float t; public int box; }
        readonly List<Scorch> scorches = new List<Scorch>();
        static Mesh quad;
        static Mesh ScorchQuad { get { if (!quad) { quad = Instantiate(WeaponModels.Quad); quad.name = "scorch quad"; } return quad; } }

        void Scorches(Vector3 at, Vector3? n, float R, float gy)
        {
            float k = Mathf.Clamp(R / 4.5f, .35f, 1.2f);
            if (gy > float.MinValue && at.y - gy < Mathf.Max(1.2f, R * .35f)) AddScorch(new Vector3(at.x, gy, at.z), Vector3.up, -1, k);
            if (n.HasValue && n.Value.y < .5f) { AddScorch(at, n.Value, -1, k); return; }
            float reach = Mathf.Max(.9f, R * .3f);
            foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                if (Combat.RayBoxes(at, d, reach, out var hb) && Mathf.Abs(hb.n.y) < .5f) AddScorch(at + d * hb.t, hb.n, hb.box, k * (1 - hb.t / reach * .4f));
        }

        void AddScorch(Vector3 at, Vector3 n, int box, float k)
        {
            var go = new GameObject("scorch"); go.transform.SetParent(transform, false);
            float sz = (2.4f + Random.value * .6f) * k;
            go.transform.position = at + n * .03f; go.transform.rotation = Quaternion.LookRotation(-n) * Quaternion.Euler(0, 0, Random.value * 360); go.transform.localScale = new Vector3(sz, sz, 1);
            go.AddComponent<MeshFilter>().sharedMesh = ScorchQuad;
            var mr = go.AddComponent<MeshRenderer>(); var m = GunFx.MakeMat(false, scorchTex); m.renderQueue = 2990; mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var eg = new GameObject("embers"); eg.transform.SetParent(go.transform, false); eg.transform.localPosition = new Vector3(0, 0, -.01f); eg.transform.localScale = Vector3.one * .25f;
            eg.AddComponent<MeshFilter>().sharedMesh = ScorchQuad;
            var er = eg.AddComponent<MeshRenderer>(); var em = GunFx.MakeMat(true, glowTex); em.renderQueue = 2995; er.sharedMaterial = em; er.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            scorches.Add(new Scorch { go = go, m = m, em = em, box = box });
            while (scorches.Count > 24) { Destroy(scorches[0].go); scorches.RemoveAt(0); }
        }

        void ClearScorch() { foreach (var s in scorches) if (s.go) Destroy(s.go); scorches.Clear(); }

        void UpdScorch(float dt)
        {
            for (int i = scorches.Count - 1; i >= 0; i--)
            {
                var s = scorches[i]; s.t += dt;
                if (!s.go) { scorches.RemoveAt(i); continue; }
                // a mark on something that has since broken or moved goes with it
                if (s.box >= 0 && (s.box >= BoxWorld.Boxes.Count || BoxWorld.Boxes[s.box].y1 <= BoxWorld.Boxes[s.box].y0)) { Destroy(s.go); scorches.RemoveAt(i); continue; }
                float e = Mathf.Max(0, .9f - s.t * .6f); s.em.SetColor("_Color", new Color(1, .48f, .12f, 1) * e * 1.6f);
                if (s.t > 1.6f && s.em) s.em.SetColor("_Color", Color.clear);
                if (s.t > 40) { float a = Mathf.Max(0, 1 - (s.t - 40) / 5); s.m.SetColor("_Color", new Color(1, 1, 1, a)); if (a <= 0) { Destroy(s.go); scorches.RemoveAt(i); } }
            }
        }

        // ------------------------------------------------------------------ simulate and draw
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            UpdScorch(dt);
            lightT += dt; light.intensity = lightT < .5f ? lightK * Mathf.Pow(1 - lightT / .5f, 2) : 0;
            if (live <= 0) return;
            var cam = Camera.main; if (!cam) return;
            Vector3 cr = cam.transform.right, cu = cam.transform.up, cf = cam.transform.forward, eye = cam.transform.position;
            int any = 0;
            for (int layer = 0; layer < 4; layer++)
            {
                vb.Clear(); cb.Clear(); ub.Clear(); ib.Clear();
                for (int i = 0; i < MAX; i++)
                {
                    ref var p = ref ps[i]; if (!p.a) continue;
                    int lay = p.kind == 4 ? 2 : p.kind;
                    if (lay != layer) continue;
                    p.t += dt; if (p.t < 0) continue;
                    float u = p.t / p.life; if (u >= 1) { p.a = false; continue; }
                    any++;
                    if (p.drag > 0) { float kd = Mathf.Exp(-p.drag * dt); p.v *= kd; }
                    p.v.y -= p.grav * dt; p.p += p.v * dt; p.rot += p.spin * dt;
                    float e = 1 - (1 - u) * (1 - u) * (1 - u);
                    float size = p.s0 + (p.s1 - p.s0) * e;
                    float fade = (p.fadeIn > 0 ? Mathf.Clamp01(p.t / p.fadeIn) : 1);
                    Color col;
                    switch (p.kind)
                    {
                        case 0:
                            {   // fire: white-hot, through its colour, to a dark ember, fading out over the second half
                                float hot = Mathf.Max(0, 1 - u * 1.4f);
                                var cc = u < .5f ? Color.Lerp(p.c0, p.c1, u / .5f) : Color.Lerp(p.c1, new Color(p.c1.r * .3f, p.c1.g * .1f, p.c1.b * .05f), (u - .5f) / .5f);
                                cc = Color.Lerp(cc, Color.white, hot * hot * .5f).linear;   // web colours are display (sRGB) values
                                float al = fade * (1 - Mathf.SmoothStep(.45f, 1, u)) * .9f;
                                col = new Color(cc.r * al * 1.6f, cc.g * al * 1.6f, cc.b * al * 1.6f, al * .55f);
                                Quad(p.p, cr, cu, size, col, p.rot);
                            }
                            break;
                        case 1:
                            {
                                float al = p.op * fade * Mathf.Pow(1 - u, 1.2f);
                                var cc = Color.Lerp(p.c0, p.c1, u).linear;
                                Quad(p.p, cr, cu, size, new Color(cc.r, cc.g, cc.b, al), p.rot);
                            }
                            break;
                        case 2:
                            {
                                float al = p.op * (1 - u);
                                var cc = Color.Lerp(p.c0, p.c1, u).linear * al; cc.a = 1;
                                Quad(p.p, cr, cu, size, cc, p.rot);
                            }
                            break;
                        case 3:
                            {
                                float al = p.op * (1 - u);
                                var cc = Color.Lerp(p.c0, p.c1, u).linear * al; cc.a = 1;
                                Quad(p.p, Vector3.right, Vector3.forward, size, cc, 0);
                            }
                            break;
                        case 4:
                            {   // spark: a streak along its velocity, bright then orange
                                float al = 1 - u; var cc = Color.Lerp(p.c0, p.c1, u).linear * (al * 2.2f); cc.a = 1;
                                var tail = p.p - p.v * .035f;
                                Streak(tail, p.p, p.s0, eye, cc);
                            }
                            break;
                    }
                }
                var mesh = meshes[layer]; mesh.Clear();
                if (vb.Count > 0) { mesh.SetVertices(vb); mesh.SetColors(cb); mesh.SetUVs(0, ub); mesh.SetTriangles(ib, 0, false); mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4000); }
            }
            if (any == 0) live--; else live = 4;
        }

        void Quad(Vector3 p, Vector3 r, Vector3 u, float s, Color c, float rot)
        {
            float h = s * .5f, cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
            var R = (r * cs + u * sn) * h; var U = (u * cs - r * sn) * h;
            int b = vb.Count;
            vb.Add(p - R - U); vb.Add(p + R - U); vb.Add(p + R + U); vb.Add(p - R + U);
            ub.Add(new Vector2(0, 0)); ub.Add(new Vector2(1, 0)); ub.Add(new Vector2(1, 1)); ub.Add(new Vector2(0, 1));
            cb.Add(c); cb.Add(c); cb.Add(c); cb.Add(c);
            ib.Add(b); ib.Add(b + 2); ib.Add(b + 1); ib.Add(b); ib.Add(b + 3); ib.Add(b + 2);
        }

        void Streak(Vector3 a, Vector3 b, float w, Vector3 eye, Color c)
        {
            var d = b - a; var side = Vector3.Cross(d, eye - (a + b) * .5f); if (side.sqrMagnitude < 1e-8f) return; side = side.normalized * w * .5f;
            int k = vb.Count;
            vb.Add(a - side); vb.Add(a + side); vb.Add(b + side); vb.Add(b - side);
            for (int i = 0; i < 4; i++) { cb.Add(c); ub.Add(new Vector2(.5f, i == 0 || i == 3 ? .35f : .65f)); }
            ib.Add(k); ib.Add(k + 1); ib.Add(k + 2); ib.Add(k); ib.Add(k + 2); ib.Add(k + 3);
        }
    }
}
