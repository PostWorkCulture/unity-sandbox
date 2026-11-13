// Maps chat: the Fighter's side of open edges and deep water (Sky High, Lagoon) and of being flung by a train (Railed),
// ported from the web game's freeEdge / startEdgeFall / edgeFallStep / waterFallStep. Part of the Fighter partial class.
// Called from six one-line hooks in the match chat's Fighter.cs, added by Editor/MapHooks.cs if they're missing:
//   Physics:  if (EdgeFallStep(dt)) return;        (first line)    and   if (EdgeAfterMove()) return;   (before the bounds clamp)
//   Physics1: if (MapDropHere()) sup = -1e9f;       (no floor under you)
//   MoveAxis: if (EdgeBlock(pos.x + dx, pos.z + dz)) return false;   and   if (EdgeFree()) { ...no clamp...; return true; }
// On maps without edges every one of these is a no-op.
using System;
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter
    {
        /// Seconds since going over the edge, or -1.
        [NonSerialized] public float edgeFallT = -1f;
        /// Flung (by a train): not held inside the map and no bot edge caution while this counts down.
        [NonSerialized] public float thrownT;
        [NonSerialized] public bool edgeWet;
        [NonSerialized] float edgeWetT; [NonSerialized] bool edgeDead;
        public bool EdgeFalling => edgeFallT >= 0;

        /// Someone went over the edge (MapWorld shows "MAN DOWN" and plays the whoosh).
        public static event Action<Fighter> WentOverEdge;
        /// Someone hit the water while falling (Lagoon): MapWorld splashes.
        public static event Action<Fighter> HitWater;

        bool EdgeFree() => MapRules.HasEdge && (edgeFallT >= 0 || !isBot || thrownT > 0 || !grounded);
        bool EdgeBlock(float nx, float nz) => MapRules.DeepWater && isBot && grounded && thrownT <= 0 && MapRules.Drop(nx, nz) && !MapRules.Drop(pos.x, pos.z);
        bool MapDropHere() => MapRules.HasEdge && MapRules.Drop(pos.x, pos.z);

        bool EdgeFallStep(float dt)
        {
            if (thrownT > 0) thrownT -= dt;
            if (edgeFallT < 0) return false;
            EdgeFallTick(dt);
            return true;
        }

        bool EdgeAfterMove()
        {
            if (!MapRules.HasEdge) return false;
            if (alive && edgeFallT < 0 && !InFinisher && pos.y < -.4f && MapRules.Drop(pos.x, pos.z)) StartEdgeFall();
            return EdgeFree();
        }

        void StartEdgeFall()
        {
            Vector3 n;
            if (MapRules.DeepWater)
            {
                const float e = .25f; float px = pos.x, pz = -pos.z;
                float gx = MapRules.LgSd(px + e, pz) - MapRules.LgSd(px - e, pz), gz = MapRules.LgSd(px, pz + e) - MapRules.LgSd(px, pz - e);
                n = new Vector3(gx, 0, -gz); if (n.sqrMagnitude < 1e-8f) n = new Vector3(1, 0, 0); n.Normalize();
            }
            else n = Mathf.Abs(pos.x) / 18.5f > Mathf.Abs(pos.z) / 22.5f ? new Vector3(Mathf.Sign(pos.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(pos.z));
            float outward = vel.x * n.x + vel.z * n.z;
            if (outward < 3.2f) { vel.x += n.x * (3.2f - outward); vel.z += n.z * (3.2f - outward); }
            edgeFallT = 0; edgeWet = false; edgeWetT = 0; edgeDead = false; onLadder = null; ads = false; crouchOn = false; grounded = false;
            WentOverEdge?.Invoke(this);
        }

        /// One step of the fall. Runs from Physics while alive; MapWorld keeps calling it after the elimination so the body carries on.
        public void EdgeFallTick(float dt)
        {
            if (edgeFallT < 0) return;
            edgeFallT += dt; grounded = false; onLadder = null;
            if (!alive) prevPos = pos;
            if (!MapRules.DeepWater)
            {
                vel.y -= GRAV * 1.2f * dt; float k = Mathf.Max(0, 1 - dt * .3f); vel.x *= k; vel.z *= k;
                pos += vel * dt;
                if (!edgeDead && alive && (edgeFallT > 1.15f || pos.y < -26)) EdgeKill();
            }
            else
            {
                float wy = MapRules.LagoonWaterY(pos.x, pos.z, Time.time), bed = -2.4f - 3.3f;
                if (!edgeWet)
                {
                    vel.y -= GRAV * .75f * dt; pos += vel * dt;
                    if (pos.y < wy - .2f) { edgeWet = true; edgeWetT = 0; HitWater?.Invoke(this); }
                }
                else
                {
                    edgeWetT += dt; float kh = Mathf.Exp(-3 * dt); vel.x *= kh; vel.z *= kh;
                    vel.y = -(.3f + 2.2f * Mathf.Exp(-edgeWetT * 1.3f)); pos += vel * dt; if (pos.y < bed) pos.y = bed;
                }
                if (!edgeDead && alive && ((edgeWet && edgeWetT > .35f) || edgeFallT > 3)) EdgeKill();
            }
        }

        public float EdgeWetTime => edgeWet ? edgeWetT : 0;

        void EdgeKill()
        {
            edgeDead = true;
            var lb = lastBy; var by = lb != null && lb != this && lb.team != team && simTime - lastHurt < 10 ? lb : null;
            var prev = RoundManager.WeaponNameOf;
            RoundManager.WeaponNameOf = f => f == by ? "Man down" : prev != null ? prev(f) : f.primary;
            var keep = vel;
            try { Die(by, false); } finally { RoundManager.WeaponNameOf = prev; }
            vel = keep;   // Die stops the body; it carries on falling
        }

        /// Moves the fighter without a visual jump (riding a train).
        public void Carry(Vector3 d) { pos += d; prevPos += d; }

        /// Back to normal (round start).
        public void EdgeReset() { edgeFallT = -1; thrownT = 0; edgeWet = false; edgeWetT = 0; edgeDead = false; }
    }
}
