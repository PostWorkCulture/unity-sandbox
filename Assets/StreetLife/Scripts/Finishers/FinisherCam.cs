// Finishers chat: the cinematic camera for finishing moves you're in (web updCamera's finisher shot). It takes the shot
// the planner picked (FinPlanner: a direction from the move's focus that sees the whole move), framed on the move's focus
// at its distance, eases back to the shoulder camera afterwards, and adds the shake from downs and finishers. Runs after
// ShoulderCamera.
// 14 Nov: nothing gets between the camera and the move. If something does anyway (a car driving in, another fighter,
// something knocked loose), it's hidden while it's in the way, and if a wall cuts off the view the camera swings to a
// shot that sees both fighters again. The camera never goes through a wall (it pulls in towards the focus).
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(1100), RequireComponent(typeof(Camera))]
    public class FinisherCam : MonoBehaviour
    {
        Camera cam;
        bool active; float outK, fov = 55;
        Vector3 pos, look; Quaternion lastRot; Vector3 lastPos; float lastFov;
        float noiseT, blockedT, reaimT;
        Finisher cur;
        readonly HashSet<Renderer> hidden = new HashSet<Renderer>();
        readonly HashSet<Renderer> hideNow = new HashSet<Renderer>();
        readonly List<Vector3> targets = new List<Vector3>();

        void Awake() { cam = GetComponent<Camera>(); }
        void OnDisable() => Unhide();

        static float Damp(float a, float b, float l, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-l * dt));

        void LateUpdate()
        {
            var fin = Finishers.I; if (!fin) return;
            float dt = Time.deltaTime; noiseT += Time.unscaledDeltaTime;
            var F = fin.PlayerMove;
            if (F != null)
            {
                if (!active || F != cur) { active = true; cur = F; pos = transform.position; look = transform.position + transform.forward * 6; fov = cam.fieldOfView; blockedT = 0; }
                var a = F.a; var v = F.v;
                var fc = F.focus ?? new Vector3((a.pos.x + v.pos.x) / 2, v.pos.y + 1.4f, (a.pos.z + v.pos.z) / 2);
                float D = F.camD * Mathf.Sqrt(F.camSide * F.camSide + F.camBack * F.camBack + .09f);
                if (!F.camSet)
                {
                    // no plan (shouldn't happen): the move's designed angle, on the side the camera is already on
                    var sd = Vector3.Dot(transform.position - a.pos, F.side) >= 0 ? F.side : -F.side;
                    F.camDir = (sd * F.camSide - F.fwd * F.camBack + Vector3.up * .3f).normalized; F.camScale = 1; F.camSet = true;
                }
                float len = D * F.camScale;
                float cl = Clear(fc, F.camDir, len + .5f);
                var want = fc + F.camDir * Mathf.Max(1.5f, Mathf.Min(cl - .3f, len));
                pos = Vector3.Lerp(pos, want, 1 - Mathf.Exp(-F.camK * dt));
                look = Vector3.Lerp(look, fc, 1 - Mathf.Exp(-12 * dt));
                fov = Damp(fov, 55, 6, dt);
                // the swing itself never passes through a wall: pull in towards the focus when something's in the way
                var shown = Shown(fc);
                // and if the swing (or the camera lagging behind the action) would lose sight of the fighters, cut straight
                // to the planned shot instead
                Targets(F, fc);
                if (!SeesAll(shown) && SeesAll(want)) { pos = want; look = fc; shown = Shown(fc); cuts++; }
                transform.position = shown; transform.LookAt(look); cam.fieldOfView = fov;
                lastPos = transform.position; lastRot = transform.rotation; lastFov = fov; outK = 1;
                // both fighters in shot (the shoulder camera hides your own model when it's close)
                var me = fin.Player; var pose = FinisherPose.Of(me);
                if (me && pose && !pose.Hidden && !pose.Xray) { var rig = me.GetComponent<CharacterRig>(); if (rig) rig.SetVisible(true); }
                KeepInView(F, fc, D, dt);
                return;
            }
            if (active) { active = false; cur = null; outK = 1; Unhide(); }
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

        /// How many times the camera cut to its shot rather than swinging (tests read it).
        public static int cuts;
        Vector3 Shown(Vector3 fc)
        {
            var shown = pos + ShakeOffset(); var fromF = shown - fc; float fl = fromF.magnitude;
            if (fl > .05f) { float h = Clear(fc, fromF / fl, fl + .3f); if (h < fl + .3f) shown = fc + fromF / fl * Mathf.Max(.3f, h - .3f); }
            if (shown.y < .3f) shown.y = .3f;
            return shown;
        }
        bool SeesAll(Vector3 c) { for (int i = 1; i < targets.Count; i++) if (!FinPlanner.See(c, targets[i])) return false; return true; }

        /// What the shot has to see: the fighters (the thrown body alone once it's flying) and the focus.
        void Targets(Finisher F, Vector3 fc)
        {
            targets.Clear(); targets.Add(fc);
            var vp = FinisherPose.Of(F.v);
            bool vShown = !F.gone && !(vp && vp.Hidden);
            if (vShown) targets.Add(FinPlanner.Above(F.v.VisualPos + Vector3.up * (F.vDown ? .35f : 1.1f), F.v0.y));
            if (F.fall == null) targets.Add(F.a.VisualPos + Vector3.up * 1.2f);   // once the body's thrown, the shot follows it alone
        }

        void KeepInView(Finisher F, Vector3 fc, float D, float dt)
        {
            var c = transform.position;
            // things that move about (other fighters, cars, loose props, trees' leaves) are hidden while they're in the way
            hideNow.Clear();
            foreach (var f in Finishers.Fighters)
            {
                if (!f || f == F.a || f == F.v) continue;
                var p = FinisherPose.Of(f); if (!p) continue;
                var b = new Bounds(f.VisualPos + Vector3.up * 1.0f, new Vector3(.9f, 2.0f, .9f));
                p.Occluding = Blocks(b, c);
            }
            var Dz = Destruction.I;
            if (Dz != null)
                foreach (var e in Dz.ents)
                {
                    if (e == null || !e.alive || e.d == null || !e.t || !e.t.gameObject.activeInHierarchy) continue;
                    var k = e.d.kind; if (k != "car" && k != "prop" && k != "drum" && k != "play" && k != "tree" && k != "knock") continue;
                    if ((e.t.position - fc).sqrMagnitude > 30 * 30) continue;
                    foreach (var r in e.rends)
                    {
                        if (!r) continue; var b = r.bounds; b.Expand(-.1f);
                        if (Blocks(b, c)) hideNow.Add(r);
                    }
                }
            foreach (var r in hidden) if (r && !hideNow.Contains(r)) r.forceRenderingOff = false;
            foreach (var r in hideNow) r.forceRenderingOff = true;
            hidden.Clear(); foreach (var r in hideNow) hidden.Add(r);

            // a wall cuts off the view (the move went somewhere the plan didn't expect): swing round to a shot that sees it
            bool blocked = false;
            for (int i = 1; i < targets.Count; i++) if (!FinPlanner.See(c, targets[i])) { blocked = true; break; }
            blockedT = blocked ? blockedT + Time.unscaledDeltaTime : 0;
            if (blockedT > .25f && Time.unscaledTime > reaimT)
            {
                reaimT = Time.unscaledTime + .6f;
                var arr = targets.ToArray();
                var dir = FinPlanner.Reaim(F, fc, D * F.camScale, arr);
                if (dir.HasValue) { F.camDir = dir.Value; blockedT = 0; }
            }
        }

        bool Blocks(Bounds b, Vector3 c)
        {
            if (b.Contains(c)) return false;   // the camera's inside it: hiding it wouldn't help, and looks odd
            foreach (var p in targets)
            {
                if (b.Contains(p)) continue;
                var d = p - c; float L = d.magnitude; if (L < .1f) continue;
                if (b.IntersectRay(new Ray(c, d / L), out float h) && h < L - .2f) return true;
            }
            return false;
        }

        void Unhide()
        {
            foreach (var r in hidden) if (r) r.forceRenderingOff = false;
            hidden.Clear();
            foreach (var f in Finishers.Fighters) { var p = f ? FinisherPose.Of(f) : null; if (p) p.Occluding = false; }
        }

        /// How far the camera can go from o along d (up to L) before a box (the arena's invisible walls don't count).
        static float Clear(Vector3 o, Vector3 d, float L)
        {
            var B = BoxWorld.Boxes; float best = L;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i]; if (b.x0 < -9000 || FinGeo.EdgeBox(b)) continue;
                float t0 = 0, t1 = best;
                if (!Slab(o.x, d.x, b.x0, b.x1, ref t0, ref t1) || !Slab(o.y, d.y, b.y0, b.y1, ref t0, ref t1) || !Slab(o.z, d.z, b.z0, b.z1, ref t0, ref t1)) continue;
                if (t0 <= 0) continue;   // starts inside (the focus in a bush): ignore it
                best = t0;
            }
            return best;
        }
        static bool Slab(float o, float d, float mn, float mx, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-9f) return o > mn && o < mx;
            float a = (mn - o) / d, b = (mx - o) / d; if (a > b) { var q = a; a = b; b = q; }
            if (a > t0) t0 = a; if (b < t1) t1 = b; return t0 <= t1;
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
