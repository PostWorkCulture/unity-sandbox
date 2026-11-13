// Character abilities (G on keyboard, the Ability button on phones), ported from the web game's useAbility, tickStatus,
// smashLand and the parts of physics they touch (match chat). As in the web game they're on in Normal rules only;
// Simple 1v1 has none. Every number below is the web game's.
//   blaze Fireball · brute Ground smash · jet Jetpack (also on jump when ready) · blink Blink · tank Shield · ghost Vanish
//   frost Frost nova · patch Med kit · thorn Thorn armour · angel Wings · crusty Honk · wednesday Sprint · antihero Grapple
//   thor Lightning · isaac Reload · dracula Life drain · astronaut Moon jump · lilpink Heartbreak
using System;
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter
    {
        // status timers (seconds)
        [NonSerialized] public float abilT, shieldT, ghostT, thornT, slowT, zipT, moonT, glideT, hookT, boostT, boostAge, smashT, stunT, jetOn;
        [NonSerialized] public Vector3 hookP;
        /// the player's G key / Ability button is held (keeps the jetpack burning, as in the web game)
        [NonSerialized] public bool abilityHeld;
        public const float JET_BURN = 1.6f, JET_VMAX = 13.5f, JET_ACC = 26f;

        /// any fighter used its ability: (who, character id)
        public static event Action<Fighter, string> AbilityUsed;

        /// Normal rules only, as in the web game (S.simple turns them off). Free roam (no round manager) has them on.
        public static bool AbilitiesOn => !Rules.Rumble && (!RoundManager.Instance || !RoundManager.Instance.simpleRules || !Rules.OneVOne);

        CharacterRig abilRig; bool abilRigLooked;
        /// the web game's character id this fighter is wearing (wednesday, blaze, ...)
        public string CharId
        {
            get
            {
                if (!abilRigLooked) { abilRig = GetComponent<CharacterRig>(); abilRigLooked = true; }
                var id = abilRig ? CharacterSkin.Of(abilRig) : null;
                if (!string.IsNullOrEmpty(id)) return id;
                var set = CharacterSet.Load();
                if (set) foreach (var c in set.All) if (c.name == characterName) return c.id;
                return "wednesday";
            }
        }
        public CharacterSet.Info CharInfo { get { var set = CharacterSet.Load(); return set ? set.Get(CharId) : null; } }
        public float AbilityCooldown { get { var i = CharInfo; return i != null && i.cooldown > 0 ? i.cooldown : 10f; } }
        public string AbilityName { get { var i = CharInfo; return i != null && !string.IsNullOrEmpty(i.ability) ? i.ability : "Ability"; } }

        public bool AbilityReady => AbilitiesOn && alive && !downed && !InFinisher && !Frozen && !EdgeFalling && abilT <= 0;

        public void ResetAbility()
        {
            abilT = 1.5f; shieldT = ghostT = thornT = slowT = zipT = moonT = glideT = hookT = boostT = boostAge = smashT = stunT = jetOn = 0;
            abilityHeld = false;
        }

        /// speed changes from abilities: frozen by a nova or a broken heart (x0.45), Wednesday's sprint (x1.6)
        float AbilitySpeedMul => (slowT > 0 ? .45f : 1f) * (zipT > 0 ? 1.6f : 1f);

        // ------------------------------------------------------------------ use
        /// Fire this fighter's ability. aimDir: where to aim (bots pass it; the player aims with the camera).
        public bool UseAbility(Vector3? aimDir = null)
        {
            if (!AbilityReady) return false;
            string id = CharId; var fw = Forward; var me = Combat.Me == this;
            var col = CharInfo != null ? CharInfo.Colour : Color.white;
            switch (id)
            {
                case "brute":
                    vel.y = 15; vel.x += fw.x * 9; vel.z += fw.z * 9; grounded = false; smashT = .01f;
                    if (me) GunFx.Shake = Mathf.Max(GunFx.Shake, .2f);
                    Emit(pos + Vector3.up * .1f, 30, 7, .28f, .45f, 4, 0, "#8a7466", "#c9b8a4", "#" + ColorUtility.ToHtmlStringRGB(col));
                    break;
                case "frost":
                    if (me) GunFx.Shake = Mathf.Max(GunFx.Shake, .15f);
                    Ring(36, 1.2f, .4f, 2, 10, 1, .26f, .55f, 0, "#ffffff", "#bfeaff", "#7fd3ff");
                    foreach (var foe in Combat.FoesOf(this))
                    {
                        float d = Flat(foe.pos - pos);
                        if (!foe.InFinisher && d < 9 && Mathf.Abs(foe.pos.y - pos.y) < 3) { foe.slowT = 3; Abilities.Hit(this, foe, 12, "Frost nova"); }
                    }
                    break;
                case "patch":
                    hp = Mathf.Min(hpMax, hp + 48);
                    Emit(pos + Vector3.up * 1.2f, 40, 3, .24f, .8f, -2, 3, "#3bb54a", "#b6ff3b", "#ffffff");
                    break;
                case "thor":
                    {
                        var mz = Eye + fw * .8f; var d = Abilities.AimFrom(this, mz, aimDir);
                        bool hb = Combat.RayBoxes(mz, d, 45, out var bh); bool hf = Combat.RayFoes(mz, d, this, out var fh);
                        float t = hb ? bh.t : 45; bool hit = hf && fh.t < t && fh.t < 45; if (hit) t = fh.t;
                        var end = mz + d * t; var a = mz; var yellow = Web.Hex("#fff27a");
                        for (int k = 1; k <= 5; k++)
                        {
                            var b = k == 5 ? end : mz + d * (t * k / 5f) + new Vector3(UnityEngine.Random.Range(-.35f, .35f), UnityEngine.Random.Range(-.35f, .35f), UnityEngine.Random.Range(-.35f, .35f));
                            GunFx.Tracer(a, b, yellow * 2.2f, .08f); a = b;
                        }
                        Emit(end, 18, 5, .18f, .35f, 0, 0, "#fff27a", "#7ff3ff", "#ffffff");
                        Abilities.Flash(end, new Color(.7f, .9f, 1f), 7, 5);
                        if (hit) Abilities.Hit(this, fh.who, fh.head ? 48 : 36, "Lightning", fh.head);
                        Swing();
                    }
                    break;
                case "antihero":
                    {
                        var e = Eye; var d = Abilities.AimFrom(this, e, aimDir);
                        float t = Combat.RayBoxes(e, d, 32, out var bh) ? Mathf.Max(2, bh.t - .6f) : 18;
                        var tgt = e + d * t; var v = tgt - e; float L = v.magnitude; v /= Mathf.Max(.001f, L);
                        float sp = Mathf.Min(26, 11 + L * 1.1f);
                        vel = new Vector3(v.x * sp, Mathf.Max(v.y * sp + 6, 7), v.z * sp); grounded = false; hookT = .55f; hookP = tgt;
                        Emit(tgt, 12, 3, .15f, .35f, 2, 0, "#d6dbe2", "#e6c25a");
                    }
                    break;
                case "dracula":
                    {   // life drain: the nearest enemy he can see within 12 m loses 25 HP and he gains it
                        var e = Eye; Fighter best = null; float bd = 12.5f;
                        foreach (var foe in Combat.FoesOf(this))
                        {
                            if (!foe.alive || foe.downed || foe.InFinisher || foe.ghostT > 0) continue;
                            var c = foe.Eye; float d = Vector3.Distance(c, e);
                            if (d < bd && Combat.Los(e, c)) { best = foe; bd = d; }
                        }
                        if (!best) return false;
                        var cc = best.Eye; cc.y -= .4f;
                        Abilities.Hit(this, best, 25, "Life drain"); hp = Mathf.Min(hpMax, hp + 25);
                        var a = cc; var red = Web.Hex("#ff1a3a");
                        for (int k = 1; k <= 4; k++)
                        {
                            var b = k == 4 ? e : Vector3.Lerp(cc, e, k / 4f) + new Vector3(UnityEngine.Random.Range(-.25f, .25f), UnityEngine.Random.Range(-.25f, .25f), UnityEngine.Random.Range(-.25f, .25f));
                            GunFx.Tracer(a, b, red * 1.8f, .07f); a = b;
                        }
                        Emit(cc, 20, 3, .18f, .45f, 0, 0, "#ff1a3a", "#8a0018", "#ffffff");
                        Emit(pos + Vector3.up * 1.2f, 24, 2.5f, .2f, .6f, -1, 2, "#ff1a3a", "#c4122e", "#3a2350");
                    }
                    break;
                case "astronaut":
                    vel.y = 11; vel.x += fw.x * 4; vel.z += fw.z * 4; grounded = false; moonT = 3;
                    Ring(24, .8f, .15f, 1, 3, .5f, .26f, .6f, 0, "#d9dde3", "#ffffff", "#ff7a1f");
                    break;
                case "lilpink":
                    { var mz = Eye + fw * .8f; Abilities.Spawn(this, "heart", mz, Abilities.AimFrom(this, mz, aimDir)); Swing(); }
                    break;
                case "wednesday":
                    zipT = 3; Emit(pos + Vector3.up * .5f, 30, 6, .22f, .4f, 0, 0, "#1fbfae", "#ff4fa3", "#ffffff");
                    break;
                case "angel":
                    vel.y = 14; vel.x += fw.x * 7; vel.z += fw.z * 7; grounded = false; glideT = 3.2f;
                    Ring(26, .9f, 1.4f, 1, 3, 1, .22f, .7f, 1.5f, "#ffffff", "#f4f1ff", "#dfe6ff");
                    break;
                case "crusty":
                    if (me) GunFx.Shake = Mathf.Max(GunFx.Shake, .25f);
                    Ring(30, 1, 1.3f, 2, 9, .5f, .3f, .45f, 0, "#e8322a", "#ffd23f", "#1f4fe0", "#ffffff");
                    foreach (var foe in Combat.FoesOf(this))
                    {
                        float dx = foe.pos.x - pos.x, dz = foe.pos.z - pos.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (foe.InFinisher || d > 7 || Mathf.Abs(foe.pos.y - pos.y) > 3 || !foe.alive || foe.downed) continue;
                        float l = d > 0 ? d : 1; Abilities.Hit(this, foe, 20, "Honk");
                        if (foe.alive && !foe.downed) { foe.vel.x += dx / l * 13; foe.vel.z += dz / l * 13; foe.vel.y = Mathf.Max(foe.vel.y, 8); foe.grounded = false; foe.stunT = Mathf.Max(foe.stunT, .8f); }
                    }
                    break;
                case "isaac":
                    {
                        var w = GetComponent<Weapons>();
                        if (w)
                        {
                            foreach (var gid in new[] { primary, secondary })
                            { var g = GunsDb.Gun(gid); if (g != null && !g.InfiniteMag && g.mag > 0) w.ammo[gid] = Mathf.RoundToInt(g.mag); }
                            w.reloadT = 0; w.autoRT = -1;
                        }
                        Emit(pos + Vector3.up * 1.2f, 24, 3, .16f, .5f, 3, 2, "#ffd23f", "#c98a3a", "#ffffff");
                    }
                    break;
                case "thorn":
                    thornT = 5; Emit(pos + Vector3.up * 1.2f, 30, 4, .24f, .6f, 2, 0, "#4f9a2e", "#7bd13a", "#e9e2c8");
                    break;
                case "jet":
                    boostT = JET_BURN; boostAge = 0; vel.x += fw.x * 3; vel.z += fw.z * 3; vel.y = Mathf.Max(vel.y, 5); grounded = false;
                    if (me) GunFx.Shake = Mathf.Max(GunFx.Shake, .12f);
                    Emit(pos + Vector3.up, 30, 5, .3f, .5f, 12, 0, "#ffb347", "#ff7a1f", "#ffffff");
                    break;
                case "blaze":
                    { var mz = Eye + fw * .8f; Abilities.Spawn(this, "fireball", mz, Abilities.AimFrom(this, mz, aimDir)); Swing(); }
                    break;
                case "blink":
                    Blink(fw);
                    break;
                case "tank":
                    shieldT = 4;
                    break;
                case "ghost":
                    ghostT = 5; Emit(pos + Vector3.up * 1.2f, 24, 3, .25f, .6f, -1, 0, "#ffffff", "#dfe9f2");
                    break;
                default: return false;
            }
            string snd = AbilitySound(id);
            if (snd != null) Sfx.Play(snd, pos, 1, 18);
            abilT = AbilityCooldown;
            AbilityUsed?.Invoke(this, id);
            return true;
        }

        static string AbilitySound(string id)
        {
            switch (id)
            {
                case "frost": return "frost"; case "patch": return "heal"; case "thor": return "volt"; case "antihero": return "hook";
                case "wednesday": return "zip"; case "angel": return "jet"; case "crusty": return "smash"; case "thorn": return "thorn";
                case "jet": return "jet"; case "blaze": return "fireball"; case "blink": return "blink"; case "tank": return "shield";
                case "ghost": return "vanish"; case "dracula": return "heal"; case "astronaut": return "jet"; case "lilpink": return "fireball";
            }
            return null;
        }

        void Swing() { var w = GetComponent<Weapons>(); if (w) w.swingT = .32f; }

        /// Blink: up to 9 m straight ahead, landing at the furthest point along the dash where a body fits
        void Blink(Vector3 fw)
        {
            var c = new Vector3(pos.x, pos.y + 1.2f * SC, pos.z);
            float dist = Combat.RayBoxes(c, fw, 9.5f, out var hb) ? Mathf.Max(0, hb.t - .8f) : 9;
            Emit(c, 30, 3, .2f, .5f, 0, 0, "#36f3ff", "#a05ad6", "#ffffff");
            Abilities.Flash(c, new Color(.4f, .9f, 1f), 5, 3);
            float H = Hh, x0 = pos.x, z0 = pos.z, y0 = pos.y; var B = BoxWorld.Boxes;
            bool Fits(float x, float y, float z)
            {
                for (int i = 0; i < B.Count; i++) { var b = B[i]; if (b.x0 < x + R && b.x1 > x - R && b.z0 < z + R && b.z1 > z - R && b.y1 > y + STEP && b.y0 < y + H) return false; }
                return true;
            }
            for (float d = dist; d >= 0; d -= .25f)
            {
                float x = x0 + fw.x * d, z = z0 + fw.z * d, y = y0;
                float sup = SurfaceAt(x, z, y0 + .6f);
                if (sup > float.MinValue && sup > y0 && sup - y0 < .8f) y = sup;
                if (d <= 0 || Fits(x, y, z)) { pos = new Vector3(x, y, z); prevPos = pos; break; }
            }
            grounded = false;
            Emit(pos + Vector3.up * 1.2f, 30, 3, .2f, .5f, 0, 0, "#36f3ff", "#a05ad6", "#ffffff");
        }

        /// the highest box top under (x, z) at or below maxY (float.MinValue if none)
        static float SurfaceAt(float x, float z, float maxY)
        {
            float best = float.MinValue; var B = BoxWorld.Boxes;
            for (int i = 0; i < B.Count; i++) { var b = B[i]; if (x >= b.x0 && x <= b.x1 && z >= b.z0 && z <= b.z1 && b.y1 <= maxY && b.y1 > best) best = b.y1; }
            return best;
        }

        /// smashLand: the brute lands from his leap
        void SmashLand()
        {
            Sfx.Play("smash", pos, 1, 22);
            var me = Combat.Me; bool near = me && Flat(me.pos - pos) < 14;
            GunFx.Shake = Mathf.Max(GunFx.Shake, me == this || near ? .45f : .15f);
            Ring(28, .7f, .15f, 2, 9, 3, .32f, .6f, 12, "#8a7466", "#c9b8a4", "#ff7a2a");
            BoomFx.Dust(pos, 4.5f);
            foreach (var foe in Combat.FoesOf(this))
            {
                float dx = foe.pos.x - pos.x, dz = foe.pos.z - pos.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (foe.InFinisher || d >= 6 || Mathf.Abs(foe.pos.y - pos.y) >= 2.5f) continue;
                float l = d > 0 ? d : 1; Abilities.Hit(this, foe, Mathf.Round((40 - d * 3.5f) * 1.2f), "Ground smash");
                if (foe.alive && !foe.downed) { foe.vel.x += dx / l * 12; foe.vel.z += dz / l * 12; foe.vel.y = Mathf.Max(foe.vel.y, 9); foe.grounded = false; foe.stunT = Mathf.Max(foe.stunT, .6f); }
            }
            Destruction.KnockRay(pos + Vector3.up * .5f, Forward, 3, 20);
        }

        // ------------------------------------------------------------------ per sim step (from TickCombat)
        void TickAbility(float dt)
        {
            if (abilT > 0) abilT -= dt;
            if (downed || !alive)
            {   // going down ends every ability (the web game's down())
                zipT = hookT = glideT = moonT = thornT = smashT = boostT = shieldT = ghostT = 0;
                if (!alive) { slowT = stunT = 0; }
            }
            if (shieldT > 0) shieldT -= dt;
            if (ghostT > 0) ghostT -= dt;
            if (stunT > 0) stunT -= dt;
            if (jetOn > 0) jetOn -= dt;
            if (smashT > 0)
            {
                smashT += dt;
                if (downed || !alive || smashT > 4) smashT = 0;
                else if (smashT > .25f && grounded) { smashT = 0; SmashLand(); }
            }
            if (slowT > 0)
            {
                slowT -= dt;
                if (UnityEngine.Random.value < .25f) Emit(pos + new Vector3(UnityEngine.Random.Range(-.5f, .5f), UnityEngine.Random.Range(.3f, 2.2f), UnityEngine.Random.Range(-.5f, .5f)), 1, .4f, .18f, .5f, 1, -1, "#ffffff", "#bfeaff");
            }
            if (zipT > 0)
            {
                zipT -= dt;
                if (HorizontalSpeed > 3 && UnityEngine.Random.value < .45f) Emit(pos + Vector3.up * UnityEngine.Random.Range(.3f, 2f), 1, .3f, .22f, .35f, 0, 0, "#d0202a", "#18181c");
            }
            if (hookT > 0) hookT -= dt;
            if (glideT > 0 && !grounded && UnityEngine.Random.value < .25f) Emit(pos + new Vector3(UnityEngine.Random.Range(-.8f, .8f), UnityEngine.Random.Range(.8f, 2f), UnityEngine.Random.Range(-.8f, .8f)), 1, .4f, .18f, .6f, .6f, 0, "#ffffff", "#e8ecff");
            if (thornT > 0)
            {
                thornT -= dt;
                if (UnityEngine.Random.value < .15f) Emit(pos + new Vector3(UnityEngine.Random.Range(-.7f, .7f), UnityEngine.Random.Range(.3f, 2.2f), UnityEngine.Random.Range(-.7f, .7f)), 1, .5f, .18f, .6f, 1, 0, "#4f9a2e", "#7bd13a");
            }
        }

        // ------------------------------------------------------------------ damage (from Fighter.Damage)
        static bool reflecting;
        /// Shield takes 65% off every hit; thorn armour takes 30% off and returns 60% of what's left to the attacker.
        /// Any hit ends Vanish. Returns the damage to apply; reflect is what goes back.
        float AbilityDamage(float amount, Fighter by, out float reflect)
        {
            reflect = 0;
            bool foe = by != null && by != this && by.team != team;
            if (thornT > 0 && !downed && foe && !reflecting && !InFinisher) { amount *= .7f; reflect = amount * .6f; }
            if (shieldT > 0) amount *= .35f;
            if (ghostT > 0) ghostT = 0;
            return Mathf.Max(1, Mathf.Round(amount));
        }

        void ReflectThorns(Fighter by, float reflect)
        {
            if (reflect <= 0 || !by || !by.alive || by.InFinisher) return;
            Emit(pos + Vector3.up * 1.3f, 10, 5, .18f, .4f, 4, 0, "#b6ff3b", "#4f9a2e", "#e9e2c8");
            reflecting = true;
            try { Abilities.Hit(this, by, Mathf.Max(1, reflect), "Thorns", false, false); } finally { reflecting = false; }
        }

        // ------------------------------------------------------------------ helpers
        static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

        static void Emit(Vector3 at, int n, float speed, float size, float life, float grav, float up, params string[] cols)
        {
            var o = GunFx.E(n, speed, size, life, grav, cols); o.up = up; GunFx.Emit(at, o);
        }

        /// a ring of bursts round the fighter (nova, honk, wings, moon jump)
        void Ring(int count, float radius, float height, int n, float speed, float up, float size, float life, float grav, params string[] cols)
        {
            var o = GunFx.E(n, speed, size, life, grav, cols); o.up = up;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2;
                GunFx.Emit(new Vector3(pos.x + Mathf.Cos(a) * radius, pos.y + height, pos.z + Mathf.Sin(a) * radius), o);
            }
        }
    }
}
