// First-person gun polish (match chat), the two things the web game's view model has that the port left out:
//  - the toon outline round the gun and hands (heroOutline(g, .0025): back faces pushed out along smoothed normals);
//  - the live round counter on the receiver (gunCtr: two digits, cyan, red on the last 5 rounds).
// Works on the weapons chat's view models without changing ViewModel.cs: it finds each "vm <gun>" object under the
// view-model camera the first time it appears and adds to it.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(210)]   // after ViewModel (200) has placed the gun this frame
    public class ViewModelPolish : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();   // RoundManager.Reboot runs this again after every scene reload
        static void Ensure()
        {
            if (!Application.isPlaying || FindAnyObjectByType<ViewModelPolish>()) return;
            new GameObject("View model polish").AddComponent<ViewModelPolish>();
        }

        class Gun { public Transform root; public string id; public List<Material> counters = new List<Material>(); public Texture2D tex; public int shown = -999; }
        readonly Dictionary<Transform, Gun> guns = new Dictionary<Transform, Gun>();
        static readonly Dictionary<Mesh, Mesh> outlineMeshes = new Dictionary<Mesh, Mesh>();
        Material outlineMat; ViewModel vm; float scanT;

        void LateUpdate()
        {
            if (!vm) { var cam = Camera.main; vm = cam ? cam.GetComponent<ViewModel>() : null; if (!vm) return; }
            scanT -= Time.unscaledDeltaTime;
            if (scanT <= 0)
            {
                scanT = .25f;
                var vmCam = vm.transform.Find("View model camera");
                if (vmCam)
                    foreach (Transform t in vmCam)
                        if (t.name.StartsWith("vm ") && !guns.ContainsKey(t)) guns[t] = Polish(t);
            }
            var w = vm.weapons;
            if (!w) return;
            foreach (var g in guns.Values)
            {
                if (g == null || !g.root || !g.root.gameObject.activeInHierarchy || g.counters.Count == 0) continue;
                int n = w.Ammo(g.id);
                var st = GunsDb.Gun(g.id); if (st != null && st.InfiniteMag) n = 99;
                if (n != g.shown) { g.shown = n; DrawCounter(g.tex, n); }
            }
        }

        Gun Polish(Transform root)
        {
            var g = new Gun { root = root, id = root.name.Substring(3) };
            if (!outlineMat)
            {
                var m = Resources.Load<Material>("StreetLife/Outline");
                outlineMat = m ? new Material(m) : null;
            }
            var rends = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in rends)
            {
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var mesh = mf.sharedMesh; var sz = mesh.bounds.size;
                bool flat = Mathf.Min(sz.x, Mathf.Min(sz.y, sz.z)) < 1e-4f;
                bool transparent = r.sharedMaterial && r.sharedMaterial.renderQueue >= 3000;
                if (flat && mesh.vertexCount <= 8 && IsCounter(sz)) { MakeCounter(g, r); continue; }
                if (flat || transparent || r.name.StartsWith("flash") || !outlineMat || !mesh.isReadable) continue;
                var go = new GameObject("outline"); go.layer = r.gameObject.layer;
                go.transform.SetParent(r.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = OutlineMesh(mesh);
                var omr = go.AddComponent<MeshRenderer>(); omr.sharedMaterial = outlineMat;
                omr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; omr.receiveShadows = false;
            }
            return g;
        }

        // the counter display is a 0.05 x 0.026 m plane on the receiver
        static bool IsCounter(Vector3 sz)
        {
            var s = new List<float> { sz.x, sz.y, sz.z }; s.Sort();
            return Mathf.Abs(s[1] - .026f) < .006f && Mathf.Abs(s[2] - .05f) < .01f;
        }

        void MakeCounter(Gun g, MeshRenderer r)
        {
            if (!g.tex) { g.tex = new Texture2D(128, 64, TextureFormat.RGBA32, false) { name = "round counter " + g.id, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear }; }
            var m = GunFx.MakeMat(false, g.tex, true); m.SetColor("_Color", new Color(1.6f, 1.6f, 1.6f, 1));
            r.sharedMaterial = m; g.counters.Add(m);
        }

        /// copies the mesh with each vertex's normal averaged over every vertex at the same spot (UV3), so hard-edged
        /// boxes get a closed outline instead of split faces
        static Mesh OutlineMesh(Mesh src)
        {
            if (outlineMeshes.TryGetValue(src, out var o) && o) return o;
            o = Instantiate(src); o.name = src.name + " outline";
            var v = src.vertices; var n = src.normals;
            var sum = new Dictionary<Vector3Int, Vector3>();
            Vector3Int K(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e4f), Mathf.RoundToInt(p.y * 1e4f), Mathf.RoundToInt(p.z * 1e4f));
            for (int i = 0; i < v.Length; i++) { var k = K(v[i]); sum.TryGetValue(k, out var a); sum[k] = a + (n.Length > i ? n[i] : Vector3.zero); }
            var sm = new List<Vector3>(v.Length);
            for (int i = 0; i < v.Length; i++) sm.Add(sum[K(v[i])].normalized);
            o.SetUVs(3, sm);
            outlineMeshes[src] = o; return o;
        }

        // ------------------------------------------------------------------ the counter: two seven-segment digits
        static readonly Color Bg = new Color(.016f, .027f, .04f, 1), Cyan = Web.Hex("#7ff6ff"), Red = Web.Hex("#ff5a4a");
        // segments a..g as rectangles in a 22 x 40 digit cell (x, y from the bottom left, w, h)
        static readonly int[][] Seg =
        {
            new[] { 3, 34, 16, 5 }, new[] { 17, 20, 5, 16 }, new[] { 17, 3, 5, 16 }, new[] { 3, 0, 16, 5 },
            new[] { 0, 3, 5, 16 }, new[] { 0, 20, 5, 16 }, new[] { 3, 17, 16, 5 }
        };
        static readonly string[] Digit = { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg" };
        static Color32[] px;

        static void DrawCounter(Texture2D t, int n)
        {
            if (!t) return;
            if (px == null) px = new Color32[128 * 64];
            Color32 bg = Bg; for (int i = 0; i < px.Length; i++) px[i] = bg;
            n = Mathf.Clamp(n, 0, 99);
            Color32 c = n <= 5 ? Red : Cyan, dim = Color.Lerp(Bg, n <= 5 ? Red : Cyan, .08f);
            int[] d = { n / 10, n % 10 };
            for (int k = 0; k < 2; k++)
            {
                int ox = 30 + k * 40, oy = 12;
                for (int s = 0; s < 7; s++)
                {
                    bool on = Digit[d[k]].IndexOf((char)('a' + s)) >= 0;
                    var r = Seg[s];
                    for (int y = r[1]; y < r[1] + r[3]; y++) for (int x = r[0]; x < r[0] + r[2]; x++)
                        {
                            int X = ox + x, Y = oy + y; if (X < 0 || X >= 128 || Y < 0 || Y >= 64) continue;
                            px[Y * 128 + X] = on ? c : dim;
                        }
                }
            }
            t.SetPixels32(px); t.Apply(false);
        }
    }
}
