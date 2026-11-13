// Climbing a ladder, as in the web game: the body turns to face the rungs, the gun goes away (WeaponPose already drops it on a
// ladder), and hands and feet take turns reaching for the next rung as the fighter moves up or down. Eases in and out.
// A line-by-line port of the web game's ladder pose (index.html, "climbing a ladder"), for every fighter with a rig (bots too).
// Runs after CharacterRig (0) and WeaponPose (100); the finishers' pose (1000) still wins when someone is down.
// Added by CharacterSkin at every scene load.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(210)]
    public class CharacterClimb : MonoBehaviour
    {
        class St { public float k; public Ladder last; public Transform aLp, aRp, aLe, aRe, lLp, lRp, lLk, lRk, head; public Transform built; }
        readonly Dictionary<CharacterRig, St> state = new Dictionary<CharacterRig, St>();
        readonly List<CharacterRig> rigs = new List<CharacterRig>();
        float scanT;

        void LateUpdate()
        {
            float dt = Time.deltaTime; if (dt <= 0) return;
            if ((scanT -= Time.unscaledDeltaTime) <= 0) { scanT = 1; rigs.Clear(); rigs.AddRange(FindObjectsByType<CharacterRig>()); }
            foreach (var rig in rigs)
            {
                if (!rig || !rig.Built) continue;
                var f = rig.fighter ? rig.fighter : rig.GetComponent<Fighter>(); if (!f) continue;
                if (!state.TryGetValue(rig, out var s)) state[rig] = s = new St();
                var L = f.alive && !f.downed && !f.InFinisher ? f.onLadder : null;
                s.k = Damp(s.k, L != null ? 1 : 0, 10, dt); if (L != null) s.last = L;
                if (s.k <= .01f || s.last == null) continue;
                if (!Joints(rig, s)) continue;
                var La = s.last; float K = s.k;

                // turn to face the rungs
                float face = Mathf.Atan2(La.nx, La.nz), d = Mathf.DeltaAngle(f.yaw * Mathf.Rad2Deg, face * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                rig.transform.rotation = Quaternion.Euler(0, (f.yaw + d * K) * Mathf.Rad2Deg + 180f, 0);

                // hands and feet alternate with height: one rung every .35 m
                float a = Mathf.Sin((f.pos.y - La.y0) / .35f * Mathf.PI);
                Mix(s.aRp, Web.EulerXYZ(-2.75f + .55f * a, 0, -.08f), K);
                Mix(s.aLp, Web.EulerXYZ(-2.75f - .55f * a, 0, .08f), K);
                Mix(s.aRe, Quaternion.identity, K); Mix(s.aLe, Quaternion.identity, K);
                Mix(s.lLp, Web.EulerXYZ(-.95f * (.5f + .5f * a), 0, 0), K);
                Mix(s.lRp, Web.EulerXYZ(-.95f * (.5f - .5f * a), 0, 0), K);
                var kn = Web.EulerXYZ(.25f * a + .25f, 0, 0); Mix(s.lLk, kn, K); Mix(s.lRk, kn, K);
                Mix(s.head, Web.EulerXYZ(-.18f, 0, 0), K);
            }
        }

        /// remote test (Logs/streetlife-cmd.txt: "call CharacterClimb.Test ladder"): walks the player up the nearest ladder for 3 s
        public static string Test(string arg)
        {
            Fighter f = null; foreach (var p in FindObjectsByType<PlayerController>()) { f = p.GetComponent<Fighter>(); break; }
            var me = FindAnyObjectByType<CharacterClimb>();
            if (!f || !me || BoxWorld.Ladders.Count == 0) return "no player, climber or ladders";
            Ladder best = null; float bd = 1e9f;
            foreach (var L in BoxWorld.Ladders) { float dx = (L.x0 + L.x1) / 2 - f.pos.x, dz = (L.z0 + L.z1) / 2 - f.pos.z, dd = dx * dx + dz * dz; if (dd < bd) { bd = dd; best = L; } }
            me.StartCoroutine(me.Walk(f, best));
            return $"climbing ladder at {(best.x0 + best.x1) / 2:0.0},{(best.z0 + best.z1) / 2:0.0} from {best.y0:0.0} to {best.y1:0.0}";
        }

        System.Collections.IEnumerator Walk(Fighter f, Ladder L)
        {
            var pc = f.GetComponent<PlayerController>(); if (pc) pc.enabled = false;
            float cx = (L.x0 + L.x1) / 2, cz = (L.z0 + L.z1) / 2; bool ax = L.nx != 0;
            f.pos = new Vector3(ax ? L.rung - L.nx * .9f : cx, L.y0 + .02f, ax ? cz : L.rung - L.nz * .9f); f.vel = Vector3.zero;
            f.yaw = Mathf.Atan2(L.nx, L.nz) + .6f;   // looking a little off the ladder, so the body has to turn to it
            for (float t = 0; t < 3; t += Time.deltaTime) { f.inFwd = 1; f.inSide = 0; f.yaw = Mathf.Atan2(L.nx, L.nz) + .6f * (1 - Mathf.Min(1, t)); yield return null; }
            f.inFwd = 0; if (pc) pc.enabled = true;
        }

        static float Damp(float a, float b, float rate, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-rate * dt));
        static void Mix(Transform t, Quaternion webQ, float k) { if (t) t.localRotation = Quaternion.Slerp(t.localRotation, Web.Q(webQ), k); }

        // the joints (found again whenever the rig is rebuilt with another character)
        static bool Joints(CharacterRig rig, St s)
        {
            var root = rig.transform.Find("root");
            if (s.built == root && root) return s.aRp;
            s.built = root; s.aLp = s.aRp = s.aLe = s.aRe = s.lLp = s.lRp = s.lLk = s.lRk = s.head = null;
            if (!root) return false;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                switch (t.name)
                {
                    case "aL.p": s.aLp = t; break; case "aR.p": s.aRp = t; break; case "aL.e": s.aLe = t; break; case "aR.e": s.aRe = t; break;
                    case "lL.p": s.lLp = t; break; case "lR.p": s.lRp = t; break; case "lL.k": s.lLk = t; break; case "lR.k": s.lRk = t; break;
                    case "headG": s.head = t; break;
                }
            return s.aRp;
        }
    }
}
