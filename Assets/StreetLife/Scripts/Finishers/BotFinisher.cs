// Finishers chat: what the bot does around the downed state (web botThink):
//  - when it's down: crawls away from you if you can see it and are within 16 m, otherwise lies still and gets back up;
//  - when you're down: either walks over and finishes you (50%) or stays put and shoots you on the ground (50%).
// Runs after BotBrain (which already pushes in on a target under 35 health) and only adjusts its inputs.
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(20)]
    public class BotFinisher : MonoBehaviour
    {
        Fighter f; BotBrain brain;
        Fighter planFor; string plan; bool forced;

        void Awake() { f = GetComponent<Fighter>(); brain = GetComponent<BotBrain>(); }

        /// Test (Shift+F9): next time you're down, the bot comes to finish you.
        public void ForceFinish() { forced = true; planFor = null; }

        void Update()
        {
            if (!f || !brain || !f.alive) return;
            var T = brain.target;
            if (f.InFinisher) { f.inFire = false; f.inFwd = f.inSide = 0; f.inJump = false; return; }
            if (f.downed)
            {
                f.inFire = false; f.inJump = false;
                bool seen = T && T.alive && !T.downed && NavGrid.Los(T.Head, f.pos + Vector3.up * .5f);
                var away = f.pos - (T ? T.pos : f.pos); away.y = 0;
                bool reviving = !seen || away.magnitude > 16 || away.sqrMagnitude < 1e-4f;
                if (reviving) { f.inFwd = f.inSide = 0; return; }
                f.yaw = Mathf.Atan2(away.x, away.z); f.pitch = 0; f.inFwd = 1; f.inSide = 0;
                return;
            }
            if (!T) return;
            if (planFor && (planFor != T || !T.downed)) planFor = null;
            if (T.downed && T.alive && !planFor) { planFor = T; plan = forced || Random.value < .5f ? "finish" : "shoot"; forced = false; }
            if (!(T.downed && T.alive && !T.InFinisher && planFor == T)) return;
            if (plan == "finish")
            {
                f.inFire = false;
                if (Finishers.CanFinish(f, T) && Random.value < Time.deltaTime * 3) { Finishers.Begin(f, T); return; }
                // close in: face them for the last few metres (BotBrain's push mode does the pathing)
                var d = T.pos - f.pos; d.y = 0;
                if (d.magnitude < 3.5f && d.sqrMagnitude > 1e-4f) { f.yaw = Mathf.Atan2(d.x, d.z); f.pitch = -.4f; if (d.magnitude > 1.6f) { f.inFwd = 1; f.inSide = 0; } }
            }
            else
            {
                // shoot them on the ground: aim low, at the body lying there
                var e = f.Head; var tp = T.pos + Vector3.up * .45f;
                if (NavGrid.Los(e, tp)) { var d = tp - e; f.pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude); }
            }
        }
    }
}
