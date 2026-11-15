// Builds weapon models from the exported mesh blob (guns.bytes) and node trees (guns.json). Meshes are built once and shared.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public static class WeaponModels
    {
        public static Material[] Mats;     // the weapon materials (made by the weapon setup from guns_materials.json)
        static Material fallback;
        static Mesh[] cache;

        public static Mesh MeshAt(int i)
        {
            var d = GunsDb.Data;
            if (cache == null || cache.Length != d.meshes.Length) cache = new Mesh[d.meshes.Length];
            if (cache[i]) return cache[i];
            var made = WeaponFixes.Made(i); if (made) return cache[i] = made;
            var m = MeshIO.Build(GunsDb.Bin, d.meshes[i], "gun" + i); WeaponFixes.Edit(i, m);
            return cache[i] = m;
        }

        /// material id for gloss-black trim (Tools/gunparts.py): the plain black (id 1) with a lacquered finish
        public const int TrimBlack = 1001;
        static Material trimBlack;

        public static Material Mat(int id)
        {
            if (id == TrimBlack)
            {
                if (!trimBlack && Mats != null && Mats.Length > 1 && Mats[1])
                {
                    trimBlack = new Material(Mats[1]) { name = "gun trim black" };
                    if (trimBlack.HasProperty("_BaseColor")) trimBlack.SetColor("_BaseColor", new Color(.012f, .012f, .014f, 1));
                    if (trimBlack.HasProperty("_Smoothness")) trimBlack.SetFloat("_Smoothness", .38f);
                    if (trimBlack.HasProperty("_Metallic")) trimBlack.SetFloat("_Metallic", .15f);
                }
                if (trimBlack) return trimBlack;
                id = 1;
            }
            if (Mats != null && id >= 0 && id < Mats.Length && Mats[id]) return Mats[id];
            if (!fallback) fallback = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "gun fallback" };
            return fallback;
        }

        /// Instantiate an exported node tree under parent. Returns the root. All renderers are added to rends if given.
        public static Transform Build(WNode[] nodes, Transform parent, string name, int layer = -1, bool shadows = true, List<Renderer> rends = null)
        {
            if (nodes == null || nodes.Length == 0) return null;
            var made = new Transform[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = nodes[i];
                var go = new GameObject(i == 0 ? name : string.IsNullOrEmpty(n.name) ? "part" + i : n.name);
                var t = go.transform;
                t.SetParent(n.parent >= 0 ? made[n.parent] : parent, false);
                t.localPosition = new Vector3(n.pos[0], n.pos[1], n.pos[2]);
                t.localRotation = new Quaternion(n.rot[0], n.rot[1], n.rot[2], n.rot[3]);
                t.localScale = new Vector3(n.scl[0], n.scl[1], n.scl[2]);
                if (layer >= 0) go.layer = layer;
                made[i] = t;
                if (n.meshes == null) continue;
                foreach (var mi in n.meshes)
                {
                    var wm = GunsDb.Data.meshes[mi];
                    var mgo = n.meshes.Length == 1 ? go : new GameObject("mesh" + mi);
                    if (mgo != go) { mgo.transform.SetParent(t, false); if (layer >= 0) mgo.layer = layer; }
                    mgo.AddComponent<MeshFilter>().sharedMesh = MeshAt(mi);
                    var mr = mgo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = Mat(wm.mat);
                    bool transparent = mr.sharedMaterial && mr.sharedMaterial.renderQueue >= 3000;
                    mr.shadowCastingMode = shadows && wm.cast && !transparent ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    rends?.Add(mr);
                }
            }
            return made[0];
        }

        public static Transform Find(Transform root, string name)
        {
            if (!root) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        public static void SetLayer(Transform root, int layer)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        // ---- muzzle flash: two crossed additive quads with the web game's star-burst texture
        static Material flashMat; static Mesh quad;
        public static Mesh Quad
        {
            get
            {
                if (quad) return quad;
                quad = new Mesh { name = "fxquad" };
                quad.SetVertices(new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) });
                quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                quad.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.RecalculateBounds();
                return quad;
            }
        }

        public static Material FlashMat
        {
            get
            {
                if (flashMat) return flashMat;
                flashMat = GunFx.MakeMat(true, FlashTex());
                return flashMat;
            }
        }

        static Texture2D FlashTex()
        {
            // radial white-orange glow with five spikes (flashTex() in the web game)
            int n = 64; var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "muzzle flash" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + .5f - 32, dy = y + .5f - 32, r = Mathf.Sqrt(dx * dx + dy * dy) / 30f;
                    Color c;
                    if (r < .25f) c = Color.Lerp(new Color(1, 1, .94f, 1), new Color(1, .84f, .43f, .85f), r / .25f);
                    else if (r < .6f) c = Color.Lerp(new Color(1, .84f, .43f, .85f), new Color(1, .59f, .16f, .25f), (r - .25f) / .35f);
                    else if (r < 1) c = Color.Lerp(new Color(1, .59f, .16f, .25f), new Color(1, .47f, .08f, 0), (r - .6f) / .4f);
                    else c = new Color(0, 0, 0, 0);
                    float a = Mathf.Atan2(dy, dx);
                    for (int i = 0; i < 5; i++)
                    {
                        float d = Mathf.DeltaAngle(a * Mathf.Rad2Deg, i * 72f) * Mathf.Deg2Rad, w = .09f * (1 - r);
                        if (r < .97f && Mathf.Abs(d) < w) { c.r = 1; c.g = Mathf.Max(c.g, .86f); c.b = Mathf.Max(c.b, .55f); c.a = Mathf.Max(c.a, .55f); }
                    }
                    px[y * n + x] = new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);   // premultiplied for additive
                }
            t.SetPixels(px); t.Apply(); return t;
        }

        /// A muzzle-flash group: two crossed quads of the given size. Returns the group; its renderers are in rends.
        public static Transform MakeFlash(Transform parent, Vector3 localPos, float size, int layer, List<Renderer> rends)
        {
            var g = new GameObject("flash").transform; g.SetParent(parent, false); g.localPosition = localPos;
            for (int k = 0; k < 2; k++)
            {
                var q = new GameObject("flash" + k); q.transform.SetParent(g, false);
                q.transform.localRotation = Quaternion.Euler(0, k * 90, 0); q.transform.localScale = Vector3.one * size;
                q.AddComponent<MeshFilter>().sharedMesh = Quad;
                var mr = q.AddComponent<MeshRenderer>(); mr.sharedMaterial = FlashMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (layer >= 0) q.layer = layer;
                rends?.Add(mr);
            }
            if (layer >= 0) g.gameObject.layer = layer;
            g.gameObject.SetActive(false);
            return g;
        }
    }
}
