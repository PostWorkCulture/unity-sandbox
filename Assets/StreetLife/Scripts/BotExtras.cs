// What the bot does besides moving and shooting (match chat), ported from the web game's bot: when to use its
// character's ability (each character has its own rule, below), when to throw its lethal (grenade, stun, flash,
// cluster: lobbed on the right arc at where it last saw you; mines laid when you're close or lost), shop grenades,
// and the Sentry. Runs after BotBrain each frame and only presses buttons; BotBrain still owns movement and aim.
using UnityEngine;

namespace StreetLife
{
    [RequireComponent(typeof(BotBrain)), DefaultExecutionOrder(-5)]
    public class BotExtras : MonoBehaviour
    {
        Fighter f; BotBrain a; Weapons w;
        float reactCD, hurtSeen = -99, wallUntil, wallCD, shopNext = -1, shopUntil;
        bool wallNow;

        // the web game's STYLES: wall building (build, wallHp, wallCD) and the guns each style picks in Normal rules
        public class Kit { public float build, wallHp, wallCD; public string[] prim, sec; }
        public static readonly System.Collections.Generic.Dictionary<string, Kit> Kits = new System.Collections.Generic.Dictionary<string, Kit>
        {
            ["rusher"] = new Kit { build = .3f, wallHp = 35, prim = new[] { "ar", "mg", "gar", "scout", "m60", "burst", "shotgun", "autoshot" }, sec = new[] { "sword", "smg", "hornet", "sword", "duals", "vector" } },
            ["sniper"] = new Kit { build = .7f, wallHp = 70, prim = new[] { "sniper", "sniper", "sniper", "scout", "crossbow" }, sec = new[] { "smg", "duals" } },
            ["lurker"] = new Kit { build = .4f, wallHp = 55, prim = new[] { "ar", "scout", "mg", "m60", "burst", "crossbow", "shotgun", "autoshot" }, sec = new[] { "sword", "smg", "duals", "vector" } },
            ["flanker"] = new Kit { build = .5f, wallHp = 60, prim = new[] { "ar", "scout", "gar", "burst", "crossbow", "shotgun" }, sec = new[] { "smg", "hornet", "sword", "duals", "vector" } },
            ["turtle"] = new Kit { build = 1.6f, wallHp = 95, wallCD = 10, prim = new[] { "mg", "ar", "gar", "m60", "burst", "crossbow" }, sec = new[] { "smg", "rocket", "duals", "swarm", "ump" } },
            ["hunter"] = new Kit { build = .6f, wallHp = 55, prim = new[] { "ar", "mg", "scout", "m60", "burst", "crossbow", "autoshot" }, sec = new[] { "smg", "duals", "p90", "ump" } },
            ["breacher"] = new Kit { build = .4f, wallHp = 50, prim = new[] { "shotgun", "autoshot", "shotgun" }, sec = new[] { "smg", "hornet", "duals", "sting" } },
            ["kiter"] = new Kit { build = .8f, wallHp = 65, prim = new[] { "ar", "scout", "mg", "gar", "m60", "burst", "crossbow" }, sec = new[] { "smg", "hornet", "duals", "p90" } },
            ["camper"] = new Kit { build = 1.3f, wallHp = 90, wallCD = 25, prim = new[] { "mg", "ar", "sniper", "m60", "burst" }, sec = new[] { "smg", "duals" } },
            ["duelist"] = new Kit { build = 1, wallHp = 70, prim = new[] { "sniper", "scout", "ar", "gar", "burst", "mg", "m60", "crossbow", "shotgun", "autoshot" }, sec = new[] { "smg", "hornet", "rocket", "sword", "duals", "vector", "p90", "ump", "sting", "swarm" } },
        };
        static readonly System.Collections.Generic.Dictionary<string, float> DifBuild = new System.Collections.Generic.Dictionary<string, float> { ["easy"] = .15f, ["normal"] = .3f, ["hard"] = .7f, ["extreme"] = .8f };
        Kit K => Kits.TryGetValue(a.styleId, out var k) ? k : Kits["duelist"];

        void Awake() { f = GetComponent<Fighter>(); a = GetComponent<BotBrain>(); }
        void OnEnable() { RoundManager.RoundStarted += OnRound; }
        void OnDisable() { RoundManager.RoundStarted -= OnRound; }

        /// each round the bot takes its style's kit: a random primary and secondary from its list in Normal rules, the
        /// Simple kit in Simple rules, their weapon-ladder step in Rumble (the web game's applyStyle, plus RumbleLadder)
        /// a deathmatch respawn: the style's kit again
        public void Rekit() => OnRound(0);

