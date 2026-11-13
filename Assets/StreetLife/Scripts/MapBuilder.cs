// Builds the Street Life map from the exported mesh data, loads its collision boxes, and adds its lamps.
// Runs in edit mode too, so the map shows in the Scene view (the generated objects are never saved into the scene).
using UnityEngine;

namespace StreetLife
{
    [ExecuteAlways]
    public class MapBuilder : MonoBehaviour
    {
        public MaterialLibrary library;
        public TextAsset mapJson;
        public TextAsset mapBin;
        [Tooltip("Things that break or move (dyn.json / dyn.bytes). Optional.")]
        public TextAsset dynJson;
        public TextAsset dynBin;

        const string GenName = "__Generated";
        Transform gen;

        void OnEnable()
        {
            MapSet.Apply(this);   // maps chat: swap in the chosen map's data (play mode only)
            if (!mapJson || !mapBin || !library) return;
            var w = JsonUtility.FromJson<WorldJson>(mapJson.text);
            BoxWorld.Load(w);
            var old = transform.Find(GenName);
            if (old) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
            Build(w);
        }

        void OnDisable()
        {
            if (gen && !Application.isPlaying)
            {
                foreach (var mf in gen.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) DestroyImmediate(mf.sharedMesh);
                foreach (var mr in gen.GetComponentsInChildren<MeshRenderer>(true)) if (mr.sharedMaterial && mr.sharedMaterial.name.StartsWith("line ")) DestroyImmediate(mr.sharedMaterial);
                DestroyImmediate(gen.gameObject);
            }
        }

