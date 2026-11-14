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

        public static string Wall(string a)
        {
            var p = P; if (!p) return "no player"; p.Frozen = false; p.buildT = 0; if (p.walls <= 0) p.walls = 4;
            bool ok = Walls.Build(p); return $"wall built {ok}, walls left {p.walls}, boxes {BoxWorld.Boxes.Count}";
        }

        public static string ShootWall(string a)
        {
            var p = P; if (!p) return "no player";
            int hits = 0; for (int i = 0; i < 12; i++) { var o = p.Eye; var d = p.Forward; float t = BoxWorld.Ray(o, d, 10, out int bi); if (t >= 0 && Destruction.Hit(bi, 25, o + d * t, d, p)) hits++; }
            return "brick hits " + hits;
        }

        public static string Kiosk(string a)
        {
            var p = P; if (!p) return "no player"; var k = StreetLife.Kiosk.Nearest(p); if (!k.HasValue) return "no kiosk on " + MapSet.Current;
            p.Place(k.Value + new Vector3(1.5f, 0, 0), 0); p.Frozen = false;
            var w = p.GetComponent<Weapons>(); float ar = p.armor; w.Buy("armor"); float ar2 = p.armor; w.Buy("speed");
            return $"kiosk {k.Value} near {StreetLife.Kiosk.Near(p)} armour {ar}->{ar2} cd {p.shopCD:F0} speedT {p.speedT:F0}";
        }

        public static string Sense(string a)
        {
            var p = P; if (!p) return "no player"; var w = p.GetComponent<Weapons>(); w.SetLoadout(p.primary, p.secondary, "sense"); p.Frozen = false;
            bool ok = w.UseCons(); return $"sense used {ok} senseT {p.senseT:F1} consT {w.consT:F0}";
        }

        public static string Gold(string a)
        {
            var p = P; if (!p) return "no player"; var w = p.GetComponent<Weapons>(); w.SetLoadout(p.primary, p.secondary, "gold"); p.armor = 5; p.lastHurt = -99;
            return "gold set, armour " + p.armor;
        }

        public static string SetRules(string a)
        {   // e.g. "duels 1v1", "rumble 2v2", "duels ffa4"
            var parts = a.Split(' '); Rules.Mode = parts[0];
            var pl = parts.Length > 1 ? parts[1] : "1v1"; if (pl.StartsWith("ffa")) { Rules.Players = "ffa"; Rules.FfaN = pl.Length > 3 ? pl[3] - '0' : 4; } else Rules.Players = pl;
            PlayerPrefs.Save(); return $"rules {Rules.Mode} {Rules.Players} n {Rules.FighterCount}";
        }

        public static string Fighters(string a)
        {
            if (!RM) return "no rm"; var sb = new System.Text.StringBuilder($"{RM.state} r{RM.round} t{RM.timer:F0} dm {RM.Deathmatch}: ");
            foreach (var f in RM.fighters) { var br = f.GetComponent<BotBrain>(); var w = f.GetComponent<Weapons>(); sb.Append($"[{f.displayName} t{f.team} {(f.alive ? (f.downed ? "down" : "up") : "out")} hp{f.hp:F0} k{f.kills} {(w ? w.Gun : "")} ->{(br && br.target ? br.target.displayName : "-")}] "); }
            sb.Append(" score "); for (int i = 0; i < 7; i++) sb.Append(RM.score[i] + ","); return sb.ToString();
        }

        public static string Map(string a) { PlayerPrefs.SetString("StreetLife.map", a.Trim()); PlayerPrefs.Save(); return "map pick " + a.Trim() + " (takes effect on next play)"; }

        /// the player takes out every enemy (down, then out): for kill clips and round flow
        public static string KillAll(string a)
        {
            var p = P; if (!p || !RM) return "no player";
            int n = 0; foreach (var f in RM.fighters) if (f && f.alive && f.team != p.team) { f.Damage(500, p, a == "head"); if (f.alive) f.Damage(500, p, false); n++; }
            return $"took out {n}, state {RM.state} score {RM.score[0]}-{RM.score[1]}";
        }

        /// Rumble: the player drops onto the bot from above and punches (the air strike)
        public static string AirPunch(string a)
        {
            var p = P; var b = B; if (!p || !b) return "no fighters";
            if (!Rules.Rumble) return "set rumble first (SetRules rumble 1v1)";
            b.Frozen = true; float hp0 = b.hp + b.armor;
            var fwd = new Vector3(Mathf.Sin(b.yaw), 0, Mathf.Cos(b.yaw));
            p.Place(b.pos + fwd * 1.6f + Vector3.up * 1.3f, b.yaw + Mathf.PI);
            p.grounded = false; p.vel = new Vector3(0, -3, 0);
            var w = p.GetComponent<Weapons>();
            var m = typeof(Weapons).GetMethod("Melee", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            m.Invoke(w, new object[] { w.G });
            return $"gun {w.Gun}: bot {hp0:F0} -> {b.hp + b.armor:F0}, stunT {b.stunT:F2}, bot vel {b.vel}";
        }

        /// the player goes down, then fires a marker at the bot (tagging)
        public static string Tag(string a)
        {
            var p = P; var b = B; if (!p || !b) return "no fighters";
            b.Frozen = true;
            if (!p.downed) p.Damage(Mathf.Max(1, p.hp + p.armor), b, false);
            if (!p.downed) return "player didn't go down (hp " + p.hp + ")";
            var cam = Camera.main; cam.transform.position = p.pos + Vector3.up * 1.4f + (b.pos - p.pos).normalized * .3f; cam.transform.LookAt(b.pos + Vector3.up * 1.2f);
            var intel = Object.FindAnyObjectByType<Intel>();
            var m = typeof(Intel).GetMethod("TagShot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            m.Invoke(intel, new object[] { p });
            return $"bot tagged {b.Tagged} (for {b.tagT - Time.time:F1} s)";
        }

        public static string Unfreeze(string a) { if (B) B.Frozen = false; return "bot free"; }

        public static string State(string a)
        {
            var p = P; var b = B; if (!p) return "no player";
            return $"round {RM.round} state {RM.state} score {RM.score[0]}-{RM.score[1]} player {p.CharId} hp {p.hp:F0} abilT {p.abilT:F1} | bot {(b ? b.CharId : "-")} hp {(b ? b.hp : 0):F0} abilT {(b ? b.abilT : 0):F1} alive {(b && b.alive)} lethal {(b ? b.lethal : "")} walls {(b ? b.walls : 0)} gun {(b && b.GetComponent<Weapons>() ? b.GetComponent<Weapons>().Gun : "")} style {(RM.botBrain ? RM.botBrain.styleId : "")} mode {(RM.botBrain ? RM.botBrain.Mode : "")} | p armour {p.armor:F1} uses left {(b && b.GetComponent<Weapons>() ? b.GetComponent<Weapons>().consN : -1)}";
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
