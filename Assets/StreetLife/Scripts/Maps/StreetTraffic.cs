// Street Life traffic (maps chat, Pete 13 Nov): the road now runs out through gaps in both end fences onto a ring road round the
// outside of the plot. The four cars start parked in the street as before, pull out after a while and circle the ring; now and
// then one turns back in and drives through the middle between the two houses. Some park again, some drive straight through,
// and some put their foot down and go for whoever is standing in the road. The lorry sometimes stops outside a house to unload
// three boxes onto the pavement (they stay as cover until the round ends).
// Driving is on the left. Inside the plot a car keeps its two collision boxes (cover, and you can ride on the roof); outside, its
// boxes are parked out of the world. Destruction still wrecks and burns the cars: a wrecked car is handed back and stays put.
// It takes the cars over from Destruction by setting carState "ext". Test from a chat: "call StreetTraffic.Test <cmd>".
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class StreetTraffic : MonoBehaviour
    {
        public static StreetTraffic I;

        // layout, in Unity coordinates (the plot is x ±46, z ±30; the hedge and Tripo fence reach x ±48.4, z ±32.4)
        const float RX = 56, RZ = 41, RC = 12, RHW = 4, LANE = 2;        // ring road: centre line, corner radius, half width, lane offset
        const float SLANE = 1.25f, PARKX = 3.4f, ZF = 30, GAP = 9.2f;     // street driving lanes, parking line, end fences, gap half width
        const float DEC = 16;                                             // decide / merge this far before / after a junction
        const float FRONT = -1;                                           // the car models face -z

        // ------------------------------------------------------------------ routes
        class Route
        {
            public readonly List<Vector3> p = new List<Vector3>();
            public readonly List<float> c = new List<float>();
            public float[] vl;
            public bool loop;
            public float sN, sS;   // loops: where the junctions are
            public float Len => c.Count > 0 ? c[c.Count - 1] : 0;

            public void Add(Vector3 q)
            {
                q.y = 0;
                if (p.Count > 0) { float d = Vector3.Distance(p[p.Count - 1], q); if (d < .05f) return; c.Add(c[c.Count - 1] + d); }
                else c.Add(0);
                p.Add(q);
            }
            public void Line(Vector3 a, Vector3 b, float step = 2)
            {
                Add(a); int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / step));
                for (int i = 1; i <= n; i++) Add(Vector3.Lerp(a, b, i / (float)n));
            }
            public void Bezier(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, int n = 24)
            {
                Add(a);
                for (int i = 1; i <= n; i++)
                {
                    float t = i / (float)n, u = 1 - t;
                    Add(u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * cc + t * t * t * d);
                }
            }
            /// speed limit at each point from the bend (4 m/s² sideways)
            public void Finish()
            {
                int n = p.Count; vl = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int i0 = i - 1, i1 = i + 1;
                    if (loop) { i0 = (i0 + n - 1) % (n - 1); i1 = i1 % (n - 1); if (i == n - 1) { vl[i] = vl[0]; continue; } }
                    if (i0 < 0 || i1 >= n) { vl[i] = 30; continue; }
                    var a = p[i] - p[i0]; var b = p[i1] - p[i];
                    float ang = Vector3.Angle(a, b) * Mathf.Deg2Rad, L = (a.magnitude + b.magnitude) * .5f;
                    vl[i] = ang < 1e-3f ? 30 : Mathf.Min(30, Mathf.Sqrt(4 * L / ang));
                }
            }
            public float Wrap(float s) { if (!loop) return Mathf.Clamp(s, 0, Len); float L = Len; s %= L; return s < 0 ? s + L : s; }
            public int Seg(float s)
            {
                int lo = 0, hi = c.Count - 1;
                while (hi - lo > 1) { int m = (lo + hi) >> 1; if (c[m] <= s) lo = m; else hi = m; }
                return lo;
            }
            public void Sample(float s, out Vector3 pos, out Vector3 tan)
            {
                s = Wrap(s); int i = Seg(s); int j = Mathf.Min(i + 1, p.Count - 1);
                float L = c[j] - c[i], u = L > 1e-4f ? (s - c[i]) / L : 0;
                pos = Vector3.Lerp(p[i], p[j], u);
                tan = p[j] - p[i]; if (tan.sqrMagnitude < 1e-6f && i > 0) tan = p[i] - p[i - 1];
                tan.y = 0; tan.Normalize();
            }
        }

        // ------------------------------------------------------------------ cars
        class Spot { public Vector3 at; public float hz; public Car owner; }

        class Car
        {
            public Destruction.Ent e; public bool truck; public string name;
            public Vector3[] off; public float hw, hl, top, y;
            public int mode;            // 0 parked, 1 on the ring, 2 on a route, 3 unloading, 4 wrecked (Destruction's)
            public Route r; public int loopI, nextLoop = -1;
            public float s, prevS, v, vmax, wait, lat, latT, latV;
            public bool reckless, deliver, toPark, boxesIn = true;
            public Spot spot;
            public float stopS = -1, blockedT, honkT, unloadT;
            public Vector3 pos, fwd = new Vector3(0, 0, -1);
            public Fighter target;
            public int crates;
        }

        MapWorld w; MapBuilder mb; MaterialLibrary lib;
        readonly List<Car> cars = new List<Car>();
        readonly List<Spot> spots = new List<Spot>();
        readonly Route[] loops = new Route[2];
        Fighter[] fighters = new Fighter[0];
        readonly Dictionary<Fighter, float> hitT = new Dictionary<Fighter, float>();
        float quietT, t;
        AudioSource engine;
        static string forceNext = "";
        static string initLog = "";

        // the lorry's delivery: three boxes and hazard lights
        class Crate { public Transform t; public int box; public Vector3 from, to; public float t0 = -1; public bool shown, landed; }
        readonly List<Crate> crateList = new List<Crate>();
        Renderer[] hazard;

        public void Init(MapWorld world, MapBuilder builder)
        {
            I = this; w = world; mb = builder; lib = builder ? builder.library : null;
            if (!Destruction.I || !lib) { enabled = false; return; }
            OpenFences();
            MoveOutsideThings();
            BuildRoads();
            BuildLoops();
            foreach (var e in Destruction.I.ents)
            {
                if (e.d.kind != "car" || e.boxes.Length == 0) continue;
                var c = new Car { e = e, truck = e.d.carKind == "truck", name = e.d.carKind };
                c.off = new Vector3[e.boxes.Length]; c.y = e.homePos.y;
                float x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f, y1 = 0;
                for (int k = 0; k < e.boxes.Length; k++)
                {
                    var b = e.homeBoxes[k];
                    c.off[k] = new Vector3((b.x0 + b.x1) / 2 - e.homePos.x, 0, (b.z0 + b.z1) / 2 - e.homePos.z);
                    x0 = Mathf.Min(x0, b.x0); x1 = Mathf.Max(x1, b.x1); z0 = Mathf.Min(z0, b.z0); z1 = Mathf.Max(z1, b.z1); y1 = Mathf.Max(y1, b.y1);
                }
                c.hw = (x1 - x0) / 2; c.hl = (z1 - z0) / 2; c.top = y1;
                var sp = new Spot { at = new Vector3(e.homePos.x, 0, e.homePos.z), hz = e.homePos.x > 0 ? -1 : 1 };
                spots.Add(sp);
                cars.Add(c);
                Park(c, sp, true);
                if (c.truck) MakeDelivery(c);
            }
            if (cars.Count == 0) { enabled = false; return; }
            var clip = Resources.Load<AudioClip>("StreetLife/sfx/engine");
            if (clip) { engine = gameObject.AddComponent<AudioSource>(); engine.clip = clip; engine.loop = true; engine.spatialBlend = 0; engine.volume = 0; engine.Play(); }
            RoundManager.RoundStarted += OnRound;
        }

        void OnDestroy() { RoundManager.RoundStarted -= OnRound; if (I == this) I = null; }

        void OnRound(int r)
        {
            foreach (var cr in crateList) HideCrate(cr);
            if (hazard != null) foreach (var h in hazard) if (h) h.enabled = false;
            foreach (var c in cars) if (c.mode != 4) c.e.carState = "park";   // Destruction puts them home; Update picks them up again
        }

        // ------------------------------------------------------------------ the plot: gaps in the end fences, things moved out of the road
        readonly List<GameObject> fenceParts = new List<GameObject>();

        void OpenFences()
        {
            // the fences' collision walls: split each end wall round the gap (the bounds still keep fighters inside the plot)
            int n = BoxWorld.Boxes.Count;
            for (int i = 0; i < n; i++)
            {
                var b = BoxWorld.Boxes[i];
                if (b.x1 - b.x0 < 80 || Mathf.Abs((b.z0 + b.z1) / 2) < ZF - 1 || b.y1 - b.y0 < 2) continue;
                var right = b; right.x0 = GAP;
                b.x1 = -GAP; BoxWorld.Set(i, b);
                BoxWorld.Boxes.Add(right);
            }
            // the fence meshes: rebuild them readable and drop the triangles in the gaps
            initLog = "fence boxes split; ";
            if (!mb || !mb.mapJson || !mb.mapBin) { initLog += "no map data"; return; }
            var wj = JsonUtility.FromJson<WorldJson>(mb.mapJson.text);
            var geo = mb.transform.Find("__Generated/Geometry"); if (!geo) { initLog += "no geometry under " + mb.name; return; }
            var bin = mb.mapBin.bytes;
            for (int i = 0; i < wj.meshes.Length; i++)
            {
                var wm = wj.meshes[i];
                float bx0 = wm.bmin[0], bx1 = wm.bmin[0] + wm.bsize[0], bz0 = wm.bmin[2], bz1 = wm.bmin[2] + wm.bsize[2], by1 = wm.bmin[1] + wm.bsize[1];
                if (by1 < .1f || bx0 > GAP || bx1 < -GAP || (bz1 < ZF - .8f && bz0 > -ZF + .8f)) continue;
                if (wm.bsize[0] > 150 || bz1 > ZF + 1 || bz0 < -ZF - 1) continue;   // the lawn; the lamps and bushes outside
                var go = geo.Find("mesh" + i + "_m" + wm.mat); if (!go) { initLog += "missing mesh" + i + "; "; continue; }
                var mf = go.GetComponent<MeshFilter>(); if (!mf) continue;
                var m = MeshIO.Build(bin, wm, go.name);
                var v = m.vertices; var tri = m.triangles; var keep = new List<int>(tri.Length); int cut = 0;
                for (int k = 0; k < tri.Length; k += 3)
                {
                    var cpt = (v[tri[k]] + v[tri[k + 1]] + v[tri[k + 2]]) / 3;
                    if (Mathf.Abs(cpt.x) < GAP && Mathf.Abs(cpt.z) > ZF - .8f && Mathf.Abs(cpt.z) < ZF + 1.2f && cpt.y > .06f) { cut++; continue; }
                    keep.Add(tri[k]); keep.Add(tri[k + 1]); keep.Add(tri[k + 2]);
                }
                initLog += "mesh" + i + " cut " + cut + "/" + tri.Length / 3 + "; ";
                if (cut == 0) { Destroy(m); continue; }
                m.SetTriangles(keep, 0, true); m.UploadMeshData(true);
                // a new object rather than a swapped mesh, so the GPU Resident Drawer picks it up
                var src = go.GetComponent<MeshRenderer>();
                var cutGo = new GameObject(go.name + " (gap)"); cutGo.transform.SetParent(go.parent, false);
                cutGo.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = cutGo.AddComponent<MeshRenderer>(); mr.sharedMaterial = src.sharedMaterial; mr.shadowCastingMode = src.shadowCastingMode;
                go.gameObject.SetActive(false);
                fenceParts.Add(cutGo);
            }
            gameObject.AddComponent<StreetFence>().Init(mb, fenceParts);
            // gate posts either side of each gap
            var post = Lit("#f1ede4"); var cap = Lit("#3a3d45");
            foreach (float zs in new[] { -1f, 1f })
                foreach (float xs in new[] { -1f, 1f })
                {
                    var p = new Vector3(xs * (GAP + .3f), 0, zs * ZF);
                    Cube(transform, p + Vector3.up * 1.5f, new Vector3(.6f, 3f, .6f), post, true);
                    Cube(transform, p + Vector3.up * 3.05f, new Vector3(.75f, .12f, .75f), cap, true);
                }
        }

        // the bins at the ends of the road go onto the pavement; the trees round the plot move out past the ring road
        void MoveOutsideThings()
        {
            var moves = new Dictionary<Vector2Int, Vector3>
            {
                { K(54, 22), new Vector3(66, 0, 22) }, { K(59, 4), new Vector3(71, 0, 3) }, { K(55, -12), new Vector3(67, 0, -12) }, { K(60, -27), new Vector3(72, 0, -27) },
                { K(-55, 26), new Vector3(-67, 0, 26) }, { K(-59, 8), new Vector3(-70.5f, 0, 9.5f) }, { K(-54, -8), new Vector3(-66, 0, -9) }, { K(-60, -24), new Vector3(-72, 0, -24) },
                { K(-38, 40), new Vector3(-38, 0, 54) }, { K(-14, 37), new Vector3(-14, 0, 51) }, { K(10, 43), new Vector3(10, 0, 57) }, { K(32, 38), new Vector3(32, 0, 52) },
                { K(-30, -40), new Vector3(-30, 0, -54) }, { K(-8, -44), new Vector3(-8, 0, -58) }, { K(14, -38), new Vector3(14, 0, -52) }, { K(36, -42), new Vector3(36, 0, -56) },
                { K(52, -41), new Vector3(56, 0, -47) }, { K(-52, 42), new Vector3(-55.5f, 0, 47.5f) },
            };
            // Pete: no red plants. The Tripo red plant (library material 93) comes out of every room.
            var red = Mat(93); var gone = new List<Destruction.Ent>();
            foreach (var e in Destruction.I.ents) if (e.d.kind == "knock" && e.rends.Count > 0 && e.rends[0] && e.rends[0].sharedMaterial == red) gone.Add(e);
            foreach (var e in gone) Destruction.I.Remove(e);
            initLog += gone.Count + " red plants removed; ";
            foreach (var e in Destruction.I.ents)
            {
                Vector3 to;
                if (e.d.kind == "tree") { if (!moves.TryGetValue(K(e.homePos.x, e.homePos.z), out to)) continue; to.y = e.homePos.y; }
                else if (e.d.prop == "bin" && Mathf.Abs(e.homePos.x) < 6 && Mathf.Abs(e.homePos.z) > 20) to = new Vector3(Mathf.Sign(e.homePos.x) * 7.6f, e.homePos.y, e.homePos.z);
                else continue;
                var d = to - e.homePos; d.y = 0;
                if (e.d.kind != "tree") MoveOutline(e, d);
                e.homePos += d; e.t.localPosition += d;
                for (int k = 0; k < e.boxes.Length; k++)
                {
                    var b = e.homeBoxes[k]; b.x0 += d.x; b.x1 += d.x; b.z0 += d.z; b.z1 += d.z; e.homeBoxes[k] = b;
                    var cur = BoxWorld.Boxes[e.boxes[k]]; cur.x0 += d.x; cur.x1 += d.x; cur.z0 += d.z; cur.z1 += d.z; BoxWorld.Set(e.boxes[k], cur);
                }
            }
        }
        // the black outline drawn round a box-shaped prop goes with it
        void MoveOutline(Destruction.Ent e, Vector3 d)
        {
            var root = mb ? mb.transform.Find("__Generated/Outlines") : null; if (!root || e.boxes.Length == 0) return;
            var b = e.homeBoxes[0]; var ctr = new Vector3((b.x0 + b.x1) / 2, (b.y0 + b.y1) / 2, (b.z0 + b.z1) / 2);
            foreach (Transform ch in root)
            {
                var r = ch.GetComponent<Renderer>(); if (!r) continue;
                if ((r.bounds.center - ctr).sqrMagnitude < .09f) { ch.position += d; initLog += "outline moved; "; }
            }
        }

        static Vector2Int K(float x, float z) => new Vector2Int(Mathf.RoundToInt(x), Mathf.RoundToInt(z));

        // ------------------------------------------------------------------ road surfaces
        void BuildRoads()
        {
            var asphalt = Mat(132); var paint = Mat(52);
            var centre = CentreLoop();
            // the ring: a strip either side of the centre line
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
            int n = centre.Count;
            for (int i = 0; i < n; i++)
            {
                var tg = (centre[(i + 1) % n] - centre[(i - 1 + n) % n]).normalized; var left = new Vector3(-tg.z, 0, tg.x);
                foreach (float sd in new[] { 1f, -1f }) { var q = centre[i] + left * RHW * sd; q.y = .002f; v.Add(q); uv.Add(new Vector2(q.x, q.z)); }
            }
            for (int i = 0; i < n; i++) { int a = i * 2, b = ((i + 1) % n) * 2; Quad(tr, a, a + 1, b + 1, b, v); }
            // the bell mouths from the street ends out to the ring
            foreach (float zs in new[] { 1f, -1f })
            {
                int o = v.Count; float zi = zs * (RZ - RHW);
                foreach (var q in new[] { new Vector3(-6, 0, zs * ZF), new Vector3(6, 0, zs * ZF), new Vector3(16, 0, zi), new Vector3(-16, 0, zi) }) { v.Add(q); uv.Add(new Vector2(q.x, q.z)); }
                Quad(tr, o, o + 1, o + 2, o + 3, v);
            }
            Surface("Ring road", v, uv, tr, asphalt);
            // centre dashes round the ring (not across the junctions) and on into the street mouths
            var dv = new List<Vector3>(); var duv = new List<Vector2>(); var dt = new List<int>();
            var lp = new Route { loop = true }; foreach (var q in centre) lp.Add(q); lp.Add(centre[0]);
            for (float s = 2; s < lp.Len - 3; s += 6)
            {
                lp.Sample(s, out var a, out var ta); lp.Sample(s + 3, out var b, out _);
                if (Mathf.Abs(a.x) < 18 && Mathf.Abs(Mathf.Abs(a.z) - RZ) < 1) continue;
                Dash(dv, duv, dt, a, b);
            }
            foreach (float zs in new[] { 1f, -1f }) Dash(dv, duv, dt, new Vector3(0, 0, zs * (ZF + 2)), new Vector3(0, 0, zs * (ZF + 5)));
            // give-way lines where the street meets the ring
            foreach (float zs in new[] { 1f, -1f }) GiveWay(dv, duv, dt, zs);
            Surface("Ring road lines", dv, duv, dt, paint);
        }

        void GiveWay(List<Vector3> v, List<Vector2> uv, List<int> tr, float zs)
        {
            float z = zs * (RZ - RHW - .6f);
            float x0 = zs > 0 ? -6f : 0, x1 = zs > 0 ? 0 : 6f;   // across the lane coming out of the street
            for (float x = x0 + .2f; x < x1 - .3f; x += 1.2f)
                for (int k = 0; k < 2; k++) Dash(v, uv, tr, new Vector3(x, 0, z - zs * k * .45f), new Vector3(x + .6f, 0, z - zs * k * .45f), .15f);
        }

        static void Dash(List<Vector3> v, List<Vector2> uv, List<int> tr, Vector3 a, Vector3 b, float hw = .125f)
        {
            var d = (b - a); d.y = 0; d.Normalize(); var l = new Vector3(-d.z, 0, d.x) * hw; int o = v.Count;
            foreach (var q in new[] { a - l, a + l, b + l, b - l }) { var p = q; p.y = .018f; v.Add(p); uv.Add(new Vector2(p.x, p.z)); }
            Quad(tr, o, o + 1, o + 2, o + 3, v);
        }

        // two triangles, wound to face up whatever order the corners come in
        static void Quad(List<int> tr, int a, int b, int c, int d, List<Vector3> v)
        {
            var nrm = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (nrm.y >= 0) { tr.Add(a); tr.Add(b); tr.Add(c); tr.Add(a); tr.Add(c); tr.Add(d); }
            else { tr.Add(a); tr.Add(c); tr.Add(b); tr.Add(a); tr.Add(d); tr.Add(c); }
        }

        void Surface(string name, List<Vector3> v, List<Vector2> uv, List<int> tr, Material m)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tr, 0);
            var nr = new Vector3[v.Count]; for (int i = 0; i < nr.Length; i++) nr[i] = Vector3.up; mesh.normals = nr;
            mesh.RecalculateBounds(); mesh.UploadMeshData(true);
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Material Mat(int i) => lib.map != null && i >= 0 && i < lib.map.Length && lib.map[i] ? lib.map[i] : lib.unlitBase;

        Material Lit(string hex)
        {
            var m = new Material(Mat(52)); m.SetColor("_BaseColor", Hex.ToColor(hex)); return m;
        }

        static Transform Cube(Transform parent, Vector3 at, Vector3 size, Material m, bool shadow)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            go.transform.SetParent(parent, false); go.transform.localPosition = at; go.transform.localScale = size;
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        // ------------------------------------------------------------------ the ring's lanes
        static List<Vector3> CentreLoop()
        {
            var pts = new List<Vector3>();
            float ax = RX - RC, az = RZ - RC;
            void Arc(float cx, float cz, float a0, float a1)
            {
                int n = Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 6f);
                for (int i = 1; i <= n; i++) { float a = Mathf.Lerp(a0, a1, i / (float)n) * Mathf.Deg2Rad; pts.Add(new Vector3(cx + RC * Mathf.Cos(a), 0, cz + RC * Mathf.Sin(a))); }
            }
            void Line(Vector3 a, Vector3 b) { int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 2)); for (int i = 1; i <= n; i++) pts.Add(Vector3.Lerp(a, b, i / (float)n)); }
            pts.Add(new Vector3(0, 0, RZ));                                   // north junction, heading east
            Line(new Vector3(0, 0, RZ), new Vector3(ax, 0, RZ)); Arc(ax, az, 90, 0);
            Line(new Vector3(RX, 0, az), new Vector3(RX, 0, -az)); Arc(ax, -az, 0, -90);
            Line(new Vector3(ax, 0, -RZ), new Vector3(0, 0, -RZ));            // south junction
            Line(new Vector3(0, 0, -RZ), new Vector3(-ax, 0, -RZ)); Arc(-ax, -az, -90, -180);
            Line(new Vector3(-RX, 0, -az), new Vector3(-RX, 0, az)); Arc(-ax, az, 180, 90);
            Line(new Vector3(-ax, 0, RZ), new Vector3(0, 0, RZ));
            pts.RemoveAt(pts.Count - 1);                                      // the last point is the first again
            return pts;
        }

        void BuildLoops()
        {
            var cA = CentreLoop();
            var cB = new List<Vector3> { cA[0] }; for (int i = cA.Count - 1; i > 0; i--) cB.Add(cA[i]);
            loops[0] = Lane(cA); loops[1] = Lane(cB);
        }

        static Route Lane(List<Vector3> centre)
        {
            var r = new Route { loop = true }; int n = centre.Count;
            for (int i = 0; i <= n; i++)
            {
                int k = i % n; var tg = (centre[(k + 1) % n] - centre[(k - 1 + n) % n]).normalized; var left = new Vector3(-tg.z, 0, tg.x);
                r.Add(centre[k] + left * LANE);
            }
            r.Finish();
            r.sN = 0;
            float best = 1e9f; for (int i = 0; i < r.p.Count; i++) { float d = Mathf.Abs(r.p[i].x) + Mathf.Abs(r.p[i].z + RZ); if (r.p[i].z < 0 && d < best) { best = d; r.sS = r.c[i]; } }
            return r;
        }

        // ------------------------------------------------------------------ route building
        static Vector3 Inward(bool north) => new Vector3(0, 0, north ? -1 : 1);
        static float LaneX(float hz) => hz < 0 ? SLANE : -SLANE;          // heading -z drives on +x (on the left)

        /// from the street end (heading out) onto a ring lane just past the junction
        void ExitTo(Route r, Vector3 q, Vector3 h, bool north, int li)
        {
            var L = loops[li]; float sm = L.Wrap((north ? L.sN : L.sS) + DEC);
            L.Sample(sm, out var m, out var tm);
            r.Bezier(q, q + h * 9, m - tm * 9, m);
        }

        void Depart(Car c)
        {
            var r = new Route(); var h = new Vector3(0, 0, c.fwd.z >= 0 ? 1 : -1);   // off the way it is facing
            var p = c.pos; float lx = LaneX(h.z), endZ = h.z * ZF;
            float room = Mathf.Abs(endZ - p.z);
            if (Mathf.Abs(p.x - lx) > .3f && room > 14)
            {
                r.Add(p); var a = p + h * 2; var b = new Vector3(lx, 0, p.z + h.z * 12);
                r.Bezier(a, a + h * 5, b - h * 5, b, 16);
                r.Line(b, new Vector3(lx, 0, endZ));
            }
            else r.Line(p, new Vector3(p.x, 0, endZ));
            bool north = h.z > 0;
            int li = Random.value < .5f ? 0 : 1;
            ExitTo(r, r.p[r.p.Count - 1], h, north, li);
            r.Finish();
            if (c.spot != null) { c.spot.owner = null; c.spot = null; }
            Go(c, r, li, Random.Range(6f, 9f));
            c.reckless = false; c.deliver = false; c.toPark = false;
        }

        /// from a ring lane at its decision point into the street, through it, and out onto a lane at the far end
        void EnterStreet(Car c, bool north)
        {
            var L = loops[c.loopI];
            var h = Inward(north); float lx = LaneX(h.z);
            L.Sample(c.s, out var p0, out var t0);
            var q = new Vector3(lx, 0, -h.z * ZF);
            var r = new Route();
            r.Bezier(p0, p0 + t0 * 9, q - h * 9, q);
            float sStreet = r.Len;
            // what this visit is for
            string force = forceNext; forceNext = "";
            bool anyoneOnRoad = false; foreach (var f in fighters) if (f && f.alive && Mathf.Abs(f.pos.x) < 8 && Mathf.Abs(f.pos.z) < ZF - 1) anyoneOnRoad = true;
            Spot free = null; foreach (var sp in spots) if (sp.owner == null && sp.hz == h.z && (free == null || Random.value < .5f)) free = sp;
            c.deliver = c.truck && (force == "deliver" || (force == "" && Random.value < .6f));
            c.toPark = !c.deliver && free != null && (force == "park" || (force == "" && Random.value < .35f));
            c.reckless = !c.deliver && !c.toPark && (force == "reckless" || (force == "" && anyoneOnRoad && Random.value < .35f));
            c.stopS = -1;
            if (c.toPark)
            {
                // pull in to the kerb over the last 12 m
                float zs = free.at.z, run = Mathf.Abs(zs - q.z);
                var b = new Vector3(free.at.x, 0, zs);
                if (run > 15) { var a = new Vector3(lx, 0, zs - h.z * 13); r.Line(q, a); r.Bezier(a, a + h * 5, b - h * 5, b, 16); }
                else r.Bezier(q, q + h * run / 3, b - h * run / 3, b, 16);
                free.owner = c; c.spot = free;
                r.Finish(); Go(c, r, -1, Random.Range(6.5f, 9f));
                c.stopS = r.Len;
            }
            else
            {
                var far = new Vector3(lx, 0, h.z * ZF);
                r.Line(q, far);
                if (c.deliver) c.stopS = sStreet + Mathf.Abs((h.z < 0 ? 9.5f : -9.5f) - q.z);
                int li = Random.value < .5f ? 0 : 1;
                ExitTo(r, far, h, !north, li);
                r.Finish();
                Go(c, r, li, c.reckless ? Random.Range(19f, 23f) : Random.Range(7f, 10f));
                if (c.reckless) { c.target = Nearest(c); Sfx.Play("horn", c.pos, 1, 40, .85f); }
            }
            quietT = 0;
        }

        void Go(Car c, Route r, int nextLoop, float vmax)
        {
            c.r = r; c.s = 0; c.prevS = 0; c.mode = 2; c.nextLoop = nextLoop; c.vmax = vmax; c.lat = c.latT = 0; c.blockedT = 0;
        }

        Fighter Nearest(Car c)
        {
            Fighter best = null; float bd = 1e9f;
            foreach (var f in fighters)
            {
                if (!f || !f.alive || f.Frozen || Mathf.Abs(f.pos.x) > 8 || Mathf.Abs(f.pos.z) > ZF) continue;
                float d = Vector3.Distance(f.pos, c.pos); if (d < bd) { bd = d; best = f; }
            }
            return best;
        }

        void Park(Car c, Spot sp, bool atHome)
        {
            c.mode = 0; c.v = 0; c.spot = sp; if (sp != null) sp.owner = c;
            c.wait = atHome ? Random.Range(8f, 30f) : Random.Range(12f, 40f);
            c.reckless = c.deliver = c.toPark = false; c.stopS = -1; c.lat = c.latT = 0;
            if (atHome) { c.pos = c.e.homePos; c.pos.y = 0; c.fwd = new Vector3(0, 0, FRONT); }
            c.e.carState = "ext";
            SetBoxes(c, true);
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, .05f); t += dt; quietT += dt;
            if (Time.frameCount % 30 == 0 || fighters.Length == 0) fighters = FindObjectsByType<Fighter>();
            float ev = 0, ep = 0, epitch = 1;
            foreach (var c in cars)
            {
                if (c.e.burnt) { if (c.mode != 4) { c.mode = 4; if (c.spot != null) { c.spot.owner = null; c.spot = null; } if (c.truck) StopHazard(); } continue; }
                if (c.e.carState == "park" || c.mode == 4)
                {   // a new round: Destruction has put it back home
                    foreach (var sp in spots) if (sp.owner == c) sp.owner = null;
                    Spot home = null; foreach (var sp in spots) if (Vector3.Distance(sp.at, new Vector3(c.e.homePos.x, 0, c.e.homePos.z)) < .5f) home = sp;
                    if (home != null && home.owner != null && home.owner != c) { var other = home.owner; other.spot = null; }
                    Park(c, home, true); if (c.truck) StopHazard(); continue;
                }
                switch (c.mode)
                {
                    case 0: c.wait -= dt; if (c.wait <= 0 && !Blocked(c)) Depart(c); break;
                    case 1: case 2: Drive(c, dt); break;
                    case 3: Unload(c, dt); break;
                }
                if (c.v > .4f && Camera.main)
                {
                    var d = c.pos - Camera.main.transform.position; float dist = d.magnitude;
                    float vol = 1 / (1 + Mathf.Max(0, dist - 2) / 9) * Mathf.Clamp(c.v / 6, .35f, 1.2f);
                    if (vol > ev) { ev = vol; var flat = new Vector3(d.x, 0, d.z); ep = flat.sqrMagnitude > 1e-4f ? Vector3.Dot(flat.normalized, Camera.main.transform.right) * .7f : 0; epitch = .85f + c.v / 30f; }
                }
            }
            if (engine)
            {
                bool on = !MainMenu.Paused;
                engine.volume = Mathf.MoveTowards(engine.volume, on ? Mathf.Clamp01(ev * .55f * Sfx.SfxVolume) : 0, dt * 2);
                engine.panStereo = ep; engine.pitch = Mathf.MoveTowards(engine.pitch, epitch, dt);
            }
            UpdCrates(dt);
        }

        bool Blocked(Car c)
        {   // don't pull out into a car coming past
            foreach (var o in cars) if (o != c && (o.mode == 1 || o.mode == 2) && Mathf.Abs(o.pos.x - c.pos.x) < 5 && Vector3.Distance(o.pos, c.pos) < 16) return true;
            return false;
        }

        void Drive(Car c, float dt)
        {
            var r = c.mode == 1 ? loops[c.loopI] : c.r;
            bool inside = Mathf.Abs(c.pos.z) < ZF + 4 && Mathf.Abs(c.pos.x) < 20;
            // the speed it may do: bends ahead, a stop ahead, the car in front, people and things in the road
            float want = c.vmax;
            float look = c.v * c.v / 10 + 6;
            for (int i = r.Seg(r.Wrap(c.s)) + 1, k = 0; k < 40; i++, k++)
            {
                if (i >= r.p.Count) { if (!r.loop) break; i = 1; }
                float d = r.c[i] - r.Wrap(c.s); if (d < 0) d += r.Len; if (d > look) break;
                want = Mathf.Min(want, Mathf.Sqrt(r.vl[i] * r.vl[i] + 2 * 5 * d));
            }
            if (c.stopS >= 0) want = Mathf.Min(want, Mathf.Sqrt(2 * 3.2f * Mathf.Max(0, c.stopS - c.s)) + (c.stopS - c.s > .3f ? .4f : 0));
            var left = new Vector3(-c.fwd.z, 0, c.fwd.x);
            bool hardStop = false;
            foreach (var o in cars)
            {
                if (o == c || o.mode == 4) continue;
                var rel = o.pos - c.pos; float along = Vector3.Dot(rel, c.fwd), side = Vector3.Dot(rel, left);
                if (along <= 0 || along > 40 || Mathf.Abs(side) > 1.7f) continue;
                if (o.mode == 0 && Mathf.Abs(o.pos.x) > 2.6f) continue;   // parked at the kerb, clear of the lanes
                float gap = along - c.hl - o.hl - 2.2f;
                want = Mathf.Min(want, Mathf.Sqrt(2 * 7 * Mathf.Max(0, gap)));
            }
            if (inside)
            {
                float stopD = c.v * c.v / 24 + 1.4f;
                if (!c.reckless)
                    foreach (var f in fighters)
                    {
                        if (!f || !f.alive) continue;
                        var rel = f.pos - c.pos; float along = Vector3.Dot(rel, c.fwd), side = Vector3.Dot(rel, left);
                        if (along < c.hl - .4f || along > c.hl + stopD + 2.5f || Mathf.Abs(side) > c.hw + Fighter.R + .4f || f.pos.y > c.top + 1.2f) continue;
                        want = Mathf.Min(want, Mathf.Sqrt(2 * 12 * Mathf.Max(0, along - c.hl - 1.1f)));
                        hardStop = true;
                        if (c.v > 1.5f || c.blockedT > 4) { if (t - c.honkT > 3.5f) { c.honkT = t; Sfx.Play("horn", c.pos, 1, 20); } }
                    }
                float bd = BoxAhead(c, c.lat, 18);
                if (bd < 18)
                {
                    // something solid in the lane (a burnt-out car, a shoved crate): go round it on the other side if that's clear
                    float other = c.latT == 0 ? -2 * SLANE : 0;   // the other lane is on its right
                    if (c.latT == 0 && BoxAhead(c, other, 22) >= 22 && !Oncoming(c)) c.latT = other;
                    else want = Mathf.Min(want, Mathf.Sqrt(2 * 8 * Mathf.Max(0, bd - 1)));
                }
                else if (c.latT != 0 && !c.reckless && BoxAhead(c, 0, 6, -c.hl * 2 - 1) >= 6) c.latT = 0;
            }
            // reckless: aim at someone in the road (staying in the two driving lanes)
            if (c.reckless)
            {
                if (!c.target || !c.target.alive || Vector3.Dot(c.target.pos - c.pos, c.fwd) < 0) c.target = inside ? NearestAhead(c) : c.target;
                if (c.target && Mathf.Abs(c.pos.z) < ZF + 6)
                {
                    float lt = Vector3.Dot(c.target.pos - (c.pos - left * c.lat), left);
                    float laneX = (c.pos - left * c.lat).x;
                    // across both lanes, and out to the kerb where nobody is parked beside them
                    float maxL = 1.3f; var tp = c.target.pos; bool parkedBy = false;
                    foreach (var o in cars) if (o.mode == 0 && Mathf.Sign(o.pos.x) == Mathf.Sign(tp.x) && Mathf.Abs(o.pos.z - tp.z) < o.hl + 4) parkedBy = true;
                    if (!parkedBy) maxL = 3.3f;
                    float tx = Mathf.Clamp(laneX + left.x * lt, -maxL, maxL);
                    c.latT = left.x != 0 ? (tx - laneX) / left.x : lt;
                }
                else c.latT = 0;
            }
            float acc = c.reckless ? 9 : 4.5f, dec = hardStop ? 16 : 8;
            c.v = want > c.v ? Mathf.Min(want, c.v + acc * dt) : Mathf.Max(want, c.v - dec * dt);
            if (c.v < .05f && want < .05f) c.blockedT += dt; else c.blockedT = 0;
            // move along
            c.prevS = c.s; c.s += c.v * dt;
            float latPrev = c.lat; c.lat = Mathf.MoveTowards(c.lat, c.latT, (c.reckless ? 3.2f : 2.2f) * dt);
            if (c.mode == 1)
            {
                var L = loops[c.loopI]; float s0 = L.Wrap(c.prevS), s1 = L.Wrap(c.s);
                c.s = s1;
                foreach (bool north in new[] { true, false })
                {
                    float sd = L.Wrap((north ? L.sN : L.sS) - DEC);
                    bool crossed = s0 <= s1 ? (sd > s0 && sd <= s1) : (sd > s0 || sd <= s1);
                    if (!crossed) continue;
                    float p = .26f; int inStreet = 0; foreach (var o in cars) if (o.mode == 2 || o.mode == 3) inStreet++;
                    if (quietT > 25) p = .75f;
                    if (forceNext != "") p = 1;
                    if (inStreet >= 2 || StreetBlocked(north)) p = 0;
                    if (Random.value < p) { c.s = sd; EnterStreet(c, north); break; }
                }
            }
            else if (c.s >= c.r.Len - .01f || (c.stopS >= 0 && c.s >= c.stopS - .05f && c.v < .3f))
            {
                if (c.stopS >= 0 && c.s >= c.stopS - .05f)
                {
                    if (c.toPark) { Place(c, dt, latPrev); Park(c, c.spot, false); return; }
                    if (c.deliver) { c.s = c.stopS; c.stopS = -1; c.v = 0; c.mode = 3; c.unloadT = 0; c.crates = 0; Place(c, dt, latPrev); return; }
                }
                if (c.nextLoop >= 0) { var L = loops[c.nextLoop]; float end = c.r.Len; c.loopI = c.nextLoop; c.mode = 1; c.s = L.Wrap(LoopS(L, c.r.p[c.r.p.Count - 1]) + (c.s - end)); c.vmax = Random.Range(12.5f, 16.5f); c.reckless = false; c.lat = c.latT = 0; }
            }
            Place(c, dt, latPrev);
        }

        float LoopS(Route L, Vector3 p)
        {
            float best = 1e9f, s = 0; for (int i = 0; i < L.p.Count; i++) { float d = (L.p[i] - p).sqrMagnitude; if (d < best) { best = d; s = L.c[i]; } }
            return s;
        }

        bool StreetBlocked(bool north)
        {   // a car stopped in the lane right inside the gate, or someone parked in the mouth
            var h = Inward(north); float lx = LaneX(h.z);
            foreach (var o in cars) if (o.mode != 1 && Mathf.Abs(o.pos.x - lx) < 1.8f && Mathf.Abs(o.pos.z + h.z * (ZF - 6)) < 8 && o.v < 2) return true;
            return false;
        }

        bool Oncoming(Car c)
        {
            foreach (var o in cars)
            {
                if (o == c || o.mode == 4 || o.mode == 0) continue;
                var rel = o.pos - c.pos; if (Vector3.Dot(o.fwd, c.fwd) > -.5f) continue;
                if (Vector3.Dot(rel, c.fwd) > -2 && Vector3.Dot(rel, c.fwd) < 40 && Mathf.Abs(rel.x) < 4) return true;
            }
            return false;
        }

        Fighter NearestAhead(Car c)
        {
            Fighter best = null; float bd = 45;
            foreach (var f in fighters)
            {
                if (!f || !f.alive || f.Frozen || Mathf.Abs(f.pos.x) > 7 || Mathf.Abs(f.pos.z) > ZF || f.pos.y > 1.5f) continue;
                float a = Vector3.Dot(f.pos - c.pos, c.fwd); if (a < c.hl || a > bd) continue; bd = a; best = f;
            }
            return best;
        }

        /// distance from the car's nose to the nearest solid box in its lane (shifted sideways by lat), up to max
        float BoxAhead(Car c, float lat, float max, float from = 0)
        {
            var left = new Vector3(-c.fwd.z, 0, c.fwd.x);
            var centre = c.pos + left * (lat - c.lat);
            float sz = Mathf.Sign(c.fwd.z); if (Mathf.Abs(c.fwd.z) < .7f) return max;
            float x0 = centre.x - c.hw - .15f, x1 = centre.x + c.hw + .15f;
            float za = centre.z + sz * (c.hl + from), zb = centre.z + sz * (c.hl + max);
            float z0 = Mathf.Min(za, zb), z1 = Mathf.Max(za, zb);
            float best = max;
            var B = BoxWorld.Boxes;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i];
                if (b.y1 < .35f || b.y0 > c.top || b.x1 < x0 || b.x0 > x1 || b.z1 < z0 || b.z0 > z1) continue;
                if (b.x1 - b.x0 > 60 || b.z1 - b.z0 > 60) continue;
                if (OwnedByLiveCar(i)) continue;
                float d = sz > 0 ? b.z0 - za : za - b.z1; d += from; if (d < best) best = Mathf.Max(0, d);
            }
            return best;
        }

        bool OwnedByLiveCar(int i)
        {
            foreach (var o in cars) if (o.mode != 4) foreach (var bi in o.e.boxes) if (bi == i) return true;
            return false;
        }

        // ------------------------------------------------------------------ placing the car and its boxes, people in the way
        void Place(Car c, float dt, float latPrev)
        {
            var r = c.mode == 1 ? loops[c.loopI] : c.r;
            r.Sample(c.s, out var p, out var tg);
            var left = new Vector3(-tg.z, 0, tg.x);
            var old = c.pos;
            c.pos = p + left * c.lat;
            float latRate = dt > 0 ? (c.lat - latPrev) / dt : 0;
            var f = tg * Mathf.Max(c.v, 2) + left * latRate; f.y = 0;
            if (f.sqrMagnitude > 1e-4f) c.fwd = f.normalized;
            var e = c.e;
            e.t.position = new Vector3(c.pos.x, c.y, c.pos.z);
            e.t.rotation = Quaternion.LookRotation(c.fwd * FRONT);
            var delta = c.pos - old; delta.y = 0;
            // riders on the roof come along
            if (c.boxesIn && delta.sqrMagnitude > 0)
            {
                var tb = BoxWorld.Boxes[e.boxes[e.boxes.Length - 1]];
                foreach (var fi in fighters)
                {
                    if (!fi || !fi.alive || !fi.grounded) continue;
                    if (fi.pos.x > tb.x0 - .1f && fi.pos.x < tb.x1 + .1f && fi.pos.z > tb.z0 - .1f && fi.pos.z < tb.z1 + .1f && Mathf.Abs(fi.pos.y - tb.y1) < .12f) fi.Carry(delta);
                }
            }
            bool inside = Mathf.Abs(c.pos.z) < ZF + .5f && Mathf.Abs(c.fwd.z) > .9f;
            SetBoxes(c, inside);
            if (Mathf.Abs(c.pos.z) < ZF + 3 && c.v > .05f) Contact(c);
        }

        void SetBoxes(Car c, bool inside)
        {
            var e = c.e;
            if (!inside)
            {
                if (!c.boxesIn) return;
                c.boxesIn = false;
                for (int k = 0; k < e.boxes.Length; k++) BoxWorld.Set(e.boxes[k], new Box { x0 = c.pos.x - .05f, x1 = c.pos.x + .05f, y0 = -60, y1 = -59.9f, z0 = Mathf.Sign(c.pos.z) * (ZF + 2) - .05f, z1 = Mathf.Sign(c.pos.z) * (ZF + 2) + .05f });
                return;
            }
            c.boxesIn = true;
            float flip = c.fwd.z * FRONT > 0 ? 1 : -1;   // turned round: the boxes swap ends
            for (int k = 0; k < e.boxes.Length; k++)
            {
                var hb = e.homeBoxes[k]; float hx = (hb.x1 - hb.x0) / 2, hz = (hb.z1 - hb.z0) / 2;
                var cc = c.pos + c.off[k] * flip;
                BoxWorld.Set(e.boxes[k], new Box { x0 = cc.x - hx, x1 = cc.x + hx, y0 = hb.y0, y1 = hb.y1, z0 = cc.z - hz, z1 = cc.z + hz, wood = hb.wood, glass = hb.glass, prop = hb.prop, tag = hb.tag });
            }
        }

        /// anyone the car runs into: knocked flying (and hurt) if it's going at a fair lick, otherwise just shoved aside
        void Contact(Car c)
        {
            var left = new Vector3(-c.fwd.z, 0, c.fwd.x);
            foreach (var f in fighters)
            {
                if (!f || !f.alive) continue;
                var rel = f.pos - c.pos; float along = Vector3.Dot(rel, c.fwd), side = Vector3.Dot(rel, left);
                if (Mathf.Abs(along) > c.hl + Fighter.R || Mathf.Abs(side) > c.hw + Fighter.R || f.pos.y > c.top - .25f) continue;
                float sgn = side >= 0 ? 1 : -1;
                float push = c.hw + Fighter.R + .05f - Mathf.Abs(side);
                f.Carry(left * sgn * push);
                if (c.v < 4.5f || f.Frozen) continue;
                hitT.TryGetValue(f, out var last);
                if (last != 0 && Time.time - last < 1.2f) continue;
                hitT[f] = Time.time;
                f.vel = c.fwd * c.v * .75f + left * sgn * Random.Range(5f, 8f) + Vector3.up * Random.Range(7.5f, 10f);
                f.grounded = false; f.thrownT = .6f;
                float dmg = Mathf.Clamp(14 + c.v * 2.9f, 20, 78);
                Sfx.Play("punch", f.pos, 1, 22, .7f); Sfx.Play("impact", f.pos, .8f, 22);
                var lb = f.lastBy; var by = lb != null && lb != f && lb.team != f.team && f.simTime - f.lastHurt < 10 ? lb : null;
                var prev = RoundManager.WeaponNameOf;
                RoundManager.WeaponNameOf = x => x == by ? "Roadkill" : prev != null ? prev(x) : x.primary;
                try { Combat.Damage(f, dmg, by, false, "car"); } finally { RoundManager.WeaponNameOf = prev; }
            }
        }

        // ------------------------------------------------------------------ the lorry's delivery
        void MakeDelivery(Car truck)
        {
            var card = Lit("#b98a57"); var tape = Lit("#d8bd8e");
            for (int i = 0; i < 3; i++)
            {
                var root = new GameObject("Delivery box").transform; root.SetParent(transform, false);
                Cube(root, Vector3.zero, Vector3.one * .78f, card, true);
                Cube(root, new Vector3(0, .392f, 0), new Vector3(.8f, .012f, .16f), tape, false);
                Cube(root, new Vector3(0, 0, .392f), new Vector3(.16f, .8f, .012f), tape, false);
                root.gameObject.SetActive(false);
                var cr = new Crate { t = root, box = BoxWorld.Boxes.Count };
                BoxWorld.Boxes.Add(new Box { x0 = -.05f, x1 = .05f, y0 = -60, y1 = -59.9f, z0 = -.05f, z1 = .05f, wood = true });
                NavGrid.Dynamic.Add(cr.box);
                crateList.Add(cr);
            }
            // hazard lights on the back corners
            var amber = new Material(lib.unlitBase ? lib.unlitBase : Mat(52)); amber.SetColor("_BaseColor", new Color(1f, .55f, .08f) * 4);
            hazard = new Renderer[2];
            for (int k = 0; k < 2; k++)
            {
                var lamp = Cube(truck.e.t, new Vector3((k == 0 ? -1 : 1) * (truck.hw - .18f), .95f, -FRONT * (truck.hl + .01f)), new Vector3(.2f, .12f, .04f), amber, false);
                var rr = lamp.GetComponent<Renderer>(); rr.enabled = false; hazard[k] = rr;
            }
        }

        void StopHazard() { if (hazard != null) foreach (var h in hazard) if (h) h.enabled = false; }

        void Unload(Car c, float dt)
        {
            c.unloadT += dt; c.v = 0;
            bool blink = Mathf.Repeat(c.unloadT, .8f) < .4f;
            if (hazard != null) foreach (var h in hazard) if (h) h.enabled = blink;
            if (c.unloadT < .2f) foreach (var cr in crateList) HideCrate(cr);   // the last delivery has been taken in
            var back = c.pos - c.fwd * (c.hl + .45f);
            float side = Mathf.Sign(c.pos.x);                                   // the near pavement
            for (int i = 0; i < 3; i++)
            {
                if (c.crates > i || c.unloadT < 1.6f + i * 1.9f) continue;
                c.crates = i + 1;
                var cr = crateList[i];
                var to = new Vector3(side * 7.4f, .05f + .39f, back.z + c.fwd.z * -(.2f) + (i == 1 ? -c.fwd.z * .85f : 0));
                if (i == 2) to = new Vector3(side * 7.4f, .05f + .39f + .78f, back.z - c.fwd.z * .42f);
                cr.from = new Vector3(back.x, 1.2f, back.z); cr.to = to; cr.t0 = t; cr.shown = true; cr.landed = false;
                cr.t.gameObject.SetActive(true); cr.t.position = cr.from; cr.t.rotation = Quaternion.Euler(0, Random.Range(-12f, 12f), 0);
                Sfx.Play("wood", cr.from, .5f, 16, 1.2f);
            }
            if (c.unloadT > 9.5f && !Blocked(c))
            {
                StopHazard();
                c.mode = 2; c.deliver = false; c.vmax = Random.Range(7f, 9.5f);
            }
        }

        void UpdCrates(float dt)
        {
            foreach (var cr in crateList)
            {
                if (!cr.shown || cr.landed || cr.t0 < 0) continue;
                float u = Mathf.Clamp01((t - cr.t0) / .9f);
                var p = Vector3.Lerp(cr.from, cr.to, u); p.y += Mathf.Sin(u * Mathf.PI) * .8f;
                cr.t.position = p;
                if (u >= 1)
                {
                    cr.landed = true; Sfx.Play("crate", cr.to, .8f, 18);
                    const float h = .39f;
                    BoxWorld.Set(cr.box, new Box { x0 = cr.to.x - h, x1 = cr.to.x + h, y0 = cr.to.y - h, y1 = cr.to.y + h, z0 = cr.to.z - h, z1 = cr.to.z + h, wood = true });
                }
            }
        }

        void HideCrate(Crate cr)
        {
            cr.shown = false; cr.landed = false; cr.t0 = -1; if (cr.t) cr.t.gameObject.SetActive(false);
            BoxWorld.Set(cr.box, new Box { x0 = -.05f, x1 = .05f, y0 = -60, y1 = -59.9f, z0 = -.05f, z1 = .05f, wood = true });
        }

        // ------------------------------------------------------------------ testing from a chat
        /// "state", "street [park|deliver|reckless]" (the next car to pass a junction comes in), "go" (every parked car leaves now),
        /// "cam x y z yaw pitch" (a fixed camera for screenshots), "cam off", "put x z" (the player to x, z)
        public static string Test(string a)
        {
            var it = I; if (!it) return "no traffic (not on Street Life, or not playing)";
            var parts = (a ?? "").Trim().Split(' ');
            switch (parts[0])
            {
                case "street": forceNext = parts.Length > 1 ? parts[1] : "through"; if (forceNext == "through") forceNext = "x"; return "next car in: " + forceNext;
                case "log": return initLog;
                case "probe":
                    {   // which renderers cover a point (x, y, z)
                        float F(int i) => float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
                        var pt = new Vector3(F(1), F(2), F(3)); var sb2 = new System.Text.StringBuilder();
                        foreach (var r in Object.FindObjectsByType<Renderer>())
                            if (r.enabled && r.gameObject.activeInHierarchy && r.bounds.Contains(pt))
                            { var tr = r.transform; string path = tr.name; while (tr.parent) { tr = tr.parent; path = tr.name + "/" + path; } sb2.Append(path).Append(" | "); }
                        return sb2.ToString();
                    }
                case "fence":
                    {
                        var sf = it.GetComponent<StreetFence>(); if (!sf) return "no fence";
                        int n = parts.Length > 1 ? int.Parse(parts[1]) : 0; sf.Apply(n); if (parts.Length > 2 && parts[2] == "save") StreetFence.Saved = n;
                        return "fence " + StreetFence.Names[n];
                    }
                case "go": foreach (var c in it.cars) if (c.mode == 0) c.wait = 0; return "all parked cars leaving";
                case "cam":
                    {
                        var cam = GameObject.Find("Traffic test camera");
                        if (parts.Length > 1 && parts[1] == "off") { if (cam) Object.Destroy(cam); return "camera off"; }
                        if (!cam) { cam = new GameObject("Traffic test camera"); var cc = cam.AddComponent<Camera>(); cc.depth = 90; cc.fieldOfView = 55; cc.farClipPlane = 600; cam.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = true; }
                        float F(int i) => float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
                        cam.transform.position = new Vector3(F(1), F(2), F(3)); cam.transform.rotation = Quaternion.Euler(F(5), F(4), 0);
                        return "camera at " + cam.transform.position;
                    }
                case "put":
                    {
                        var p = RoundManager.Instance ? RoundManager.Instance.player : null; if (!p) return "no player";
                        float F(int i) => float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
                        p.Carry(new Vector3(F(1), 0, F(2)) - new Vector3(p.pos.x, 0, p.pos.z)); return "player at " + p.pos;
                    }
            }
            var sb = new System.Text.StringBuilder();
            foreach (var c in it.cars)
                sb.Append(c.name).Append(' ').Append(new[] { "parked", "ring", "route", "unloading", "wrecked" }[c.mode]).Append(c.reckless ? " RECKLESS" : "").Append(c.deliver ? " deliver" : "").Append(c.toPark ? " to park" : "")
                  .Append(" at ").Append(c.pos.x.ToString("0.0")).Append(',').Append(c.pos.z.ToString("0.0")).Append(" v ").Append(c.v.ToString("0.0")).Append(c.mode == 0 ? " wait " + c.wait.ToString("0") : "").Append(" | ");
            return sb.ToString();
        }
    }
}