        void Build(WorldJson w)
        {
            gen = new GameObject(GenName).transform;
            gen.SetParent(transform, false);
            if (!Application.isPlaying) gen.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.NotEditable;
            var bin = mapBin.bytes;
            var geo = new GameObject("Geometry").transform; geo.SetParent(gen, false);
            for (int i = 0; i < w.meshes.Length; i++)
            {
                var wm = w.meshes[i];
                var go = new GameObject("mesh" + i + "_m" + wm.mat);
                go.transform.SetParent(geo, false);
                var sm = MeshIO.Build(bin, wm, go.name);
                go.AddComponent<MeshFilter>().sharedMesh = sm;
                if (Application.isPlaying) sm.UploadMeshData(true);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = wm.mat >= 0 && wm.mat < library.map.Length ? library.map[wm.mat] : library.unlitBase;
                var m = mr.sharedMaterial;
                bool transparent = m && m.renderQueue >= 3000;
                mr.shadowCastingMode = wm.cast && !transparent ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            BuildLines(w);
            BuildLamps(w);
            BuildDynamic();
        }

        // Each breakable / moving thing gets its own object (own transform, own meshes) so it can be shoved, toppled or hidden.
        void BuildDynamic()
        {
            if (!dynJson || !dynBin) return;
            var dj = JsonUtility.FromJson<DynJson>(dynJson.text);
            if (dj == null || dj.entities == null) return;
            var bin = dynBin.bytes;
            var root = new GameObject("Dynamic").transform; root.SetParent(gen, false);
            var made = new System.Collections.Generic.List<Transform>();
            foreach (var d in dj.entities)
            {
                var go = new GameObject(d.kind + d.id + (string.IsNullOrEmpty(d.prop) ? "" : "_" + d.prop));
                Transform parent = root;
                if (d.kind == "anim" && d.ppos != null && d.ppos.Length == 3)
                {
                    parent = new GameObject("pivot" + d.id).transform; parent.SetParent(root, false);
                    parent.localPosition = V(d.ppos); parent.localRotation = Q(d.prot); parent.localScale = d.pscl != null && d.pscl.Length == 3 ? V(d.pscl) : Vector3.one;
                }
                var t = go.transform; t.SetParent(parent, false);
                t.position = root.TransformPoint(V(d.pos)); t.rotation = root.rotation * Q(d.rot);
                var ws = d.scl != null && d.scl.Length == 3 ? V(d.scl) : Vector3.one;
                var ps = parent.lossyScale;
                t.localScale = new Vector3(ws.x / Mathf.Max(1e-4f, ps.x), ws.y / Mathf.Max(1e-4f, ps.y), ws.z / Mathf.Max(1e-4f, ps.z));
                // its meshes, plus lighter distance versions (LOD1) for the heavy ones, and a cut-off for small things far away
                var lod0 = new System.Collections.Generic.List<Renderer>(); var lod1 = new System.Collections.Generic.List<Renderer>();
                bool hasLod = d.lod1 != null && d.meshes != null && d.lod1.Length == d.meshes.Length;
                if (d.meshes != null)
                    for (int k = 0; k < d.meshes.Length; k++)
                    {
                        var r0 = Part(dj, bin, d.meshes[k], t, "m"); if (!r0) continue;
                        lod0.Add(r0);
                        var r1 = hasLod && d.lod1[k] >= 0 ? Part(dj, bin, d.lod1[k], t, "lod") : null;
                        lod1.Add(r1 ? r1 : r0);
                    }
                Lods(d, t, lod0, lod1, hasLod);
                made.Add(t);
            }
            if (Application.isPlaying) Destruction.Init(root, dj, made);
            WorldLook.Ensure(gen);
        }

        static bool MobileQuality => QualitySettings.names.Length > 0 && QualitySettings.names[QualitySettings.GetQualityLevel()].Contains("Mobile");

        Renderer Part(DynJson dj, byte[] bin, int mi, Transform t, string prefix)
        {
            if (mi < 0 || mi >= dj.meshes.Length) return null;
            var wm = dj.meshes[mi];
            var mgo = new GameObject(prefix + mi); mgo.transform.SetParent(t, false);
            var mesh = MeshIO.Build(bin, wm, mgo.name);
            mgo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = mgo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = wm.mat >= 0 && wm.mat < library.map.Length ? library.map[wm.mat] : library.unlitBase;
            bool transparent = mr.sharedMaterial && mr.sharedMaterial.renderQueue >= 3000;
            mr.shadowCastingMode = wm.cast && !transparent ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (Application.isPlaying) mesh.UploadMeshData(true);   // GPU copy only: nothing reads these on the CPU
            return mr;
        }

        // LOD1 under 12% of screen height; small loose things and furniture vanish under 0.8% (a few pixels).
        // On phones and in the browser, things smaller than 1.2 m don't cast shadows.
        static void Lods(WDyn d, Transform t, System.Collections.Generic.List<Renderer> lod0, System.Collections.Generic.List<Renderer> lod1, bool hasLod)
        {
            if (lod0.Count == 0 || d.kind == "anim") return;
            var b = lod0[0].bounds; foreach (var r in lod0) b.Encapsulate(r.bounds);
            bool small = b.size.magnitude < 1.2f;
            if (small && MobileQuality) foreach (var r in lod0) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bool cull = d.kind == "knock" || d.kind == "prop" || d.kind == "chip" || d.kind == "drum" || d.kind == "glass";
            if (!hasLod && !cull) return;
            var lg = t.gameObject.AddComponent<LODGroup>();
            float cutoff = cull ? .008f : 0f;
            if (hasLod)
            {
                foreach (var r in lod1) if (!lod0.Contains(r)) r.shadowCastingMode = lod0[0].shadowCastingMode;
                lg.SetLODs(new[] { new LOD(.12f, lod0.ToArray()), new LOD(cutoff, lod1.ToArray()) });
            }
            else lg.SetLODs(new[] { new LOD(cutoff, lod0.ToArray()) });
            lg.RecalculateBounds();
        }

        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        static Quaternion Q(float[] a) => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;

        void BuildLines(WorldJson w)
        {
            var lineMat = library.lineBase ? library.lineBase : library.unlitBase;
            if (w.lines == null || w.lines.Length == 0 || !lineMat) return;
            var root = new GameObject("Outlines").transform; root.SetParent(gen, false);
            int k = 0;
            foreach (var l in w.lines)
            {
                if (l.pos == null || l.pos.Length < 6) continue;
                int n = l.pos.Length / 3; var v = new Vector3[n]; var ix = new int[n];
                for (int i = 0; i < n; i++) { v[i] = new Vector3(l.pos[i * 3], l.pos[i * 3 + 1], l.pos[i * 3 + 2]); ix[i] = i; }
                var mesh = new Mesh { name = "lines" + k };
                mesh.indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(v); mesh.SetIndices(ix, MeshTopology.Lines, 0);
                var go = new GameObject("lines" + k++); go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mat = new Material(lineMat) { name = "line " + l.color };
                mat.SetColor("_BaseColor", Hex.ToColor(l.color, l.opacity));
                mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // three.js light units -> URP: divide by pi (three's Lambert BRDF has a 1/pi that URP leaves out)
        void BuildLamps(WorldJson w)
        {
            if (w.lights == null) return;
            var root = new GameObject("Lamps").transform; root.SetParent(gen, false);
            foreach (var l in w.lights)
            {
                if (l.intensity <= 0 || l.pos == null) continue;
                var go = new GameObject(l.type); go.transform.SetParent(root, false);
                go.transform.position = new Vector3(l.pos[0], l.pos[1], l.pos[2]);
                var li = go.AddComponent<Light>();
                li.color = Hex.ToColor(l.color);
                li.intensity = l.intensity / Mathf.PI;
                li.range = l.distance > 0 ? l.distance : 20f;
                li.shadows = LightShadows.None;
                if (l.type == "SpotLight" && l.target != null)
                {
                    li.type = LightType.Spot;
                    li.spotAngle = Mathf.Clamp(l.angle * 2f * Mathf.Rad2Deg, 1f, 179f);
                    li.innerSpotAngle = li.spotAngle * (1f - Mathf.Clamp01(l.penumbra));
                    var t = new Vector3(l.target[0], l.target[1], l.target[2]);
                    if ((t - go.transform.position).sqrMagnitude > 1e-6f) go.transform.rotation = Quaternion.LookRotation(t - go.transform.position);
                }
                else li.type = LightType.Point;
            }
        }
    }
}
