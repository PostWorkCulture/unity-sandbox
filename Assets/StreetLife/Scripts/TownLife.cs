// Small ambient life from the web game's townLife: every so often a little flock of birds crosses the sky, flapping and
// gliding, and ten butterflies flutter round the front gardens either side of the road. (Cars, clouds, the balloon and the
// windmill are in Destruction; the web game doesn't actually sway its trees.)
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class TownLife : MonoBehaviour
    {
        public static void Ensure() { if (!FindAnyObjectByType<TownLife>()) new GameObject("TownLife").AddComponent<TownLife>(); }

        const float Z_LIM = 23f;
        class Bird { public Transform t, w0, w1; public float ph; }
        class Flock { public Transform g; public Vector3 dir; public float sp, life, ph; public List<Bird> birds = new List<Bird>(); }
        class Fly { public Transform g, p1, p2; public float hx, hz, ph; }

        readonly List<Flock> flocks = new List<Flock>();
        readonly List<Fly> flies = new List<Fly>();
        float next, t;
        MaterialPropertyBlock mpb;

        static float R() => Random.value;

        Transform Block(Transform parent, Vector3 size, Vector3 pos, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size;
            var r = go.GetComponent<Renderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.Clear(); mpb.SetColor("_BaseColor", c); r.SetPropertyBlock(mpb);
            return go.transform;
        }

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            next = 4 + R() * 8;
            var cols = new[] { Hex.ToColor("#ffd23f"), Color.white, Hex.ToColor("#ff8fab"), Hex.ToColor("#8ecae6") };
            for (int i = 0; i < 10; i++)
            {
                var g = new GameObject("butterfly").transform; g.SetParent(transform, false);
                var p1 = new GameObject("wingL").transform; p1.SetParent(g, false);
                var p2 = new GameObject("wingR").transform; p2.SetParent(g, false);
                Block(p1, new Vector3(.22f, .18f, .01f), new Vector3(-.11f, 0, 0), cols[i % 4]);
                Block(p2, new Vector3(.22f, .18f, .01f), new Vector3(.11f, 0, 0), cols[i % 4]);
                float side = i % 2 == 1 ? 1 : -1;
                flies.Add(new Fly { g = g, p1 = p1, p2 = p2, hx = side * (10 + R() * 2.5f), hz = (R() * 2 - 1) * Z_LIM, ph = R() * 9 });
            }
        }

        void SpawnFlock()
        {
            float a = R() * Mathf.PI * 2; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            int n = 1 + Mathf.FloorToInt(R() * 5); float y = 16 + R() * 14, sp = 8 + R() * 5;
            var f = new Flock { dir = dir, sp = sp, life = 240 / sp, ph = R() * 6 };
            f.g = new GameObject("flock").transform; f.g.SetParent(transform, false);
            f.g.position = new Vector3(-dir.x * 120 + (R() - .5f) * 60, y, -dir.z * 120 + (R() - .5f) * 60);
            f.g.rotation = Quaternion.LookRotation(dir);
            var c = Hex.ToColor("#2b2d33");
            for (int k = 0; k < n; k++)
            {
                var b = new GameObject("bird").transform; b.SetParent(f.g, false);
                int row = Mathf.CeilToInt(k / 2f);
                b.localPosition = new Vector3((k % 2 == 1 ? 1 : -1) * row * 1.4f, (R() - .5f) * .8f, -row * 1.6f);
                Block(b, new Vector3(.25f, .2f, .6f), Vector3.zero, c);
                var w0 = new GameObject("w").transform; w0.SetParent(b, false); w0.localPosition = new Vector3(-.1f, 0, 0); Block(w0, new Vector3(.7f, .05f, .32f), new Vector3(-.35f, 0, 0), c);
                var w1 = new GameObject("w").transform; w1.SetParent(b, false); w1.localPosition = new Vector3(.1f, 0, 0); Block(w1, new Vector3(.7f, .05f, .32f), new Vector3(.35f, 0, 0), c);
                f.birds.Add(new Bird { t = b, w0 = w0, w1 = w1, ph = R() * 6 });
            }
            flocks.Add(f);
        }

        void Update()
        {
            float dt = Time.deltaTime; t += dt;
            next -= dt; if (next <= 0) { next = 8 + R() * 16; SpawnFlock(); }
            for (int i = flocks.Count - 1; i >= 0; i--)
            {
                var f = flocks[i]; f.life -= dt;
                f.g.position += f.dir * f.sp * dt + Vector3.up * Mathf.Sin(t * .7f + f.ph) * .4f * dt;
                foreach (var b in f.birds)
                {
                    bool glide = Mathf.Sin(t * 1.1f + b.ph) > .55f;
                    float fl = glide ? .15f : Mathf.Sin(t * 13 + b.ph) * .7f;
                    b.w0.localRotation = Quaternion.Euler(0, 0, -fl * Mathf.Rad2Deg); b.w1.localRotation = Quaternion.Euler(0, 0, fl * Mathf.Rad2Deg);
                }
                if (f.life <= 0) { Destroy(f.g.gameObject); flocks.RemoveAt(i); }
            }
            foreach (var u in flies)
            {
                float k = t * .6f + u.ph;
                u.g.localPosition = new Vector3(u.hx + Mathf.Sin(k) * 2.2f + Mathf.Sin(k * 2.3f) * .6f, 1 + Mathf.Abs(Mathf.Sin(k * 1.7f)) * 1.1f, u.hz + Mathf.Cos(k * .8f) * 2.2f);
                u.g.localRotation = Quaternion.Euler(0, k * Mathf.Rad2Deg, 0);
                float fl = Mathf.Sin(t * 18 + u.ph) * 1.1f * Mathf.Rad2Deg;
                u.p1.localRotation = Quaternion.Euler(0, fl, 0); u.p2.localRotation = Quaternion.Euler(0, -fl, 0);
            }
        }
    }
}
