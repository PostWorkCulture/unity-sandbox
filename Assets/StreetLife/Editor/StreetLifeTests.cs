// Play-mode test helpers for driving the game from a chat (match chat): remote command "call StreetLifeTests.<Method> [arg]".
using UnityEngine;

namespace StreetLife.EditorTools
{
    public static class StreetLifeTests
    {
        static RoundManager RM => RoundManager.Instance;
        static Fighter P => RM ? RM.player : null;
        static Fighter B => RM ? RM.bot : null;
        static bool watching;

        public static string Begin(string a)
        {
            if (!watching) { watching = true; Fighter.AbilityUsed += (f, id) => Debug.Log($"[test] ability {id} by {(f.isBot ? "bot" : "player")} hp {f.hp:F0}"); }
            if (!RM) return "no round manager";
            RM.BeginFromMenu(); return "match started, rules " + (RM.simpleRules ? "simple" : "normal");
        }

        /// put the bot 9 m in front of the player, facing each other, both standing
        public static string Face(string a)
        {
            var p = P; var b = B; if (!p || !b) return "no fighters";
            var at = p.pos + p.Forward * 9; b.Place(new Vector3(at.x, p.pos.y, at.z), p.yaw + Mathf.PI);
            return $"bot at {b.pos}";
        }

        public static string Ability(string id)
        {
            var p = P; if (!p) return "no player";
            var rig = p.GetComponent<CharacterRig>(); if (rig && !string.IsNullOrEmpty(id)) CharacterSkin.Apply(rig, id);
            p.abilT = 0; p.Frozen = false;
            var b = B; Vector3? dir = null;
            if (b) { var d = b.pos + Vector3.up * 1.2f - p.Eye; dir = d.normalized; p.yaw = Mathf.Atan2(d.x, d.z); }
            float hp0 = b ? b.hp : 0;
            bool ok = p.UseAbility(dir);
            return $"{p.CharId}: used {ok}, ability {p.AbilityName}, cd {p.AbilityCooldown}, bot hp {hp0:F0}->{(b ? b.hp : 0):F0} slow {(b ? b.slowT : 0):F1} vel {p.vel}";
        }

        public static string Boom(string a)
        {
            var p = P; if (!p) return "no player";
            var at = p.pos + p.Forward * 7 + Vector3.up * .3f;
            Combat.Explode(null, at, null, new Combat.Blast { dmg = 0, radius = 4.5f, gun = "grenade", colors = new[] { "#ffd23f", "#ff7a1f", "#3a3d45", "#ffffff" } });
            return "boom at " + at;
        }

        public static string Flame(string a)
        {
            var p = P; if (!p) return "no player";
            var from = p.Eye + p.Forward * .9f + Vector3.down * .3f;
            for (int i = 0; i < 12; i++) GunFx.FlameJet(from, p.Forward, 9, 16, 25);
            return "flame";
        }

        public static string Aim(string on)
        {
            var p = P; if (!p) return "no player";
            var pc = p.GetComponent<PlayerController>(); if (pc) pc.enabled = on != "off";
            if (on == "off") return "aim off";
            pc.enabled = false; p.ads = true; return "aiming with " + (p.GetComponent<Weapons>() ? p.GetComponent<Weapons>().Gun : "?");
        }

        public static string State(string a)
        {
            var p = P; var b = B; if (!p) return "no player";
            return $"round {RM.round} state {RM.state} score {RM.score[0]}-{RM.score[1]} player {p.CharId} hp {p.hp:F0} abilT {p.abilT:F1} | bot {(b ? b.CharId : "-")} hp {(b ? b.hp : 0):F0} abilT {(b ? b.abilT : 0):F1} alive {(b && b.alive)} lethal {(b ? b.lethal : "")}";
        }
    }
}
