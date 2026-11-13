// First-person aim, ported from the web game's updVM: when you aim with a supported gun the camera eases into your
// eyes and the gun is drawn by its own overlay camera (fixed 52 degree view, so it never clips into walls and keeps
// its size while the main view zooms), held in two hands with its sights lined up on the centre of the screen.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StreetLife
{
    [RequireComponent(typeof(Camera)), DefaultExecutionOrder(200)]
    public class ViewModel : MonoBehaviour
    {
        public const int Layer = 30;      // view-model layer: drawn only by the overlay camera
        public Weapons weapons;
        Camera vmCam;
        class Vm { public Transform wrap, g; public VmJson v; public Transform flash; public List<Renderer> rends = new List<Renderer>(); }
        readonly Dictionary<string, Vm> models = new Dictionary<string, Vm>();
        Vm cur; string curId; float t, vkick;

        void Start()
        {
            var main = GetComponent<Camera>();
            main.cullingMask &= ~(1 << Layer);
            var go = new GameObject("View model camera"); go.transform.SetParent(transform, false);
            vmCam = go.AddComponent<Camera>(); vmCam.fieldOfView = 52; vmCam.nearClipPlane = .01f; vmCam.farClipPlane = 20; vmCam.cullingMask = 1 << Layer;
            var d = vmCam.GetUniversalAdditionalCameraData(); d.renderType = CameraRenderType.Overlay; d.renderShadows = false; d.renderPostProcessing = true;
            var md = main.GetUniversalAdditionalCameraData(); if (!md.cameraStack.Contains(vmCam)) md.cameraStack.Add(vmCam);
        }

        Vm Get(string id)
        {
            if (models.TryGetValue(id, out var m)) return m;
            var hj = GunsDb.Held(id); var vj = GunsDb.Vm(id);
            if (hj == null || vj == null) { models[id] = null; return null; }
            m = new Vm { v = vj };
            m.wrap = new GameObject("vm " + id).transform; m.wrap.SetParent(vmCam.transform, false); m.wrap.gameObject.layer = Layer;
            m.g = new GameObject("gun").transform; m.g.SetParent(m.wrap, false); m.g.gameObject.layer = Layer;
            m.g.localRotation = Web.Q(Web.EulerXYZ(0, Mathf.PI, 0));
            WeaponModels.Build(hj.nodes, m.g, "model", Layer, false, m.rends);
            WeaponModels.Build(vj.nodes, m.g, "hands", Layer, false, m.rends);
            if (id != "rocket" && id != "crossbow" && id != "swarm")
                m.flash = WeaponModels.MakeFlash(m.g, Web.V(new Vector3(0, vj.muzY, vj.muzZ)), .22f, Layer, m.rends);
            foreach (var r in m.rends) if (r) r.receiveShadows = false;
            m.wrap.gameObject.SetActive(false);
            models[id] = m; return m;
        }

        void LateUpdate()
        {
            if (!vmCam || !weapons || !GunsDb.Loaded) return;
            var w = weapons; var f = w.F; float dt = Time.deltaTime;
            float raise = w.vmRaise;
            var g = w.G;
            if (raise <= 0 || f == null) { if (cur != null) cur.wrap.gameObject.SetActive(false); return; }
            if (g != null && g.vm) curId = w.Gun;
            var m = curId != null ? Get(curId) : null;
            if (cur != m) { if (cur != null) cur.wrap.gameObject.SetActive(false); cur = m; }
            if (m == null) return;
            m.wrap.gameObject.SetActive(true);
            t += dt; vkick = Mathf.Max(w.kick, vkick - dt * 7);
            float e = raise * raise * (3 - 2 * raise), sc = .36f;
            float sp = f.HorizontalSpeed, bob = Mathf.Min(1, sp / 8);
            float swx = Mathf.Sin(t * 1.3f) * .004f + Mathf.Sin(t * 9) * .006f * bob, swy = Mathf.Sin(t * 1.9f) * .003f + Mathf.Abs(Mathf.Cos(t * 9)) * .006f * bob;
            float relo = w.reloadT > 0 ? Mathf.Sin(Mathf.Min(1, 1 - w.reloadT / Mathf.Max(.01f, w.reloadMax)) * Mathf.PI) : 0;
            var v = m.v;
            float ax = v.dx * sc, ay = -v.sightY * sc, az = -.3f + v.rearZ * sc, lx = .2f, ly = -.42f, lz = -.38f;
            var pos = new Vector3(Mathf.Lerp(lx, ax, e) + swx, Mathf.Lerp(ly, ay, e) + swy - relo * .12f, Mathf.Lerp(lz, az, e) + vkick * .035f);
            var rot = new Vector3(Mathf.Lerp(-.25f, 0, e) + vkick * .05f + relo * .5f, Mathf.Lerp(.18f, 0, e), Mathf.Lerp(.25f, 0, e) - relo * .3f);
            if (curId == "sting") { pos.x += .085f * e; pos.y -= .06f * e; rot.z -= .42f * e; rot.y += .07f * e; }   // point-shooting: canted low and right
            m.wrap.localPosition = Web.V(pos); m.wrap.localRotation = Web.Q(Web.EulerXYZ(rot.x, rot.y, rot.z)); m.wrap.localScale = Vector3.one * sc;
            // the RPG optic and the Swarm display take over the screen
            bool hide = (curId == "rocket" || curId == "swarm") && raise > .85f && f.ads;
            foreach (var r in m.rends) if (r) r.enabled = !hide;
            if (m.flash)
            {
                bool on = w.flashT > .02f; m.flash.gameObject.SetActive(on && !hide);
                if (on) { m.flash.localRotation = Quaternion.Euler(0, 0, Random.value * 170); m.flash.localScale = Vector3.one * (.7f + Random.value * .5f); }
            }
        }
    }
}
