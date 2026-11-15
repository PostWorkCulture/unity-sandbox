// Fixes to the exported weapon models, applied once when the weapon data loads (GunsDb.Load calls Apply):
//   - the red-dot sights on the M4, AKM, FAMAS, Vector, P90 and UMP are scaled down about their base (to half; 40% for
//     the P90 and UMP's taller ring sights);
//   - no sight glass on any AR, SMG or LMG (no tinted pane when aiming down sights);
//   - extra parts from Tools/gunparts.py (Resources/StreetLife/GunParts.json): the Kar98's black trim, the Shotty Horror's detail;
//     and each gun's first-person sight line (VmJson.sightY) follows;
//   - the Rocket Launcher keeps only its front sight ring, at less than half size (the rear ring is cut away);
//   - the Dual Pistols get the parts a real pistol has: a trigger guard, front and rear sights, an ejection port,
//     an accessory rail, slide stop, takedown pin and magazine release. Fixes.Pistol is one pistol on its own, which
//     the menu preview shows as a matched pair.
// Mesh edits happen as WeaponModels.MeshAt builds each mesh (Edit); new parts are meshes appended to the table (Made).
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public static class WeaponFixes
    {
        public const float SightScale = .5f, RingSightScale = .4f, RingScale = .42f;
        static readonly string[] RedDots = { "gar", "ar", "burst", "vector", "p90", "ump" };
        static readonly string[] NoGlass = { "gar", "ar", "burst", "smg", "hornet", "vector", "p90", "ump", "sting", "mg", "m60" };

        struct Squash { public Vector3 pivot; public float k; public bool ring; }
        static readonly Dictionary<int, Squash> edits = new Dictionary<int, Squash>();
        static readonly Dictionary<int, int> splits = new Dictionary<int, int>();     // new mesh index -> source mesh (left pistol only)
        static readonly Dictionary<int, System.Func<Mesh>> made = new Dictionary<int, System.Func<Mesh>>();
        static readonly Dictionary<int, Mesh> madeCache = new Dictionary<int, Mesh>();
        static int trigMesh = -1;
        /// one Dual Pistol on its own (root + parts), for the menu preview; null if the data isn't there
        public static WNode[] Pistol;

        public static void Apply()
        {
            edits.Clear(); splits.Clear(); made.Clear(); Pistol = null; trigMesh = -1;
            var d = GunsDb.Data; if (d == null || d.meshes == null) return;
            var meshes = new List<WMesh>(d.meshes);

            // ---- red dots: every mesh from the bottom of the sight's housing (material 20) up scales about the housing's
            // foot, centred on its base plate (material 22)
            foreach (var id in RedDots)
            {
                var h = GunsDb.Held(id); if (h == null) continue;
                var ms = MeshesOf(h.nodes); WMesh plate = null, housing = null;
                foreach (var mi in ms) { if (meshes[mi].mat == 22) plate = meshes[mi]; if (meshes[mi].mat == 20) housing = meshes[mi]; }
                if (plate == null || housing == null) continue;
                float baseY = housing.bmin[1] - .005f;
                var sight = new List<int>(); float lo = 99;
                foreach (var mi in ms) if (meshes[mi].bmin[1] >= baseY) { sight.Add(mi); lo = Mathf.Min(lo, meshes[mi].bmin[1]); }
                var pivot = new Vector3(plate.bmin[0] + plate.bsize[0] * .5f, lo, plate.bmin[2] + plate.bsize[2] * .5f);
                float k = id == "p90" || id == "ump" ? RingSightScale : SightScale;   // their ring sights stand taller
                foreach (var mi in sight) edits[mi] = new Squash { pivot = pivot, k = k };
                var vm = GunsDb.Vm(id); if (vm != null) vm.sightY = lo + (vm.sightY - lo) * k;
            }

            // ---- no glass in any AR, SMG or LMG sight: the tinted pane (flat, wide, up on the sight) comes out, so aiming
            // down sights is a clear view
            foreach (var id in NoGlass)
            {
                var h = GunsDb.Held(id); if (h == null) continue;
                var glass = MeshesOf(h.nodes).FindAll(mi => meshes[mi].bsize[2] < .003f && meshes[mi].bsize[0] > .12f && meshes[mi].bmin[1] > .15f);
                if (glass.Count > 0) foreach (var n in h.nodes) if (n.meshes != null && n.meshes.Length > 0) n.meshes = System.Array.FindAll(n.meshes, mi => !glass.Contains(mi));
            }

            // ---- rocket: front ring only, smaller; its centre dot comes down with it
            var rk = GunsDb.Held("rocket");
            if (rk != null)
            {
                var pivot = new Vector3(0, .26f, -.82f);
                foreach (var mi in MeshesOf(rk.nodes))
                {
                    if (meshes[mi].mat == 149) edits[mi] = new Squash { pivot = pivot, k = RingScale, ring = true };
                    else if (meshes[mi].mat == 153) edits[mi] = new Squash { pivot = pivot, k = RingScale };
                }
                var vm = GunsDb.Vm("rocket"); if (vm != null) vm.sightY = pivot.y + (vm.sightY - pivot.y) * RingScale;
            }

            // ---- dual pistols: new parts on both pistols (x 0 and dx), and a single pistol for the preview
            var du = GunsDb.Held("duals");
            if (du != null)
            {
                int Add(int mat, System.Func<Mesh> make)
                {
                    int i = meshes.Count; meshes.Add(new WMesh { mat = mat, cast = true, verts = 0, tris = 0, q = 0, bmin = new float[3], bsize = new float[3] });
                    made[i] = make; return i;
                }
                var parts = new[] { Add(143, Frame), Add(1, Dark), Add(145, Brass) };
                foreach (var mi in MeshesOf(du.nodes)) if (meshes[mi].mat == 143) trigMesh = mi;
                var nodes = new List<WNode>(du.nodes);
                if (!System.Array.Exists(du.nodes, n => n.name == "pistol parts"))
                    foreach (var x in new[] { 0f, du.dx }) nodes.Add(Node("pistol parts", x, parts));
                du.nodes = nodes.ToArray();
                // the preview's single pistol: each exported mesh cut to the left one, plus the parts
                var single = new List<int>();
                foreach (var mi in MeshesOf(du.nodes)) if (mi < d.meshes.Length) { int s = meshes.Count; meshes.Add(meshes[mi]); splits[s] = mi; single.Add(s); }
                single.AddRange(parts);
                Pistol = new[] { new WNode { name = "pistol", parent = -1, pos = new float[] { 0, 0, 0 }, rot = new float[] { 0, 0, 0, 1 }, scl = new float[] { 1, 1, 1 }, meshes = new int[0] },
                                 Node("pistol body", 0, single.ToArray()) };
            }
            // ---- extra parts built by Tools/gunparts.py (Resources/StreetLife/GunParts.json): the Kar98's black trim and
            // fittings, the Shotty Horror's rib, receiver plates, trigger guard, shell carrier and gold-framed stock
            var pa = Resources.Load<TextAsset>("StreetLife/GunParts");
            if (pa)
            {
                var pj = JsonUtility.FromJson<PartsJson>(pa.text);
                var byGun = new Dictionary<string, List<int>>();
                if (pj != null && pj.parts != null)
                    foreach (var pt in pj.parts)
                    {
                        if (GunsDb.Held(pt.gun) == null || pt.v == null || pt.t == null) continue;
                        int i = meshes.Count; var src = pt;
                        meshes.Add(new WMesh { mat = pt.mat, cast = true, verts = 0, tris = 0, q = 0, bmin = new float[3], bsize = new float[3] });
                        made[i] = () => PartMesh(src);
                        if (!byGun.TryGetValue(pt.gun, out var l)) byGun[pt.gun] = l = new List<int>();
                        l.Add(i);
                    }
                foreach (var kv in byGun)
                {
                    var h = GunsDb.Held(kv.Key); var nodes = new List<WNode>(h.nodes);
                    nodes.Add(Node("extra parts", 0, kv.Value.ToArray())); h.nodes = nodes.ToArray();
                }
            }
            d.meshes = meshes.ToArray();
        }

        [System.Serializable] class PartJson { public string gun, name; public int mat; public float[] v, n; public int[] t; }
        [System.Serializable] class PartsJson { public PartJson[] parts; }

        static Mesh PartMesh(PartJson p)
        {
            int nv = p.v.Length / 3; var v = new Vector3[nv]; var n = new Vector3[nv];
            for (int i = 0; i < nv; i++)
            {
                v[i] = new Vector3(p.v[i * 3], p.v[i * 3 + 1], p.v[i * 3 + 2]);
                n[i] = p.n != null && p.n.Length >= nv * 3 ? new Vector3(p.n[i * 3], p.n[i * 3 + 1], p.n[i * 3 + 2]) : Vector3.up;
            }
            var m = new Mesh { name = p.name };
            if (nv > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v; m.normals = n; m.uv = new Vector2[nv]; m.triangles = p.t; m.RecalculateBounds();
            return m;
        }

        static WNode Node(string name, float x, int[] ms) => new WNode { name = name, parent = 0, pos = new[] { x, 0f, 0f }, rot = new float[] { 0, 0, 0, 1 }, scl = new float[] { 1, 1, 1 }, meshes = ms };

        static List<int> MeshesOf(WNode[] nodes)
        {
            var l = new List<int>(); if (nodes == null) return l;
            foreach (var n in nodes) if (n.meshes != null) foreach (var mi in n.meshes) if (!l.Contains(mi)) l.Add(mi);
            return l;
        }

        // ================================================================== called by WeaponModels.MeshAt
        /// a mesh made here, or null
        public static Mesh Made(int i)
        {
            if (madeCache.TryGetValue(i, out var m) && m) return m;
            if (made.TryGetValue(i, out var f)) return madeCache[i] = f();
            if (splits.TryGetValue(i, out var src)) return madeCache[i] = LeftOnly(WeaponModels.MeshAt(src), "gun" + src + " (one)");
            return null;
        }

        /// edits an exported mesh as it's built
        public static void Edit(int i, Mesh mesh)
        {
            if (i == trigMesh) { TrimTrigger(mesh); return; }
            if (!edits.TryGetValue(i, out var e)) return;
            var v = mesh.vertices; var tris = mesh.triangles;
            if (e.ring)
            {
                // cut the rear ring (above the tube, behind the middle); shrink the front one (above the tube, at the front)
                var keep = new List<int>(tris.Length);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    bool rear = false;
                    for (int j = 0; j < 3; j++) { var p = v[tris[t + j]]; if (p.y > .28f && p.z > .05f) rear = true; }
                    if (!rear) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
                }
                for (int j = 0; j < v.Length; j++) if (v[j].y > .268f && v[j].z < -.66f) v[j] = e.pivot + (v[j] - e.pivot) * e.k;
                mesh.vertices = v; mesh.SetTriangles(keep, 0, true);
            }
            else
            {
                for (int j = 0; j < v.Length; j++) v[j] = e.pivot + (v[j] - e.pivot) * e.k;
                mesh.vertices = v; mesh.RecalculateBounds();
            }
        }

        // the pistols' trigger is a solid block that fills where the guard should be: slim it to a curved blade's width
        static void TrimTrigger(Mesh mesh)
        {
            var v = mesh.vertices;
            for (int j = 0; j < v.Length; j++)
            {
                var p = v[j]; float cx = p.x > .31f ? .62f : 0;
                if (p.y < -.045f && p.z > -.115f && p.z < .0f) v[j] = new Vector3(cx + (p.x - cx) * .35f, p.y, p.z);
            }
            mesh.vertices = v; mesh.RecalculateBounds();
        }

        static Mesh LeftOnly(Mesh src, string name)
        {
            var v = src.vertices; var n = src.normals; var uv = src.uv; var t = src.triangles;
            var map = new Dictionary<int, int>(); var V = new List<Vector3>(); var N = new List<Vector3>(); var U = new List<Vector2>(); var T = new List<int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                if ((v[t[i]].x + v[t[i + 1]].x + v[t[i + 2]].x) / 3 > .31f) continue;
                for (int j = 0; j < 3; j++)
                {
                    int a = t[i + j];
                    if (!map.TryGetValue(a, out var b)) { b = V.Count; map[a] = b; V.Add(v[a]); N.Add(n.Length > a ? n[a] : Vector3.up); U.Add(uv.Length > a ? uv[a] : Vector2.zero); }
                    T.Add(b);
                }
            }
            var m = new Mesh { name = name }; m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, U); m.SetTriangles(T, 0, true); return m;
        }

        // ================================================================== the pistol's new parts (one pistol, centred on x = 0; Unity space: -z is the muzzle)
        // slide: x ±.05, y .02..12, z -.44...20; frame: y -.03...02 (dust cover to -.05 at the front); grip from z ~.03 back
        static Mesh Frame()
        {
            var b = new MB();
            // trigger guard: a flat loop from under the dust cover round to the front of the grip
            b.Tube(new[] { new Vector2(-.150f, -.020f), new Vector2(-.156f, -.085f), new Vector2(-.140f, -.140f), new Vector2(-.095f, -.168f),
                           new Vector2(-.030f, -.170f), new Vector2(.015f, -.160f), new Vector2(.040f, -.135f) }, .019f, .0085f);
            // accessory rail under the dust cover
            b.Box(new Vector3(0, -.058f, -.29f), new Vector3(.034f, .016f, .15f));
            return b.Mesh("pistol frame parts");
        }

        static Mesh Dark()
        {
            var b = new MB();
            // front sight blade and rear sight (two posts with a notch between)
            b.Box(new Vector3(0, .133f, -.405f), new Vector3(.012f, .026f, .03f));
            b.Box(new Vector3(0, .125f, .172f), new Vector3(.07f, .012f, .036f));
            b.Box(new Vector3(-.024f, .138f, .172f), new Vector3(.02f, .022f, .036f));
            b.Box(new Vector3(.024f, .138f, .172f), new Vector3(.02f, .022f, .036f));
            // ejection port on the right of the slide top
            b.Box(new Vector3(.022f, .1205f, -.07f), new Vector3(.046f, .004f, .13f));
            // rail slots
            for (int i = 0; i < 6; i++) b.Box(new Vector3(0, -.067f, -.355f + i * .026f), new Vector3(.038f, .006f, .009f));
            // slide stop lever and takedown pin (left side), pin end on the right
            b.Box(new Vector3(-.056f, .012f, -.07f), new Vector3(.007f, .013f, .085f));
            b.Box(new Vector3(-.0565f, .018f, -.025f), new Vector3(.008f, .022f, .018f));
            b.Box(new Vector3(-.055f, -.008f, -.145f), new Vector3(.006f, .016f, .016f));
            b.Box(new Vector3(.055f, -.008f, -.145f), new Vector3(.006f, .012f, .012f));
            // barrel crown at the muzzle
            b.Box(new Vector3(0, .063f, -.447f), new Vector3(.03f, .03f, .012f));
            return b.Mesh("pistol dark parts");
        }

        static Mesh Brass()
        {
            var b = new MB();
            b.Box(new Vector3(-.056f, -.055f, .055f), new Vector3(.008f, .02f, .02f));     // magazine release
            b.Box(new Vector3(0, .1465f, -.405f), new Vector3(.006f, .003f, .006f));       // front sight dot
            return b.Mesh("pistol brass parts");
        }

        /// a small mesh builder: boxes and flat swept tubes, hard edges
        class MB
        {
            readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); readonly List<Vector2> uv = new List<Vector2>(); readonly List<int> t = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nr)
            {
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); for (int k = 0; k < 4; k++) n.Add(nr);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                // wind so the face points along nr
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), nr) > 0) { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
                else { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
            }

            public void Box(Vector3 c, Vector3 s)
            {
                var h = s * .5f;
                Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
                Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.right);
                Quad(P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), Vector3.left);
                Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), Vector3.up);
                Quad(P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), Vector3.down);
                Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Vector3.forward);
                Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), Vector3.back);
            }

            /// a flat band swept through (z, y) points (smoothed), half width w across x, half thickness r in the plane
            public void Tube(Vector2[] pts, float w, float r)
            {
                var path = new List<Vector2>();
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    Vector2 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Length - 1, i + 2)];
                    for (int s = 0; s < 6; s++)
                    {
                        float u = s / 6f, u2 = u * u, u3 = u2 * u;
                        path.Add(.5f * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (-p0 + 3 * p1 - 3 * p2 + p3) * u3));
                    }
                }
                path.Add(pts[pts.Length - 1]);
                Vector3 At(Vector2 p) => new Vector3(0, p.y, p.x);
                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector2 a = path[i], b2 = path[i + 1];
                    Vector2 ta = (path[Mathf.Min(path.Count - 1, i + 1)] - path[Mathf.Max(0, i - 1)]).normalized, tb = (path[Mathf.Min(path.Count - 1, i + 2)] - path[i]).normalized;
                    Vector3 na = new Vector3(0, ta.x, -ta.y), nb = new Vector3(0, tb.x, -tb.y);    // in-plane normal perpendicular to the tangent, in the y-z plane
                    Vector3 A = At(a), B = At(b2), X = Vector3.right * w;
                    Vector3 ao = A + na * r, ai = A - na * r, bo = B + nb * r, bi = B - nb * r;
                    Quad(ao - X, ao + X, bo + X, bo - X, (na + nb).normalized);       // outer face
                    Quad(ai + X, ai - X, bi - X, bi + X, -(na + nb).normalized);      // inner face
                    Quad(ao + X, ai + X, bi + X, bo + X, Vector3.right);              // sides
                    Quad(ai - X, ao - X, bo - X, bi - X, Vector3.left);
                }
            }

            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name }; m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0, true); m.RecalculateBounds(); return m;
            }
        }
    }
}
