// Stun stars and screen-edge effects (match chat), from the web game's stunStars and its d-edge layer:
//  - Stunned or dazed (Stun grenade, Flash, Frost nova, Rumble air strikes...): four inked cartoon stars spin round above
//    the head with a faint halo ring and little blue sparks, on anyone.
//  - Your own screen: stunned or dazed: a warm yellow edge, slow spinning rays and three bobbing stars along the top;
//    caught in a grenade or rocket blast: a pulsing red edge and soot spots for 1.1 s; slowed by frost: frosted, cracked
//    glass round the edges and a cold blue tint. Each fades in and out over a quarter of a second.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(1050)]
    public class ScreenFx : MonoBehaviour
    {
        static ScreenFx I;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();   // RoundManager.Reboot runs this again after every scene reload
        static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("ScreenFx").AddComponent<ScreenFx>();
        }

        float blastT;
        void OnEnable() { Combat.OnDamage += OnDamage; }
        void OnDisable() { Combat.OnDamage -= OnDamage; if (I == this) I = null; }
        void OnDamage(Fighter v, float amt, Fighter src, bool head, string gun) { if (v && v == Combat.Me && (gun == "grenade" || gun == "rocket" || gun == "nade" || gun == "gold")) blastT = 1.1f; }

        // ------------------------------------------------------------------ the stars over a head
        class Stars { public Transform root, spin; public Transform[] star = new Transform[4]; public Transform[] spark = new Transform[5]; }
        readonly Dictionary<Fighter, Stars> stars = new Dictionary<Fighter, Stars>();
        static Mesh starMesh, ringMesh, sparkMesh; static Material yellow, ink, ringMat, sparkMat;

        void LateUpdate()
        {
            if (blastT > 0) blastT -= Time.deltaTime;
            float t = Time.time;
            foreach (var f in Combat.Fighters)
            {
                if (!f) continue;
                bool on = (f.stunT > 0 || f.dazeT > 0) && f.alive && !f.InFinisher && !Cine.HideHud;
                stars.TryGetValue(f, out var S);
                if (!on) { if (S != null && S.root.gameObject.activeSelf) S.root.gameObject.SetActive(false); continue; }
                if (S == null) stars[f] = S = Make();
                if (!S.root.gameObject.activeSelf) S.root.gameObject.SetActive(true);
                S.root.position = FinisherPose.HeadPos(f) + Vector3.up * .55f * Fighter.SC;
                S.root.localScale = Vector3.one * 1.2f * Fighter.SC;
                for (int i = 0; i < 4; i++)
                {
                    float a = t * 4.2f + i / 4f * Mathf.PI * 2;
                    S.star[i].localPosition = new Vector3(Mathf.Cos(a) * .55f, Mathf.Sin(a * 2) * .05f, Mathf.Sin(a) * .55f);
                    S.star[i].localRotation = Quaternion.Euler(0, (-a + Mathf.PI / 2) * Mathf.Rad2Deg, (t * 5 + i) * Mathf.Rad2Deg);
                }
                for (int i = 0; i < 5; i++)
                {
                    float ph = (t * 2.3f + i * .37f) % 1f, a = i * 1.9f + Mathf.Floor(t * 2.3f + i * .37f) * 2.4f;
                    S.spark[i].localPosition = new Vector3(Mathf.Cos(a) * (.2f + ph * .25f), -.35f + ph * .3f, Mathf.Sin(a) * (.2f + ph * .25f));
                    S.spark[i].localScale = Vector3.one * .07f * (1 - ph);
                }
            }
        }

        Stars Make()
        {
            if (!starMesh)
            {
                starMesh = StarMesh(); ringMesh = TorusMesh(.55f, .012f, 40, 6);
                var sp = GameObject.CreatePrimitive(PrimitiveType.Sphere); sparkMesh = sp.GetComponent<MeshFilter>().sharedMesh; Destroy(sp);
                yellow = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "stun star" };
                yellow.SetColor("_BaseColor", Web.Hex("#ffd21f")); yellow.SetFloat("_Smoothness", .65f); yellow.SetFloat("_Metallic", .1f);
                yellow.EnableKeyword("_EMISSION"); yellow.SetColor("_EmissionColor", Web.Hex("#ffb000") * .55f);
                ink = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "stun star ink" };   // back faces only: an outline
                ink.SetColor("_BaseColor", Web.Hex("#14161c")); ink.SetFloat("_Smoothness", 0); ink.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
                ringMat = GunFx.MakeMat(false, null); var rc = Web.Hex("#fff6c0"); rc.a = .4f; ringMat.SetColor("_Color", rc);
                sparkMat = GunFx.MakeMat(true, null); var sc = Web.Hex("#7fd8ff"); sc.a = .9f; sparkMat.SetColor("_Color", sc);
            }
            var S = new Stars();
            S.root = new GameObject("stun stars").transform; S.root.SetParent(transform, false);
            S.spin = new GameObject("spin").transform; S.spin.SetParent(S.root, false); S.spin.localRotation = Quaternion.Euler(.35f * Mathf.Rad2Deg, 0, .18f * Mathf.Rad2Deg);
            for (int i = 0; i < 4; i++)
            {
                var g = new GameObject("star").transform; g.SetParent(S.spin, false); S.star[i] = g;
                Part(g, starMesh, yellow, Vector3.one, true);
                Part(g, starMesh, ink, Vector3.one * 1.22f, false);   // the ink outline: the same star, bigger, back faces only
            }
            Part(S.spin, ringMesh, ringMat, Vector3.one, false);
            for (int i = 0; i < 5; i++) S.spark[i] = Part(S.root, sparkMesh, sparkMat, Vector3.one * .07f, false);
            return S;
        }

        static Transform Part(Transform p, Mesh m, Material mat, Vector3 scale, bool shadow)
        {
            var go = new GameObject("p"); go.transform.SetParent(p, false); go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = m; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        // a five-pointed star, .17 out and .075 in, .09 thick (the web game's extruded star with its bevel)
        static Mesh StarMesh()
        {
            var v = new List<Vector3>(); var tr = new List<int>(); var col = new List<Color>();
            var ring = new Vector2[10];
            for (int i = 0; i < 10; i++) { float a = i / 10f * Mathf.PI * 2 - Mathf.PI / 2, r = i % 2 == 1 ? .075f : .17f; ring[i] = new Vector2(Mathf.Cos(a) * r, -Mathf.Sin(a) * r); }
            const float d = .045f;
            foreach (var z in new[] { d, -d })
            {
                int c = v.Count; v.Add(new Vector3(0, 0, z));
                for (int i = 0; i < 10; i++) v.Add(new Vector3(ring[i].x, ring[i].y, z));
                for (int i = 0; i < 10; i++) { int a = c + 1 + i, b = c + 1 + (i + 1) % 10; if (z > 0) { tr.Add(c); tr.Add(a); tr.Add(b); } else { tr.Add(c); tr.Add(b); tr.Add(a); } }
            }
            for (int i = 0; i < 10; i++)
            {
                var p0 = ring[i]; var p1 = ring[(i + 1) % 10]; int b = v.Count;
                v.Add(new Vector3(p0.x, p0.y, d)); v.Add(new Vector3(p1.x, p1.y, d)); v.Add(new Vector3(p0.x, p0.y, -d)); v.Add(new Vector3(p1.x, p1.y, -d));
                tr.Add(b); tr.Add(b + 2); tr.Add(b + 1); tr.Add(b + 1); tr.Add(b + 2); tr.Add(b + 3);
            }
            for (int i = 0; i < tr.Count; i += 3) { int k = tr[i + 1]; tr[i + 1] = tr[i + 2]; tr[i + 2] = k; }   // Unity winds front faces clockwise
            foreach (var _ in v) col.Add(Color.white);
            var m = new Mesh { name = "stun star" }; m.SetVertices(v); m.SetTriangles(tr, 0); m.SetColors(col); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh TorusMesh(float R, float r, int seg, int side)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var col = new List<Color>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2; var c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j <= side; j++) { float b = j / (float)side * Mathf.PI * 2; v.Add(c * R + (c * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b)) * r); col.Add(Color.white); }
            }
            for (int i = 0; i < seg; i++) for (int j = 0; j < side; j++) { int a = i * (side + 1) + j, b = a + side + 1; t.Add(a); t.Add(b); t.Add(a + 1); t.Add(a + 1); t.Add(b); t.Add(b + 1); }
            var m = new Mesh { name = "halo ring" }; m.SetVertices(v); m.SetTriangles(t, 0); m.SetColors(col); m.RecalculateNormals(); return m;
        }

        // ------------------------------------------------------------------ your screen's edges
        float kBlast, kStun, kFreeze;
        static Texture2D vigRed, vigGold, frost, rays, starTex, soot;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            var me = Combat.Me; if (!me || MainMenu.Open || Cine.HideHud) return;
            float dt = Time.unscaledDeltaTime * 4;   // .25 s fades
            bool alive = me.alive;
            string st = !alive ? "" : blastT > 0 ? "blast" : me.stunT > 0 || me.dazeT > 0 ? "stun" : "";
            kBlast = Mathf.MoveTowards(kBlast, st == "blast" ? 1 : 0, dt);
            kStun = Mathf.MoveTowards(kStun, st == "stun" ? 1 : 0, dt);
            kFreeze = Mathf.MoveTowards(kFreeze, st == "freeze" ? 1 : 0, dt);
            if (kBlast <= 0 && kStun <= 0 && kFreeze <= 0) return;
            GUI.depth = 8;   // under the HUD
            var full = new Rect(0, 0, Screen.width, Screen.height); var o = GUI.color; float t = Time.unscaledTime;
            if (kFreeze > 0)
            {
                if (!frost) frost = FrostTex();
                GUI.color = new Color(120 / 255f, 190 / 255f, 1f, .16f * kFreeze); GUI.DrawTexture(full, Texture2D.whiteTexture);
                GUI.color = new Color(1, 1, 1, kFreeze); GUI.DrawTexture(full, frost);
            }
            if (kBlast > 0)
            {
                if (!vigRed) vigRed = Vig(new Color(170 / 255f, 10 / 255f, 0, .45f), new Color(70 / 255f, 0, 0, .9f), .4f);
                if (!soot) soot = SootTex();
                float pulse = .75f + .25f * Mathf.PingPong(t * 4, 1);   // fxPulse
                GUI.color = new Color(1, 1, 1, kBlast * pulse); GUI.DrawTexture(full, vigRed);
                GUI.color = new Color(1, 1, 1, kBlast); GUI.DrawTexture(full, soot);
            }
            if (kStun > 0)
            {
                if (!vigGold) vigGold = Vig(new Color(1f, 200 / 255f, 30 / 255f, .28f), new Color(1f, 170 / 255f, 0, .55f), .42f);
                if (!rays) rays = RaysTex();
                if (!starTex) starTex = StarTex();
                GUI.color = new Color(1, 1, 1, kStun); GUI.DrawTexture(full, vigGold);
                var m = GUI.matrix; float big = Mathf.Max(Screen.width, Screen.height) * 1.5f;
                GUIUtility.RotateAroundPivot(t * 360f / 6f % 360, new Vector2(Screen.width / 2f, Screen.height / 2f));   // fxSpin
                GUI.DrawTexture(new Rect(Screen.width / 2f - big / 2, Screen.height / 2f - big / 2, big, big), rays);
                GUI.matrix = m;
                float vmin = Mathf.Min(Screen.width, Screen.height) / 100f, sz = 8 * vmin, gap = 2.2f * vmin;
                for (int i = 0; i < 3; i++)
                {
                    float ph = Mathf.PingPong(t / .9f + i / 3f, 1);   // fxStar: bob and rock
                    float x = Screen.width / 2f + (i - 1) * (sz + gap) - sz / 2, y = 2.5f * vmin + Mathf.Lerp(-.6f, .6f, ph) * vmin;
                    var m2 = GUI.matrix; GUIUtility.RotateAroundPivot(Mathf.Lerp(-18, 18, ph), new Vector2(x + sz / 2, y + sz / 2));
                    GUI.DrawTexture(new Rect(x, y, sz, sz), starTex); GUI.matrix = m2;
                }
            }
            GUI.color = o;
        }

        // ------------------------------------------------------------------ textures (made once)
        static Texture2D Vig(Color mid, Color edge, float clear)
        {
            const int S = 128; var tx = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float dx = (x + .5f) / S * 2 - 1, dy = (y + .5f) / S * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    Color c = d < clear ? new Color(mid.r, mid.g, mid.b, 0) : d < .7f ? Color.Lerp(new Color(mid.r, mid.g, mid.b, 0), mid, (d - clear) / (.7f - clear)) : Color.Lerp(mid, edge, (d - .7f) / .3f);
                    tx.SetPixel(x, y, c);
                }
            tx.Apply(); return tx;
        }

        static float[] P;
        static float Nz(float x, float y)
        {
            if (P == null) { P = new float[512]; var r = new System.Random(7); for (int i = 0; i < 512; i++) P[i] = (float)r.NextDouble(); }
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float xf = x - xi, yf = y - yi; xf = xf * xf * (3 - 2 * xf); yf = yf * yf * (3 - 2 * yf);
            float V(int a, int b) => P[(int)(((uint)(a * 73856093) ^ (uint)(b * 19349663)) & 511)];
            float A = V(xi, yi), B = V(xi + 1, yi), C = V(xi, yi + 1), D = V(xi + 1, yi + 1);
            return A + (B - A) * xf + (C - A) * yf + (A - B - C + D) * xf * yf;
        }

        // frosted glass creeping in from the edges, with cracks (the web game's frostTex)
        static Texture2D FrostTex()
        {
            const int W = 480, H = 300; var px = new Color32[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    float dx = (x / (float)W - .5f) * 2, dy = (y / (float)H - .5f) * 2, e = Mathf.Pow(Mathf.Pow(Mathf.Abs(dx), 3) + Mathf.Pow(Mathf.Abs(dy), 3), 1 / 3f);
                    float f = Nz(x / 22f, y / 22f) * .5f + Nz(x / 9f, y / 9f) * .3f + Nz(x / 4f, y / 4f) * .2f;
                    float a = Mathf.Clamp01((e + f * .5f - .98f) * 3); float l = 215 + f * 40;
                    px[y * W + x] = new Color32((byte)Mathf.Min(255, l), (byte)Mathf.Min(255, l + 12), 255, (byte)(a * 235));
                }
            var r = new System.Random(11);
            for (int k = 0; k < 36; k++)
            {
                int side = k % 4; float x = side == 0 ? 0 : side == 1 ? W : (float)r.NextDouble() * W, y = side == 2 ? 0 : side == 3 ? H : (float)r.NextDouble() * H;
                float ang = Mathf.Atan2(H / 2f - y, W / 2f - x) + ((float)r.NextDouble() - .5f) * 1.2f;
                for (int s = 0; s < 4; s++)
                {
                    float nx = x + Mathf.Cos(ang) * 10, ny = y + Mathf.Sin(ang) * 12;
                    for (int i = 0; i <= 14; i++) { int px0 = Mathf.RoundToInt(Mathf.Lerp(x, nx, i / 14f)), py0 = Mathf.RoundToInt(Mathf.Lerp(y, ny, i / 14f)); if (px0 >= 0 && py0 >= 0 && px0 < W && py0 < H) px[py0 * W + px0] = new Color32(255, 255, 255, 180); }
                    x = nx; y = ny; ang += ((float)r.NextDouble() - .5f) * .9f;
                }
            }
            var tx = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp }; tx.SetPixels32(px); tx.Apply(); return tx;
        }

        // pale yellow bands every 10 degrees, clear in the middle (spun round while stunned)
        static Texture2D RaysTex()
        {
            const int S = 256; var tx = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float dx = (x + .5f) / S * 2 - 1, dy = (y + .5f) / S * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 360; bool band = ang % 20 < 10;
                    float mask = Mathf.Clamp01((d * .7f - .3f) / .45f);
                    tx.SetPixel(x, y, new Color(1, 230 / 255f, 120 / 255f, band ? .08f * 2.5f * mask : 0));
                }
            tx.Apply(); return tx;
        }

        static Texture2D StarTex()
        {
            const int S = 96; var tx = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++) { float a = i / 10f * Mathf.PI * 2 + Mathf.PI / 2, r = i % 2 == 1 ? .2f : .46f; pts[i] = new Vector2(.5f + Mathf.Cos(a) * r, .48f + Mathf.Sin(a) * r); }
            bool In(Vector2 p, float grow)
            {
                bool inside = false;
                for (int i = 0, j = 9; i < 10; j = i++)
                {
                    var a = Vector2.Lerp(new Vector2(.5f, .48f), pts[i], 1 + grow); var b = Vector2.Lerp(new Vector2(.5f, .48f), pts[j], 1 + grow);
                    if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                }
                return inside;
            }
            var ink = Web.Hex("#14161c"); var gold = Web.Hex("#ffd21f");
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    var p = new Vector2((x + .5f) / S, (y + .5f) / S);
                    tx.SetPixel(x, y, In(p, 0) ? gold : In(p + new Vector2(0, .02f), .14f) ? ink : new Color(0, 0, 0, 0));
                }
            tx.Apply(); return tx;
        }

        static Texture2D SootTex()
        {
            const int W = 256, H = 160; var tx = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var clear = new Color32[W * H]; tx.SetPixels32(clear);
            var spots = new[] { (.08f, .86f, .011f, .85f), (.14f, .78f, .006f, .7f), (.9f, .88f, .015f, .8f), (.84f, .8f, .007f, .7f), (.06f, .12f, .013f, .75f), (.93f, .16f, .01f, .8f), (.22f, .9f, .005f, .6f), (.78f, .08f, .006f, .6f) };
            foreach (var (sx, sy, r, a) in spots)
            {
                float rr = r * 100 * Mathf.Min(W, H) / 100f * 1.6f;
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                    {
                        float d = Mathf.Sqrt((x - sx * W) * (x - sx * W) + (y - sy * H) * (y - sy * H));
                        if (d < rr) tx.SetPixel(x, y, new Color(20 / 255f, 14 / 255f, 12 / 255f, a * Mathf.Clamp01((rr - d) / 1.5f)));
                    }
            }
            tx.Apply(); return tx;
        }

        /// remote test: call ScreenFx.Test stun|daze|blast|freeze (on the player)
        public static string Test(string a)
        {
            var me = Combat.Me; if (!me || !I) return "no player";
            switch ((a ?? "").Trim())
            {
                case "blast": I.blastT = 4f; break;
                case "daze": me.dazeT = 3; break;
                default: me.stunT = 3; foreach (var f in Combat.Fighters) if (f && f != me) f.stunT = 3; break;
            }
            return "showing " + a;
        }
    }
}
