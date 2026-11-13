// Finishers chat: the props the finishing moves bring on, built the way the web game builds them (rounded boxes,
// cylinders and spheres with black ink outlines): Dog pile's four dogs, Heartbreaker's heart, Knockout's boxing gloves,
// Overcharge's x-ray skeleton, the Hot potato, Clear!'s defibrillator paddles and heart monitor, Combat's harpoon gun
// and spear, and the Ice sculpture's block. All in the web game's model space (facing +z), mirrored to Unity's on build.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetLife
{
    public static class FinisherProps
    {
        const float SC = Fighter.SC;

        // ------------------------------------------------------------------ meshes (cached)
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        static Mesh Cached(string key, System.Func<Mesh> make) { if (!meshes.TryGetValue(key, out var m) || !m) { m = make(); m.name = key; meshes[key] = m; } return m; }

        public static Mesh Box(float w, float h, float d) => RBox(w, h, d, 0);

        /// web RBX: a box with rounded edges and corners (radius capped at 45% of the smallest side).
        public static Mesh RBox(float w, float h, float d, float r)
        {
            r = Mathf.Min(r, Mathf.Min(w, Mathf.Min(h, d)) * .45f);
            return Cached($"rbox{w:F3},{h:F3},{d:F3},{r:F3}", () =>
            {
                int n = r > 0 ? 5 : 1;
                var half = new Vector3(w, h, d) / 2; var inner = half - Vector3.one * r;
                var V = new List<Vector3>(); var N = new List<Vector3>(); var T = new List<int>();
                Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var f in dirs)
                {
                    Vector3 a = f.y != 0 ? Vector3.right : Vector3.up, b = Vector3.Cross(f, a); int o = V.Count;
                    for (int j = 0; j <= n; j++)
                        for (int i = 0; i <= n; i++)
                        {
                            float u = i / (float)n * 2 - 1, v = j / (float)n * 2 - 1;
                            var p = Vector3.Scale(f + a * u + b * v, half);
                            if (r > 0)
                            {
                                var c = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                                var dn = p - c; var nn = dn.sqrMagnitude > 1e-10f ? dn.normalized : f;
                                V.Add(c + nn * r); N.Add(nn);
                            }
                            else { V.Add(p); N.Add(f); }
                        }
                    for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
                        {
                            int q = o + j * (n + 1) + i;
                            T.AddRange(new[] { q, q + 1, q + n + 2, q, q + n + 2, q + n + 1 });
                        }
                }
                return Finish(V, N, T);
            });
        }

        /// web CylinderGeometry(rTop, rBottom, height, segments), centred, with caps; rTop 0 makes a cone.
        public static Mesh Cyl(float rt, float rb, float h, int seg, float lift = 0, bool sideways = false) => Cached($"cyl{rt:F3},{rb:F3},{h:F3},{seg},{lift:F2},{sideways}", () =>
        {
            var V = new List<Vector3>(); var N = new List<Vector3>(); var T = new List<int>();
            float y0 = -h / 2 + lift, y1 = h / 2 + lift, slope = (rb - rt) / h;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); var nn = (d + Vector3.up * slope).normalized;
                V.Add(d * rb + Vector3.up * y0); N.Add(nn); V.Add(d * rt + Vector3.up * y1); N.Add(nn);
            }
            for (int i = 0; i < seg; i++) { int q = i * 2; T.AddRange(new[] { q, q + 1, q + 3, q, q + 3, q + 2 }); }
            foreach (var (y, r, up) in new[] { (y1, rt, 1f), (y0, rb, -1f) })
            {
                if (r <= 0) continue;
                int c = V.Count; V.Add(new Vector3(0, y, 0)); N.Add(Vector3.up * up);
                for (int i = 0; i <= seg; i++) { float a = i / (float)seg * Mathf.PI * 2; V.Add(new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r)); N.Add(Vector3.up * up); }
                for (int i = 0; i < seg; i++) T.AddRange(new[] { c, c + 1 + i, c + 2 + i });
            }
            if (sideways) for (int i = 0; i < V.Count; i++) { V[i] = new Vector3(-V[i].y, V[i].x, V[i].z); N[i] = new Vector3(-N[i].y, N[i].x, N[i].z); }
            return Finish(V, N, T, new Vector3(0, lift, 0));
        });

        public static Mesh Sphere(float r, int w = 16, int hs = 12) => Cached($"sph{r:F3},{w},{hs}", () =>
        {
            var V = new List<Vector3>(); var N = new List<Vector3>(); var T = new List<int>();
            for (int j = 0; j <= hs; j++) for (int i = 0; i <= w; i++)
                {
                    float th = j / (float)hs * Mathf.PI, ph = i / (float)w * Mathf.PI * 2;
                    var n = new Vector3(Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Cos(ph)); V.Add(n * r); N.Add(n);
                }
            for (int j = 0; j < hs; j++) for (int i = 0; i < w; i++) { int q = j * (w + 1) + i; T.AddRange(new[] { q, q + w + 1, q + 1, q + 1, q + w + 1, q + w + 2 }); }
            return Finish(V, N, T);
        });

        /// A flat quad facing -z (the web game's +z, mirrored), w x h, with UVs.
        public static Mesh Quad(float w, float h) => Cached($"quad{w:F3},{h:F3}", () =>
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-w / 2, -h / 2, 0), new Vector3(w / 2, -h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(-w / 2, h / 2, 0) };
            // the web plane's front (+z) becomes -z: seen from there, +x is on the viewer's right, so u runs with x
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds(); return m;
        });

        /// Every primitive here is convex: wind each triangle to face away from the centre (Unity: clockwise = front).
        static Mesh Finish(List<Vector3> V, List<Vector3> N, List<int> T, Vector3 centre = default)
        {
            for (int i = 0; i < T.Count; i += 3)
            {
                Vector3 a = V[T[i]], b = V[T[i + 1]], c = V[T[i + 2]];
                var cr = Vector3.Cross(b - a, c - a); var mid = (a + b + c) / 3 - centre;
                if (cr.sqrMagnitude < 1e-14f) cr = N[T[i]];
                if (Vector3.Dot(cr, mid) < 0) { var q = T[i + 1]; T[i + 1] = T[i + 2]; T[i + 2] = q; }
            }
            var m = new Mesh(); if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(N); m.SetTriangles(T, 0); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ materials (cached by look)
        public static Material Mat(string hex, float rough = .7f, float metal = 0, string emissive = null, float ek = 0)
        {
            string k = $"{hex}|{rough}|{metal}|{emissive}|{ek}";
            if (!mats.TryGetValue(k, out var m) || !m) { m = FinisherFx.LitMat(FinisherFx.C(hex), rough, metal, emissive != null ? FinisherFx.C(emissive) : (Color?)null, ek); mats[k] = m; }
            return m;
        }
        public static Material Basic(string hex)
        {
            string k = "basic|" + hex;
            if (!mats.TryGetValue(k, out var m) || !m) { m = FinisherFx.UnlitMat(FinisherFx.C(hex)); mats[k] = m; }
            return m;
        }

        // ------------------------------------------------------------------ building, in web model space
        /// Web model space to Unity: z is mirrored.
        public static Vector3 W(float x, float y, float z) => new Vector3(x, y, -z);
        /// A web rotation (radians, one axis at a time here) mirrored to Unity: x and y turn the other way.
        public static Quaternion WR(float x, float y, float z) => Quaternion.Euler(-x * Mathf.Rad2Deg, -y * Mathf.Rad2Deg, z * Mathf.Rad2Deg);

        public static Transform Group(Transform parent, string name, float x = 0, float y = 0, float z = 0)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = W(x, y, z); return t;
        }

        /// web piece(): a mesh at (x,y,z) with an optional rotation and (by default) an ink outline hull scaled 1.06.
        public static Transform Piece(Transform parent, Mesh mesh, Material m, float x, float y, float z, Vector3? rot = null, bool outline = true, float ol = 1.06f, bool shadow = true)
        {
            var go = new GameObject("piece"); var t = go.transform; t.SetParent(parent, false); t.localPosition = W(x, y, z);
            if (rot.HasValue) t.localRotation = WR(rot.Value.x, rot.Value.y, rot.Value.z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (outline)
            {
                var o = new GameObject("ink").transform; o.SetParent(t, false); o.localScale = Vector3.one * ol;
                o.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var orr = o.gameObject.AddComponent<MeshRenderer>(); orr.sharedMaterial = FinisherFx.Outline; orr.shadowCastingMode = ShadowCastingMode.Off; orr.receiveShadows = false;
            }
            return t;
        }

        public static GameObject Root(string name) => new GameObject(name);

        // ------------------------------------------------------------------ the props
        public class Dog { public Transform g, body, head, jaw, tail; public Transform[] legs; public Vector3 T, st, @out; public float face; public int i; }

        /// web dogMesh: a blocky dog (body, head with snout, nose, eyes and floppy ears, jaw, tail, four legs).
        public static Dog DogMesh(string col)
        {
            var c = FinisherFx.C(col); var earC = "#" + ColorUtility.ToHtmlStringRGB(c * .7f);
            Material m = Mat(col, .85f), dk = Mat("#1b1d23", .5f), ear = Mat(earC, .85f);
            var g = Root("dog").transform;
            var body = Group(g, "body"); Piece(body, RBox(.5f, .42f, 1.0f, .1f), m, 0, .62f, 0);
            var head = Group(body, "head", 0, .86f, .52f);
            Piece(head, RBox(.42f, .38f, .42f, .08f), m, 0, .06f, .06f); Piece(head, RBox(.24f, .17f, .28f, .05f), m, 0, 0, .34f);
            Piece(head, Box(.1f, .07f, .04f), dk, 0, .05f, .49f, outline: false);
            foreach (var sx in new[] { -1f, 1f })
            {
                Piece(head, Box(.06f, .06f, .02f), dk, sx * .11f, .14f, .28f, outline: false);
                Piece(head, Box(.1f, .17f, .06f), ear, sx * .16f, .3f, -.02f, new Vector3(0, 0, sx * .35f));
            }
            var jaw = Group(head, "jaw", 0, -.08f, .22f); Piece(jaw, RBox(.2f, .06f, .26f, .02f), m, 0, -.03f, .12f);
            var tail = Group(body, "tail", 0, .78f, -.48f); Piece(tail, Box(.08f, .08f, .36f), m, 0, 0, -.16f);
            var legs = new List<Transform>();
            foreach (var (x, z) in new[] { (-.17f, .35f), (.17f, .35f), (-.17f, -.35f), (.17f, -.35f) })
            { var l = Group(body, "leg", x, .46f, z); Piece(l, Box(.13f, .46f, .13f), m, 0, -.22f, 0); legs.Add(l); }
            g.localScale = Vector3.one * SC * 1.1f;
            return new Dog { g = g, body = body, head = head, jaw = jaw, tail = tail, legs = legs.ToArray() };
        }

        /// web heartMesh: a glossy red heart (two lobes and a point) with two dark vessels on top.
        public static Transform HeartMesh()
        {
            var g = Root("heart").transform;
            Material m = Mat("#c8102e", .45f, 0, "#5a0010", .35f), dk = Mat("#7d0d17", .6f);
            Piece(g, RBox(.15f, .15f, .13f, .05f), m, -.065f, .05f, 0); Piece(g, RBox(.15f, .15f, .13f, .05f), m, .065f, .05f, 0);
            Piece(g, RBox(.15f, .15f, .13f, .03f), m, 0, -.04f, 0, new Vector3(0, 0, Mathf.PI / 4));
            Piece(g, Box(.04f, .09f, .04f), dk, -.03f, .15f, 0, outline: false); Piece(g, Box(.035f, .07f, .035f), dk, .035f, .14f, .01f, new Vector3(0, 0, -.4f), outline: false);
            g.localScale = Vector3.one * 1.8f; return g;
        }

        /// web gloveMesh: a big red boxing glove with a thumb and a white cuff.
        public static Transform GloveMesh()
        {
            var g = Root("glove").transform;
            Material red = Mat("#d8202a", .35f, 0, "#4a0006", .25f), wh = Mat("#f4f4f2", .6f);
            Piece(g, RBox(.72f, .78f, .66f, .24f), red, 0, -.12f, .06f); Piece(g, RBox(.26f, .34f, .24f, .09f), red, .38f, -.04f, .16f, new Vector3(0, 0, -.35f));
            Piece(g, RBox(.62f, .2f, .58f, .08f), wh, 0, .33f, 0);
            return g;
        }

        public class Skel { public Transform g, hd; public Transform[] arms, legs; }
        /// web skelMesh: Overcharge's x-ray skeleton (glowing white bones, dark eye sockets), posed like the body it's inside.
        public static Skel SkelMesh()
        {
            var g = Root("skeleton").transform; Material m = Basic("#f4f8ff"), dk = Basic("#16171c");
            System.Action<Transform, float, float, float, float, float, float, Material> b = (p, w, h, d, x, y, z, mm) => Piece(p, Box(w, h, d), mm ?? m, x, y, z, outline: false, shadow: false);
            var hd = Group(g, "skull", 0, 2.01f, 0); b(hd, .5f, .44f, .46f, 0, .36f, 0, null); b(hd, .34f, .12f, .3f, 0, .1f, .04f, null);
            foreach (var sx in new[] { -1f, 1f }) b(hd, .13f, .12f, .02f, sx * .12f, .38f, .235f, dk);
            b(hd, .08f, .08f, .02f, 0, .24f, .235f, dk);
            b(g, .1f, 1.0f, .1f, 0, 1.5f, -.06f, null); for (int i = 0; i < 4; i++) b(g, .78f - i * .1f, .07f, .38f, 0, 1.86f - i * .17f, 0, null); b(g, .6f, .14f, .28f, 0, 1.04f, 0, null);
            var arms = new List<Transform>(); var legs = new List<Transform>();
            foreach (var sx in new[] { -1f, 1f })
            {
                var a = Group(g, "arm", sx * .765f, 1.92f, 0); b(a, .1f, .85f, .1f, 0, -.45f, 0, null); b(a, .2f, .16f, .2f, 0, -.9f, 0, null); arms.Add(a);
                var l = Group(g, "leg", sx * .25f, 1, 0); b(l, .11f, .9f, .11f, 0, -.48f, 0, null); b(l, .22f, .08f, .3f, 0, -.96f, .06f, null); legs.Add(l);
            }
            g.gameObject.SetActive(false);
            return new Skel { g = g, hd = hd, arms = arms.ToArray(), legs = legs.ToArray() };
        }

        public class Potato { public Transform g, fuse, tip; public float top; }
        /// web potatoMesh: a lumpy potato with an outline, a fuse that burns down, and a glowing tip.
        public static Potato PotatoMesh()
        {
            var g = Root("hot potato").transform;
            var body = Piece(g, Sphere(.2f, 14, 10), Mat("#b5793f", .8f), 0, 0, 0, ol: 1.08f); body.localScale = new Vector3(1.3f, .95f, 1);
            float top = .17f;
            var fuse = Piece(g, Cyl(.022f, .022f, 1, 6, .5f), Mat("#3a2a1e", .9f), .04f, top, 0, new Vector3(0, 0, -.25f), outline: false);
            var tip = Piece(g, Sphere(.045f, 8, 6), Basic("#ffd23f"), .04f, top + .34f, 0, outline: false, shadow: false);
            g.localScale = Vector3.one * 1.7f;
            return new Potato { g = g, fuse = fuse, tip = tip, top = top };
        }

        /// web paddleMesh: a defibrillator paddle (handle, steel plate, red button).
        public static Transform PaddleMesh()
        {
            var g = Root("paddle").transform;
            Piece(g, RBox(.16f, .3f, .16f, .05f), Mat("#3a3d45"), 0, -.05f, 0); Piece(g, RBox(.34f, .06f, .28f, .03f), Mat("#d6dbe2", .25f, .7f), 0, -.24f, 0);
            Piece(g, Box(.08f, .08f, .08f), Mat("#e8463a"), 0, .1f, 0, outline: false);
            return g;
        }

        public class Monitor { public Transform g; public Texture2D tex; public readonly List<float> pts = new List<float>(); }
        /// Clear!'s heart monitor: a stand, a dark screen and a live trace (green, flat red at the end).
        public static Monitor MonitorMesh()
        {
            var g = Root("heart monitor").transform;
            Piece(g, Box(.08f, 1.1f, .08f), Mat("#8a8f99"), 0, .55f, 0); Piece(g, RBox(.84f, .56f, .16f, .04f), Mat("#2b2d33"), 0, 1.25f, 0);
            var tex = new Texture2D(160, 96, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var sm = FinisherFx.UnlitMat(Color.white); sm.SetTexture("_BaseMap", tex); sm.SetTexture("_MainTex", tex); sm.enableInstancing = false;
            var sc = Piece(g, Quad(.74f, .46f), sm, 0, 1.25f, .09f, outline: false, shadow: false);
            var mon = new Monitor { g = g, tex = tex };
            DrawMonitor(mon, 0, false);
            return mon;
        }

        static Color32[] monPx;
        /// web drawMonitor: grid, the last 80 samples of the trace, and the readout (heart 72, or 0 when flat).
        public static void DrawMonitor(Monitor M, float v, bool flat)
        {
            M.pts.Add(v); if (M.pts.Count > 80) M.pts.RemoveAt(0);
            const int W = 160, H = 96;
            if (monPx == null) monPx = new Color32[W * H];
            var bg = new Color32(7, 17, 8, 255); var grid = new Color32(18, 58, 24, 255);
            var line = flat ? new Color32(255, 74, 61, 255) : new Color32(59, 255, 106, 255);
            for (int i = 0; i < monPx.Length; i++) monPx[i] = bg;
            for (int x = 0; x < W; x += 16) for (int y = 0; y < H; y++) monPx[y * W + x] = grid;
            // canvas y runs down; the texture's rows run up
            System.Action<int, int, Color32> put = (x, y, c) => { if (x >= 0 && x < W && y >= 0 && y < H) monPx[(H - 1 - y) * W + x] = c; };
            for (int i = 1; i < M.pts.Count; i++)
            {
                int x0 = (i - 1) * 2, x1 = i * 2; float y0 = 58 - M.pts[i - 1] * 34, y1 = 58 - M.pts[i] * 34;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0))));
                for (int s = 0; s <= steps; s++)
                {
                    float u = s / (float)steps; int px = Mathf.RoundToInt(Mathf.Lerp(x0, x1, u)), py = Mathf.RoundToInt(Mathf.Lerp(y0, y1, u));
                    for (int dy = -1; dy <= 1; dy++) put(px, py + dy, line);
                }
            }
            // readout: a small heart and "72", or "0" (blocky 3x5 digits)
            if (flat) Digits("0", 8, 6, line, put); else { Heart(8, 6, line, put); Digits("72", 22, 6, line, put); }
            M.tex.SetPixels32(monPx); M.tex.Apply(false);
        }
        static readonly Dictionary<char, string> font = new Dictionary<char, string> { { '0', "111101101101111" }, { '7', "111001010010010" }, { '2', "111001111100111" } };
        static void Digits(string s, int x, int y, Color32 c, System.Action<int, int, Color32> put)
        {
            foreach (var ch in s)
            {
                var f = font[ch];
                for (int j = 0; j < 5; j++) for (int i = 0; i < 3; i++) if (f[j * 3 + i] == '1') for (int a = 0; a < 2; a++) for (int b = 0; b < 2; b++) put(x + i * 2 + a, y + j * 2 + b, c);
                x += 8;
            }
        }
        static void Heart(int x, int y, Color32 c, System.Action<int, int, Color32> put)
        {
            string[] rows = { "0110110", "1111111", "1111111", "0111110", "0011100", "0001000" };
            for (int j = 0; j < rows.Length; j++) for (int i = 0; i < 7; i++) if (rows[j][i] == '1') put(x + i * 2 - 2, y + j * 2, c);
        }

        /// web harpoonMesh: Combat's harpoon gun (barrel, steel collar, yellow grip); `tip` is the muzzle.
        public static Transform HarpoonMesh(out Transform tip)
        {
            var g = Root("harpoon").transform;
            Material dk = Mat("#2b2d33"), y = Mat("#e0a21a"), st = Mat("#c9ced8", .3f, .8f);
            Piece(g, Cyl(.07f, .08f, 1.0f, 8), dk, 0, -.55f, 0); Piece(g, Cyl(.12f, .12f, .13f, 10, 0, true), st, 0, -.2f, 0); Piece(g, RBox(.14f, .32f, .13f, .03f), y, 0, -.02f, .09f);
            tip = Group(g, "tip", 0, -1.07f, 0);
            return g;
        }

        /// web spearMesh: the harpoon's spear (shaft and point along +y).
        public static Transform SpearMesh()
        {
            var g = Root("spear").transform; var st = Mat("#c9ced8", .3f, .8f);
            Piece(g, Cyl(.025f, .025f, .9f, 6), st, 0, 0, 0, outline: false); Piece(g, Cyl(0, .07f, .22f, 6), st, 0, .55f, 0, outline: false);
            return g;
        }

        /// The Ice sculpture's block: clear blue ice with white edges, standing on its base.
        public static Transform IceBlock(float w, float h, float d)
        {
            var g = Root("ice").transform;
            var ice = FinisherFx.FadeMat(new Color(.81f, .94f, 1f, .45f), false, true);
            ice.SetFloat("_Smoothness", .95f); ice.SetFloat("_Metallic", .1f); ice.EnableKeyword("_EMISSION"); ice.SetColor("_EmissionColor", FinisherFx.C("#5fbfff") * .15f);
            var body = new GameObject("block"); body.transform.SetParent(g, false); body.transform.localPosition = new Vector3(0, h / 2, 0);
            body.AddComponent<MeshFilter>().sharedMesh = Box(w, h, d);
            var r = body.AddComponent<MeshRenderer>(); r.sharedMaterial = ice; r.shadowCastingMode = ShadowCastingMode.Off;
            var edge = Basic("#ffffff"); float e = .025f;
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                    Edge(g, edge, new Vector3(sx * w / 2, h / 2, sz * d / 2), new Vector3(e, h, e));
            for (int sy = 0; sy <= 1; sy++) for (int s = -1; s <= 1; s += 2)
                {
                    Edge(g, edge, new Vector3(0, sy * h, s * d / 2), new Vector3(w, e, e));
                    Edge(g, edge, new Vector3(s * w / 2, sy * h, 0), new Vector3(e, e, d));
                }
            return g;
        }
        static void Edge(Transform g, Material m, Vector3 p, Vector3 size)
        {
            var go = new GameObject("edge"); go.transform.SetParent(g, false); go.transform.localPosition = p;
            go.AddComponent<MeshFilter>().sharedMesh = Box(size.x, size.y, size.z);
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// A dark hole (Heartbreaker): a thin black square.
        public static Transform Hole() { var g = Root("hole").transform; Piece(g, Box(.26f, .26f, .05f), Basic("#1a0406"), 0, 0, 0, outline: false, shadow: false); return g; }

        /// A small cube of a given colour (what the dogs end up holding).
        public static Transform Bit(string col, float s) { var g = Root("bit").transform; var p = Piece(g, Box(1, 1, 1), Mat(col), 0, 0, 0, outline: false); p.localScale = Vector3.one * s; return g; }
    }
}
