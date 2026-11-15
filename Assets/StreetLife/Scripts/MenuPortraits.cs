// Character pictures for the main menu's character boxes. Each character is built once in a small studio far below
// the map, lit by its own lights and photographed head-and-shoulders into a texture, then the model is thrown away.
// One character a frame, so the menu never stalls; nothing is saved to disk (all 18 take well under a second).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StreetLife
{
    public class MenuPortraits : MonoBehaviour
    {
        public const int Layer = 28;                                   // only the portrait camera draws this layer
        public const int W = 160, H = 128;
        static readonly Vector3 Studio = new Vector3(0, -4000, 0);
        static readonly Color Backdrop = new Color32(32, 32, 32, 255);  // the menu's tile grey
        static MenuPortraits I;
        static readonly Dictionary<string, Texture2D> pics = new Dictionary<string, Texture2D>();
        static readonly List<string> queue = new List<string>();
        public static event Action<string, Texture2D> Ready;

        /// the picture if it's done; otherwise null, and it's queued (Ready fires when it arrives)
        public static Texture2D Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (pics.TryGetValue(id, out var t) && t) return t;
            if (!queue.Contains(id)) queue.Add(id);
            if (!I) I = new GameObject("Menu portraits").AddComponent<MenuPortraits>();
            return null;
        }

        Camera cam; RenderTexture rt; Light key, fill, rim; MaterialLibrary lib; Transform studio;

        bool Setup()
        {
            if (cam) return true;
            var src = FindAnyObjectByType<CharacterRig>(); var set = CharacterSet.Load();
            if (!src || !src.library || !set) return false;
            lib = Instantiate(src.library); lib.name = "portrait materials";
            if (set.materials != null && set.materials.Length > 0) lib.character = set.materials;
            studio = new GameObject("studio").transform; studio.SetParent(transform, false); studio.position = Studio;
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "character portrait", antiAliasing = 4 };
            var cg = new GameObject("portrait camera"); cg.transform.SetParent(studio, false);
            cam = cg.AddComponent<Camera>(); cam.enabled = false; cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Backdrop; cam.orthographic = true;
            cam.nearClipPlane = .05f; cam.farClipPlane = 30; cam.targetTexture = rt; cam.aspect = (float)W / H;
            var data = cg.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = false; data.renderShadows = false;
            key = MakeLight("key", 9f, new Color(1f, .97f, .93f)); fill = MakeLight("fill", 3.5f, new Color(.86f, .9f, 1f)); rim = MakeLight("rim", 7f, Color.white);
            var mc = Camera.main; if (mc) mc.cullingMask &= ~(1 << Layer);
            return true;
        }

        Light MakeLight(string n, float intensity, Color c)
        {
            var go = new GameObject(n); go.transform.SetParent(studio, false);
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 8; l.intensity = intensity; l.color = c;
            l.shadows = LightShadows.None; l.cullingMask = 1 << Layer; l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        void Update()
        {
            if (queue.Count == 0) return;
            if (!GfxWarmup.Done || Time.frameCount < 30) return;   // 15 Nov: not in the menu's first frames, where the first picture added ~0.4 s to the opening pause
            if (!Setup()) return;
            var id = queue[0]; queue.RemoveAt(0);
            if (pics.ContainsKey(id)) return;
            try { var t = Shoot(id); if (t) { pics[id] = t; Ready?.Invoke(id, t); } }
            catch (Exception e) { Debug.LogWarning("Street Life: couldn't make the picture for " + id + ": " + e.Message); }
        }

        Texture2D Shoot(string id)
        {
            var set = CharacterSet.Load(); var e = set ? set.EntryOf(id) : null;
            if (e == null || !e.json || !e.bin) return null;
            var go = new GameObject("portrait " + id); go.transform.SetParent(studio, false);
            var rig = go.AddComponent<CharacterRig>(); rig.library = lib; rig.charJson = e.json; rig.charBin = e.bin;
            rig.Build();
            var rends = go.GetComponentsInChildren<Renderer>(true);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            var bb = new Bounds(); bool any = false;
            foreach (var r in rends) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
            Texture2D tex = null;
            if (any)
            {
                // head and shoulders: the top ~45% of the figure, the model facing the camera (it faces -z)
                float h = bb.size.y, viewH = Mathf.Max(.3f, h * .45f);
                var c = new Vector3(bb.center.x, bb.max.y - viewH * .5f + h * .02f, bb.center.z);
                cam.orthographicSize = viewH * .5f;
                cam.transform.position = c - Vector3.forward * 5; cam.transform.rotation = Quaternion.identity;
                key.transform.position = c + new Vector3(-1.2f, .9f, -2.2f);
                fill.transform.position = c + new Vector3(1.6f, -.2f, -2.4f);
                rim.transform.position = c + new Vector3(.4f, 1.2f, 1.8f);
                key.enabled = fill.enabled = rim.enabled = true;
                cam.Render();
                key.enabled = fill.enabled = rim.enabled = false;
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex = new Texture2D(W, H, TextureFormat.RGB24, false) { name = "portrait " + id, wrapMode = TextureWrapMode.Clamp };
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(false, true);
                RenderTexture.active = prev;
            }
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) Destroy(mf.sharedMesh);
            Destroy(go);
            return tex;
        }

        void OnDestroy() { if (rt) { rt.Release(); Destroy(rt); } if (I == this) I = null; }
    }
}
