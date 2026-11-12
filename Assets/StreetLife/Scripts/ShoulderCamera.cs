// Over-the-shoulder camera, ported from the web game's Duels camera: shoulder offset to the right, pulls in
// closer and lower indoors, never clips through walls (five probe rays), eases back out, FOV 70 (+8 indoors), aim zoom.
using UnityEngine;

namespace StreetLife
{
    [RequireComponent(typeof(Camera))]
    public class ShoulderCamera : MonoBehaviour
    {
        public Fighter target;
        public CharacterRig rig;
        [Tooltip("Aim-down-sights field of view (the M4's zoom in the web game)")]
        public float adsFov = 33f;
        public float adsZoomTime = .26f;

        Camera cam;
        float indoor, camUse = float.NaN;
        static readonly Vector2[] probes = { new Vector2(0, 0), new Vector2(.28f, 0), new Vector2(-.28f, 0), new Vector2(0, .24f), new Vector2(0, -.24f) };

        static float Damp(float a, float b, float lambda, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-lambda * dt));

        void Awake() { cam = GetComponent<Camera>(); cam.fieldOfView = 70; }

        void LateUpdate()
        {
            if (!target || !BoxWorld.Loaded) return;
            float dt = Time.deltaTime;
            var f = target;
            Vector3 dir = f.Aim, right = f.Right, up = Vector3.up;
            Vector3 head = f.VisualHead;

            bool ceiling = BoxWorld.Ray(head, up, 3.2f) >= 0;
            indoor = Damp(indoor, ceiling ? 1 : 0, 6, dt);
            float ind = indoor;
            float sh = Mathf.Lerp(f.ads ? 1.0f : 1.45f, f.ads ? .7f : .9f, ind);
            float rs = BoxWorld.Ray(head, right, sh + .4f); if (rs >= 0) sh = Mathf.Max(0, rs - .4f);
            Vector3 pivot = head + right * sh;
            float dist = Mathf.Lerp(f.ads ? 2.6f : 4.6f, f.ads ? 1.9f : 3.1f, ind), lift = Mathf.Lerp(.45f, .15f, ind);
            Vector3 want = pivot - dir * dist + up * lift;
            Vector3 d = want - pivot; float L = d.magnitude; d /= L;
            float use = L;
            Vector3 side = Vector3.Cross(d, up).normalized;
            foreach (var p in probes)
            {
                Vector3 o = pivot + side * p.x + up * p.y;
                float h = BoxWorld.Ray(o, d, L + .4f);
                if (h >= 0) use = Mathf.Min(use, h - .4f);
            }
            // pull in at once when something gets between you and the camera, ease back out after
            use = Mathf.Max(.35f, use);
            camUse = float.IsNaN(camUse) || use < camUse ? use : Damp(camUse, use, 7, dt);
            Vector3 cp = pivot + d * camUse; if (cp.y < .3f) cp.y = .3f;
            transform.position = cp;
            transform.LookAt(head + right * sh + dir * 60f);
            if (rig) rig.SetVisible(camUse > 1.0f);

            float fov = f.ads ? adsFov : 70 + 8 * ind;
            cam.fieldOfView = Damp(cam.fieldOfView, fov, f.ads ? 3.2f / adsZoomTime : 10, dt);
        }
    }
}
