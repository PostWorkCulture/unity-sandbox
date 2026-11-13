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

        void Awake() { f = GetComponent<Fighter>(); a = GetComponent<BotBrain>(); }

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

            if (!w || w.Busy || f.dazeT > 0 || f.stunT > 0) return;
            var last = a.LastSeen;

            // ---- the lethal
            if (w.ConsReady())
            {
                string cid = f.lethal;
                if (cid == "mine")
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
