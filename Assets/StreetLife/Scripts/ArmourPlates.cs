// Armour plates (match chat), from the web game's placePlates / movePlate / updPlates:
//  - A few pickups (+50 armour, up to 100) on the hardest places to get to: the highest flat spots you can actually
//    reach from the spawns by walking, climbing, dropping and jumping (the bots' nav grid, main area only), scored by
//    height plus a little for distance from the spawns.
//  - 3 on Street Life and Railed, 2 elsewhere, kept about 30% of the map's width apart (less if the map can't fit them).
//  - A taken plate comes back 60 s later somewhere else in the pool, away from the other plates, where it was and
//    anyone standing nearby. Every round start shuffles them to fresh spots.
//  - Duels only (no armour in Rumble). Bots pick them up when they walk over one, as on the web.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(75)]
    public class ArmourPlates : MonoBehaviour
    {
        public static ArmourPlates I;
        public const float BACK = 60, GIVE = 50, MAX = 100;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();   // RoundManager.Reboot runs this again after every scene reload
        static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("ArmourPlates").AddComponent<ArmourPlates>();
        }

        class Plate { public Vector3 p; public float back; public bool move; public Transform root, spin; }
        readonly List<Plate> plates = new List<Plate>();
        readonly List<Vector3> pool = new List<Vector3>();
        float gap = 12; bool built; string builtFor;
        public static IEnumerable<Vector3> Showing { get { if (I) foreach (var P in I.plates) if (P.back <= 0 && P.root.gameObject.activeSelf) yield return P.p; } }

        void OnEnable() { RoundManager.RoundStarted += OnRound; }
        void OnDisable() { RoundManager.RoundStarted -= OnRound; if (I == this) I = null; }

        bool On => !Rules.Rumble;

        void OnRound(int r)
        {
            if (!built || builtFor != MapSet.Current) Build();
            foreach (var P in plates) { P.back = 0; P.move = false; P.p = new Vector3(1e5f, 0, 1e5f); }
            foreach (var P in plates) { Move(P, false); P.root.gameObject.SetActive(On); }
        }

        // ------------------------------------------------------------------ where they can go
        void Build()
        {
            foreach (var P in plates) if (P.root) Destroy(P.root.gameObject);
            plates.Clear(); pool.Clear(); built = true; builtFor = MapSet.Current;
            if (!NavGrid.Ready || NavGrid.Nodes.Count == 0) { built = false; return; }

            // flood out from the spawns along the bots' links (walk, jump, double jump, drop, ladder)
            var N = NavGrid.Nodes; var seen = new bool[N.Count]; var q = new Stack<int>();
            var spawns = BoxWorld.Spawns;
            foreach (var sp in spawns)
            {
                int id = NavGrid.Nearest(new Vector3(sp.x, sp.y, sp.z), 3f, 1.2f, true);
                if (id >= 0 && !seen[id]) { seen[id] = true; q.Push(id); }
            }
            while (q.Count > 0)
            {
                int id = q.Pop();
                foreach (var e in N[id].edges) if (e.to >= 0 && e.to < N.Count && !seen[e.to]) { seen[e.to] = true; q.Push(e.to); }
            }
            // heights by cell, for the flatness test
            var byCell = new Dictionary<long, List<float>>();
            long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            for (int i = 0; i < N.Count; i++)
            {
                var k = Key(N[i].ix, N[i].iz);
                if (!byCell.TryGetValue(k, out var l)) byCell[k] = l = new List<float>(2);
                l.Add(N[i].p.y);
            }
            bool Has(int x, int z, float h) { if (!byCell.TryGetValue(Key(x, z), out var l)) return false; foreach (var y in l) if (Mathf.Abs(y - h) < .05f) return true; return false; }

            var cand = new List<(Vector3 p, float sc)>();
            for (int i = 0; i < N.Count; i++)
            {
                if (!seen[i]) continue;
                var n = N[i]; float h = n.p.y; if (h < .9f) continue;
                if (!Has(n.ix + 1, n.iz, h) || !Has(n.ix - 1, n.iz, h) || !Has(n.ix, n.iz + 1, h) || !Has(n.ix, n.iz - 1, h)) continue;
                float dsp = 1e9f; foreach (var sp in spawns) dsp = Mathf.Min(dsp, new Vector2(sp.x - n.p.x, sp.z - n.p.z).magnitude);
                cand.Add((n.p, h + Mathf.Min(dsp, 20) * .08f));
            }
            cand.Sort((a, b) => b.sc.CompareTo(a.sc));
            string id0 = MapSet.Current; bool big = id0 == "street" || id0 == "rail" || string.IsNullOrEmpty(id0);
            int want = big ? 3 : 2, cap = big ? 40 : 20;
            foreach (var c in cand)
            {
                if (pool.Count >= cap) break;
                bool far = true; foreach (var p in pool) if (Flat(p, c.p) <= 5) { far = false; break; }
                if (far) pool.Add(c.p);
            }
            if (pool.Count == 0) return;
            var bb = BoxWorld.Bounds;
            gap = Mathf.Max(8, .3f * Mathf.Max(bb.x1 - bb.x0, bb.z1 - bb.z0));
            while (gap > 4 && !Fits(gap, want)) gap *= .85f;
            for (int i = 0; i < want; i++) { var root = MakeVisual(out var spin); plates.Add(new Plate { root = root, spin = spin, back = 0, p = new Vector3(1e5f, 0, 1e5f) }); }
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        bool Fits(float g, int want)
        {
            var pick = new List<Vector3>();
            foreach (var c in pool) { bool ok = true; foreach (var p in pick) if (Flat(p, c) <= g) { ok = false; break; } if (ok) pick.Add(c); }
            return pick.Count >= want;
        }

        void Move(Plate P, bool keepAway)
        {
            if (pool.Count == 0) return;
            for (float g = gap; g > 2; g *= .8f)
            {
                var ok = new List<Vector3>();
                foreach (var c in pool)
                {
                    bool good = true;
                    foreach (var o in plates) if (o != P && Flat(o.p, c) <= g) { good = false; break; }
                    if (!good) continue;
                    if (keepAway && Flat(P.p, c) <= g) continue;
                    foreach (var f in Combat.Fighters) if (f && f.alive && Flat(f.pos, c) < 5) { good = false; break; }
                    if (good) ok.Add(c);
                }
                if (ok.Count > 0) { P.p = ok[Random.Range(0, ok.Count)]; P.root.position = P.p + Vector3.up * .9f; return; }
            }
        }

        // ------------------------------------------------------------------ pickups
        void Update()
        {
            var rm = RoundManager.Instance;
            if (!built && rm && NavGrid.Ready) OnRound(rm.round);
            float dt = Time.deltaTime;
            foreach (var P in plates)
            {
                if (!On) { if (P.root.gameObject.activeSelf) P.root.gameObject.SetActive(false); continue; }
                if (P.back > 0)
                {
                    P.back -= dt;
                    if (P.back <= 0) { if (P.move) { P.move = false; Move(P, true); } P.root.gameObject.SetActive(true); }
                    continue;
                }
                if (!P.root.gameObject.activeSelf) P.root.gameObject.SetActive(true);
                P.spin.localRotation = Quaternion.Euler(0, Time.time * 2 * Mathf.Rad2Deg, 0);
                P.root.position = P.p + Vector3.up * (.9f + Mathf.Sin(Time.time * 2.5f) * .12f);
                if (!rm || rm.state != RoundManager.State.Fight) continue;
                foreach (var f in Combat.Fighters)
                {
                    if (!f || !f.alive || f.downed || f.InFinisher || f.armor >= MAX) continue;
                    if (Flat(f.pos, P.p) < 1.1f && Mathf.Abs(f.pos.y - P.p.y) < 1.6f)
                    {
                        f.armor = Mathf.Min(MAX, f.armor + GIVE);
                        P.back = BACK; P.move = true; P.root.gameObject.SetActive(false);
                        GunFx.Emit(P.p + Vector3.up * .9f, GunFx.E(24, 4, .16f, .5f, 0, "#2f7fe0", "#bfe3ff", "#ffffff"));
                        Sfx.Play("plate", f.pos, 1, 10);
                        if (f == Combat.Me && rm) rm.feed.Add(new RoundManager.FeedItem { text = "You  picked up armour", t = Time.time, mine = true });
                        break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ the look: a blue metal plate with a light strip, over a glowing ring
        static Material plateMat, stripMat, ringMat; static Mesh boxMesh, ringMesh;

        Transform MakeVisual(out Transform spinT)
        {
            if (!plateMat)
            {
                plateMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "armour plate" };
                plateMat.SetColor("_BaseColor", Web.Hex("#2f7fe0")); plateMat.SetFloat("_Metallic", .6f); plateMat.SetFloat("_Smoothness", .7f);
                plateMat.EnableKeyword("_EMISSION"); plateMat.SetColor("_EmissionColor", Web.Hex("#1f5fd0") * .35f);
                plateMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                stripMat = GunFx.MakeMat(false, null, true); stripMat.SetColor("_Color", Web.Hex("#bfe3ff") * 1.6f);
                ringMat = GunFx.MakeMat(false, null); var rc = Web.Hex("#7fc4ff"); rc.a = .7f; ringMat.SetColor("_Color", rc);
                boxMesh = Box(); ringMesh = Torus(.55f, .04f, 32, 8);
            }
            var root = new GameObject("ArmourPlate").transform; root.SetParent(transform, false);
            var spin = new GameObject("spin").transform; spin.SetParent(root, false);
            Part(spin, boxMesh, plateMat, Vector3.zero, new Vector3(.62f, .78f, .12f), true);
            Part(spin, boxMesh, stripMat, new Vector3(0, .18f, 0), new Vector3(.5f, .08f, .14f), false);
            Part(root, ringMesh, ringMat, new Vector3(0, -.55f, 0), Vector3.one, false);
            root.gameObject.SetActive(false);
            root.position = new Vector3(1e5f, 0, 1e5f);
            spinT = spin;
            return root;
        }

        static void Part(Transform parent, Mesh mesh, Material mat, Vector3 at, Vector3 scale, bool shadow)
        {
            var go = new GameObject("part"); go.transform.SetParent(parent, false);
            go.transform.localPosition = at; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Mesh Box()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var m = Instantiate(tmp.GetComponent<MeshFilter>().sharedMesh); Destroy(tmp);
            var c = new Color[m.vertexCount]; for (int i = 0; i < c.Length; i++) c[i] = Color.white; m.colors = c;
            return m;
        }

        static Mesh Torus(float R, float r, int seg, int side)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>(); var col = new List<Color>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2; var c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j <= side; j++)
                {
                    float b = j / (float)side * Mathf.PI * 2; var d = c * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(c * R + d * r); n.Add(d); col.Add(Color.white);
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a = i * (side + 1) + j, b = a + side + 1;
                    t.Add(a); t.Add(b); t.Add(a + 1); t.Add(a + 1); t.Add(b); t.Add(b + 1);
                }
            var m = new Mesh { name = "plate ring" }; m.SetVertices(v); m.SetNormals(n); m.SetColors(col); m.SetTriangles(t, 0);
            m.RecalculateBounds(); return m;
        }

        /// call ArmourPlates.Test [state|goto N]
        public static string Test(string a)
        {
            if (!I) return "no ArmourPlates";
            var p = (a ?? "").Trim().Split(' ');
            if (p[0] == "goto")
            {
                int i = p.Length > 1 ? int.Parse(p[1]) : 0; if (i >= I.plates.Count) return "no plate " + i;
                var me = Combat.Me; if (!me) return "no player"; var P = I.plates[i];
                if (p.Length > 2 && p[2] == "near") { me.armor = 100; me.Place(P.p + new Vector3(0, .05f, -3.2f), 0); return $"player 3 m from plate {i}"; }
                me.armor = 0; me.Place(P.p + new Vector3(0, .05f, 0), me.yaw); return $"player put on plate {i} at {P.p}";
            }
            var sb = new System.Text.StringBuilder($"map {I.builtFor} pool {I.pool.Count} gap {I.gap:F1} on {I.On}: ");
            foreach (var P in I.plates) sb.Append($"[{P.p.x:F1},{P.p.y:F1},{P.p.z:F1} back {P.back:F0} shown {P.root.gameObject.activeSelf}] ");
            if (Combat.Me) sb.Append($" player armour {Combat.Me.armor:F0}");
            return sb.ToString();
        }
    }
}
