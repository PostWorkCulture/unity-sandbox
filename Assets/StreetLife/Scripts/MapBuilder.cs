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

        const string GenName = "__Generated";
        Transform gen;

        void OnEnable()
        {
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
                go.AddComponent<MeshFilter>().sharedMesh = MeshIO.Build(bin, wm, go.name);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = wm.mat >= 0 && wm.mat < library.map.Length ? library.map[wm.mat] : library.unlitBase;
                var m = mr.sharedMaterial;
                bool transparent = m && m.renderQueue >= 3000;
                mr.shadowCastingMode = wm.cast && !transparent ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            BuildLines(w);
            BuildLamps(w);
        }

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
