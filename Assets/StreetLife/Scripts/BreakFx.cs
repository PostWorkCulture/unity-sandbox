// (Named BreakFx so it never clashes with the weapons chat's Fx.) Cheap effects for destruction, in the web game's style: flying chunks that bounce and shrink away, glass shards,
// puffs of smoke and dust, and an explosion (flash, fireball, shockwave ring, smoke column).
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class BreakFx : MonoBehaviour
    {
        static BreakFx inst;
        public static BreakFx I { get { if (!inst) inst = new GameObject("BreakFx").AddComponent<BreakFx>(); return inst; } }

        class Bit { public Transform t; public Renderer r; public Vector3 v, w; public float age, ttl, floorY, grav, grow, size; public bool puff, flat; public Color c; }
        readonly List<Bit> live = new List<Bit>();
        readonly Stack<Bit> cubes = new Stack<Bit>(), quads = new Stack<Bit>();
        MaterialPropertyBlock mpb;
        Material puffMat, glowMat;

        /// A plain lit material for things made from Unity's primitives at run time. In the editor a primitive comes with the
        /// project's default material; in a build it comes with the built-in one, which this render pipeline draws bright pink.
        /// That was the "purple confetti": every chunk of debris, the birds and butterflies, and tree stumps (16 Nov).
        static Material plain;
        public static Material Plain
        {
            get
            {
                if (!plain)
                {
                    var sh = Shader.Find("Universal Render Pipeline/Lit"); if (!sh) sh = Shader.Find("Universal Render Pipeline/Unlit");
                    plain = new Material(sh) { name = "plain (run-time primitives)" };
                    if (plain.HasProperty("_Smoothness")) plain.SetFloat("_Smoothness", .25f);
                }
                return plain;
            }
        }

        Material Transparent(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
            m.SetColor("_BaseColor", c);
            return m;
        }

        void Awake() { mpb = new MaterialPropertyBlock(); puffMat = Transparent(new Color(.6f, .6f, .62f, .6f)); glowMat = Transparent(new Color(1f, .6f, .2f, .8f)); }

        Bit Get(bool puff)
        {
            var st = puff ? quads : cubes;
            if (st.Count > 0) { var b = st.Pop(); b.t.gameObject.SetActive(true); return b; }
            var go = GameObject.CreatePrimitive(puff ? PrimitiveType.Sphere : PrimitiveType.Cube);
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<Renderer>();
            if (puff) { r.sharedMaterial = puffMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            else r.sharedMaterial = Plain;
            return new Bit { t = go.transform, r = r, puff = puff };
        }

        /// Chunks thrown out from a broken thing, bouncing on floorY and shrinking away after ttl.
        public void Chunks(Vector3 at, Vector3 size, Vector3 away, Color[] cols, int n, float floorY, float ttl = 3.2f, bool glass = false)
        {
            for (int i = 0; i < n; i++)
            {
                var b = Get(false);
                float sz = glass ? Random.Range(.03f, .12f) : Random.Range(.12f, .3f);
                b.t.localScale = glass ? new Vector3(sz, .01f, sz * Random.Range(.6f, 1.4f)) : new Vector3(Random.Range(.4f, .9f) * Mathf.Min(size.x, 1.2f), sz * .5f, sz);
                b.t.position = at + new Vector3(Random.Range(-.4f, .4f) * size.x, Random.Range(-.4f, .4f) * size.y, Random.Range(-.4f, .4f) * size.z);
                b.t.rotation = Random.rotation;
                b.v = away * Random.Range(3f, 8f) + new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(4f, 9f), Random.Range(-2.5f, 2.5f));
                b.w = new Vector3(Random.Range(-480f, 480f), Random.Range(-480f, 480f), Random.Range(-480f, 480f));
                b.age = 0; b.ttl = ttl * Random.Range(.8f, 1.2f); b.floorY = floorY + b.t.localScale.y / 2; b.grav = glass ? 22 : 26; b.size = 1; b.flat = glass;
                b.c = cols[i % cols.Length];
                mpb.Clear(); mpb.SetColor("_BaseColor", b.c); b.r.SetPropertyBlock(mpb);
                live.Add(b);
            }
        }

        /// Soft puffs (smoke, dust, steam): rise or sink slowly, grow and fade.
        public void Puffs(Vector3 at, int n, float spread, float up, float size, float ttl, Color c, float grow = 1.6f)
        {
            for (int i = 0; i < n; i++)
            {
                var b = Get(true);
                b.t.position = at + Random.insideUnitSphere * spread;
                b.t.localScale = Vector3.one * size * Random.Range(.7f, 1.2f);
                b.v = new Vector3(Random.Range(-.6f, .6f), up * Random.Range(.6f, 1.2f), Random.Range(-.6f, .6f));
                b.w = Vector3.zero; b.age = 0; b.ttl = ttl * Random.Range(.8f, 1.25f); b.grav = 0; b.size = size; b.grow = grow; b.floorY = -999; b.c = c;
                mpb.Clear(); mpb.SetColor("_BaseColor", c); b.r.SetPropertyBlock(mpb);
                live.Add(b);
            }
        }

        /// The explosion: a flash of light, a fireball that billows and darkens, a ring of dust, then a smoke column.
        public void Boom(Vector3 at, float radius)
        {
            if (BoomFx.Play(at, radius, null, true)) return;   // the full explosion (BoomFx.cs)
            var go = new GameObject("Boom"); go.transform.position = at + Vector3.up * .3f;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, .7f, .35f); l.range = radius * 4; l.intensity = 8;
            StartCoroutine(FadeLight(l, .5f));
            Puffs(at + Vector3.up * .6f, 10, radius * .25f, 1.2f, radius * .45f, .7f, new Color(1f, .62f, .2f, .85f), 2.2f);
            Puffs(at + Vector3.up * .4f, 6, radius * .2f, .6f, radius * .3f, .5f, new Color(1f, .9f, .6f, .9f), 2.5f);
            Puffs(at + Vector3.up * 1.2f, 14, radius * .35f, 2.4f, radius * .4f, 2.6f, new Color(.18f, .18f, .2f, .55f), 1.8f);
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2;
                Puffs(at + new Vector3(Mathf.Cos(a), .1f, Mathf.Sin(a)) * radius * .4f, 1, .1f, .2f, .6f, .8f, new Color(.78f, .72f, .62f, .5f), 2.4f);
            }
            Chunks(at + Vector3.up * .5f, Vector3.one, Vector3.up, new[] { new Color(1f, .75f, .2f), new Color(1f, .45f, .1f), new Color(.2f, .2f, .22f) }, 12, at.y, 1.2f);
        }

        System.Collections.IEnumerator FadeLight(Light l, float t)
        {
            float k = 0, i0 = l.intensity;
            while (k < t && l) { k += Time.deltaTime; l.intensity = i0 * (1 - k / t); yield return null; }
            if (l) Destroy(l.gameObject);
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, .05f);
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var b = live[i]; b.age += dt;
                if (b.age >= b.ttl) { b.t.gameObject.SetActive(false); (b.puff ? quads : cubes).Push(b); live.RemoveAt(i); continue; }
                if (b.puff)
                {
                    b.t.position += b.v * dt; b.v *= 1 - dt * .8f;
                    float u = b.age / b.ttl;
                    b.t.localScale = Vector3.one * b.size * (1 + u * b.grow);
                    var c = b.c; c.a *= 1 - u * u; mpb.Clear(); mpb.SetColor("_BaseColor", c); b.r.SetPropertyBlock(mpb);
                    continue;
                }
                b.v.y -= b.grav * dt;
                var p = b.t.position + b.v * dt;
                if (p.y < b.floorY && b.v.y < 0) { p.y = b.floorY; b.v.y *= -.3f; b.v.x *= .6f; b.v.z *= .6f; b.w *= .5f; if (b.flat) b.w = Vector3.zero; }
                b.t.position = p; b.t.Rotate(b.w * dt, Space.Self);
                if (b.age > b.ttl - .6f) b.t.localScale *= Mathf.Max(.01f, 1 - dt / .6f * 1.8f);
            }
        }
    }
}
