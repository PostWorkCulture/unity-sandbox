// Builds any of the web game's characters from the exported parts (torso, head, arms with elbows, legs with knees)
// and drives a simple procedural run cycle. Guns are held the way the web game holds them (holdGun): the detailed model
// is posed in the body and both arms reach for it with two-bone IK; the sword swings with the web game's key poses.
// The full foot-planting animation comes in phase 5.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class CharacterRig : MonoBehaviour
    {
        public Fighter fighter;
        public Arsenal arsenal;
        public MaterialLibrary library;
        public Armory armory;
        [Tooltip("Character id from the web game (wednesday, crusty, angel, antihero, jet, ghost, dracula, astronaut, lilpink, brute, blaze, blink, tank, frost, patch, thorn, thor, isaac)")]
        public string characterId = "wednesday";

        Transform root, flip, legL, legR, kneeL, kneeR, armL, armR, elbowL, elbowR, head, upper, upIn;
        CharJson cj;
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<Renderer> weaponRenderers = new List<Renderer>();
        readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
        float phase, runBlend, airBlend, lean, hRun;
        bool visible = true;
        public bool Built => root;
        public string BuiltId { get; private set; }

        // the weapon currently shown
        string shownId; Transform weaponT; WeaponJson wj;

        void Start() { Build(characterId); }

        public void Build(string id)
        {
            if (string.IsNullOrEmpty(id)) id = characterId;
            if (root && BuiltId == id) return;
            var e = armory ? armory.CharEntry(id) : null;
            if (e == null || !e.json || !e.bin || !library) { Debug.LogWarning("Street Life: no exported character '" + id + "'"); return; }
            if (root) { Destroy(root.gameObject); root = null; }
            renderers.Clear(); weaponRenderers.Clear(); rest.Clear(); shownId = null; weaponT = null; wj = null;
            characterId = id; BuiltId = id;
            if (fighter && armory) fighter.ApplyCharacter(armory.Char(id));

            cj = JsonUtility.FromJson<CharJson>(e.json.text);
            var bin = e.bin.bytes;
            var made = new Transform[cj.nodes.Length];
            for (int i = 0; i < cj.nodes.Length; i++)
            {
                var n = cj.nodes[i];
                var go = new GameObject(string.IsNullOrEmpty(n.name) ? "part" + i : n.name);
                var t = go.transform;
                t.SetParent(n.parent >= 0 ? made[n.parent] : transform, false);
                t.localPosition = new Vector3(n.pos[0], n.pos[1], n.pos[2]);
                t.localRotation = new Quaternion(n.rot[0], n.rot[1], n.rot[2], n.rot[3]);
                t.localScale = new Vector3(n.scl[0], n.scl[1], n.scl[2]);
                made[i] = t;
                if (n.meshes == null) continue;
                foreach (var mi in n.meshes)
                {
                    var wm = cj.meshes[mi];
                    var mgo = n.meshes.Length == 1 ? go : new GameObject("mesh" + mi);
                    if (mgo != go) mgo.transform.SetParent(t, false);
                    mgo.AddComponent<MeshFilter>().sharedMesh = MeshIO.Build(bin, wm, id + mi);
                    var mr = mgo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = wm.mat >= 0 && wm.mat < library.character.Length ? library.character[wm.mat] : library.unlitBase;
                    mr.shadowCastingMode = wm.cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderers.Add(mr);
                }
            }
            root = made[0];
            root.localScale = Vector3.one * Fighter.SC;
            flip = Find("flip"); head = Find("headG"); upper = Find("up"); upIn = Find("upIn");
            legL = Find("lL.p"); legR = Find("lR.p"); kneeL = Find("lL.k"); kneeR = Find("lR.k");
            armL = Find("aL.p"); armR = Find("aR.p"); elbowL = Find("aL.e"); elbowR = Find("aR.e");
            foreach (var t in new[] { legL, legR, kneeL, kneeR, armL, armR, elbowL, elbowR, head, upper })
                if (t) rest[t] = t.localRotation;
            bool v = visible; visible = !v; SetVisible(v);
        }

        Transform Find(string n)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
            return null;
        }

        public void SetVisible(bool v)
        {
            if (v == visible) return; visible = v;
            foreach (var r in renderers) if (r) r.enabled = v;
            foreach (var r in weaponRenderers) if (r) r.enabled = v;
        }

        void RotX(Transform t, float deg) { RotXZ(t, deg, 0); }
        void RotXZ(Transform t, float x, float z) { if (t) t.localRotation = (rest.TryGetValue(t, out var r) ? r : Quaternion.identity) * Quaternion.Euler(x, 0, z); }

        // ------------------------------------------------------------------ weapon shown in the hands
        void ShowWeapon(string id)
        {
            if (id == shownId) return;
            shownId = id;
            if (weaponT) Destroy(weaponT.gameObject);
            weaponT = null; wj = null; weaponRenderers.Clear();
            if (string.IsNullOrEmpty(id) || !armory || !root) return;
            wj = armory.WeaponModel(id);
            if (wj == null || wj.meshes == null || wj.meshes.Length == 0) { wj = null; return; }
            bool held = wj.mode == "held" && cj.arms != null && cj.arms.elbows && upIn && armR && elbowR;
            Transform parent = held ? upIn : (wj.low != null && !string.IsNullOrEmpty(wj.low.parent) ? Find(wj.low.parent) : armR);
            if (!parent) parent = armR ? armR : root;
            weaponT = armory.MakeWeapon(id, parent, weaponRenderers).transform;
            if (!held)
            {
                // the web game hangs it off the hand at the character's own hand height, pointing forward
                bool low = wj.low != null && wj.low.pos != null && wj.low.pos.Length == 3 && wj.low.rot != null && wj.low.rot.Length == 4;
                weaponT.localPosition = new Vector3(0, cj.handY, low ? wj.low.pos[2] : -.06f);
                weaponT.localRotation = low ? new Quaternion(wj.low.rot[0], wj.low.rot[1], wj.low.rot[2], wj.low.rot[3]) : Web.Q(Web.EulerYXZ(Mathf.PI / 2, 0, 0));
                if (!low && wj.hold != null && wj.hold.s > 0) weaponT.localScale = Vector3.one * .55f;   // two-handed model on a character without elbows (Isaac): smaller, in one hand
            }
            foreach (var r in weaponRenderers) r.enabled = visible;
        }

        /// World position of the muzzle (second = the left pistol of the Dual Pistols).
        public Vector3 Muzzle(bool second = false)
        {
            if (weaponT && wj != null && wj.mode == "held" && wj.hold != null && weaponT.parent == upIn)
            {
                float s = wj.hold.s > 0 ? wj.hold.s : 1;
                var local = Web.V(new Vector3(second && wj.hold.twin ? -wj.hold.dx * s : 0, wj.hold.muzY * s, wj.hold.muzZ * s));
                return weaponT.TransformPoint(local);
            }
            if (weaponT && wj != null)
            {
                var c = (Web.A(wj.min) + Web.A(wj.max)) * .5f; c.z = Mathf.Min(wj.min[2], wj.max[2]);
                return weaponT.TransformPoint(c);
            }
            var f = fighter;
            return f ? f.VisualHead + f.Aim * .9f + Vector3.down * .3f : transform.position;
        }

        void LateUpdate()
        {
            if (!root || !fighter) return;
            float dt = Time.deltaTime;
            var f = fighter;
            transform.position = f.VisualPos;
            transform.rotation = Quaternion.Euler(0, f.yaw * Mathf.Rad2Deg + 180f, 0);   // the model faces -z

            // speed relative to the body (forward / sideways) for the run cycle
            var v = new Vector3(f.vel.x, 0, f.vel.z);
            float spd = v.magnitude, maxSpd = 11.25f * Fighter.MOVE_MUL * f.charSpeed * 1.2f;
            float fwd = Vector3.Dot(v, f.Forward);
            bool air = !f.grounded && f.onLadder == null;
            runBlend = Mathf.MoveTowards(runBlend, f.grounded ? Mathf.Clamp01(spd / 4f) : 0, dt * 6);
            airBlend = Mathf.MoveTowards(airBlend, air ? 1 : 0, dt * 8);
            float stride = Mathf.Lerp(7f, 4.2f, Mathf.Clamp01(spd / maxSpd));   // metres per full cycle
            phase += spd / stride * Mathf.PI * 2 * dt * (fwd < -1 ? -1 : 1);
            float s = Mathf.Sin(phase), amp = Mathf.Lerp(0, 46, runBlend);

            // legs swing (positive x = leg forward on this model), knees bend on the back swing
            float tuck = airBlend * 28;
            RotX(legL, s * amp + tuck * .6f); RotX(legR, -s * amp + tuck * .3f);
            RotX(kneeL, -Mathf.Max(0, -s) * amp * 1.3f - tuck - f.cv * 70);
            RotX(kneeR, -Mathf.Max(0, s) * amp * 1.3f - tuck * 1.4f - f.cv * 70);
            if (f.cv > 0) { RotX(legL, s * amp + 55 * f.cv); RotX(legR, -s * amp + 55 * f.cv); }
            // arms swing opposite, elbows bent while running
            float arm = s * amp * .8f;
            RotXZ(armL, -arm - airBlend * 20, -6 - airBlend * 18);
            RotXZ(armR, arm - airBlend * 20, 6 + airBlend * 18);
            RotX(elbowL, Mathf.Lerp(8, 55, runBlend)); RotX(elbowR, Mathf.Lerp(8, 55, runBlend));
            // lean into the run, bob, head follows the aim a little
            lean = Mathf.Lerp(lean, Mathf.Clamp(fwd / maxSpd, -1, 1) * 9, 1 - Mathf.Exp(-8 * dt));
            float leanDeg = lean + f.cv * 12;
            RotX(upper, -leanDeg);
            RotX(head, f.pitch * Mathf.Rad2Deg * .35f);
            if (flip)
            {
                float bob = Mathf.Abs(Mathf.Cos(phase)) * .09f * runBlend;
                flip.localPosition = new Vector3(0, 1.25f + bob - f.cv * .32f, 0);
            }

            // weapon in the hands
            var a = arsenal;
            bool busy = !f.alive || f.onLadder != null;
            string want = a && !busy ? a.ShownWeapon : null;
            ShowWeapon(want);
            if (weaponT && wj != null)
            {
                weaponT.gameObject.SetActive(true);
                if (wj.mode == "held" && weaponT.parent == upIn) HoldGun(f, a, dt, spd, leanDeg * Mathf.Deg2Rad);
                else if (shownId == "sword") SwordPose(f, a);
                else if (wj.mode == "thrown") ThrowPose(f, a);
                else ArmForward(armR, f.pitch);
            }
        }

        // ------------------------------------------------------------------ the web game's holdGun, in its own (right-handed) space
        // Everything below works in web-game coordinates inside the upper body (upIn), then converts once (z negated).
        void HoldGun(Fighter f, Arsenal a, float dt, float sp, float leanRad)
        {
            var H = wj.hold; float s = H.s > 0 ? H.s : 1;
            Vector3 sh = Web.V(armR.localPosition); float sx = Mathf.Abs(sh.x), sy = sh.y;
            bool fired = a && a.SinceShot < .6f, tw = H.twin, shd = H.sh;
            float runK = f.grounded && sp > 6.5f && !f.ads && !fired && !f.crouching ? 1 : 0;
            hRun = Mathf.Lerp(hRun, runK, 1 - Mathf.Exp(-8 * dt)); float r = hRun;
            float rl = a && a.reloadT > 0 ? Mathf.Sin(Mathf.Min(1, 1 - a.reloadT / Mathf.Max(.01f, a.reloadMax)) * Mathf.PI) : 0;
            float sw = a && a.switchT > 0 ? a.switchT / .35f : 0;
            float p = f.pitch, bob = f.grounded ? Mathf.Sin(phase * 2) * Mathf.Clamp(sp / 9, 0, 1) : 0, k = a ? a.kick : 0;
            float gx = sx * (tw ? .3f : shd ? .62f : .22f + .15f * r);
            float gy = sy - (shd ? .12f : tw ? .42f : .24f) - (tw ? .25f : .4f) * r - .12f * rl - .35f * sw + bob * (.035f + .03f * r);
            float gz = (tw ? .55f : shd ? .2f : .36f) + .06f * r - .1f * rl - .07f * k;
            var rot = Web.EulerYXZ(-p * (1 - r * .7f) - leanRad + (tw ? .7f : .62f) * r + .32f * rl + .7f * sw - .06f * k * (shd ? 3 : 1),
                                   tw ? -.04f : -.06f - .55f * r - .15f * rl,
                                   tw ? 0 : .22f * r + .5f * rl);
            Vector3 grip = new Vector3(gx, gy, gz);
            Vector3 hr = new Vector3(-H.r[0] * s, H.r[1] * s, H.r[2] * s);
            Vector3 pos = grip - rot * hr;
            weaponT.localPosition = Web.V(pos); weaponT.localRotation = Web.Q(rot); weaponT.localScale = Vector3.one;

            // left hand: handguard (kept within reach), sliding to the magazine and down (mag out) during a reload; Dual Pistols: the second grip
            Vector3 lt = tw ? Web.A(H.l) : new Vector3(H.l[0], H.l[1], Mathf.Min(H.l[2], H.r[2] + .62f));
            if (a && a.reloadT > 0 && !tw)
            {
                float u = 1 - a.reloadT / Mathf.Max(.01f, a.reloadMax);
                Vector3 mag = new Vector3(0, H.r[1] - .12f, H.r[2] + .3f), outp = new Vector3(0, H.r[1] - .7f, H.r[2] + .22f);
                float w = Mathf.Clamp01(u < .25f ? u / .25f : u < .75f ? 1 : 1 - (u - .75f) / .25f);
                lt = Vector3.Lerp(lt, u > .3f && u < .6f ? outp : mag, w);
            }
            Vector3 tl = pos + rot * new Vector3(-lt.x * s, lt.y * s, lt.z * s);
            Reach(armR, elbowR, cj.arms.R, grip, new Vector3(.7f, -1, -.35f));
            Reach(armL, elbowL, cj.arms.L, tl, new Vector3(-.9f, -1, -.2f));
        }

        // two passes so the hand itself (not the wrist) lands on the target, then the final solve
        static void Reach(Transform p, Transform e, WArm A, Vector3 T, Vector3 pole)
        {
            if (!p || !e || A == null || A.hand == null) return;
            Vector3 t = T;
            for (int it = 0; it < 2; it++) { ArmIK(p, e, A, t, pole, out var X, out var Z); t = T - X * A.hand[0] - Z * A.hand[2]; }
            ArmIK(p, e, A, t, pole, out _, out _);
        }

        static void ArmIK(Transform p, Transform e, WArm A, Vector3 T, Vector3 pole, out Vector3 X, out Vector3 Z)
        {
            Vector3 S = Web.V(p.localPosition);
            float a = A.up, b = A.fore;
            Vector3 d = T - S; float L = d.magnitude; Vector3 n = L > 1e-6f ? d / L : Vector3.down;
            L = Mathf.Clamp(L, Mathf.Abs(a - b) + .02f, a + b - .002f);
            Vector3 pp = (pole - n * Vector3.Dot(n, pole)).normalized;
            float cA = Mathf.Clamp((a * a + L * L - b * b) / (2 * a * L), -1, 1), sA = Mathf.Sqrt(1 - cA * cA);
            Vector3 u = n * cA + pp * sA, el = S + u * a, v = (n * L + S - el).normalized;
            Vector3 y = -u, z = v - u * Vector3.Dot(u, v); if (z.sqrMagnitude < 1e-6f) z = pp; z.Normalize();
            X = Vector3.Cross(y, z); Z = z;
            p.localRotation = Web.Q(Quaternion.LookRotation(z, y));
            e.localRotation = Web.Q(Quaternion.AngleAxis(-Mathf.Acos(Mathf.Clamp(Vector3.Dot(u, v), -1, 1)) * Mathf.Rad2Deg, Vector3.right));
        }

        // ------------------------------------------------------------------ sword: the web game's key poses (SWK), arm aimed with the look
        struct SwKey { public float t, tw; public Vector3 A, D; public SwKey(float t, Vector3 a, Vector3 d, float tw) { this.t = t; A = a; D = d; this.tw = tw; } }
        static readonly SwKey SwRest = new SwKey(0, new Vector3(.3f, -.35f, .88f), new Vector3(.35f, .9f, -.25f), 0);
        static readonly SwKey[] SwF = { new SwKey(.16f, new Vector3(.55f, .65f, .25f), new Vector3(-.2f, .45f, -.85f), .32f), new SwKey(.3f, new Vector3(.1f, .35f, .95f), new Vector3(-.35f, .55f, .75f), 0), new SwKey(.45f, new Vector3(-.65f, -.45f, .6f), new Vector3(-.75f, -.55f, .35f), -.42f) };
        static readonly SwKey[] SwB = { new SwKey(.16f, new Vector3(-.6f, -.4f, .65f), new Vector3(-.7f, -.45f, .4f), -.36f), new SwKey(.3f, new Vector3(.1f, .2f, .98f), new Vector3(.4f, .6f, .7f), 0), new SwKey(.45f, new Vector3(.6f, .5f, .6f), new Vector3(.6f, .75f, .25f), .36f) };

        void SwordPose(Fighter f, Arsenal a)
        {
            if (!armR) return;
            float t = a && a.swingT > 0 ? 1 - a.swingT / .32f : 1;
            var keys = a && a.swingLeft ? SwB : SwF;
            var seq = new List<SwKey> { SwRest }; seq.AddRange(keys); var end = SwRest; end.t = 1; seq.Add(end);
            int i = 0; while (i < seq.Count - 2 && t > seq[i + 1].t) i++;
            var P = seq[i]; var Q = seq[i + 1];
            float u0 = Mathf.Clamp01((t - P.t) / Mathf.Max(1e-4f, Q.t - P.t)), u = u0 * u0 * (3 - 2 * u0);
            Vector3 A = Vector3.Lerp(P.A, Q.A, u).normalized, D = Vector3.Lerp(P.D, Q.D, u).normalized;
            float sxs = Mathf.Sign(Web.V(armR.localPosition).x); if (sxs == 0) sxs = 1;
            A.x *= sxs; D.x *= sxs;
            float ph = -f.pitch * .55f, c = Mathf.Cos(ph), sn = Mathf.Sin(ph);
            A = new Vector3(A.x, A.y * c - A.z * sn, A.y * sn + A.z * c); D = new Vector3(D.x, D.y * c - D.z * sn, D.y * sn + D.z * c);
            D = (D - A * Vector3.Dot(D, A)).normalized;
            armR.localRotation = Web.Q(Quaternion.LookRotation(D, -A));
            weaponT.localRotation = Quaternion.identity;
            if (armL) armL.localRotation = Web.Q(Web.EulerYXZ(-1.15f - f.pitch * .3f, .3f, -.25f * sxs));
            if (elbowR) elbowR.localRotation = Quaternion.identity;
            if (elbowL) elbowL.localRotation = Quaternion.identity;
            transform.rotation *= Quaternion.Euler(0, -Mathf.Lerp(P.tw, Q.tw, u) * sxs * Mathf.Rad2Deg, 0);
        }

        void ArmForward(Transform arm, float pitch)
        {
            if (!arm) return;
            arm.localRotation = Web.Q(Web.EulerYXZ(-Mathf.PI / 2 - pitch, 0, 0));
            if (elbowR) elbowR.localRotation = Quaternion.identity;
        }

        // lethals: wind up behind the head, then sweep through as it leaves the hand
        void ThrowPose(Fighter f, Arsenal a)
        {
            float t = a ? a.ThrowPhase : 1;
            bool show = a && a.throwT > 0;
            if (weaponT) weaponT.gameObject.SetActive(show);
            if (!show || !armR) return;
            float x = t < .5f ? Mathf.Lerp(-Mathf.PI * .6f, -Mathf.PI * 1.05f, t / .5f) : Mathf.Lerp(-Mathf.PI * 1.05f, -Mathf.PI * .35f, (t - .5f) / .5f);
            armR.localRotation = Web.Q(Web.EulerYXZ(x - f.pitch * .5f, 0, 0));
            if (elbowR) elbowR.localRotation = Quaternion.identity;
        }
    }

    /// The web game's space is right-handed (z toward the viewer); Unity's is left-handed. Points and rotations convert by negating z.
    public static class Web
    {
        public static Vector3 V(Vector3 u) => new Vector3(u.x, u.y, -u.z);
        public static Quaternion Q(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);
        public static Vector3 A(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        /// three.js Euler with order 'YXZ' (radians), as a quaternion in web space
        public static Quaternion EulerYXZ(float x, float y, float z) =>
            Quaternion.AngleAxis(y * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(z * Mathf.Rad2Deg, Vector3.forward);
    }
}
