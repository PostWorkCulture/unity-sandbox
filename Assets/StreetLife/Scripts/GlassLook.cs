// Glass that looks like glass (16 Nov): every clear pane in every map is drawn much more see-through, and breakable
// panes are drawn thinner. It is a look only: collision boxes, bullets and what breaks are untouched.
// Runs once the map is built (and again a moment later for anything made late); each renderer is handled once.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StreetLife
{
    public class GlassLook : MonoBehaviour
    {
        const float CLEAR = .22f;    // how much of the pane's own opacity is kept
        const float THIN = .18f;     // how much of a breakable pane's thickness is kept
        static GlassLook I;
        static readonly Dictionary<Material, Material> clear = new Dictionary<Material, Material>();
        static readonly HashSet<MeshRenderer> done = new HashSet<MeshRenderer>();
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), BaseMap = Shader.PropertyToID("_BaseMap"), Smooth = Shader.PropertyToID("_Smoothness");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; Ensure(); }
        static void OnScene(Scene s, LoadSceneMode m) => Ensure();
        static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("GlassLook").AddComponent<GlassLook>();
        }
        void OnEnable() { RoundManager.RoundStarted += OnRound; }
        void OnDisable() { RoundManager.RoundStarted -= OnRound; }
        void OnRound(int r) { StartCoroutine(Late()); }
        IEnumerator Late() { yield return null; yield return null; Pass(); }
        void OnDestroy() { if (I == this) I = null; }

        IEnumerator Start()
        {
            done.Clear();
            yield return null; yield return null; Pass();
            yield return new WaitForSecondsRealtime(1.5f); Pass();
            yield return new WaitForSecondsRealtime(4f); Pass();
        }

        /// is this a clear, untextured, light-coloured see-through material (a window, not smoke, water or a glow)?
        static bool IsGlass(Material m)
        {
            if (!m || m.renderQueue < 3000 || !m.HasProperty(BaseColor)) return false;
            if (m.HasProperty(BaseMap) && m.GetTexture(BaseMap)) return false;
            if (m.IsKeywordEnabled("_EMISSION")) return false;
            var c = m.GetColor(BaseColor);
            return c.a > .2f && c.a < .92f && c.maxColorComponent > .55f;
        }

        static Material Clear(Material m)
        {
            if (clear.TryGetValue(m, out var c) && c) return c;
            c = new Material(m) { name = m.name + " (clear)" };
            var col = c.GetColor(BaseColor); col.a = Mathf.Max(.05f, col.a * CLEAR); c.SetColor(BaseColor, col);
            if (c.HasProperty(Smooth)) c.SetFloat(Smooth, .96f);
            clear[m] = c; return c;
        }

        void Pass()
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                if (!r || !done.Add(r)) continue;
                var m = r.sharedMaterial; if (!IsGlass(m)) continue;
                var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null; if (!mesh) continue;
                var size = mesh.bounds.size;
                if (size.y < .3f && size.x * size.z > 150f) continue;   // a wide flat sheet is water or a floor, not a window
                r.sharedMaterial = Clear(m);
                // a single pane with its own transform: draw it thinner, about its own middle
                int ax = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
                float thin = size[ax], next = ax == 0 ? Mathf.Min(size.y, size.z) : ax == 1 ? Mathf.Min(size.x, size.z) : Mathf.Min(size.x, size.y);
                if (thin <= 0 || thin > .3f || next < thin * 3 || r.isPartOfStaticBatch) continue;
                var t = r.transform; var ls = t.localScale; var mid = mesh.bounds.center;
                var shift = Vector3.zero; shift[ax] = mid[ax] * ls[ax] * (1 - THIN);
                t.localPosition += t.localRotation * shift;
                ls[ax] *= THIN; t.localScale = ls;
                Destruction.Rehome(t);   // or the round reset would put the old thickness back
            }
        }
    }
}
