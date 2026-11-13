// Builds Wednesday from the parts exported from the web game (torso, head, arms with elbows, legs with knees,
// hair and chain) and drives a simple procedural run cycle. The full foot-planting animation comes in phase 5.
// Owned by the weapons/characters chat from phase 4 (the match chat added the tint and the knocked-out topple).
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class CharacterRig : MonoBehaviour
    {
        public Fighter fighter;
        public MaterialLibrary library;
        public TextAsset charJson, charBin;
        [Tooltip("Colour multiplier (the bot is tinted until other characters are added)")]
        public Color tint = Color.white;
        float down;

        Transform root, flip, legL, legR, kneeL, kneeR, armL, armR, elbowL, elbowR, head, upper;
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
        float phase, runBlend, airBlend, landKick, lean;
        bool visible = true;
        public bool Built => root;
        public bool Visible => visible;

        void Start() { Build(); }

        public void Build()
        {
            if (root || !charJson || !charBin || !library) return;
            var cj = JsonUtility.FromJson<CharJson>(charJson.text);
            var bin = charBin.bytes;
            var made = new Transform[cj.nodes.Length];
            var skip = new bool[cj.nodes.Length];
            for (int i = 0; i < cj.nodes.Length; i++)
            {
                var n = cj.nodes[i];
                // the export caught the M4 Wednesday was holding (a hidden low-poly copy in the right hand, and the held model under the
                // upper body, mirrored); weapons are built separately now (WeaponPose), so leave both out
                if (n.parent >= 0 && skip[n.parent]) { skip[i] = true; continue; }
                bool zeroScale = n.scl[0] == 0 && n.scl[1] == 0 && n.scl[2] == 0;
                bool heldGun = n.scl[0] < 0 && string.IsNullOrEmpty(n.name) && n.parent >= 0 && cj.nodes[n.parent].name == "upIn";
                if (zeroScale || heldGun) { skip[i] = true; continue; }
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
                    mgo.AddComponent<MeshFilter>().sharedMesh = MeshIO.Build(bin, wm, "wed" + mi);
                    var mr = mgo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = wm.mat < library.character.Length ? library.character[wm.mat] : library.unlitBase;
                    mr.shadowCastingMode = wm.cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderers.Add(mr);
                }
            }
            root = made[0];
            root.localScale = Vector3.one * Fighter.SC;
            flip = Find("flip"); head = Find("headG"); upper = Find("up");
            legL = Find("lL.p"); legR = Find("lR.p"); kneeL = Find("lL.k"); kneeR = Find("lR.k");
            armL = Find("aL.p"); armR = Find("aR.p"); elbowL = Find("aL.e"); elbowR = Find("aR.e");
            foreach (var t in new[] { legL, legR, kneeL, kneeR, armL, armR, elbowL, elbowR, head, upper })
                if (t) rest[t] = t.localRotation;
            if (tint != Color.white)
            {
                var mpb = new MaterialPropertyBlock();
                foreach (var r in renderers)
                {
                    var m = r.sharedMaterial; if (!m || !m.HasProperty("_BaseColor")) continue;
                    mpb.Clear(); mpb.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint); r.SetPropertyBlock(mpb);
                }
            }
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
        }

        void RotX(Transform t, float deg) { RotXZ(t, deg, 0); }
        void RotXZ(Transform t, float x, float z) { if (t) t.localRotation = (rest.TryGetValue(t, out var r) ? r : Quaternion.identity) * Quaternion.Euler(x, 0, z); }

        void LateUpdate()
        {
            if (!root || !fighter) return;
            float dt = Time.deltaTime;
            var f = fighter;
            if (!gameObject.activeInHierarchy) return;
            transform.position = f.VisualPos;
            transform.rotation = Quaternion.Euler(0, f.yaw * Mathf.Rad2Deg + 180f, 0);   // the model faces -z
            // knocked out: topple over backwards (a proper ragdoll is the characters chat's job)
            down = Mathf.MoveTowards(down, f.alive ? 0 : 1, dt * (f.alive ? 6 : 2.5f));
            if (down > 0) transform.rotation *= Quaternion.Euler(88f * down * down, 0, 0);

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
            RotX(upper, -(lean + f.cv * 12));
            RotX(head, f.pitch * Mathf.Rad2Deg * .35f);
            if (flip)
            {
                float bob = Mathf.Abs(Mathf.Cos(phase)) * .09f * runBlend;
                flip.localPosition = new Vector3(0, 1.25f + bob - f.cv * .32f, 0);
            }
        }
    }
}
