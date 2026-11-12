// Bot navigation, like the web game's nav grid: 0.5 m cells, several walkable floors per cell (ground, floors, stairs,
// roofs), links for walking, stepping, jumping up (single and double jump), dropping down and climbing ladders, and A*.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public enum EdgeKind : byte { Walk, Jump, DoubleJump, Drop, Ladder }

    public struct NavEdge { public int to; public EdgeKind kind; public float cost; public int ladder; }

    public class NavNode
    {
        public int ix, iz, id, comp = -1;
        public Vector3 p;
        public readonly List<NavEdge> edges = new List<NavEdge>(8);
    }

    public static class NavGrid
    {
        public const float CS = .5f, CLR = .47f, NAV_H = 2.25f;
        const float JUMP_UP = 1.9f, DJUMP_UP = 3.4f, MAX_DROP = 7f;

        public static readonly List<NavNode> Nodes = new List<NavNode>();
        public static int MainComp = -1;
        public static bool Ready;
        static List<int>[] cols;

        // Boxes that move, shrink or vanish during a round (cars, glass, furniture, trees...; set by Destruction before Build).
        // The grid is built as if they weren't there; nodes they currently sit on are 'blocked' and A* goes round them.
        // Destruction calls Refresh(area) whenever one changes, like the web game's dirtyGrid.
        public static readonly HashSet<int> Dynamic = new HashSet<int>();
        static bool[] blocked;
        public static int Version;   // bumps on every refresh, so bots know to repath
        public static bool Blocked(int n) => blocked != null && n >= 0 && n < blocked.Length && blocked[n];
        static int W, H; static float X0, Z0;

        static int Col(int ix, int iz) => ix + iz * W;
        static bool InGrid(int ix, int iz) => ix >= 0 && iz >= 0 && ix < W && iz < H;

        public static void Build()
        {
            Nodes.Clear(); Ready = false;
            var bd = BoxWorld.Bounds; var B = BoxWorld.Boxes;
            X0 = bd.x0; Z0 = bd.z0;
            W = Mathf.CeilToInt((bd.x1 - bd.x0) / CS); H = Mathf.CeilToInt((bd.z1 - bd.z0) / CS);
            cols = new List<int>[W * H];

            // coarse buckets of boxes (4 m) so each column only checks nearby boxes
            const float BK = 4f; int bw = Mathf.CeilToInt((bd.x1 - bd.x0) / BK) + 1, bh = Mathf.CeilToInt((bd.z1 - bd.z0) / BK) + 1;
            var bucket = new List<int>[bw * bh];
            for (int i = 0; i < B.Count; i++)
            {
                if (Dynamic.Contains(i)) continue;
                var b = B[i];
                int a0 = Mathf.Clamp(Mathf.FloorToInt((b.x0 - CLR - X0) / BK), 0, bw - 1), a1 = Mathf.Clamp(Mathf.FloorToInt((b.x1 + CLR - X0) / BK), 0, bw - 1);
                int c0 = Mathf.Clamp(Mathf.FloorToInt((b.z0 - CLR - Z0) / BK), 0, bh - 1), c1 = Mathf.Clamp(Mathf.FloorToInt((b.z1 + CLR - Z0) / BK), 0, bh - 1);
                for (int x = a0; x <= a1; x++) for (int z = c0; z <= c1; z++) (bucket[x + z * bw] ??= new List<int>()).Add(i);
            }

            var cand = new List<float>(8);
            for (int iz = 0; iz < H; iz++)
                for (int ix = 0; ix < W; ix++)
                {
                    float cx = X0 + (ix + .5f) * CS, cz = Z0 + (iz + .5f) * CS;
                    if (cx < bd.x0 + Fighter.R || cx > bd.x1 - Fighter.R || cz < bd.z0 + Fighter.R || cz > bd.z1 - Fighter.R) continue;
                    var near = bucket[Mathf.Clamp(Mathf.FloorToInt((cx - X0) / BK), 0, bw - 1) + Mathf.Clamp(Mathf.FloorToInt((cz - Z0) / BK), 0, bh - 1) * bw];
                    cand.Clear(); cand.Add(0);
                    if (near != null) foreach (var i in near) { var b = B[i]; if (b.x0 <= cx && b.x1 >= cx && b.z0 <= cz && b.z1 >= cz && b.y1 > .05f) cand.Add(b.y1); }
                    cand.Sort();
                    float last = -99;
                    foreach (var y in cand)
                    {
                        if (y - last < .3f) continue;
                        bool clear = true;
                        if (near != null)
                            foreach (var i in near)
                            {
                                var b = B[i];
                                if (b.x0 < cx + CLR && b.x1 > cx - CLR && b.z0 < cz + CLR && b.z1 > cz - CLR && b.y1 > y + Fighter.STEP + .01f && b.y0 < y + NAV_H) { clear = false; break; }   // a step you can walk up isn't in the way
                            }
                        if (!clear) continue;
                        var n = new NavNode { ix = ix, iz = iz, id = Nodes.Count, p = new Vector3(cx, y, cz) };
                        Nodes.Add(n); (cols[Col(ix, iz)] ??= new List<int>(2)).Add(n.id);
                        last = y;
                    }
                }

            // links to the 8 neighbouring cells
            foreach (var n in Nodes)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int jx = n.ix + dx, jz = n.iz + dz;
                        if (!InGrid(jx, jz) || cols[Col(jx, jz)] == null) continue;
                        bool diag = dx != 0 && dz != 0;
                        float d = diag ? CS * 1.4142f : CS;
                        foreach (var mi in cols[Col(jx, jz)])
                        {
                            var m = Nodes[mi]; float dy = m.p.y - n.p.y;
                            EdgeKind k; float cost;
                            if (Mathf.Abs(dy) <= Fighter.STEP + .01f || (Mathf.Abs(dy) <= 2 * Fighter.STEP + .02f && StairBetween(n, m))) { k = EdgeKind.Walk; cost = d * (1 + Mathf.Abs(dy)); }
                            else if (dy > 0 && dy <= JUMP_UP) { k = EdgeKind.Jump; cost = d + 1.5f; }
                            else if (dy > 0 && dy <= DJUMP_UP) { k = EdgeKind.DoubleJump; cost = d + 3f; }
                            else if (dy < 0 && dy >= -MAX_DROP) { k = EdgeKind.Drop; cost = d + .25f * -dy; }
                            else continue;
                            if (diag && !(HasNear(n.ix + dx, n.iz, n.p.y, m.p.y) && HasNear(n.ix, n.iz + dz, n.p.y, m.p.y))) continue;
                            if (k != EdgeKind.Walk && diag) continue;          // jumps and drops only straight across
                            if (k == EdgeKind.Jump || k == EdgeKind.DoubleJump) { if (!HeadroomAbove(n, m.p.y - NAV_H + 1.2f)) continue; }
                            if (k == EdgeKind.Drop && !HeadroomAbove(m, n.p.y + .5f - NAV_H)) continue;   // the fall must be open all the way down
                            n.edges.Add(new NavEdge { to = m.id, kind = k, cost = cost, ladder = -1 });
                        }
                    }

            // ladders: foot of the ladder <-> where you step off at the top
            for (int li = 0; li < BoxWorld.Ladders.Count; li++)
            {
                var L = BoxWorld.Ladders[li];
                bool ax = L.nx != 0;
                float cx = (L.x0 + L.x1) / 2, cz = (L.z0 + L.z1) / 2;
                float fx = ax ? L.rung - L.nx * .9f : cx, fz = ax ? cz : L.rung - L.nz * .9f;
                float tx = (ax ? L.rung : cx) + L.nx * 1.0f, tz = (ax ? cz : L.rung) + L.nz * 1.0f;
                int a = Nearest(new Vector3(fx, L.y0, fz), 1.5f, .6f, false), b = Nearest(new Vector3(tx, L.y1, tz), 1.5f, .6f, false);
                if (a < 0 || b < 0) continue;
                float cost = (L.y1 - L.y0) * 1.4f + 1.5f;
                Nodes[a].edges.Add(new NavEdge { to = b, kind = EdgeKind.Ladder, cost = cost, ladder = li });
                Nodes[b].edges.Add(new NavEdge { to = a, kind = EdgeKind.Ladder, cost = cost, ladder = li });
            }

            // connected areas (counting every link as two-way); the biggest is where everyone plays
            int comp = 0, best = -1, bestN = 0; var stack = new Stack<int>();
            var rev = new List<int>[Nodes.Count];
            foreach (var n in Nodes) foreach (var e in n.edges) (rev[e.to] ??= new List<int>()).Add(n.id);
            foreach (var n in Nodes)
            {
                if (n.comp >= 0) continue;
                int cnt = 0; stack.Push(n.id); n.comp = comp;
                while (stack.Count > 0)
                {
                    var c = Nodes[stack.Pop()]; cnt++;
                    foreach (var e in c.edges) if (Nodes[e.to].comp < 0) { Nodes[e.to].comp = comp; stack.Push(e.to); }
                    if (rev[c.id] != null) foreach (var r in rev[c.id]) if (Nodes[r].comp < 0) { Nodes[r].comp = comp; stack.Push(r); }
                }
                if (cnt > bestN) { bestN = cnt; best = comp; }
                comp++;
            }
            MainComp = best;
            blocked = new bool[Nodes.Count];
            foreach (var i in Dynamic) if (i >= 0 && i < B.Count) { var b = B[i]; Refresh(b.x0, b.x1, b.z0, b.z1); }
            Ready = true;
            Debug.Log($"Street Life nav grid: {Nodes.Count} walkable spots, {BoxWorld.Ladders.Count} ladders, main area {bestN}");
        }

        static bool HasNear(int ix, int iz, float y0, float y1)
        {
            if (!InGrid(ix, iz) || cols[Col(ix, iz)] == null) return false;
            float lo = Mathf.Min(y0, y1) - Fighter.STEP, hi = Mathf.Max(y0, y1) + Fighter.STEP;
            foreach (var i in cols[Col(ix, iz)]) { float y = Nodes[i].p.y; if (y >= lo && y <= hi) return true; }
            return false;
        }

        /// A step between two cell heights (stairs whose treads are narrower than a cell): some box top lies between them.
        static bool StairBetween(NavNode a, NavNode b)
        {
            var lo = a.p.y < b.p.y ? a : b; var hi = lo == a ? b : a;
            float mx = (a.p.x + b.p.x) / 2, mz = (a.p.z + b.p.z) / 2;
            for (int i = 0; i < BoxWorld.Boxes.Count; i++)
                { if (Dynamic.Contains(i)) continue; var x = BoxWorld.Boxes[i]; if (x.x0 <= mx + .2f && x.x1 >= mx - .2f && x.z0 <= mz + .2f && x.z1 >= mz - .2f && x.y1 - lo.p.y > .05f && x.y1 - lo.p.y <= Fighter.STEP + .01f && hi.p.y - x.y1 <= Fighter.STEP + .01f) return true; }
            return false;
        }

        static bool HeadroomAbove(NavNode n, float topY)
        {
            float need = topY + NAV_H;
            for (int i = 0; i < BoxWorld.Boxes.Count; i++)
                { if (Dynamic.Contains(i)) continue; var b = BoxWorld.Boxes[i]; if (b.x0 < n.p.x + CLR && b.x1 > n.p.x - CLR && b.z0 < n.p.z + CLR && b.z1 > n.p.z - CLR && b.y0 > n.p.y + .1f && b.y0 < need) return false; }
            return true;
        }

        /// Re-check which nodes in an area (x0..x1, z0..z1, world metres) are covered by a dynamic box right now.
        public static void Refresh(float x0, float x1, float z0, float z1)
        {
            if (cols == null || blocked == null) return;
            x0 -= CLR; x1 += CLR; z0 -= CLR; z1 += CLR;
            var B = BoxWorld.Boxes; near.Clear();
            foreach (var i in Dynamic) { if (i < 0 || i >= B.Count) continue; var b = B[i]; if (b.x1 > x0 - CLR && b.x0 < x1 + CLR && b.z1 > z0 - CLR && b.z0 < z1 + CLR) near.Add(i); }
            int a0 = Mathf.Max(0, Mathf.FloorToInt((x0 - X0) / CS)), a1 = Mathf.Min(W - 1, Mathf.FloorToInt((x1 - X0) / CS));
            int c0 = Mathf.Max(0, Mathf.FloorToInt((z0 - Z0) / CS)), c1 = Mathf.Min(H - 1, Mathf.FloorToInt((z1 - Z0) / CS));
            for (int iz = c0; iz <= c1; iz++)
                for (int ix = a0; ix <= a1; ix++)
                {
                    var c = cols[Col(ix, iz)]; if (c == null) continue;
                    foreach (var ni in c)
                    {
                        var n = Nodes[ni]; bool bl = false;
                        foreach (var i in near)
                        {
                            var b = B[i];
                            if (b.x0 < n.p.x + CLR && b.x1 > n.p.x - CLR && b.z0 < n.p.z + CLR && b.z1 > n.p.z - CLR && b.y1 > n.p.y + Fighter.STEP + .01f && b.y0 < n.p.y + NAV_H) { bl = true; break; }
                        }
                        blocked[ni] = bl;
                    }
                }
            Version++;
        }
        public static void Refresh(Box b) => Refresh(b.x0, b.x1, b.z0, b.z1);
        public static void RefreshAll() { var B = BoxWorld.Boxes; foreach (var i in Dynamic) if (i >= 0 && i < B.Count) Refresh(B[i]); }
        static readonly List<int> near = new List<int>();

        /// Nearest walkable spot to p (prefers the floor you're standing on).
        public static int Nearest(Vector3 p, float radius = 3f, float maxUp = 1.2f, bool mainOnly = true)
        {
            if (cols == null) return -1;
            int cx = Mathf.FloorToInt((p.x - X0) / CS), cz = Mathf.FloorToInt((p.z - Z0) / CS), r = Mathf.CeilToInt(radius / CS);
            int best = -1; float bv = float.MaxValue;
            for (int z = cz - r; z <= cz + r; z++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (!InGrid(x, z) || cols[Col(x, z)] == null) continue;
                    foreach (var i in cols[Col(x, z)])
                    {
                        var n = Nodes[i]; if (mainOnly && n.comp != MainComp) continue;
                        if (blocked != null && blocked[i]) continue;
                        float dy = n.p.y - p.y; if (dy > maxUp) continue;
                        float v = new Vector2(n.p.x - p.x, n.p.z - p.z).magnitude + Mathf.Abs(dy) * 2f;
                        if (v < bv) { bv = v; best = i; }
                    }
                }
            return best;
        }

        public static NavNode RandomNode(System.Random rng = null)
        {
            if (Nodes.Count == 0) return null;
            for (int t = 0; t < 200; t++)
            {
                var n = Nodes[rng != null ? rng.Next(Nodes.Count) : Random.Range(0, Nodes.Count)];
                if (n.comp == MainComp && !Blocked(n.id)) return n;
            }
            return Nodes[0];
        }

        // ------------------------------------------------------------------ A*
        static float[] g; static int[] came; static int[] seen; static int stamp;
        static readonly List<(float f, int n)> heap = new List<(float, int)>();

        public static List<int> FindPath(int from, int to, int maxExpand = 40000)
        {
            if (from < 0 || to < 0) return null;
            int N = Nodes.Count;
            if (g == null || g.Length != N) { g = new float[N]; came = new int[N]; seen = new int[N]; stamp = 0; }
            stamp++; heap.Clear();
            g[from] = 0; came[from] = -1; seen[from] = stamp; Push(Heur(from, to), from);
            int expanded = 0;
            while (heap.Count > 0 && expanded++ < maxExpand)
            {
                var (f, c) = Pop();
                if (c == to) break;
                if (f - Heur(c, to) > g[c] + 1e-3f) continue;   // stale entry
                foreach (var e in Nodes[c].edges)
                {
                    if (blocked != null && blocked[e.to] && e.to != to) continue;   // a car, a pane of glass, a table in the way right now
                    float ng = g[c] + e.cost;
                    if (seen[e.to] == stamp && ng >= g[e.to]) continue;
                    seen[e.to] = stamp; g[e.to] = ng; came[e.to] = c; Push(ng + Heur(e.to, to), e.to);
                }
            }
            if (seen[to] != stamp) return null;
            var path = new List<int>();
            for (int c = to; c >= 0; c = came[c]) { path.Add(c); if (c == from) break; }
            path.Reverse();
            return path;
        }

        public static EdgeKind EdgeBetween(int a, int b, out int ladder)
        {
            ladder = -1;
            foreach (var e in Nodes[a].edges) if (e.to == b) { ladder = e.ladder; return e.kind; }
            return EdgeKind.Walk;
        }

        static float Heur(int a, int b) => Vector3.Distance(Nodes[a].p, Nodes[b].p);
        static void Push(float f, int n)
        {
            heap.Add((f, n)); int i = heap.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (heap[p].f <= heap[i].f) break; (heap[p], heap[i]) = (heap[i], heap[p]); i = p; }
        }
        static (float, int) Pop()
        {
            var top = heap[0]; var last = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
            if (heap.Count > 0)
            {
                heap[0] = last; int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < heap.Count && heap[l].f < heap[m].f) m = l;
                    if (r < heap.Count && heap[r].f < heap[m].f) m = r;
                    if (m == i) break;
                    (heap[m], heap[i]) = (heap[i], heap[m]); i = m;
                }
            }
            return top;
        }

        /// Clear line of sight between two points (boxes block it; the web game's los).
        public static bool Los(Vector3 a, Vector3 b)
        {
            var d = b - a; float l = d.magnitude; if (l < 1e-3f) return true;
            float t = BoxWorld.Ray(a, d / l, l);
            return t < 0 || t >= l - .05f;
        }
    }
}
