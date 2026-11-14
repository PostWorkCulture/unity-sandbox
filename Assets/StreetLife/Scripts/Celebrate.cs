// Round-win celebrations (match chat), from the web game's celebrate(..., false) / updCelebrate 'gesture':
// winning a round (not by a finishing move, and not the match point: that's the crowning) gives the winners a quick
// fist pump, a two-fisted pump or a little jig for 2.4 s; two winners on a team high-five instead (each turns to the
// other and raises the hand on that side). Poses are the web game's angles, mirrored into Unity's space the way
// FinisherPose does it, applied after the weapon pose and blended in and out.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(1001)]
    public class Celebrate : MonoBehaviour
    {
        static Celebrate I;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();   // RoundManager.Reboot runs this again after every scene reload
        static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("Celebrate").AddComponent<Celebrate>();
        }

        class Who
        {
            public Fighter f; public string move; public float k; public Fighter mate; public bool right;
            public Transform aR, aL, eR, eL, lR, lL, head; public float blend;
        }
        readonly List<Who> who = new List<Who>();
        float t; bool on; float lastFinEnd = -99;
        static readonly string[] Gestures = { "punch", "jig", "punch2" };

        void OnEnable() { RoundManager.RoundEnded += OnRoundEnded; RoundManager.RoundStarted += OnRound; Finishers.Ended += OnFin; }
        void OnDisable() { RoundManager.RoundEnded -= OnRoundEnded; RoundManager.RoundStarted -= OnRound; Finishers.Ended -= OnFin; if (I == this) I = null; }
        void OnFin(Finisher f) { lastFinEnd = Time.time; }
        void OnRound(int r) { who.Clear(); on = false; }

        void OnRoundEnded(int round, int winner)
        {
            var rm = RoundManager.Instance;
            who.Clear(); on = false;
            if (!rm || winner < 0 || rm.matchOver) return;
            bool byFin = Time.time - lastFinEnd < 1.5f; foreach (var f in rm.fighters) if (f && f.InFinisher) byFin = true;
            if (byFin) return;
            foreach (var f in rm.fighters)
                if (f && f.team == winner && f.alive && !f.downed && f.gameObject.activeInHierarchy) who.Add(Find(f));
            who.RemoveAll(w => w == null);
            if (who.Count == 0) return;
            for (int i = 0; i < who.Count; i++)
            {
                var w = who[i]; w.k = Random.value * 6;
                w.move = who.Count > 1 && i < 2 ? "highfive" : Gestures[Random.Range(0, Gestures.Length)];
            }
            if (who.Count > 1) { who[0].mate = who[1].f; who[1].mate = who[0].f; who[0].right = true; who[1].right = false; }
            t = 0; on = true;
        }

        static Who Find(Fighter f)
        {
            var rig = f.GetComponent<CharacterRig>(); if (!rig || !rig.Built) return null;
            var w = new Who { f = f };
            foreach (var x in rig.GetComponentsInChildren<Transform>(true))
                switch (x.name)
                {
                    case "aR.p": w.aR = x; break; case "aL.p": w.aL = x; break;
                    case "aR.e": w.eR = x; break; case "aL.e": w.eL = x; break;
                    case "lR.p": w.lR = x; break; case "lL.p": w.lL = x; break;
                    case "headG": w.head = x; break;
                }
            return w;
        }

        // the web game's angles in Unity's space (FinisherPose's convention)
        static Quaternion WebRot(float x, float y, float z) => Quaternion.Euler(-x * Mathf.Rad2Deg, -y * Mathf.Rad2Deg, z * Mathf.Rad2Deg);
        static Quaternion WebX(float x) => Quaternion.Euler(-x * Mathf.Rad2Deg, 0, 0);

        void LateUpdate()
        {
            if (!on) return;
            var rm = RoundManager.Instance;
            if (!rm || rm.state == RoundManager.State.Fight || rm.state == RoundManager.State.Countdown) { on = false; return; }
            t += Time.deltaTime;
            foreach (var w in who)
            {
                var f = w.f; if (!f || !f.alive || f.downed || f.InFinisher) continue;
                // in over .15 s, out from 2.25 to 2.4 s
                w.blend = Mathf.Clamp01(Mathf.Min(t / .15f, (2.4f - t) / .15f));
                if (w.blend <= 0) continue;
                f.pitch = 0;
                float s = Mathf.Sin(t * 12 + w.k);
                Quaternion? ar = null, al = null, ll = null, lr = null, hd = null;
                switch (w.move)
                {
                    case "highfive":
                        if (w.mate) { var d = w.mate.pos - f.pos; f.yaw = Mathf.Atan2(d.x, d.z); }
                        {
                            float up = t > .5f && t < 1.4f ? -2.7f : -1.7f;
                            if (w.right) { ar = WebRot(up, 0, .2f); al = WebRot(-.3f, 0, -.2f); }
                            else { al = WebRot(up, 0, -.2f); ar = WebRot(-.3f, 0, .2f); }
                        }
                        break;
                    case "punch": ar = WebRot(-2.9f + s * .35f, 0, .25f); al = WebRot(-.4f, 0, -.3f); hd = WebX(-.2f); break;
                    case "punch2": ar = WebRot(-2.9f + s * .35f, 0, .4f); al = WebRot(-2.9f - s * .35f, 0, -.4f); hd = WebX(-.25f); break;
                    default:
                        {
                            float b = Mathf.Sin(t * 9 + w.k);
                            ar = WebRot(-1.3f, 0, .8f + b * .4f); al = WebRot(-1.3f, 0, -.8f + b * .4f); ll = WebX(b * .4f); lr = WebX(-b * .4f); hd = WebX(b * .2f);
                        }
                        break;
                }
                Set(w.aR, ar, w.blend); Set(w.aL, al, w.blend);
                if (ar.HasValue) Set(w.eR, WebX(-.18f), w.blend);
                if (al.HasValue) Set(w.eL, WebX(-.18f), w.blend);
                Set(w.lL, ll, w.blend); Set(w.lR, lr, w.blend); Set(w.head, hd, w.blend);
            }
            if (t > 2.4f) on = false;
        }

        static void Set(Transform j, Quaternion? q, float k) { if (j && q.HasValue) j.localRotation = Quaternion.Slerp(j.localRotation, q.Value, k); }

        /// remote test: call Celebrate.Test [punch|punch2|jig|highfive]
        public static string Test(string a)
        {
            if (!I) return "no Celebrate";
            var rm = RoundManager.Instance; if (!rm) return "no rm";
            I.who.Clear();
            foreach (var f in rm.fighters) if (f && f.alive && f.team == rm.player.team) { var w = Find(f); if (w != null) I.who.Add(w); }
            foreach (var w in I.who) { w.move = string.IsNullOrEmpty(a) ? "punch" : a.Trim(); w.k = Random.value * 6; }
            if (I.who.Count > 1) { I.who[0].mate = I.who[1].f; I.who[1].mate = I.who[0].f; I.who[0].right = true; }
            I.t = 0; I.on = true; return $"celebrating ({I.who.Count}) {a}";
        }
    }
}
