// Street Life's boundary fence (maps chat, Pete 13 Nov: "4 different high quality fences to replace the dark brown one, a bit
// more open"). Style 0 is the original Tripo fence; 1-4 are built here from simple parts, merged into one mesh per material:
//   1 Picket:   white timber pickets with pointed tops, 1.15 m
//   2 Railings: black steel park railings with spear tops and finials, 1.6 m
//   3 Slatted:  modern horizontal cedar boards with gaps on charcoal steel posts, 1.5 m
//   4 Ranch:    weathered oak post and three rails, 1.25 m (the most open)
// The style is saved in PlayerPrefs "StreetLife.fence"; "call StreetTraffic.Test fence <n>" switches it live.
// Both end fences keep the gap for the road (StreetTraffic cuts it in the Tripo fence and splits the collision walls).
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class StreetFence : MonoBehaviour
    {
        public const string Pref = "StreetLife.fence";
        public static readonly string[] Names = { "Original", "Picket", "Railings", "Slatted", "Ranch" };
        const float X = 46, Z = 30, GAP = 9.2f;

        MaterialLibrary lib; MapBuilder mb;
        readonly List<GameObject> tripo = new List<GameObject>();
        readonly List<int> walls = new List<int>(); readonly List<Box> wallHome = new List<Box>();
        GameObject built; int style = -1;

        public static int Saved { get => Mathf.Clamp(PlayerPrefs.GetInt(Pref, 0), 0, 4); set { PlayerPrefs.SetInt(Pref, value); PlayerPrefs.Save(); } }

        public void Init(MapBuilder builder, IEnumerable<GameObject> tripoFence)
        {
            mb = builder; lib = builder.library; tripo.AddRange(tripoFence);
            for (int i = 0; i < BoxWorld.Boxes.Count; i++)
            {
                var b = BoxWorld.Boxes[i];
                bool side = b.x1 - b.x0 < .5f && b.z1 - b.z0 > 50 && Mathf.Abs(Mathf.Abs((b.x0 + b.x1) / 2) - X) < .3f;
                bool end = b.z1 - b.z0 < .5f && b.x1 - b.x0 > 30 && Mathf.Abs(Mathf.Abs((b.z0 + b.z1) / 2) - Z) < .3f;
                if ((side || end) && b.y1 > 2.5f) { walls.Add(i); wallHome.Add(b); }
            }
            Apply(Saved);
        }

        public void Apply(int s)
        {
            if (s == style) return; style = s;
            if (built) Destroy(built);
            foreach (var g in tripo) if (g) g.SetActive(s == 0);
            float h = 2.8f;
            if (s > 0) { built = Build(s, out h); built.transform.SetParent(transform, false); }
            for (int k = 0; k < walls.Count; k++) { var b = wallHome[k]; b.y1 = h; BoxWorld.Set(walls[k], b); }
        }

        // ------------------------------------------------------------------ geometry
        class Part { public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int> t = new List<int>(); public Material m; }
        readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();

        Part P(string key, string hex, float smooth, float metal)
        {
            if (parts.TryGetValue(key, out var p)) return p;
            p = new Part(); var src = lib.map != null && lib.map.Length > 52 && lib.map[52] ? lib.map[52] : lib.unlitBase;
            p.m = new Material(src) { name = "Fence " + key };
            p.m.SetColor("_BaseColor", Hex.ToColor(hex));
            if (p.m.HasProperty("_Smoothness")) p.m.SetFloat("_Smoothness", smooth);
            if (p.m.HasProperty("_Metallic")) p.m.SetFloat("_Metallic", metal);
            parts[key] = p; return p;
        }

        static void Face(Part p, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm)
        {
            int o = p.v.Count;
            p.v.Add(a); p.v.Add(b); p.v.Add(c); p.v.Add(d);
            for (int i = 0; i < 4; i++) p.n.Add(nrm);
            float w = Vector3.Distance(a, b), h = Vector3.Distance(b, c);
            p.uv.Add(new Vector2(0, 0)); p.uv.Add(new Vector2(w, 0)); p.uv.Add(new Vector2(w, h)); p.uv.Add(new Vector2(0, h));
            // wind so the face points along nrm (a triangle a, b, c faces along Cross(b - a, c - a) in Unity)
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) > 0) { p.t.Add(o); p.t.Add(o + 1); p.t.Add(o + 2); p.t.Add(o); p.t.Add(o + 2); p.t.Add(o + 3); }
            else { p.t.Add(o); p.t.Add(o + 2); p.t.Add(o + 1); p.t.Add(o); p.t.Add(o + 3); p.t.Add(o + 2); }
        }

        static void Tri(Part p, Vector3 a, Vector3 b, Vector3 c, Vector3 nrm)
        {
            int o = p.v.Count; p.v.Add(a); p.v.Add(b); p.v.Add(c); for (int i = 0; i < 3; i++) { p.n.Add(nrm); p.uv.Add(Vector2.zero); }
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) > 0) { p.t.Add(o); p.t.Add(o + 1); p.t.Add(o + 2); } else { p.t.Add(o); p.t.Add(o + 2); p.t.Add(o + 1); }
        }

        /// an axis-aligned box in the run's frame: centre c, size s (x along the run, y up, z across), run direction dir
        static void BoxR(Part p, Vector3 c, Vector3 s, Vector3 dir, bool bottom = false)
        {
            var ax = dir * (s.x / 2); var ay = Vector3.up * (s.y / 2); var az = new Vector3(-dir.z, 0, dir.x) * (s.z / 2);
            var an = ax.normalized; var zn = az.normalized;
            Face(p, c - ax - ay + az, c + ax - ay + az, c + ax + ay + az, c - ax + ay + az, zn);
            Face(p, c + ax - ay - az, c - ax - ay - az, c - ax + ay - az, c + ax + ay - az, -zn);
            Face(p, c + ax - ay + az, c + ax - ay - az, c + ax + ay - az, c + ax + ay + az, an);
            Face(p, c - ax - ay - az, c - ax - ay + az, c - ax + ay + az, c - ax + ay - az, -an);
            Face(p, c - ax + ay + az, c + ax + ay + az, c + ax + ay - az, c - ax + ay - az, Vector3.up);
            if (bottom) Face(p, c - ax - ay - az, c + ax - ay - az, c + ax - ay + az, c - ax - ay + az, Vector3.down);
        }

        /// a pyramid cap on top of a w x d footprint at height y
        static void Point(Part p, Vector3 baseC, float w, float d, float h, Vector3 dir)
        {
            var ax = dir * (w / 2); var az = new Vector3(-dir.z, 0, dir.x) * (d / 2); var tip = baseC + Vector3.up * h;
            var c0 = baseC - ax - az; var c1 = baseC + ax - az; var c2 = baseC + ax + az; var c3 = baseC - ax + az;
            void T(Vector3 a, Vector3 b) { var nrm = Vector3.Cross(b - a, tip - a).normalized; var mid = (a + b) / 2 - baseC; if (Vector3.Dot(nrm, mid) < 0) nrm = -nrm; Tri(p, a, b, tip, nrm); }
            T(c0, c1); T(c1, c2); T(c2, c3); T(c3, c0);
        }

        // the four runs, each with its gap (if any), as (start, end) segments
        static List<(Vector3 a, Vector3 b)> Runs()
        {
            var r = new List<(Vector3, Vector3)>();
            foreach (float zs in new[] { -1f, 1f })
            {
                r.Add((new Vector3(-X, 0, zs * Z), new Vector3(-GAP - .6f, 0, zs * Z)));
                r.Add((new Vector3(GAP + .6f, 0, zs * Z), new Vector3(X, 0, zs * Z)));
            }
            foreach (float xs in new[] { -1f, 1f }) r.Add((new Vector3(xs * X, 0, -Z), new Vector3(xs * X, 0, Z)));
            return r;
        }

        GameObject Build(int s, out float height)
        {
            parts.Clear(); height = 1.2f;
            foreach (var (a, b) in Runs())
            {
                var dir = (b - a).normalized; float L = Vector3.Distance(a, b);
                switch (s)
                {
                    case 1: height = 1.15f; Picket(a, dir, L); break;
                    case 2: height = 1.6f; Railings(a, dir, L); break;
                    case 3: height = 1.5f; Slatted(a, dir, L); break;
                    case 4: height = 1.25f; Ranch(a, dir, L); break;
                }
            }
            var root = new GameObject("Fence " + Names[s]);
            foreach (var kv in parts)
            {
                var p = kv.Value;
                var mesh = new Mesh { name = root.name + " " + kv.Key, indexFormat = p.v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(p.v); mesh.SetNormals(p.n); mesh.SetUVs(0, p.uv); mesh.SetTriangles(p.t, 0); mesh.RecalculateBounds(); mesh.UploadMeshData(true);
                var go = new GameObject(kv.Key); go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = p.m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return root;
        }

        static int Bays(float L, float want) => Mathf.Max(1, Mathf.RoundToInt(L / want));

        void Picket(Vector3 a, Vector3 dir, float L)
        {
            var wood = P("picket", "#f4f1ea", .25f, 0); var post = P("picket post", "#ffffff", .3f, 0);
            int nb = Bays(L, 2.4f); float bay = L / nb; var across = new Vector3(-dir.z, 0, dir.x);
            for (int i = 0; i <= nb; i++)
            {
                var c = a + dir * (i * bay);
                BoxR(post, c + Vector3.up * .62f, new Vector3(.11f, 1.24f, .11f), dir);
                Point(post, c + Vector3.up * 1.24f, .13f, .13f, .07f, dir);
                BoxR(post, c + Vector3.up * 1.21f, new Vector3(.14f, .03f, .14f), dir);
            }
            // two rails behind the pickets (on the inside), pickets on the outside face
            for (int i = 0; i < nb; i++)
            {
                var c = a + dir * ((i + .5f) * bay);
                foreach (float y in new[] { .28f, .86f }) BoxR(wood, c + Vector3.up * y - across * .04f, new Vector3(bay - .11f, .085f, .045f), dir);
                int np = Mathf.FloorToInt((bay - .2f) / .165f); float span = np * .165f;
                for (int k = 0; k < np; k++)
                {
                    var q = a + dir * (i * bay + (bay - span) / 2 + (k + .5f) * .165f) + across * .0f;
                    BoxR(wood, q + Vector3.up * .5f + across * .005f, new Vector3(.08f, .96f, .022f), dir);
                    Point(wood, q + Vector3.up * .98f + across * .005f, .08f, .022f, .08f, dir);
                }
            }
        }

        void Railings(Vector3 a, Vector3 dir, float L)
        {
            var iron = P("iron", "#17191d", .55f, .7f); var gold = P("finial", "#b8913a", .7f, .9f);
            int nb = Bays(L, 2.5f); float bay = L / nb;
            for (int i = 0; i <= nb; i++)
            {
                var c = a + dir * (i * bay);
                BoxR(iron, c + Vector3.up * .82f, new Vector3(.09f, 1.64f, .09f), dir);
                BoxR(iron, c + Vector3.up * 1.66f, new Vector3(.12f, .04f, .12f), dir);
                Point(gold, c + Vector3.up * 1.68f, .08f, .08f, .14f, dir);
            }
            for (int i = 0; i < nb; i++)
            {
                var c = a + dir * ((i + .5f) * bay);
                foreach (float y in new[] { .12f, 1.32f }) BoxR(iron, c + Vector3.up * y, new Vector3(bay - .09f, .045f, .03f), dir);
                int nr = Mathf.FloorToInt((bay - .12f) / .13f); float span = nr * .13f;
                for (int k = 0; k < nr; k++)
                {
                    var q = a + dir * (i * bay + (bay - span) / 2 + (k + .5f) * .13f);
                    BoxR(iron, q + Vector3.up * .78f, new Vector3(.022f, 1.32f, .022f), dir);
                    Point(k % 2 == 0 ? gold : iron, q + Vector3.up * 1.44f, .045f, .045f, .11f, dir);
                }
            }
        }

        void Slatted(Vector3 a, Vector3 dir, float L)
        {
            var steel = P("charcoal", "#2e3136", .45f, .6f); var cedar = P("cedar", "#b9794a", .3f, 0); var cap = P("cedar cap", "#9c6238", .3f, 0);
            int nb = Bays(L, 2.0f); float bay = L / nb;
            for (int i = 0; i <= nb; i++) { var c = a + dir * (i * bay); BoxR(steel, c + Vector3.up * .76f, new Vector3(.07f, 1.52f, .07f), dir); }
            for (int i = 0; i < nb; i++)
            {
                var c = a + dir * ((i + .5f) * bay);
                for (int k = 0; k < 8; k++) BoxR(cedar, c + Vector3.up * (.2f + k * .165f), new Vector3(bay - .07f, .11f, .028f), dir);
                BoxR(cap, c + Vector3.up * 1.5f, new Vector3(bay + .07f, .03f, .08f), dir);
            }
        }

        void Ranch(Vector3 a, Vector3 dir, float L)
        {
            var oak = P("oak", "#c9b38c", .15f, 0); var post = P("oak post", "#a8916b", .12f, 0);
            int nb = Bays(L, 2.8f); float bay = L / nb; var across = new Vector3(-dir.z, 0, dir.x);
            for (int i = 0; i <= nb; i++)
            {
                var c = a + dir * (i * bay);
                BoxR(post, c + Vector3.up * .64f, new Vector3(.15f, 1.28f, .15f), dir);
                BoxR(post, c + Vector3.up * 1.295f, new Vector3(.17f, .03f, .17f), dir);
            }
            for (int i = 0; i < nb; i++)
            {
                var c = a + dir * ((i + .5f) * bay);
                foreach (float y in new[] { .38f, .74f, 1.1f }) BoxR(oak, c + Vector3.up * y + across * .09f, new Vector3(bay + .15f, .13f, .055f), dir);
            }
        }
    }
}
