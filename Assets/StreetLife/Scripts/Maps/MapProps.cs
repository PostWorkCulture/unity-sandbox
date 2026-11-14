// The breakable props of the maps that have a look pass (Beach Please, Railed): the web game builds them from plain boxes,
// cones and cylinders in flat colours. Here their shapes are rebuilt at play time, in the same places and sizes:
//  - sun loungers: a slatted frame on legs, soft-edged cushions that keep their striped covers, a bolster pillow;
//  - umbrellas: an eight-panel canopy that droops between its ribs, a scalloped valance, ribs, a finial and a weighted base;
//  - fuel drums: rolled hoops, chimes, a recessed lid with bungs, and a sharp hazard label that follows the curve;
//  - beach balls and the surfboard go glossy; the Tiki Bar's crates get the bamboo surface.
// Only what you see changes. Each part keeps its own renderer (so breaking, scorching and respawning in Destruction work
// as before) and the colliders are untouched.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public static class MapProps
    {
        // ------------------------------------------------------------------ a small mesh builder
        class Mb
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            int Add(Vector3 p, Vector3 nr) { v.Add(p); n.Add(nr); uv.Add(Vector2.zero); return v.Count - 1; }

            // a quad whose winding is fixed up to face along its vertices' normals
            void Quad(int a, int b, int c, int d)
            {
                Vector3 face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (face.sqrMagnitude < 1e-12f) face = Vector3.Cross(v[c] - v[a], v[d] - v[a]);
                Vector3 want = n[a] + n[b] + n[c] + n[d];
                if (Vector3.Dot(face, want) >= 0) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }
                else { t.Add(a); t.Add(c); t.Add(b); t.Add(a); t.Add(d); t.Add(c); }
            }

            /// a box with rounded edges (r = 0: a plain box), turned by rot about its centre
            public void Box(Vector3 centre, Vector3 size, float r, Quaternion rot)
            {
                Vector3 h = size / 2; r = Mathf.Max(0, Mathf.Min(r, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * .98f));
                for (int axis = 0; axis < 3; axis++)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int ua = (axis + 1) % 3, va = (axis + 2) % 3;
                        float[] gu = Grid(h[ua], r), gv = Grid(h[va], r);
                        int w = gu.Length, hgt = gv.Length; int first = v.Count;
                        for (int j = 0; j < hgt; j++)
                            for (int i = 0; i < w; i++)
                            {
                                Vector3 p = Vector3.zero; p[axis] = side * h[axis]; p[ua] = gu[i]; p[va] = gv[j];
                                Vector3 inner = new Vector3(Mathf.Clamp(p.x, -h.x + r, h.x - r), Mathf.Clamp(p.y, -h.y + r, h.y - r), Mathf.Clamp(p.z, -h.z + r, h.z - r));
                                Vector3 nr = p - inner;
                                if (nr.sqrMagnitude < 1e-10f) { nr = Vector3.zero; nr[axis] = side; } else nr.Normalize();
                                Add(centre + rot * (inner + nr * r), rot * nr);
                            }
                        for (int j = 0; j < hgt - 1; j++)
                            for (int i = 0; i < w - 1; i++)
                            {
                                int a = first + j * w + i;
                                Quad(a, a + 1, a + w + 1, a + w);
                            }
                    }
            }
            public void Box(Vector3 centre, Vector3 size) => Box(centre, size, 0, Quaternion.identity);

            static float[] Grid(float half, float r)
            {
                if (r <= 1e-4f) return new[] { -half, half };
                return new[] { -half, -half + r * .3f, -half + r, half - r, half - r * .3f, half };
            }

            /// a shape turned on a lathe round the y axis through (cx, cz): profile points are (radius, height), bottom to top,
            /// each segment with its own normal (crisp along the profile, smooth round the turn); arc &lt; 360 leaves it open
            public void Lathe(float cx, float cz, Vector2[] profile, int sides, float arcFrom = 0, float arcTo = 360)
            {
                for (int s = 0; s < profile.Length - 1; s++)
                {
                    Vector2 p0 = profile[s], p1 = profile[s + 1], d = p1 - p0;
                    if (d.sqrMagnitude < 1e-10f) continue;
                    Vector2 n2 = new Vector2(d.y, -d.x).normalized;   // outward for a profile running up the outside
                    int first = v.Count;
                    for (int k = 0; k <= sides; k++)
                    {
                        float a = Mathf.Lerp(arcFrom, arcTo, k / (float)sides) * Mathf.Deg2Rad; float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                        Vector3 nr = new Vector3(n2.x * ca, n2.y, n2.x * sa);
                        Add(new Vector3(cx + p0.x * ca, p0.y, cz + p0.x * sa), nr);
                        Add(new Vector3(cx + p1.x * ca, p1.y, cz + p1.x * sa), nr);
                    }
                    for (int k = 0; k < sides; k++) { int a = first + k * 2; Quad(a, a + 1, a + 3, a + 2); }
                }
            }

            /// planar UVs: u along one direction, v along another, each measured between two limits
            public void PlanarUV(Vector3 uDir, float u0, float u1, Vector3 vDir, float v0, float v1)
            {
                for (int i = 0; i < v.Count; i++)
                    uv[i] = new Vector2(Mathf.InverseLerp(u0, u1, Vector3.Dot(v[i], uDir)), Mathf.InverseLerp(v0, v1, Vector3.Dot(v[i], vDir)));
            }

            public int Vertex(Vector3 p, Vector3 nr, Vector2 st) { int i = Add(p, nr); uv[i] = st; return i; }
            public void Face(int a, int b, int c, int d) => Quad(a, b, c, d);

            public Mesh Build(string name, bool smooth = false)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0, true);
                if (smooth) m.RecalculateNormals();
                return m;
            }
        }

        // ------------------------------------------------------------------ the pass
        class Kit
        {
            public RailLookSet set; public MaterialLibrary lib; public DynJson dj; public byte[] bin;
            public string paint, timber, cloth, cane;   // the set's surface names for painted steel, timber, canvas and bamboo
            public readonly Dictionary<(string, Material), Material> made = new Dictionary<(string, Material), Material>();
            public int n;
        }

        /// Beach Please: loungers, umbrellas, drums, balls, the surfboard and the crates
        public static void Beach(MapWorld w, RailLookSet set) => Run(w, set, "paint", "post", "fabric", "bamboo");
        /// Railed: its fuel drums
        public static void Rail(MapWorld w, RailLookSet set) => Run(w, set, "metal", "wood", null, null);

        static void Run(MapWorld w, RailLookSet set, string paint, string timber, string cloth, string cane)
        {
            var mb = w ? w.builder : null; var e = w ? w.entry : null;
            if (!mb || !set || e == null || !e.dynJson || !e.dynBin) return;
            var gen = mb.transform.Find("__Generated"); var dyn = gen ? gen.Find("Dynamic") : null;
            if (!dyn) return;
            var k = new Kit { set = set, lib = mb.library, dj = JsonUtility.FromJson<DynJson>(e.dynJson.text), bin = e.dynBin.bytes, paint = paint, timber = timber, cloth = cloth, cane = cane };
            if (k.dj == null || k.dj.meshes == null) return;
            foreach (Transform t in dyn)
            {
                try
                {
                    string nm = t.name;
                    if (nm.EndsWith("_lounger")) Lounger(k, t);
                    else if (nm.EndsWith("_umbrella")) Umbrella(k, t);
                    else if (nm.StartsWith("drum")) Drum(k, t);
                    else if (cloth != null && (nm.StartsWith("knock") || nm.EndsWith("_board"))) Gloss(k, t);
                    else if (nm.StartsWith("chip") && cane != null) Crate(k, t);
                }
                catch (System.Exception ex) { Debug.LogWarning("Map props: " + t.name + ": " + ex.Message); }
            }
            Debug.Log("Map props: " + k.n + " parts rebuilt");
        }

        // a prop's parts in the order the exporter wrote them: child "m<record>" objects
        struct Part { public MeshFilter mf; public MeshRenderer mr; public WMesh rec; public Material src; public Vector3 min, size; }
        static List<Part> Parts(Kit k, Transform t)
        {
            var list = new List<Part>();
            foreach (Transform c in t)
            {
                if (c.name.Length < 2 || c.name[0] != 'm' || !int.TryParse(c.name.Substring(1), out int mi) || mi < 0 || mi >= k.dj.meshes.Length) continue;
                var mf = c.GetComponent<MeshFilter>(); var mr = c.GetComponent<MeshRenderer>(); if (!mf || !mr) continue;
                var rec = k.dj.meshes[mi]; if (rec.bmin == null || rec.bsize == null) continue;
                list.Add(new Part { mf = mf, mr = mr, rec = rec, src = mr.sharedMaterial, min = new Vector3(rec.bmin[0], rec.bmin[1], rec.bmin[2]), size = new Vector3(rec.bsize[0], rec.bsize[1], rec.bsize[2]) });
            }
            return list;
        }

        static void Put(Kit k, Part p, Mesh mesh, Material mat)
        {
            var old = p.mf.sharedMesh; p.mf.sharedMesh = mesh; if (old) Object.Destroy(old);
            mesh.RecalculateBounds(); mesh.UploadMeshData(true);
            if (mat) p.mr.sharedMaterial = mat;
            k.n++;
        }

        // one of the set's surfaces in the colour of the part's own material, mapped in the prop's own space (it moves with the prop)
        static Material Surface(Kit k, string surface, Material src, Mesh mesh, float gloss = -1)
        {
            var s = surface != null ? k.set.Get(surface) : null;
            if (s == null || !s.material || !src) return src;
            RailLook.WorldUV(mesh, Mathf.Max(.1f, s.metres), false, s.upright);
            var key = (surface + "|" + gloss, src);
            if (!k.made.TryGetValue(key, out var m))
            {
                m = RailLook.Make(s, src);
                if (gloss >= 0) m.SetFloat("_Smoothness", gloss);
                k.made[key] = m;
            }
            return m;
        }

        static string Hex(Material m) => m && m.HasProperty("_BaseColor") ? ColorUtility.ToHtmlStringRGB(m.GetColor("_BaseColor")).ToLowerInvariant() : "";

        // ------------------------------------------------------------------ sun lounger: frame, mattress, backrest, pillow
        static void Lounger(Kit k, Transform t)
        {
            var p = Parts(k, t); if (p.Count < 4) return;
            // frame, inside the old base block: legs, rails all round, slats across
            Vector3 mn = p[0].min, sz = p[0].size, mx = mn + sz; float top = mx.y - .01f;
            var f = new Mb();
            foreach (float x in new[] { mn.x + .16f, mx.x - .16f })
                foreach (float z in new[] { mn.z + .05f, mx.z - .05f }) f.Box(new Vector3(x, (mn.y + top - .05f) / 2, z), new Vector3(.06f, top - .05f - mn.y, .06f));
            foreach (float z in new[] { mn.z + .03f, mx.z - .03f }) f.Box(new Vector3((mn.x + mx.x) / 2, top - .04f, z), new Vector3(sz.x - .02f, .07f, .05f));
            foreach (float x in new[] { mn.x + .035f, mx.x - .035f }) f.Box(new Vector3(x, top - .04f, (mn.z + mx.z) / 2), new Vector3(.05f, .07f, sz.z - .1f));
            for (float x = mn.x + .14f; x < mx.x - .1f; x += .12f) f.Box(new Vector3(x, top - .012f, (mn.z + mx.z) / 2), new Vector3(.085f, .022f, sz.z - .1f));
            var frame = f.Build("lounger frame");
            Put(k, p[0], frame, Surface(k, Hex(p[0].src) == "9c6b3f" ? k.timber : k.paint, p[0].src, frame));
            // mattress and backrest: the same slabs with soft edges, the striped cover running on as before
            for (int i = 1; i <= 2; i++)
            {
                var old = MeshIO.Build(k.bin, p[i].rec, "old");
                Slab(old, out Vector3 c, out Quaternion rot, out Vector3 size);
                Object.Destroy(old);
                var b = new Mb(); b.Box(c, size, Mathf.Min(.045f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .45f), rot);
                Vector3 along = rot * Vector3.right, across = rot * Vector3.forward;
                float a0 = Vector3.Dot(c, along) - size.x / 2, c0 = Vector3.Dot(c, across) - size.z / 2;
                b.PlanarUV(along, a0, a0 + size.x, across, c0, c0 + size.z);
                var mesh = b.Build(i == 1 ? "lounger mattress" : "lounger backrest"); mesh.RecalculateTangents();
                Put(k, p[i], mesh, Cushion(k, p[i].src));
            }
            // pillow: a bolster
            var pl = new Mb(); pl.Box(p[3].min + p[3].size / 2, p[3].size, Mathf.Min(p[3].size.x, p[3].size.y) * .46f, Quaternion.identity);
            var pillow = pl.Build("lounger pillow");
            Put(k, p[3], pillow, Surface(k, k.cloth, p[3].src, pillow));
        }

        // the centre, turn and size of a box-shaped mesh that may be tilted about z: x along its length, y its thickness, z across
        static void Slab(Mesh box, out Vector3 centre, out Quaternion rot, out Vector3 size)
        {
            var v = box.vertices; var n = box.normals;
            // of its face directions that don't point along z, the one the box is thinnest along is its thickness
            Vector3 up = Vector3.up; float best = 1e9f;
            foreach (var q in n)
            {
                if (Mathf.Abs(q.z) > .5f) continue;
                var d = new Vector3(q.x, q.y, 0).normalized; float lo1 = 1e9f, hi1 = -1e9f;
                foreach (var w in v) { float s = Vector3.Dot(w, d); lo1 = Mathf.Min(lo1, s); hi1 = Mathf.Max(hi1, s); }
                if (hi1 - lo1 < best - 1e-4f) { best = hi1 - lo1; up = d; }
            }
            if (up.y < 0) up = -up;
            Vector3 along = new Vector3(up.y, -up.x, 0);   // a quarter turn from up, in the x-y plane
            rot = Quaternion.LookRotation(Vector3.forward, up);
            Vector3 lo = Vector3.one * 1e9f, hi = Vector3.one * -1e9f;
            foreach (var q in v)
            {
                var l = new Vector3(Vector3.Dot(q, along), Vector3.Dot(q, up), q.z);
                lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l);
            }
            size = hi - lo; Vector3 mid = (lo + hi) / 2;
            centre = along * mid.x + up * mid.y + Vector3.forward * mid.z;
        }

        // a striped cushion: the cover's own picture, with a canvas weave over it as a detail map
        static Material Cushion(Kit k, Material src)
        {
            if (!k.set.cushion || !src) return src;
            var key = ("cushion", src);
            if (k.made.TryGetValue(key, out var m)) return m;
            m = new Material(k.set.cushion) { name = "cushion (" + src.name + ")" };
            if (src.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
            if (src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
            k.made[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ umbrella: pole, base, canopy, lining
        static void Umbrella(Kit k, Transform t)
        {
            var p = Parts(k, t); if (p.Count < 4) return;
            float rim = p[2].min.y, apex = p[2].min.y + p[2].size.y, R = p[2].size.x / 2, foot = p[0].min.y;
            // canopy: eight panels, drooping between the ribs, with a valance hanging from the rim
            var top = new Mb(); var under = new Mb();
            const int panels = 8, across = 4, down = 6;
            for (int pn = 0; pn < panels; pn++)
            {
                var grid = new int[down + 1, across + 1]; var gridU = new int[down + 1, across + 1];
                var edge = new Vector3[across + 1];
                for (int j = 0; j <= down; j++)
                    for (int i = 0; i <= across; i++)
                    {
                        float f = i / (float)across, a = (pn + f) * (Mathf.PI * 2 / panels), s = j / (float)down;
                        float sag = Mathf.Sin(f * Mathf.PI);                                  // 0 on a rib, 1 mid-panel
                        float r = R * s * (1 - .05f * sag * s * s);
                        float y = apex - (apex - rim) * Mathf.Pow(s, 1.6f) - .035f * sag * Mathf.Sin(s * Mathf.PI * .8f);
                        var pos = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                        grid[j, i] = top.Vertex(pos, Vector3.up, Vector2.zero);
                        gridU[j, i] = under.Vertex(pos + Vector3.down * .012f, Vector3.down, Vector2.zero);
                        if (j == down) edge[i] = pos;
                    }
                for (int j = 0; j < down; j++)
                    for (int i = 0; i < across; i++)
                    {
                        top.Face(grid[j, i], grid[j, i + 1], grid[j + 1, i + 1], grid[j + 1, i]);
                        under.Face(gridU[j, i], gridU[j, i + 1], gridU[j + 1, i + 1], gridU[j + 1, i]);
                    }
                // valance: a short skirt with a scalloped hem, outside in the canopy's colour and inside in the lining's
                for (int i = 0; i < across; i++)
                {
                    Vector3 a0 = edge[i], a1 = edge[i + 1];
                    Vector3 o0 = new Vector3(a0.x, 0, a0.z).normalized, o1 = new Vector3(a1.x, 0, a1.z).normalized;
                    float h0 = .1f + .04f * Mathf.Sin(i / (float)across * Mathf.PI), h1 = .1f + .04f * Mathf.Sin((i + 1) / (float)across * Mathf.PI);
                    Vector3 b0 = a0 + Vector3.down * h0 + o0 * .01f, b1 = a1 + Vector3.down * h1 + o1 * .01f;
                    top.Face(top.Vertex(a0, o0, Vector2.zero), top.Vertex(a1, o1, Vector2.zero), top.Vertex(b1, o1, Vector2.zero), top.Vertex(b0, o0, Vector2.zero));
                    under.Face(under.Vertex(a0, -o0, Vector2.zero), under.Vertex(a1, -o1, Vector2.zero), under.Vertex(b1, -o1, Vector2.zero), under.Vertex(b0, -o0, Vector2.zero));
                }
            }
            var canopy = top.Build("umbrella canopy", true); var lining = under.Build("umbrella lining", true);
            Put(k, p[2], canopy, Surface(k, k.cloth, p[2].src, canopy));
            Put(k, p[3], lining, Surface(k, k.cloth, p[3].src, lining));
            // pole with a finial, a hub and eight ribs under the canopy
            var pole = new Mb();
            pole.Lathe(0, 0, new[] { new Vector2(0, foot), new Vector2(.03f, foot), new Vector2(.03f, apex + .03f), new Vector2(.045f, apex + .05f), new Vector2(.045f, apex + .09f), new Vector2(.02f, apex + .13f), new Vector2(0, apex + .14f) }, 10);
            float hub = rim + (apex - rim) * .35f;
            pole.Lathe(0, 0, new[] { new Vector2(.03f, hub - .05f), new Vector2(.06f, hub - .04f), new Vector2(.06f, hub + .02f), new Vector2(.03f, hub + .03f) }, 10);
            for (int pn = 0; pn < panels; pn++)
            {
                float a = pn * (Mathf.PI * 2 / panels); Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Vector3 from = dir * .05f + Vector3.up * hub, to = dir * (R * .97f) + Vector3.up * (rim + .012f);
                pole.Box((from + to) / 2, new Vector3((to - from).magnitude, .018f, .018f), 0, Quaternion.FromToRotation(Vector3.right, (to - from).normalized));
            }
            var poleMesh = pole.Build("umbrella pole");
            Put(k, p[0], poleMesh, Surface(k, k.paint, p[0].src, poleMesh));
            // base: a weighted disc with a bevel and a collar
            float b0y = p[1].min.y, b1y = b0y + p[1].size.y, br = p[1].size.x / 2;
            var bs = new Mb();
            bs.Lathe(0, 0, new[] { new Vector2(0, b0y), new Vector2(br, b0y), new Vector2(br, b1y - .025f), new Vector2(br - .03f, b1y), new Vector2(.07f, b1y + .01f), new Vector2(.055f, b1y + .09f), new Vector2(.03f, b1y + .09f) }, 16);
            var baseMesh = bs.Build("umbrella base");
            Put(k, p[1], baseMesh, Surface(k, k.paint, p[1].src, baseMesh));
        }

        // ------------------------------------------------------------------ fuel drum: body, two hoops, label
        static void Drum(Kit k, Transform t)
        {
            var p = Parts(k, t); if (p.Count < 3) return;
            float y0 = p[0].min.y, y1 = y0 + p[0].size.y, r = p[0].size.x / 2 - .008f;
            float h1 = p[1].min.y + p[1].size.y / 2, h2 = p[2].min.y + p[2].size.y / 2;   // the hoops' heights
            var b = new Mb();
            b.Lathe(0, 0, new[]
            {
                new Vector2(0, y0), new Vector2(r - .012f, y0), new Vector2(r + .006f, y0 + .012f), new Vector2(r + .006f, y0 + .035f), new Vector2(r, y0 + .045f),
                new Vector2(r, h1 - .04f), new Vector2(r + .012f, h1 - .022f), new Vector2(r + .012f, h1 + .022f), new Vector2(r, h1 + .04f),
                new Vector2(r, h2 - .04f), new Vector2(r + .012f, h2 - .022f), new Vector2(r + .012f, h2 + .022f), new Vector2(r, h2 + .04f),
                new Vector2(r, y1 - .045f), new Vector2(r + .006f, y1 - .035f), new Vector2(r + .006f, y1), new Vector2(r - .012f, y1), new Vector2(r - .02f, y1 - .025f), new Vector2(0, y1 - .025f),
            }, 24);
            // two bungs in the lid
            b.Lathe(r * .55f, 0, new[] { new Vector2(.04f, y1 - .025f), new Vector2(.04f, y1 - .008f), new Vector2(.03f, y1 - .004f), new Vector2(0, y1 - .004f) }, 10);
            b.Lathe(-r * .6f, r * .1f, new[] { new Vector2(.022f, y1 - .025f), new Vector2(.022f, y1 - .01f), new Vector2(0, y1 - .01f) }, 8);
            var body = b.Build("drum");
            Put(k, p[0], body, Surface(k, k.paint, p[0].src, body, 1f));
            for (int i = 1; i <= 2; i++)
            {
                float h = i == 1 ? h1 : h2; var ring = new Mb();
                ring.Lathe(0, 0, new[] { new Vector2(r + .011f, h - .026f), new Vector2(r + .016f, h - .02f), new Vector2(r + .016f, h + .02f), new Vector2(r + .011f, h + .026f) }, 24);
                var hoop = ring.Build("drum hoop");
                Put(k, p[i], hoop, Surface(k, k.paint, p[i].src, hoop, .9f));
            }
            if (p.Count < 4 || !p[3].src) return;
            // the label, wrapped round the drum between the hoops, where the flat one stood
            Vector3 lc = p[3].min + p[3].size / 2; float ang = Mathf.Atan2(lc.z, lc.x), half = .13f / r, ly0 = h1 + .05f, ly1 = h2 - .05f;
            var lb = new Mb(); const int seg = 6; var row = new int[seg + 1, 2];
            for (int i = 0; i <= seg; i++)
            {
                float a = ang + Mathf.Lerp(-half, half, i / (float)seg); Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                row[i, 0] = lb.Vertex(o * (r + .003f) + Vector3.up * ly0, o, new Vector2(i / (float)seg, 0));
                row[i, 1] = lb.Vertex(o * (r + .003f) + Vector3.up * ly1, o, new Vector2(i / (float)seg, 1));
            }
            for (int i = 0; i < seg; i++) lb.Face(row[i, 0], row[i + 1, 0], row[i + 1, 1], row[i, 1]);
            Material lm = p[3].src;
            if (k.set.drumLabel)
            {
                if (!k.made.TryGetValue(("label", null), out lm)) { lm = new Material(p[3].src) { name = "drum label" }; lm.SetTexture("_BaseMap", k.set.drumLabel); k.made[("label", null)] = lm; }
            }
            Put(k, p[3], lb.Build("drum label"), lm);
        }

        // ------------------------------------------------------------------ the rest
        // beach balls and the surfboard: the same colours, shiny
        static void Gloss(Kit k, Transform t)
        {
            foreach (var mr in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                var src = mr.sharedMaterial; if (!src || !src.HasProperty("_Smoothness") || src.renderQueue >= 3000) continue;
                var key = ("gloss", src);
                if (!k.made.TryGetValue(key, out var m)) { m = new Material(src) { name = src.name + " gloss" }; m.SetFloat("_Smoothness", .82f); k.made[key] = m; }
                mr.sharedMaterial = m; k.n++;
            }
        }

        // the Tiki Bar's crates: bamboo
        static void Crate(Kit k, Transform t)
        {
            foreach (var p in Parts(k, t))
            {
                if (!p.src || !p.src.HasProperty("_BaseMap") || !p.src.GetTexture("_BaseMap")) continue;
                var mesh = MeshIO.Build(k.bin, p.rec, "crate");
                Put(k, p, mesh, Surface(k, k.cane, p.src, mesh));
            }
        }
    }
}
