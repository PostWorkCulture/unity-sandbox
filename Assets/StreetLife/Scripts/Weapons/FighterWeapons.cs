// The weapons side of a fighter (weapons/characters chat). Fighter is a partial class: the match chat owns
// Fighter.cs (movement) and FighterCombat.cs (team, health, Damage, stats); this file only adds weapon status.
// See claude/street-life-interfaces.md.
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter
    {
        // status from weapons: dazed by a stun grenade (moves at 25%), whited out by a flash bang, Speed from the shop, burning
        [System.NonSerialized] public float dazeT, blindT, speedT, burnT, burnTick;
        [System.NonSerialized] public Fighter burnBy;
        // the player's screen effects
        [System.NonSerialized] public float flashA, flashHold, hurtFlash, blastT;

        /// Movement multiplier from weapon effects (stun 0.25, Speed 1.25). Weapons copy it into the match code's moveMul each frame.
        public float WeaponMoveMul => (dazeT > 0 ? .25f : 1f) * (speedT > 0 ? 1.25f : 1f);

        /// eyeOf: where shots and throws start (a little above the camera head height)
        public Vector3 Eye => new Vector3(pos.x, pos.y + 2.35f * SC * (1 - .27f * cv), pos.z);

        /// The web game's damage0 also knocks stamina: armour hits cost 0.4 per point, health hits 1.2 per point.
        public void StaminaKnock(float armourHit, float healthHit)
        {
            stam = Mathf.Max(0, stam - armourHit * .4f - healthHit * 1.2f);
            if (healthHit > 0) { if (stam <= 0) stamLock = true; stamRest = 0; }
        }
    }
}
