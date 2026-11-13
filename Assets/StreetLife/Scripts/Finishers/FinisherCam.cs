// Finishers chat: the cinematic camera for finishing moves you're in (web updCamera's finisher shot): it swings out to
// one side of the move, framed on the move's focus point at its distance, keeps clear of walls, and eases back to the
// shoulder camera afterwards. Also adds the shake from downs and finishers. Runs after ShoulderCamera.
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(1100), RequireComponent(typeof(Camera))]
    public class FinisherCam : MonoBehaviour
    {
        Camera cam;
        bool active; float outK, fov = 55;
        Vector3 pos, look; Quaternion lastRot; Vector3 lastPos; float lastFov;
        float noiseT;

        void Awake() { cam = GetComponent<Camera>(); }

        static float Damp(float a, float b, float l, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-l * dt));

        void LateUpdate()
        {
            var fin = Finishers.I; if (!fin) return;
            float dt = Time.deltaTime; noiseT += Time.unscaledDeltaTime;
            var F = fin.PlayerMove;
            if (F != null)
            {
                if (!active) { active = true; pos = transform.position; look = transform.position + transform.forward * 6; fov = cam.fieldOfView; }
                var a = F.a; var v = F.v;
                var fc = F.focus ?? new Vector3((a.pos.x + v.pos.x) / 2, v.pos.y + 1.4f, (a.pos.z + v.pos.z) / 2);
                float D = F.camD;
                var rt = new Vector3(Mathf.Cos(F.yaw0), 0, -Mathf.Sin(F.yaw0));
                System.Func<Vector3, Vector3> camOff = sd => sd * (D * F.camSide) - F.fwd * (D * F.camBack) + Vector3.up * (D * .3f);
                // in a tight spot (both sides walled in) look over the attacker's shoulder, or back at them past the victim
                System.Func<int, Vector3> shot = mode => mode == 0 ? camOff(F.camSideVec.Value) : mode == 1 ? camOff(-F.camSideVec.Value)
                    : mode == 2 ? -F.fwd * (D * .85f) + rt * (D * .3f) + Vector3.up * (D * .35f) : F.fwd * (D * .85f) + rt * (D * .3f) + Vector3.up * (D * .35f);
                System.Func<Vector3, float> clear = o => { float L = o.magnitude; float h = BoxWorld.Ray(fc, o / L, L + .5f); return h >= 0 ? h - .5f : L; };
                if (!F.camSideVec.HasValue)
                {
                    // the side the camera is already on, unless the other side has more room (web), else the roomiest of four shots
                    var lt = -rt;
                    var pr = Vector3.Dot(transform.position - a.pos, rt) < 0 ? lt : rt;
                    F.camSideVec = pr;
                    float c0 = clear(shot(0)), c1 = clear(shot(1));
                    if (c0 >= D * .9f) F.camMode = 0;
                    else if (c1 >= D * .9f) F.camMode = 1;
                    else
                    {
                        float best = -1;
                        for (int m = 0; m < 4; m++) { var o = shot(m); float c = clear(o) / o.magnitude; if (c > best + .05f) { best = c; F.camMode = m; } }
                    }
                }
                var off = shot(F.camMode); float cl = clear(off), len = off.magnitude;
                var want = fc + off * (Mathf.Max(1.5f, Mathf.Min(cl, len)) / len);
                pos = Vector3.Lerp(pos, want, 1 - Mathf.Exp(-F.camK * dt));
                look = Vector3.Lerp(look, fc, 1 - Mathf.Exp(-12 * dt));
                fov = Damp(fov, 55, 6, dt);
                // the swing itself never passes through a wall: pull in towards the focus when something's in the way
                var shown = pos + ShakeOffset(); var fromF = shown - fc; float fl = fromF.magnitude;
                if (fl > .05f) { float h = BoxWorld.Ray(fc, fromF / fl, fl + .3f); if (h >= 0) shown = fc + fromF / fl * Mathf.Max(.3f, h - .3f); }
                if (shown.y < .3f) shown.y = .3f;
                transform.position = shown; transform.LookAt(look); cam.fieldOfView = fov;
                lastPos = transform.position; lastRot = transform.rotation; lastFov = fov; outK = 1;
                // both fighters in shot (the shoulder camera hides your own model when it's close)
                var me = fin.Player; var pose = FinisherPose.Of(me);
                if (me && pose && !pose.Hidden && !pose.Xray) { var rig = me.GetComponent<CharacterRig>(); if (rig) rig.SetVisible(true); }
                return;
            }
            if (active) { active = false; outK = 1; }
            if (outK > 0)
            {
                // ease back to wherever the shoulder camera wants to be this frame
                outK = Mathf.Max(0, outK - Time.unscaledDeltaTime / .6f); float k = outK * outK * (3 - 2 * outK);
                transform.position = Vector3.Lerp(transform.position, lastPos, k);
                transform.rotation = Quaternion.Slerp(transform.rotation, lastRot, k);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, lastFov, k);
            }
            if (Finishers.Shake > .01f) transform.position += ShakeOffset();
        }

        /// Smooth shake (noise, not jitter), scaled by Finishers.Shake.
        Vector3 ShakeOffset()
        {
            float s = Finishers.Shake; if (s <= .01f) return Vector3.zero;
            float t = noiseT * 22;
            var o = new Vector3(Mathf.PerlinNoise(t, 1.3f) - .5f, Mathf.PerlinNoise(2.7f, t) - .5f, Mathf.PerlinNoise(t, 5.1f) - .5f);
            return o * (s * .35f);
        }
    }
}
