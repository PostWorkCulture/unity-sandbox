// Finishers chat: the downed state, as in the web game. At 0 health a fighter goes down instead of dying: they lie
// prone and crawl, enemies can finish them (F within 3 m), and more damage while down (30 downed health) kills them.
// Left alone they get back up after 5 s with whatever downed health they had left.
// The hooks that call into this file are added to Fighter.cs / FighterCombat.cs by Editor/FinisherHooks.cs
// (see claude/street-life-interfaces.md).
using System;
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter
    {
        public const float DOWN_HP = 30f, BLEED_TIME = 15f, REVIVE_TIME = 5f, CRAWL_SPEED = 2.5f;

        /// Down at 0 health: prone, crawling, can be finished. Weapons: a downed fighter can't fire.
        [NonSerialized] public bool downed;
        /// Downed health (30): damage while down comes off this, and at 0 they're out.
        [NonSerialized] public float dhp;
        [NonSerialized] public float bleed, revive;
        [NonSerialized] public Fighter downedBy;
        [NonSerialized] public float downedAt = -99f;
        /// The finishing move this fighter is the victim of (null if none).
        [NonSerialized] public Finisher finishing;
        /// The finishing move this fighter is doing to someone (null if none).
        [NonSerialized] public Finisher finishingOther;
        /// Died while down (bled out, shot on the ground) or from a finisher: the body stays where it fell.
        [NonSerialized] public bool corpse;

        /// In a finishing move, either side of it: no firing, no movement of your own.
        public bool InFinisher => finishing != null || finishingOther != null;

        /// Hook (Fighter.Update): true while a finishing move drives this fighter (it moves them itself, at the same 60 Hz).
        bool FinisherHold() => InFinisher;

        /// Hook (Fighter.Step): crawling pace while down, no jumping, aiming or crouching.
        void DownedMove(ref float spd)
        {
            if (!downed) return;
            // web: speed = 2.5 x character speed, set after the 1.2 boost (Physics1 multiplies by charSpeed x 1.2 again)
            spd = CRAWL_SPEED / 1.2f / (stamLock ? .75f : 1f);
            inJump = false; inJumpPress = false; ads = false; crouchOn = false; inFire = false;
        }

        /// Hook (FighterCombat.Damage, first thing): hits on a downed fighter or one being finished. Returns true when handled here.
        bool DownedHit(float amount, Fighter by, bool head, out float taken)
        {
            taken = 0;
            // the attacker in a finishing move, shot by someone other than their victim: the move is interrupted
            if (finishingOther != null && by != null && by != this && by.team != team && by.alive && by != finishingOther.v)
                Finishers.Cancel(finishingOther);
            if (finishing != null) return true;                                   // being finished: nothing else can hurt you
            if (!downed) return false;
            if (by != null && by != this && by.team == team) return true;          // no friendly fire
            dhp -= amount; taken = amount;
            lastHurt = simTime; lastBy = by; lastHitTime = Time.time;
            if (by != null) { lastHitFrom = by.pos; by.damageDealt += amount; }
            Damaged?.Invoke(amount, by);
            if (dhp <= 0) { downed = false; corpse = true; Finishers.OnDownedKill(this, by); Die(by != null ? by : downedBy, head); }
            return true;
        }

        /// Hook (FighterCombat.Damage, at 0 health): go down instead of dying. Returns false when they should just die.
        bool GoDown(Fighter by, bool head)
        {
            if (downed || finishing != null || !Finishers.DownedStateOn) return false;
            downed = true; hp = 0; dhp = DOWN_HP; bleed = BLEED_TIME; revive = 0; downedAt = Time.time;
            downedBy = by != null && by != this && by.team != team ? by : null;
            ads = false; inFire = false; crouchOn = false; vel = new Vector3(0, vel.y, 0);
            if (onLadder != null) { var L = onLadder; onLadder = null; grounded = false; vel = new Vector3(-L.nx * 2.4f, Mathf.Min(vel.y, 0), -L.nz * 2.4f); }
            Finishers.OnDown(this, by, head);
            return true;
        }

        /// Hook (FighterCombat.TickCombat): bleeding and getting back up.
        void TickDowned(float dt)
        {
            if (!downed || !alive || finishing != null) return;
            hp = 0;
            bleed -= dt; revive += dt / REVIVE_TIME * ReviveHelp();   // match chat: x2 with a team-mate over you (2v2)
            if (revive >= 1)
            {
                downed = false; hp = Mathf.Max(1, Mathf.Round(dhp)); lastHurt = simTime; revive = 0; downedBy = null;
                Finishers.OnRevive(this);
            }
            else if (bleed <= 0) { downed = false; corpse = true; Finishers.OnDownedKill(this, lastBy); Die(lastBy != null ? lastBy : downedBy); }
        }

        /// Clears the downed state (round start, respawn).
        public void ResetDowned()
        {
            downed = false; dhp = 0; bleed = 0; revive = 0; downedBy = null; corpse = false;
            finishing = null; finishingOther = null;
        }

        // ---- used by the finishing moves, which move both fighters themselves at 60 Hz ----
        internal void FinStep() { prevPos = pos; }
        internal void FinAlpha(float a) { acc = Mathf.Clamp(a, 0, SIM); }
        internal void FinPlace(Vector3 p) { pos = p; prevPos = p; }
        internal void FinEvent(float amount, Fighter by) { Damaged?.Invoke(amount, by); }
    }
}
