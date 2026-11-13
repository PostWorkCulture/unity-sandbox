// Finishers chat: drives a fighter's model while they're down (prone, crawling), in a finishing move (the move's pose),
// or dead on the ground. Runs after CharacterRig and the weapon pose, and only takes over while one of those applies.
// Every joint eases to its target (no snapping between keyframes) and blends in and out of the normal animation.
// The web game's poses are in its own space (three.js, model facing +z); they're mirrored to Unity's here:
// a joint's (x, y, z) rotation becomes (-x, -y, z), the model's root turns by yaw + 180 like CharacterRig.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(1000)]
    public class FinisherPose : MonoBehaviour
    {
        Fighter f; CharacterRig rig;
        Transform root, body;
        // aR, aL, eR, eL, lR, lL, kR, kL, head, up
        readonly Transform[] J = new Transform[10];
        static readonly string[] names = { "aR.p", "aL.p", "aR.e", "aL.e", "lR.p", "lL.p", "lR.k", "lL.k", "headG", "up" };
        readonly Quaternion[] cur = new Quaternion[10];
        readonly Quaternion?[] tg = new Quaternion?[10];
        readonly List<Renderer> rends = new List<Renderer>();

        /// Shattered or gone: not drawn until the next round.
        public bool Hidden;
        /// Overcharge's x-ray flicker: the body vanishes and the skeleton shows.
        public bool Xray;
        bool hidByMe;
        float act, rx, rz, yawS, liftS, phase, dyaw, dyawT;

        // tints (Roast's charring, Ice sculpture's frost): base colour towards c by k, glow from the texture dimmed
        Color tintC; float tintK, tintSmooth = -1; bool tintOn, tintWas;
        readonly Dictionary<Renderer, (Color c, Color e)> baseCols = new Dictionary<Renderer, (Color, Color)>();
        MaterialPropertyBlock mpb;

        static readonly List<FinisherPose> all = new List<FinisherPose>();

        void Awake() { f = GetComponent<Fighter>(); rig = GetComponent<CharacterRig>(); mpb = new MaterialPropertyBlock(); }
        void OnEnable() { all.Add(this); }
        void OnDisable() { all.Remove(this); }

        public static FinisherPose Of(Fighter f) => f ? f.GetComponent<FinisherPose>() : null;

        /// The way a downed body lies (radians, same convention as Fighter.yaw): its hit box runs along it.
        public float LyingYaw => act > 0 ? yawS * Mathf.Deg2Rad : f.yaw;

        public static void ResetAll() { foreach (var p in all) p.ResetVisual(); }

        public void ResetVisual()
        {
            Hidden = false; Xray = false; tintOn = false; act = 0; rx = rz = 0;
            if (hidByMe && rig) rig.SetVisible(true);
            hidByMe = false;
            if (Find()) { root.localScale = Vector3.one * Fighter.SC; RestoreTint(); }
        }

        bool Find()
        {
            if (root) return true;
            if (!rig || !rig.Built) return false;
            root = rig.transform.Find("root"); if (!root) return false;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < names.Length; i++) { J[i] = null; foreach (var t in all) if (t.name == names[i]) { J[i] = t; break; } }
            body = null; foreach (var t in all) if (t.name == "body") { body = t; break; }
            rends.Clear(); rends.AddRange(root.GetComponentsInChildren<Renderer>(true));
            baseCols.Clear();
            // read each texture's colours now, not in the middle of a move (one small GPU read-back per texture)
            foreach (var r in rends) { var m = r ? r.sharedMaterial : null; if (m && m.HasProperty("_BaseMap")) { var tex = m.GetTexture("_BaseMap"); if (tex) TexSampler.Average(tex); } }
            return true;
        }

        public List<Renderer> Renderers { get { Find(); return rends; } }
        public Transform Body { get { Find(); return body ? body : transform; } }

        /// web handW: a point 1 m down the arm from the shoulder (where the hand is), in the world.
        public static Vector3 Hand(Fighter f, bool left)
        {
            var p = Of(f);
            if (p && p.Find() && p.J[left ? 1 : 0]) return p.J[left ? 1 : 0].TransformPoint(new Vector3(0, -1.0f, 0));
            return f.pos + Vector3.up * 1.1f * Fighter.SC + f.Right * (left ? -.45f : .45f);
        }
        /// The arm's world rotation (gloves, paddles and the harpoon are held along it).
        public static Quaternion ArmRot(Fighter f, bool left)
        {
            var p = Of(f);
            if (p && p.Find() && p.J[left ? 1 : 0]) return p.J[left ? 1 : 0].rotation;
            return Quaternion.Euler(0, f.yaw * Mathf.Rad2Deg + 180, 0);
        }
        /// A point on the head (Dog pile's bites, stun effects).
        public static Vector3 HeadPos(Fighter f)
        {
            var p = Of(f);
            if (p && p.Find() && p.J[8]) return p.J[8].position + Vector3.up * .3f * Fighter.SC;
            return f.pos + Vector3.up * 2.0f * Fighter.SC;
        }

        public void Tint(Color c, float k, float smooth = -1) { tintC = c; tintK = Mathf.Clamp01(k); tintSmooth = smooth; tintOn = true; }

        // ------------------------------------------------------------------ the web game's angles in Unity's space
        static Quaternion WebRot(Vector3 r) => Quaternion.Euler(-r.x * Mathf.Rad2Deg, -r.y * Mathf.Rad2Deg, r.z * Mathf.Rad2Deg);
        static Quaternion WebX(float x) => Quaternion.Euler(-x * Mathf.Rad2Deg, 0, 0);
        static Quaternion WebXZ(float x, float z) => Quaternion.Euler(-x * Mathf.Rad2Deg, 0, z * Mathf.Rad2Deg);
        static float Damp(float a, float b, float l, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-l * dt));

        void LateUpdate()
        {
            if (!f || !rig || !Find()) return;
            float dt = Time.deltaTime;
            var F = Finishers.Of(f);
            FPose P = null; bool fin = F != null, isV = fin && F.v == f, down = false, crawl = false, dead = false;
            float yaw = f.yaw, lam = 22;
            if (fin)
            {
                P = isV ? (F.vP.on ? F.vP : null) : (F.aP.on ? F.aP : null);
                down = isV && F.vDown; yaw = isV ? F.vYaw : F.aYaw;
                if (P != null && P.snap > 0) lam = P.snap;
            }
            else if (f.downed && f.alive)
            {
                crawl = down = true;
                dyawT -= dt; if (dyawT <= 0 || act <= 0) { dyawT = .15f; dyaw = FinGeo.DownYaw(f, f.yaw); }
                yaw = dyaw;
            }
            else if (!f.alive && f.corpse) { dead = true; yaw = yawS * Mathf.Deg2Rad; }

            // visibility
            bool hide = Hidden || Xray;
            if (hide) { rig.SetVisible(false); hidByMe = true; }
            else if (hidByMe) { rig.SetVisible(true); hidByMe = false; }

            ApplyTint();

            bool on = fin || crawl || dead;
            if (act <= 0 && on) { rx = 0; rz = 0; yawS = f.yaw * Mathf.Rad2Deg; liftS = 0; for (int j = 0; j < J.Length; j++) if (J[j]) cur[j] = J[j].localRotation; }
            act = Mathf.MoveTowards(act, on ? 1 : 0, dt / (on ? .12f : .25f));
            if (act <= 0) { if (root.localScale.z != Fighter.SC) root.localScale = Vector3.one * Fighter.SC; return; }

            // ---- root: yaw, lean forward / back (rx), roll (rz), lifted off the ground when lying down
            if (P != null && P.root)
            {
                float k = 1 - Mathf.Exp(-(P.snap > 0 ? P.snap : 30) * dt);
                rx = Mathf.Lerp(rx, P.rx, k); rz = Mathf.Lerp(rz, P.rz, k); yaw = P.ry;
            }
            else
            {
                float target = dead ? 1.5f : down ? 1.3f : 0;
                rx = Damp(rx, target, dead ? 11 : 8, dt); rz = Damp(rz, 0, 8, dt);
            }
            yawS = Mathf.LerpAngle(yawS, yaw * Mathf.Rad2Deg, 1 - Mathf.Exp(-20 * dt));
            var rot = Quaternion.Euler(-rx * Mathf.Rad2Deg, yawS + 180, rz * Mathf.Rad2Deg);
            transform.rotation = act >= 1 ? rot : Quaternion.Slerp(transform.rotation, rot, act);
            liftS = Damp(liftS, down || dead ? .32f * Mathf.Clamp01(rx / 1.3f) : 0, 14, dt);
            transform.position += Vector3.up * liftS * act;

            // ---- joints
            for (int j = 0; j < tg.Length; j++) tg[j] = null;
            if (P != null)
            {
                if (P.ar) { tg[0] = WebRot(P.AR); tg[2] = WebX(-.18f); }
                if (P.al) { tg[1] = WebRot(P.AL); tg[3] = WebX(-.18f); }
                if (P.ll || P.lr) { tg[5] = WebX(P.ll ? P.LL : 0); tg[4] = WebX(P.lr ? P.LR : 0); tg[6] = tg[7] = Quaternion.identity; }
                if (P.head) tg[8] = WebX(P.HEAD);
            }
            if (isV) tg[9] = Quaternion.identity;                       // the victim's spine is straight (no aim lean)
            bool prone = crawl || dead || (isV && down);
            if (prone)
            {
                // web crawlPose: arms reaching forward in turn, elbows bent, head up; legs trailing with a small kick
                float sp = new Vector2(f.vel.x, f.vel.z).magnitude, a = 0;
                if (crawl) { if (f.grounded) phase += sp * dt * (Mathf.PI / (.7f + .18f * sp)); a = Mathf.Sin(phase) * Mathf.Clamp01(sp / 2); }
                if (dead) a = 0;
                if (tg[0] == null) { tg[0] = WebXZ(-2.55f + .4f * a, -.5f); tg[2] = WebX(-(.75f - .45f * a)); }
                if (tg[1] == null) { tg[1] = WebXZ(-2.55f - .4f * a, .5f); tg[3] = WebX(-(.75f + .45f * a)); }
                if (tg[8] == null) tg[8] = WebX(dead ? -.2f : -.5f);
                if (tg[4] == null) { tg[5] = WebXZ(.1f + .14f * a, .07f); tg[4] = WebXZ(.1f - .14f * a, -.07f); tg[7] = WebX(.22f + .3f * Mathf.Max(0, a)); tg[6] = WebX(.22f + .3f * Mathf.Max(0, -a)); }
                tg[9] = Quaternion.identity;
            }
            float kj = 1 - Mathf.Exp(-lam * dt);
            for (int j = 0; j < J.Length; j++)
            {
                var t = J[j]; if (!t) continue;
                var baseQ = t.localRotation;
                cur[j] = Quaternion.Slerp(cur[j], tg[j] ?? baseQ, kj);
                t.localRotation = act >= 1 ? cur[j] : Quaternion.Slerp(baseQ, cur[j], act);
            }

            // ---- squash (Pancake)
            if (P != null && P.hasScale) root.localScale = P.scale;
            else if (root.localScale.z != Fighter.SC) root.localScale = Vector3.one * Fighter.SC;
        }

        void ApplyTint()
        {
            if (!tintOn) { if (tintWas) RestoreTint(); return; }
            tintWas = true;
            foreach (var r in rends)
            {
                if (!r) continue; var m = r.sharedMaterial; if (!m) continue;
                if (!baseCols.TryGetValue(r, out var b))
                {
                    var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                    if (rig.tint != Color.white) c *= rig.tint;
                    var e = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
                    b = (c, e); baseCols[r] = b;
                }
                r.GetPropertyBlock(mpb);
                var col = Color.Lerp(b.c, tintC, tintK); col.a = b.c.a;
                mpb.SetColor("_BaseColor", col);
                mpb.SetColor("_EmissionColor", b.e * (1 - tintK));
                if (tintSmooth >= 0) mpb.SetFloat("_Smoothness", Mathf.Lerp(m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : .3f, tintSmooth, tintK));
                r.SetPropertyBlock(mpb);
            }
            tintOn = false;   // the move sets it again every step it wants it
        }

        void RestoreTint()
        {
            tintWas = false;
            foreach (var r in rends)
            {
                if (!r) continue;
                if (rig && rig.tint != Color.white && r.sharedMaterial && r.sharedMaterial.HasProperty("_BaseColor"))
                { mpb.Clear(); mpb.SetColor("_BaseColor", r.sharedMaterial.GetColor("_BaseColor") * rig.tint); r.SetPropertyBlock(mpb); }
                else r.SetPropertyBlock(null);
            }
        }

        /// A point on the front of the chest facing `towards` (Heartbreaker's hole): the furthest body vertex that way, near chest height.
        public Vector3 ChestFront(Vector3 centre, Vector3 towards)
        {
            float best = .18f; var dir = towards.normalized; var side = Vector3.Cross(Vector3.up, dir);
            foreach (var r in Renderers)
            {
                var mf = r ? r.GetComponent<MeshFilter>() : null; var mesh = mf ? mf.sharedMesh : null;
                if (!mesh || !mesh.isReadable || !r.gameObject.activeInHierarchy) continue;
                var M = r.localToWorldMatrix; var v = mesh.vertices; int step = Mathf.Max(1, v.Length / 3000);
                for (int i = 0; i < v.Length; i += step)
                {
                    var p = M.MultiplyPoint3x4(v[i]) - centre;
                    if (Mathf.Abs(p.y) > .14f || Mathf.Abs(Vector3.Dot(p, side)) > .12f) continue;
                    float d = Vector3.Dot(p, dir); if (d > best && d < .6f) best = d;
                }
            }
            return centre + dir * (best + .01f);
        }
    }
}
