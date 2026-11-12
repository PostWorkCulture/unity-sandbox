// The combat side of a fighter: team, health and armour, damage and kills, match stats.
// Weapons (owned by the weapons chat) call Damage / NoteShot / NoteHit; see claude/street-life-interfaces.md.
using System;
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter
    {
        [Header("Match")]
        public string displayName = "You";
        public int team;
        public bool isBot;
        public bool alive = true;
        public string primary = "gar", secondary = "sword", lethal = "grenade";   // the web game's Simple 1v1 kit

        /// No moving, jumping or firing (countdown, round end).
        [NonSerialized] public bool Frozen;
        [NonSerialized] public bool inFire;

        // match stats
        [NonSerialized] public int kills, deaths, assists, shots, hits, headshots;
        [NonSerialized] public float damageDealt;
        [NonSerialized] public float lastShotTime = -99f;
        [NonSerialized] public Vector3 lastShotPos;
        [NonSerialized] public Fighter lastBy;
        [NonSerialized] public Vector3 lastHitFrom;
        [NonSerialized] public float lastHitTime = -99f;
        [NonSerialized] public Vector3 deathPos;
        readonly System.Collections.Generic.Dictionary<Fighter, float> damagedBy = new System.Collections.Generic.Dictionary<Fighter, float>();

        public static event Action<Fighter, Fighter, bool> Killed;   // victim, killer (may be null), headshot
        public event Action<float, Fighter> Damaged;                  // amount, attacker

        public float RealTime => Time.time;

        /// Resets health, armour, stamina and position at the start of a round.
        public void ResetForRound(Vector3 p, float yawRad)
        {
            alive = true; armor = 0; inFire = false; lastBy = null; damagedBy.Clear();
            Place(p, yawRad);
            hp = hpMax;
        }

        public void ResetStats() { kills = deaths = assists = shots = hits = headshots = 0; damageDealt = 0; }

        /// Apply a hit. Armour soaks first. Returns the damage actually taken.
        public float Damage(float amount, Fighter by, bool head = false)
        {
            if (!alive || amount <= 0) return 0;
            if (by != null && by != this && by.team == team) return 0;   // no friendly fire
            float soak = Mathf.Min(armor, amount); armor -= soak;
            float taken = Mathf.Min(hp, amount - soak);
            hp -= taken;
            lastHurt = simTime; lastBy = by;
            lastHitTime = Time.time;
            if (by != null) { lastHitFrom = by.pos; by.damageDealt += taken + soak; damagedBy[by] = Time.time; }
            Damaged?.Invoke(taken + soak, by);
            if (hp <= 0.001f) Die(by, head);
            return taken + soak;
        }

        public void Die(Fighter by, bool head = false)
        {
            if (!alive) return;
            alive = false; hp = 0; deaths++; deathPos = pos; inFire = false; vel = Vector3.zero;
            if (by != null && by != this) by.kills++;
            foreach (var kv in damagedBy)   // anyone else who hurt them in the last 15 s gets an assist
                if (kv.Key != null && kv.Key != by && kv.Key != this && Time.time - kv.Value < 15f) kv.Key.assists++;
            Killed?.Invoke(this, by, head);
        }

        public void NoteShot() { shots++; lastShotTime = Time.time; lastShotPos = pos; }
        public void NoteHit(bool head) { hits++; if (head) headshots++; }

        void TickCombat(float dt) { }
    }
}