        void OnRound(int r)
        {
            reactCD = 0; hurtSeen = f.lastHurt; wallNow = false; wallCD = 0; shopNext = -1; a.ForceGoal = null;
            if (!w) w = GetComponent<Weapons>(); if (!w) return;
            var rm = RoundManager.Instance;
            string pri, sec, leth = f.lethal;
            if (Rules.Rumble) { pri = sec = RumbleLadder.WeaponOf(f); }   // wherever they are on the weapon ladder (fists to start)
            else if (rm && rm.simpleRules && Rules.OneVOne && !Rules.Rumble) { pri = "gar"; sec = "sword"; leth = "grenade"; }
            else { var k = K; pri = k.prim[Random.Range(0, k.prim.Length)]; sec = k.sec[Random.Range(0, k.sec.Length)]; }
            if (GunsDb.Gun(pri) == null) pri = "gar"; if (GunsDb.Gun(sec) == null) sec = Rules.Rumble ? pri : "sword";
            w.SetLoadout(pri, sec, leth);
        }

        static bool Close(string id) => id == "sword" || id == "shotgun" || id == "autoshot";

        void Update()
        {
            if (!f || !a || !f.alive || f.downed || f.InFinisher || f.Frozen) return;
            var T = a.target; if (!T) return;
            var rm = RoundManager.Instance; if (rm && rm.state != RoundManager.State.Fight) return;
            if (!w) w = GetComponent<Weapons>();
            float dt = Time.deltaTime;
            float dist = Flat(T.pos - f.pos);
            bool see = a.Sees; var eye = f.Eye;
            var tgtBody = new Vector3(T.pos.x, T.pos.y + (T.downed ? .45f : 1.25f * Fighter.SC), T.pos.z);
            string gun = w ? w.Gun : f.primary;
            string mode = a.Mode, style = a.styleId;
            bool rush = mode == "rush" || mode == "push", hunt = mode == "search" || mode == "chase" || mode == "hunt";
            bool ambush = mode == "ambush" || mode == "lurk", flank = mode == "flank";

            // ---- abilities (Normal rules only)
            if (f.AbilityReady)
            {
                bool hurt = f.simTime - f.lastHurt < .4f;
                float pr = a.IdealRange;
                switch (f.CharId)
                {
                    case "blaze": if (see && dist < 28) { var d = tgtBody - eye; d.y += dist * .02f; f.UseAbility(d.normalized); } break;
                    case "tank": if (hurt && f.hp < f.hpMax * .7f || rush && see && dist < 25) f.UseAbility(); break;
                    case "ghost": if (hurt && f.hp < 60 || !see && (ambush || flank) && dist < 24 || rush && see && dist > 10 && dist < 30) f.UseAbility(); break;
                    case "blink":
                        if (see && (((Close(gun) || rush) && dist > 7 && dist < 22) || (hurt && f.hp < 50) || (style == "sniper" && dist < 10) || (style == "kiter" && dist < pr * .5f)))
                        {
                            float y = f.yaw; bool side = !Close(gun) && !rush;
                            if (side) f.yaw += Mathf.PI / 2 * (Random.value < .5f ? 1 : -1);
                            f.UseAbility(); if (side) f.yaw = y;
                        }
                        break;
                    case "jet": if (!see && T.pos.y > f.pos.y + 2 && (a.LostT > 2 || hunt)) f.UseAbility(); break;
                    case "brute":
                        if ((see && dist < 6.5f && Mathf.Abs(T.pos.y - f.pos.y) < 2) || a.StuckT > .4f)
                        { if (see) f.yaw = Mathf.Atan2(T.pos.x - f.pos.x, T.pos.z - f.pos.z); f.UseAbility(); }
                        break;
                    case "frost": if (see && dist < 8) f.UseAbility(); break;
                    case "isaac":
                        { var g = GunsDb.Gun(gun); if (see && w && g != null && !g.InfiniteMag && g.mag > 0 && w.Ammo(gun) < g.mag * .3f) f.UseAbility(); }
                        break;
                    case "thor": if (see && dist < 40) f.UseAbility((tgtBody - eye).normalized); break;
                    case "antihero":
                        if (!see && (a.LostT > 2 || hunt) && T.pos.y > f.pos.y + 2) f.UseAbility((new Vector3(T.pos.x, T.pos.y + 3, T.pos.z) - eye).normalized);
                        break;
                    case "wednesday": if (see && (((Close(gun) || rush) && dist > 8) || (hurt && f.hp < 50))) f.UseAbility(); break;
                    case "angel":
                    case "astronaut":
                        if ((!see && (a.LostT > 2 || hunt) && T.pos.y > f.pos.y + 2) || (hurt && f.hp < 40 && see && dist < 10)) f.UseAbility();
                        break;
                    case "crusty": if (see && dist < 6) f.UseAbility(); break;
                    case "dracula": if (see && dist < 12 && (hurt || f.hp < f.hpMax * .85f)) f.UseAbility(); break;
                    case "lilpink": if (see && dist < 30) { var d = tgtBody - eye; d.y += dist * .02f; f.UseAbility(d.normalized); } break;
                    case "patch": if (f.hp < 55 && (hurt || !see)) f.UseAbility(); break;
                    case "thorn": if (see && hurt && f.hp < 85) f.UseAbility(); break;
                }
            }

            // ---- walls: a reaction to being shot, or the style's habit of walling up when it first spots you
            if (Walls.On && f.walls > 0 && !Close(gun))
            {
                var k = K;
                if (f.lastHurt > hurtSeen)
                {
                    hurtSeen = f.lastHurt;
                    if (Time.time > reactCD && T.alive)
                    {
                        reactCD = Time.time + Random.Range(2.2f, 4f);
                        float wall = f.hp < k.wallHp ? .3f * k.build : 0, total = wall + 1.1f + .5f + (1 - f.hp / f.hpMax) * .8f + .6f + (style == "rusher" || style == "breacher" ? 1.4f : .35f);
                        if (Random.value * total < wall) { wallNow = true; wallUntil = Time.time + .8f; }
                    }
                }
                float dif = DifBuild.TryGetValue(a.difficulty, out var db) ? db : .3f;
                if (see && (wallNow && Time.time < wallUntil || k.build > 1.2f && a.SeeT < 1.2f && dist > 9 && Time.time > wallCD && Random.value < dif * k.build * dt * 8))
                {
                    wallNow = false; float y = f.yaw; f.yaw = Mathf.Atan2(T.pos.x - f.pos.x, T.pos.z - f.pos.z);
                    if (Walls.Build(f)) wallCD = Time.time + k.wallCD;
                    f.yaw = y;
                }
            }

            // ---- the shop kiosk: now and then, when it's not in a fight, pop over for what it needs most
            {
                if (a.ForceGoal.HasValue)
                {
                    if (Kiosk.Near(f)) { Kiosk.BotBuy(f); a.ForceGoal = null; }
                    else if (f.shopCD > 0 || Time.time > shopUntil || Kiosk.Want(f) == null || (see && dist < 10 && !T.downed)) a.ForceGoal = null;
                }
                else if (f.shopCD <= 0 && rm && rm.state == RoundManager.State.Fight && !(see && dist < 16))
                {
                    if (shopNext < 0) shopNext = Time.time + Random.Range(40f, 70f);
                    if (Time.time > shopNext && Kiosk.Want(f) != null)
                    {
                        shopNext = Time.time + Random.Range(60f, 100f);
                        var sh = Kiosk.Nearest(f);
                        if (sh.HasValue && Vector3.Distance(sh.Value, f.pos) < 45 && Random.value < .5f) { a.ForceGoal = sh; shopUntil = Time.time + 14; }
                    }
                }
            }

            // ---- Rumble: hop in for an air strike
            if (Rules.Rumble && f.grounded && see && dist > 2.2f && dist < 4.6f && Mathf.Abs(T.pos.y - f.pos.y) < 1 && Random.value < dt * 1.5f) f.inJumpPress = true;

            if (!w || w.Busy || f.dazeT > 0 || f.stunT > 0 || Rules.Rumble) return;
            var last = a.LastSeen;

            // ---- the lethal
            if (w.ConsReady())
            {
                string cid = f.lethal;
                if (cid == "sense") { if (a.LostT > 3 && Random.value < dt * .4f) w.UseCons(); }
                else if (cid == "mine")
                {
                    if ((!see && a.LostT > 1.5f || see && dist < 9) && Random.value < dt * .35f) w.UseCons();
                }
                else if (Projectiles.Aimed.Contains(cid) && last.HasValue && dist > 6 && dist < 22 && Random.value < dt * (see ? .45f : .6f))
                {
                    var dir = Lob(eye, see ? T.pos : last.Value);
                    w.throwDirOverride = dir; try { w.UseCons(); } finally { w.throwDirOverride = null; }
                }
            }
            // ---- shop grenades
            if ((w.nades > 0 || w.goldNade > 0) && w.nadeT <= 0 && last.HasValue && dist > 7 && dist < 22 && Random.value < dt * (see ? .3f : .5f))
            {
                var dir = Lob(eye, see ? T.pos : last.Value);
                w.throwDirOverride = dir; try { w.ThrowNade(); } finally { w.throwDirOverride = null; }
            }
            // ---- the Sentry
            if (w.turretKit > 0 && last.HasValue && dist < 25 && Random.value < dt * (see ? .8f : .3f))
            {
                float y0 = f.yaw; f.yaw = Mathf.Atan2(last.Value.x - f.pos.x, last.Value.z - f.pos.z);
                if (Turrets.Deploy(f)) w.turretKit--; else f.yaw = y0;
            }
        }

        /// the web game's lob: the low arc that lands a grenade at the target, plus a little
        static Vector3 Lob(Vector3 from, Vector3 to)
        {
            var g = GunsDb.Gun("grenade"); float sp = g != null && g.speed > 0 ? g.speed : 16, gg = g != null && g.grav > 0 ? g.grav : 22;
            float dx = to.x - from.x, dz = to.z - from.z, hd = Mathf.Max(.1f, Mathf.Sqrt(dx * dx + dz * dz));
            float ang = .5f * Mathf.Asin(Mathf.Clamp(gg * hd / (sp * sp), -1, 1)) + .05f;
            return new Vector3(dx / hd * Mathf.Cos(ang), Mathf.Sin(ang), dz / hd * Mathf.Cos(ang)).normalized;
        }

        static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);
    }
}
