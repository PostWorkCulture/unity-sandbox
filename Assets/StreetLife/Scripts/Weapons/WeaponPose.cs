// How the character holds and moves the weapon, ported from the web game's animFighter / holdGun / armIK / swordPose:
// both hands on the gun by two-bone IK (right hand on the grip, left on the handguard, sliding to the magazine during
// a reload), the gun lowered to a run carry, kicked by recoil, dipped during swaps; Dual Pistols one in each hand;
// the Sword held high with alternating diagonal slashes and a light trail; the throwing arm swing.
// All pose maths uses the web game's own (right-handed) numbers and is mirrored into Unity at the end.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(100)]   // after CharacterRig's run cycle, so the arms are ours
    public class WeaponPose : MonoBehaviour
    {
        static bool warnedNoArms;
        public Fighter fighter;
        public Weapons weapons;
        public CharacterRig rig;

        Transform root, upIn, aLp, aRp, aLe, aRe, swordG;
        Vector3 shR, shL;                    // shoulders (web space, in upIn)
        ArmJson armL, armR; float handY;
        class Held { public Transform rig; public HeldJson h; public List<Renderer> rends = new List<Renderer>(); public Transform[] flash; }
        readonly Dictionary<string, Held> held = new Dictionary<string, Held>();
        readonly List<Renderer> swordRends = new List<Renderer>();
        Held cur; float hRun, phase; bool built, lastVis = true, wasOn;
        public bool HeldVisible => cur != null && cur.rig && cur.rig.gameObject.activeSelf;

        void OnEnable() { CharacterSkin.Changed += OnSkin; }
        void OnDisable() { CharacterSkin.Changed -= OnSkin; }
        void OnSkin(CharacterRig r, string id) { if (r == rig) Forget(); }

        /// the rig was rebuilt (another character): drop everything that hung off the old joints and start again
        void Forget()
        {
            foreach (var h in held.Values) if (h != null && h.rig) Destroy(h.rig.gameObject);
            held.Clear(); swordRends.Clear(); if (swordG) Destroy(swordG.gameObject);
            cur = null; built = false; enabled = true; wasOn = false;
        }

        bool Build()
        {
            if (built && !upIn) Forget();
            if (built) return true;
            if (!rig || !rig.Built || !GunsDb.Loaded) return false;
            root = Find("root"); upIn = Find("upIn"); aLp = Find("aL.p"); aRp = Find("aR.p"); aLe = Find("aL.e"); aRe = Find("aR.e");
            if (!upIn || !aLp || !aRp || !aLe || !aRe) { built = true; enabled = false; if (!warnedNoArms) { warnedNoArms = true; Debug.LogWarning("Street Life: the character has no arm joints (Isaac); weapons won't be posed."); } return false; }
            // each character's own arm lengths and hand offsets (characters chat), Wednesday's when there are none
            var A = GunsDb.Data.arms; armL = A.aL; armR = A.aR; handY = A.handY;
            var cs = CharacterSet.Load(); var who = CharacterSkin.Of(rig);
            var ca = cs && !string.IsNullOrEmpty(who) ? cs.Arms(who) : null;
            if (ca != null && ca.L != null && ca.R != null && ca.L.hand != null && ca.R.hand != null && ca.R.hand.Length == 3)
            {
                armL = new ArmJson { up = ca.L.up, fore = ca.L.fore, hand = ca.L.hand };
                armR = new ArmJson { up = ca.R.up, fore = ca.R.fore, hand = ca.R.hand };
                handY = ca.R.hand[1] + .02f;   // the grip sits 2 cm above the hand point (Wednesday: -1.091 vs -1.111)
            }
            shR = Web.V(aRp.localPosition); shL = Web.V(aLp.localPosition);
            // the sword lives in the right hand, like the web game's gunModel('sword')
            swordG = new GameObject("Sword").transform; swordG.SetParent(aRp, false);
            swordG.localPosition = Web.V(new Vector3(0, handY, .06f));
            WeaponModels.Build(GunsDb.Data.sword.nodes, swordG, "dragon sword", -1, true, swordRends);
            swordG.gameObject.SetActive(false);
            built = true; return true;
        }

        Transform Find(string n) { foreach (var t in rig.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }

        Held GetHeld(string id)
        {
            if (held.TryGetValue(id, out var h)) return h;
            var hj = GunsDb.Held(id); if (hj == null) { held[id] = null; return null; }
            h = new Held { h = hj };
            h.rig = new GameObject("held " + id).transform; h.rig.SetParent(upIn, false);
            h.rig.localScale = new Vector3(-hj.s, hj.s, hj.s);   // mirrored across x, like the first-person view
            WeaponModels.Build(hj.nodes, h.rig, id, -1, true, h.rends);
            if (!hj.nf)
            {
                var xs = hj.twin ? new[] { 0f, hj.dx } : new[] { 0f };
                h.flash = new Transform[xs.Length];
                for (int i = 0; i < xs.Length; i++) h.flash[i] = WeaponModels.MakeFlash(h.rig, Web.V(new Vector3(xs[i], hj.muz[0], hj.muz[1])), .34f, -1, h.rends);
            }
            foreach (var r in h.rends) if (r) r.enabled = lastVis;
            h.rig.gameObject.SetActive(false);
            held[id] = h; return h;
        }

        public Vector3 HandWorld => built && aRp ? aRp.TransformPoint(Web.V(new Vector3(0, handY, .06f))) : fighter.Eye;
        public Vector3 MuzzleWorld(bool left)
        {
            if (cur == null || !cur.rig) return HandWorld;
            var h = cur.h; float x = left && h.twin ? h.dx : 0;
            return cur.rig.TransformPoint(Web.V(new Vector3(x, h.muz[0], h.muz[1])));
        }

        static float Damp(float a, float b, float l, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-l * dt));

        void LateUpdate()
        {
            if (!Build() || !fighter || !weapons) return;
            float dt = Time.deltaTime; var f = fighter; var w = weapons;
            string id = w.Gun;
            // down, in a finishing move or on a ladder: no gun in hand, and the arms are left to whoever is posing them
            bool on = f.alive && !f.downed && !f.InFinisher && f.onLadder == null;
            float sp = f.HorizontalSpeed; if (f.grounded) phase += sp * dt * 1.15f;

            // ---- base arm pose (both arms forward at the aim, swing for throws, dips for reload and swap)
            float relo = w.reloadT > 0 ? Mathf.Sin(Mathf.Min(1, 1 - w.reloadT / Mathf.Max(.01f, w.reloadMax)) * Mathf.PI) * .7f : 0;
            float sw = w.switchT > 0 ? w.switchT * 2 : 0;
            float sg = w.swingT > 0 ? Mathf.Sin((1 - w.swingT / .32f) * Mathf.PI) : 0;
            float p = f.pitch - relo - sw, k = w.kick * .12f;
            float twist = 0;
            if (on)
            {
                SetW(aRp, Web.EulerXYZ(-Mathf.PI / 2 - p - k + sg * .9f, -.08f - sg * 1.4f, 0));
                SetW(aLp, Web.EulerXYZ(-Mathf.PI / 2 - p + .1f - k, .62f, 0));
                if (id == "sword") twist = SwordPose(p);
                if (id == "duals") SetW(aLp, Web.EulerXYZ(-Mathf.PI / 2 - p - k * (w.dualL ? 1 : 0), .06f, 0));
            }
            if (root && (on || wasOn)) root.localRotation = Web.Q(Web.EulerXYZ(0, twist, 0));
            wasOn = on;

            // ---- the held gun and the two-handed IK
            var want = on && id != "sword" ? GetHeld(id) : null;
            if (cur != want) { if (cur != null && cur.rig) cur.rig.gameObject.SetActive(false); cur = want; if (cur != null) cur.rig.gameObject.SetActive(true); }
            swordG.gameObject.SetActive(on && id == "sword");
            if (cur != null) HoldGun(cur, dt, sp, p);
            else if (on) { SetW(aRe, Quaternion.identity); SetW(aLe, Quaternion.identity); }

            // muzzle flash on the held gun (hidden while aiming in first person: the view model has its own)
            if (cur != null && cur.flash != null)
                for (int i = 0; i < cur.flash.Length; i++)
                {
                    bool lit = w.flashT > 0 && !(id == "duals" && (i == 0) == w.dualL) && !(w.isPlayer && w.vmRaise > .3f);
                    cur.flash[i].gameObject.SetActive(lit);
                    if (lit) { cur.flash[i].localRotation = Quaternion.Euler(0, 0, Random.value * 170); cur.flash[i].localScale = Vector3.one * (.75f + Random.value * .5f); }
                }
            SwordTrail(on && id == "sword");

            // follow the character's visibility (the camera hides the body when it gets too close)
            bool vis = rig.Visible;
            if (vis != lastVis) { lastVis = vis; foreach (var hh in held.Values) if (hh != null) foreach (var r in hh.rends) if (r) r.enabled = vis; foreach (var r in swordRends) if (r) r.enabled = vis; }
        }

        static void SetW(Transform t, Quaternion webQ) { if (t) t.localRotation = Web.Q(webQ); }

        // ---------------------------------------------------------------- holdGun
        void HoldGun(Held H, float dt, float sp, float pAdj)
        {
            var f = fighter; var w = weapons; var hj = H.h;
            float s = hj.s; var Hr = Web.V(hj.hr); var Hl = Web.V(hj.hl);
            float sx = Mathf.Abs(shR.x), sy = shR.y;
            bool fired = Combat.Time - w.shotAt < .6f;
            float runK = f.grounded && sp > 6.5f && !f.ads && !fired && !f.crouching ? 1 : 0;
            hRun = Damp(hRun, runK, 8, dt); float r = hRun;
            float rl = w.reloadT > 0 ? Mathf.Sin(Mathf.Min(1, 1 - w.reloadT / Mathf.Max(.01f, w.reloadMax)) * Mathf.PI) : 0;
            float swp = w.switchT > 0 ? w.switchT / .35f : 0;
            float p = f.pitch, bob = f.grounded ? Mathf.Sin(phase * 2) * Mathf.Clamp01(sp / 9) : 0, k = w.kick;
            bool tw = hj.twin, shd = hj.sh;
            float gx = sx * (tw ? .3f : shd ? .62f : .22f + .15f * r);
            float gy = sy - (shd ? .12f : tw ? .42f : .24f) - (tw ? .25f : .4f) * r - .12f * rl - .35f * swp + bob * (.035f + .03f * r);
            float gz = (tw ? .55f : shd ? .2f : .36f) + .06f * r - .1f * rl - .07f * k;
            var q = Web.EulerYXZ(-p * (1 - r * .7f) + (tw ? .7f : .62f) * r + .32f * rl + .7f * swp - .06f * k * (shd ? 3 : 1),
                                 tw ? -.04f : -.06f - .55f * r - .15f * rl,
                                 tw ? 0 : .22f * r + .5f * rl);
            var sc = new Vector3(-s, s, s);
            var pos = new Vector3(gx, gy, gz) - q * new Vector3(-Hr.x * s, Hr.y * s, Hr.z * s);
            var M = Matrix4x4.TRS(pos, q, sc);
            H.rig.localPosition = Web.V(pos); H.rig.localRotation = Web.Q(q);
            Vector3 tR = M.MultiplyPoint3x4(Hr);
            var lt = tw ? Hl : new Vector3(Hl.x, Hl.y, Mathf.Min(Hl.z, Hr.z + .62f));
            if (w.reloadT > 0 && !tw)
            {
                float u = 1 - w.reloadT / Mathf.Max(.01f, w.reloadMax);
                var mag = new Vector3(0, Hr.y - .12f, Hr.z + .3f); var outp = new Vector3(0, Hr.y - .7f, Hr.z + .22f);
                float wgt = Mathf.Clamp01(u < .25f ? u / .25f : u < .75f ? 1 : 1 - (u - .75f) / .25f);
                lt = Vector3.Lerp(lt, u > .3f && u < .6f ? outp : mag, wgt);
            }
            Vector3 tL = M.MultiplyPoint3x4(lt);
            Reach(aRp, aRe, shR, armR, tR, new Vector3(.7f, -1, -.35f));
            Reach(aLp, aLe, shL, armL, tL, new Vector3(-.9f, -1, -.2f));
        }

        // arm IK with the hand offset folded in (two passes, as the web game does)
        void Reach(Transform P, Transform E, Vector3 S, ArmJson A, Vector3 T, Vector3 pole)
        {
            var t = T; Vector3 X, Z;
            for (int it = 0; it < 2; it++) { ArmIK(P, E, S, A, t, pole, out X, out Z); t = T - X * A.hand[0] - Z * A.hand[2]; }
            ArmIK(P, E, S, A, t, pole, out X, out Z);
        }

        static void ArmIK(Transform P, Transform E, Vector3 S, ArmJson A, Vector3 T, Vector3 pole, out Vector3 X, out Vector3 Z)
        {
            float a = A.up, b = A.fore;
            var d = T - S; float L = d.magnitude; var n = d / (L > 0 ? L : 1);
            L = Mathf.Clamp(L, Mathf.Abs(a - b) + .02f, a + b - .002f);
            var pp = (pole - n * Vector3.Dot(n, pole)).normalized;
            float cA = Mathf.Clamp((a * a + L * L - b * b) / (2 * a * L), -1, 1), sA = Mathf.Sqrt(1 - cA * cA);
            var u = n * cA + pp * sA; var e = S + u * a; var v = (n * L + S - e).normalized;
            var y = -u; var z = v - u * Vector3.Dot(u, v); if (z.sqrMagnitude < 1e-6f) z = pp; z.Normalize();
            X = Vector3.Cross(y, z); Z = z;
            SetW(P, Web.Basis(y, z));
            SetW(E, Web.EulerXYZ(-Mathf.Acos(Mathf.Clamp(Vector3.Dot(u, v), -1, 1)), 0, 0));
        }

        // ---------------------------------------------------------------- the sword: high guard, alternating diagonal slashes
        struct SwK { public float t, tw; public Vector3 A, D; public SwK(float t, Vector3 A, Vector3 D, float tw) { this.t = t; this.A = A; this.D = D; this.tw = tw; } }
        static readonly SwK SR = new SwK(0, new Vector3(.3f, -.35f, .88f), new Vector3(.35f, .9f, -.25f), 0);
        static readonly SwK[] SF = { new SwK(.16f, new Vector3(.55f, .65f, .25f), new Vector3(-.2f, .45f, -.85f), .32f), new SwK(.3f, new Vector3(.1f, .35f, .95f), new Vector3(-.35f, .55f, .75f), 0), new SwK(.45f, new Vector3(-.65f, -.45f, .6f), new Vector3(-.75f, -.55f, .35f), -.42f) };
        static readonly SwK[] SB = { new SwK(.16f, new Vector3(-.6f, -.4f, .65f), new Vector3(-.7f, -.45f, .4f), -.36f), new SwK(.3f, new Vector3(.1f, .2f, .98f), new Vector3(.4f, .6f, .7f), 0), new SwK(.45f, new Vector3(.6f, .5f, .6f), new Vector3(.6f, .75f, .25f), .36f) };

        float SwordPose(float p)
        {
            var w = weapons; float t = w.swingT > 0 ? 1 - w.swingT / .32f : 1;
            var seq = new List<SwK> { SR }; seq.AddRange(w.swL ? SB : SF); var end = SR; end.t = 1; seq.Add(end);
            int i = 0; while (i < seq.Count - 2 && t > seq[i + 1].t) i++;
            var P = seq[i]; var Q = seq[i + 1];
            float u0 = Mathf.Clamp01((t - P.t) / (Q.t - P.t)), u = u0 * u0 * (3 - 2 * u0);
            var A = Vector3.Lerp(P.A, Q.A, u).normalized; var D = Vector3.Lerp(P.D, Q.D, u).normalized;
            float sx = Mathf.Sign(shR.x); if (sx == 0) sx = 1; A.x *= sx; D.x *= sx;
            float ph = -p * .55f, c = Mathf.Cos(ph), s = Mathf.Sin(ph);
            A = new Vector3(A.x, A.y * c - A.z * s, A.y * s + A.z * c); D = new Vector3(D.x, D.y * c - D.z * s, D.y * s + D.z * c);
            D = (D - A * Vector3.Dot(D, A)).normalized;
            SetW(aRp, Web.Basis(-A, D));
            SetW(aLp, Web.EulerXYZ(-1.15f - p * .3f, .3f, -.25f * sx));
            return (P.tw + (Q.tw - P.tw) * u) * sx;
        }

        // a bright arc left behind the blade during the slash
        Mesh trail; MeshRenderer trailR; readonly List<(Vector3 a, Vector3 b, float t)> tp = new List<(Vector3, Vector3, float)>();
        void SwordTrail(bool on)
        {
            if (!trail)
            {
                var go = new GameObject("sword trail"); trail = new Mesh { name = "sword trail" }; trail.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = trail; trailR = go.AddComponent<MeshRenderer>(); trailR.sharedMaterial = GunFx.MakeMat(false, null);
                trailR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            const int N = 16; const float LIFE = .16f; float now = Combat.Time;
            float t = weapons.swingT > 0 ? 1 - weapons.swingT / .32f : 1;
            if (on && swordG && t > .12f && t < .52f) tp.Insert(0, (swordG.TransformPoint(Web.V(new Vector3(0, 0, .45f))), swordG.TransformPoint(Web.V(new Vector3(0, 0, 1.5f))), now));
            while (tp.Count > 0 && (tp.Count > N || now - tp[tp.Count - 1].t > LIFE)) tp.RemoveAt(tp.Count - 1);
            trail.Clear(); int n = tp.Count; if (n < 2) return;
            var v = new Vector3[n * 2]; var c = new Color[n * 2]; var ix = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Max(0, 1 - (now - tp[i].t) / LIFE) * (1 - i / (float)N);
                v[i * 2] = tp[i].a; v[i * 2 + 1] = tp[i].b; c[i * 2] = new Color(1, .85f, .45f, a * .55f); c[i * 2 + 1] = new Color(1, .97f, .85f, a * .95f);
                if (i < n - 1) { int q = i * 2; ix.AddRange(new[] { q, q + 1, q + 2, q + 1, q + 3, q + 2 }); }
            }
            trail.vertices = v; trail.colors = c; trail.uv = new Vector2[n * 2]; trail.SetTriangles(ix, 0); trail.RecalculateBounds();
        }
    }
}
