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
            Abilities.Log = true; if (!watching) { watching = true; Fighter.AbilityUsed += (f, id) => Debug.Log($"[test] ability {id} by {(f.isBot ? "bot" : "player")} hp {f.hp:F0}"); }
            if (!RM) return "no round manager";
            var mm = Object.FindAnyObjectByType<MainMenu>();
            var play = mm ? typeof(MainMenu).GetMethod("Play", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) : null;
            if (play != null) play.Invoke(mm, null); else RM.BeginFromMenu();
            return "match started, rules " + (RM.simpleRules ? "simple" : "normal") + (play != null ? " (menu Play)" : "");
        }

        /// put the bot 9 m in front of the player, facing each other, both standing
        public static string Face(string a)
        {
            var p = P; var b = B; if (!p || !b) return "no fighters";
            float dist = a.Length > 0 ? float.Parse(a) : 9;
            var at = p.pos + p.Forward * dist; b.Place(new Vector3(at.x, p.pos.y, at.z), p.yaw + Mathf.PI); b.Frozen = true; p.hp = p.hpMax;
            if (Combat.RayBoxes(p.Eye, (b.Eye - p.Eye).normalized, dist, out var hb)) return $"bot at {b.pos}, blocked at {hb.t:F1} m by box {hb.box} {BoxWorld.Boxes[hb.box].x0:F1},{BoxWorld.Boxes[hb.box].y0:F1},{BoxWorld.Boxes[hb.box].z0:F1} - {BoxWorld.Boxes[hb.box].x1:F1},{BoxWorld.Boxes[hb.box].y1:F1},{BoxWorld.Boxes[hb.box].z1:F1}";
            return $"bot at {b.pos}, clear";
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

        /// do something, then screenshots at the given times: "boom" | "flame" | "<character id>", e.g. call StreetLifeTests.Burst boom
        public static string Burst(string what)
        {
            var go = new GameObject("test shooter"); var t = go.AddComponent<TestShooter>(); t.what = what; return "burst " + what;
        }

        public static string Flame(string a)
        {
            var p = P; if (!p) return "no player";
            var from = p.Eye + p.Forward * .9f + Vector3.down * .3f;
            GunFx.FlameJet(from, p.Forward, 9, 16, 25);
            return "flame";
        }

        public static string Aim(string on)
        {
            var p = P; if (!p) return "no player";
            var pc = p.GetComponent<PlayerController>(); if (pc) pc.enabled = on != "off";
            if (on == "off") return "aim off";
            pc.enabled = false; p.ads = true; return "aiming with " + (p.GetComponent<Weapons>() ? p.GetComponent<Weapons>().Gun : "?");
        }

        public static string Profile(string a) { var pm = Object.FindAnyObjectByType<ProfileMenu>(); if (!pm) return "no profile menu"; pm.Open(); return "profile open"; }

        public static string Unfreeze(string a) { if (B) B.Frozen = false; return "bot free"; }

        public static string State(string a)
        {
            var p = P; var b = B; if (!p) return "no player";
            return $"round {RM.round} state {RM.state} score {RM.score[0]}-{RM.score[1]} player {p.CharId} hp {p.hp:F0} abilT {p.abilT:F1} | bot {(b ? b.CharId : "-")} hp {(b ? b.hp : 0):F0} abilT {(b ? b.abilT : 0):F1} alive {(b && b.alive)} lethal {(b ? b.lethal : "")} uses left {(b && b.GetComponent<Weapons>() ? b.GetComponent<Weapons>().consN : -1)}";
        }
    }

    public class TestShooter : MonoBehaviour
    {
        public string what;
        System.Collections.IEnumerator Start()
        {
            float[] at = { .08f, .25f, .5f, 1f, 2f, 3.5f };
            if (what == "boom") StreetLifeTests.Boom("");
            else if (what == "flame") { }
            else { StreetLifeTests.Face("7"); StreetLifeTests.Ability(what); }
            float t0 = Time.time; int k = 0;
            while (k < at.Length)
            {
                if (what == "flame" && Time.time - t0 < 1.2f) StreetLifeTests.Flame("");
                if (Time.time - t0 >= at[k]) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs", $"burst_{what}_{k}.png")); k++; }
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
