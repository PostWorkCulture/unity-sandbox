// Maps chat: the beach and the lagoon. Beach Please: out past the shoreline the sea slows you to 80% (the web game's M.wade),
// your steps splash and slosh (wade1 / wade2 every .85 m), a hard landing in the shallows splashes, and the surf gets louder
// the nearer the camera is to the water. Both: white splash rings on the surface (falls, landings) from a small pool.
// Order -4: after the weapons set fighter.moveMul (-5), before Fighter moves (0).
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(-4)]
    public class Waterside : MonoBehaviour
    {
        MapWorld w;
        Fighter[] fighters = new Fighter[0];
        readonly Dictionary<Fighter, float> wadeDist = new Dictionary<Fighter, float>(), slowSet = new Dictionary<Fighter, float>();
        readonly Dictionary<Fighter, bool> wasGrounded = new Dictionary<Fighter, bool>(), stepSide = new Dictionary<Fighter, bool>();
        readonly Dictionary<Fighter, float> fallV = new Dictionary<Fighter, float>();
        AudioSource surf;
        class Ring { public Transform t; public Material m; public float age = 9, size = 1; }
        readonly List<Ring> rings = new List<Ring>(); int ringIx;

        public void Init(MapWorld world)
        {
            w = world;
            var ringTex = RingTexture();
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            var quad = QuadMesh();
            for (int i = 0; i < 24; i++)
            {
                var go = new GameObject("splash ring"); go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                var m = new Material(sh);
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3100;
                m.SetTexture("_BaseMap", ringTex); m.SetColor("_BaseColor", new Color(1, 1, 1, 0));
                mr.sharedMaterial = m; go.SetActive(false);
                rings.Add(new Ring { t = go.transform, m = m });
            }
            if (MapRules.Wading)
            {
                var clip = Resources.Load<AudioClip>("StreetLife/sfx/waves");
                if (clip) { surf = gameObject.AddComponent<AudioSource>(); surf.clip = clip; surf.loop = true; surf.spatialBlend = 0; surf.volume = 0; surf.Play(); }
            }
        }

        static Mesh QuadMesh()
        {
            var m = new Mesh { name = "ring quad" };
            m.SetVertices(new List<Vector3> { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            return m;
        }

        static Texture2D RingTexture()
        {
            const int S = 128; var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "splash ring" };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float dx = (x + .5f) / S * 2 - 1, dy = (y + .5f) / S * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1 - Mathf.Abs(r - .86f) / .12f); a = a * a * (3 - 2 * a);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        public float WaterY(float x, float z) => MapRules.DeepWater ? MapRules.LagoonWaterY(x, z, Time.timeSinceLevelLoad) : MapRules.BeachWaterY(x, z, Time.timeSinceLevelLoad);

        /// A white ring spreading on the water (the web game's M.splash).
        public void Splash(float x, float z, float size)
        {
            if (rings.Count == 0) return;
            var r = rings[ringIx]; ringIx = (ringIx + 1) % rings.Count;
            r.age = 0; r.size = size; r.t.position = new Vector3(x, WaterY(x, z) + .03f, z); r.t.gameObject.SetActive(true);
        }

        void Update()
        {
            if (Time.frameCount % 30 == 0 || fighters.Length == 0) fighters = FindObjectsByType<Fighter>(FindObjectsInactive.Exclude);
            float dt = Time.deltaTime;
            if (MapRules.Wading)
                foreach (var f in fighters)
                {
                    if (!f) continue;
                    // undo last frame's slowdown if nothing reset moveMul since (the weapons set it every frame when they're on)
                    if (slowSet.TryGetValue(f, out var was) && Mathf.Approximately(f.moveMul, was)) f.moveMul /= .8f;
                    slowSet.Remove(f);
                    bool inSea = f.alive && f.pos.y < .25f && MapRules.Wade(f.pos.x, f.pos.z);
                    wasGrounded.TryGetValue(f, out var g0); fallV.TryGetValue(f, out var fv);
                    if (inSea)
                    {
                        f.moveMul *= .8f; slowSet[f] = f.moveMul;
                        float v = f.HorizontalSpeed;
                        if (f.grounded && !g0 && fv < -4) { Splash(f.pos.x, f.pos.z, Mathf.Min(1.6f, .6f - fv * .06f)); Sfx.Play("splash", f.pos, Mathf.Clamp(-fv / 14, .3f, 1f) * .6f, 14); }
                        if (f.grounded && v > .5f)
                        {
                            wadeDist.TryGetValue(f, out var wd); wd += v * dt;
                            if (wd > .85f)
                            {
                                wd = 0; stepSide.TryGetValue(f, out var side); stepSide[f] = !side;
                                Splash(f.pos.x + f.vel.x * .06f, f.pos.z + f.vel.z * .06f, .38f + v * .03f);
                                Sfx.Play(side ? "wade1" : "wade2", f.pos, Mathf.Clamp(v / 8, .4f, 1.1f) * .25f, 12, Random.Range(.92f, 1.08f));
                            }
                            wadeDist[f] = wd;
                        }
                    }
                    wasGrounded[f] = f.grounded; fallV[f] = f.vel.y;
                }
            if (surf)
            {
                var cam = Camera.main;
                if (cam) { var c = cam.transform.position; float d = Mathf.Max(0, MapRules.Shore(-c.z) - c.x); surf.volume = Mathf.Clamp(1 - d / 40, .12f, 1) * .4f * Sfx.SfxVolume; }
            }
            foreach (var r in rings)
            {
                if (r.age > 1.2f) continue;
                r.age += dt; if (r.age > 1.2f) { r.t.gameObject.SetActive(false); continue; }
                float k = r.age / 1.2f;
                r.t.localScale = Vector3.one * (r.size * (.35f + 1.4f * (1 - (1 - k) * (1 - k))));
                r.m.SetColor("_BaseColor", new Color(1, 1, 1, .75f * (1 - k)));
            }
        }
    }
}
