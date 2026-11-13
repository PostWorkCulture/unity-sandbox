// Shared combat rules, ported from the web game: ray tests against the collision boxes (with the hit face and wood
// penetration), the fighter hitbox (head box over a body box, turned with the fighter), line of sight, damage
// (armour, stamina knock, downs), explosions with falloff and knockback, and spread.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public struct BoxHit { public float t; public int box; public Vector3 n; }
    public struct FoeHit { public float t; public bool head; public Fighter who; }

    public static class Combat
    {
        public static readonly List<Fighter> Fighters = new List<Fighter>();   // every fighter with weapons (Weapons registers itself)
        public static float Time;            // S.t: combat clock, seconds
        public static Fighter Me;            // the player

        // ------------------------------------------------------------------ teams
        public static bool IsFoe(Fighter a, Fighter b) => a && b && a != b && a.team != b.team;
        public static IEnumerable<Fighter> FoesOf(Fighter f)
        {
            foreach (var o in Fighters) if (o && o.alive && IsFoe(f, o)) yield return o;
        }

        // ------------------------------------------------------------------ rays against the map boxes
        /// rayBoxes: nearest box along the ray within maxT. skipGlass: sight passes through intact glass.
        public static bool RayBoxes(Vector3 o, Vector3 d, float maxT, out BoxHit hit, bool skipGlass = false)
        {
            hit = default; float best = maxT; int bi = -1, nAx = 0; float nS = 0;
            var B = BoxWorld.Boxes;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i];
                if (skipGlass && b.glass) continue;
                float t0 = -1e9f, t1 = 1e9f; int ax = 0; float sg = 0; bool miss = false;
                for (int a = 0; a < 3; a++)
                {
                    float oo = a == 0 ? o.x : a == 1 ? o.y : o.z, dd = a == 0 ? d.x : a == 1 ? d.y : d.z;
                    float mn = a == 0 ? b.x0 : a == 1 ? b.y0 : b.z0, mx = a == 0 ? b.x1 : a == 1 ? b.y1 : b.z1;
                    if (Mathf.Abs(dd) < 1e-9f) { if (oo < mn || oo > mx) { miss = true; break; } continue; }
                    float ta = (mn - oo) / dd, tb = (mx - oo) / dd, s = -1;
                    if (ta > tb) { var q = ta; ta = tb; tb = q; s = 1; }
                    if (ta > t0) { t0 = ta; ax = a; sg = s; }
                    if (tb < t1) t1 = tb;
                    if (t0 > t1) { miss = true; break; }
                }
                if (miss) continue;
                if (t0 <= t1 && t0 >= 0 && t0 < best) { best = t0; bi = i; nAx = ax; nS = sg; }
            }
            if (bi < 0) return false;
            float dc = nAx == 0 ? d.x : nAx == 1 ? d.y : d.z;
            var n = Vector3.zero; n[nAx] = dc == 0 ? 1 : -Mathf.Sign(dc);
            hit = new BoxHit { t = best, box = bi, n = n };
            return true;
        }

        /// rayPen: the first solid hit, plus up to three wooden boxes the bullet passes through before it
        public static BoxHit? RayPen(Vector3 o, Vector3 d, float maxT, List<BoxHit> woods)
        {
            woods.Clear(); BoxHit? hit = null; float best = maxT;
            var B = BoxWorld.Boxes;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i];
                float t0 = -1e9f, t1 = 1e9f; int ax = 0; bool miss = false;
                for (int a = 0; a < 3; a++)
                {
                    float oo = a == 0 ? o.x : a == 1 ? o.y : o.z, dd = a == 0 ? d.x : a == 1 ? d.y : d.z;
                    float mn = a == 0 ? b.x0 : a == 1 ? b.y0 : b.z0, mx = a == 0 ? b.x1 : a == 1 ? b.y1 : b.z1;
                    if (Mathf.Abs(dd) < 1e-9f) { if (oo < mn || oo > mx) { miss = true; break; } continue; }
                    float ta = (mn - oo) / dd, tb = (mx - oo) / dd;
                    if (ta > tb) { var q = ta; ta = tb; tb = q; }
                    if (ta > t0) { t0 = ta; ax = a; }
                    if (tb < t1) t1 = tb;
                    if (t0 > t1) { miss = true; break; }
                }
                if (miss || !(t0 <= t1 && t0 >= 0 && t0 < maxT)) continue;
                var h = new BoxHit { t = t0, box = i, n = Normal(ax, d) };
                if (b.wood) woods.Add(h); else if (t0 < best) { best = t0; hit = h; }
            }
            woods.Sort((p, q) => p.t.CompareTo(q.t));
            if (hit.HasValue) woods.RemoveAll(w => w.t >= hit.Value.t);
            if (woods.Count > 3) { hit = woods[3]; woods.RemoveRange(3, woods.Count - 3); }
            return hit;
        }

        static Vector3 Normal(int ax, Vector3 d) { var n = Vector3.zero; float dc = d[ax]; n[ax] = dc == 0 ? 1 : -Mathf.Sign(dc); return n; }

        public static bool Los(Vector3 a, Vector3 b)
        {
            var d = b - a; float L = d.magnitude; if (L < 1e-5f) return true;
            return !RayBoxes(a, d / L, L, out _, true);
        }

        // ------------------------------------------------------------------ fighter hitboxes
        static float Slab(float ox, float oy, float oz, float dx, float dy, float dz, float hx, float y0, float y1, float hz)
        {
            float t0 = 0, t1 = 1e9f;
            float[] o = { ox, oy, oz }, d = { dx, dy, dz }, mn = { -hx, y0, -hz }, mx = { hx, y1, hz };
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(d[i]) < 1e-9f) { if (o[i] < mn[i] || o[i] > mx[i]) return -1; continue; }
                float a = (mn[i] - o[i]) / d[i], b = (mx[i] - o[i]) / d[i];
                if (a > b) { var q = a; a = b; b = q; }
                if (a > t0) t0 = a; if (b < t1) t1 = b; if (t0 > t1) return -1;
            }
            return t0;
        }

        /// rayFighter: head box (.76 wide, 2.0-2.68 m) and body box (2 m wide, 0-2.02 m), turned with the fighter
        public static bool RayFighter(Vector3 o, Vector3 d, Fighter f, out float t, out bool head)
        {
            t = 0; head = false;
            if (!f.alive) return false;
            if (f.downed)
            {   // lying on the ground: the finishers chat's downed box (no headshots)
                float td = Finishers.RayDowned(o, d, f);
                if (td < 0) return false;
                t = td; return true;
            }
            float c = Mathf.Cos(f.yaw), s = Mathf.Sin(f.yaw);
            float wx = o.x - f.pos.x, wz = o.z - f.pos.z, oy = o.y - f.pos.y;
            float ox = wx * c - wz * s, oz = wx * s + wz * c, dx = d.x * c - d.z * s, dz = d.x * s + d.z * c;
            float sc = Fighter.SC, cy = sc * (1 - .27f * f.cv);
            float th = Slab(ox, oy, oz, dx, d.y, dz, .38f * sc, 2.0f * cy, 2.68f * cy, .36f * sc);
            float tb = Slab(ox, oy, oz, dx, d.y, dz, 1.0f * sc, 0, 2.02f * cy, .34f * sc);
            if (th >= 0 && (tb < 0 || th <= tb + .05f)) { t = th; head = true; return true; }
            if (tb >= 0) { t = tb; return true; }
            return false;
        }

        public static bool RayFoes(Vector3 o, Vector3 d, Fighter f, out FoeHit hit)
        {
            hit = default; bool any = false;
            foreach (var v in FoesOf(f))
                if (RayFighter(o, d, v, out var t, out var h) && (!any || t < hit.t)) { hit = new FoeHit { t = t, head = h, who = v }; any = true; }
            return any;
        }

        // ------------------------------------------------------------------ spread
        public static Vector3 SpreadDir(Vector3 d, float deg)
        {
            if (deg <= 0) return d;
            float a = deg * Mathf.Deg2Rad * Mathf.Sqrt(GR.Next()), t = GR.Next() * Mathf.PI * 2;
            var up = Mathf.Abs(d.y) > .95f ? Vector3.right : Vector3.up;
            var u = Vector3.Cross(d, up).normalized; var v = Vector3.Cross(d, u);
            return (d * Mathf.Cos(a) + u * (Mathf.Sin(a) * Mathf.Cos(t)) + v * (Mathf.Sin(a) * Mathf.Sin(t))).normalized;
        }

        // ------------------------------------------------------------------ damage
        public static System.Action<Fighter, float, Fighter, bool, string> OnDamage;   // victim, amount, source, head, gun
        /// the player's hit feedback (hitMarker + dmgNumber in the web game): victim, number shown, headshot, strong marker
        public static System.Action<Fighter, float, bool, bool> OnFeedback;
        public static void Feedback(Fighter victim, float shown, bool head, bool strong) { OnFeedback?.Invoke(victim, shown, head, strong); }

        /// Every weapon hit goes through here: the match code's Fighter.Damage (armour first, stats, kills, feed, going down),
        /// plus the web game's stamina knock and the hit effects. Returns true if the hit put them down or out.
        public static bool Damage(Fighter f, float dmg, Fighter src, bool head, string gun)
        {
            if (!f || !f.alive || dmg <= 0) return false;
            if (src && src != f && src.team == f.team) return false;
            dmg = Mathf.Max(1, Mathf.Round(dmg));
            bool wasDown = f.downed;
            if (!wasDown)
            {
                float ab = Mathf.Min(f.armor, dmg);
                if (ab > 0) HitSprayArmour(f);
                f.StaminaKnock(ab, dmg - ab);
            }
            if (f == Me && src && src != Me) { GunFx.Shake = Mathf.Max(GunFx.Shake, .18f); f.hurtFlash = .55f; }
            if (f == Me && (gun == "grenade" || gun == "rocket")) f.blastT = 1.1f;
            f.Damage(dmg, src, head);   // at 0 health the fighter goes down by itself (finishers chat); while down it comes off downed health
            OnDamage?.Invoke(f, dmg, src, head, gun);
            return !f.alive || (f.downed && !wasDown);
        }
        static void HitSprayArmour(Fighter f) => GunFx.HitSpray(f.pos + Vector3.up * 1.3f, null, 5, 1, new[] { Web.Hex("#2f7fe0"), Web.Hex("#bfe3ff"), Web.Hex("#1b4fa0") }, true, "#4d9dff");

        /// the gun the fighter is holding, for the kill feed
        public static string GunName(Fighter f)
        {
            var w = f ? f.GetComponent<Weapons>() : null; var g = w ? w.G : null;
            return g != null ? g.name : f && f.primary == "gar" ? "M4" : f ? f.primary : "";
        }

        // ------------------------------------------------------------------ explosions
        public class Blast { public float dmg, radius; public string gun = "rocket"; public string[] colors; public bool small; }
        public static Fighter RocketSkip;

        public static Blast Rocket => new Blast { dmg = GunsDb.Gun("rocket").dmg, radius = GunsDb.Gun("rocket").radius, gun = "rocket", colors = new[] { "#ffd23f", "#ff7a1f", "#ff3b1f", "#3a3d45", "#ffffff" } };

        /// explode: falloff from full damage at 0.6 m to 20% (min 18) at the edge, needs line of sight past 1.2 m;
        /// your own blasts hurt you half and push you less. Also sets off mines in range.
        public static void Explode(Fighter src, Vector3 at, Vector3? n, Blast cfg)
        {
            cfg = cfg ?? Rocket;
            if (n.HasValue) at += n.Value * .2f;
            Sfx.Play("explode", at, cfg.gun == "swarm" ? .5f : 1, 22);
            float R = cfg.radius;
            var cols = new Color[cfg.colors != null ? cfg.colors.Length : 0];
            for (int i = 0; i < cols.Length; i++) cols[i] = Web.Hex(cfg.colors[i]);
            if (cfg.gun == "grenade" || cfg.gun == "rocket") GunFx.Boom(at, R, cols);
            else GunFx.Emit(at + Vector3.up * .2f, new EmitOpts { n = Mathf.RoundToInt(70 * R / 4.5f), speed = 12 * R / 4.5f, up = 3, size = .38f, life = .8f, grav = 4, colors = cols });
            if (Me && Me.alive) { float dd = Vector3.Distance(Me.pos, at); if (dd < 14) GunFx.Shake = Mathf.Max(GunFx.Shake, .6f * (1 - dd / 14) + .15f); }
            foreach (var o in Fighters.ToArray())
            {
                if (!o || !o.alive || (o != src && !IsFoe(src, o)) || o == RocketSkip) continue;
                var c = new Vector3(o.pos.x, o.pos.y + 1.1f * Fighter.SC, o.pos.z); float dd = Vector3.Distance(c, at);
                if (dd > R + .6f) continue;
                if (!Los(at, c) && dd > 1.2f) continue;
                float dm = Mathf.Round(Mathf.Max(Mathf.Min(18, cfg.dmg * .2f), cfg.dmg * (1 - Mathf.Max(0, dd - .6f) / R)));
                if (o == src) dm = Mathf.Round(dm * .5f);
                var kb = c - at; kb.y = 0; kb = kb.sqrMagnitude > 1e-6f ? kb.normalized : Vector3.zero;
                if (o.InFinisher) { }   // a finishing move moves both fighters itself
                else if (o == src) { o.vel.x += kb.x * 3; o.vel.z += kb.z * 3; }
                else { o.vel.x += kb.x * 9; o.vel.z += kb.z * 9; o.vel.y = Mathf.Max(o.vel.y, 7); o.grounded = false; }
                bool downs = o.downed ? dm >= o.dhp : dm >= o.hp + o.armor;
                if (src == Me && o != Me) Feedback(o, dm, false, downs);
                if (src && o != src) src.NoteHit(false);
                Damage(o, dm, src, false, cfg.gun);
            }
            Destruction.Blast(at, R, src, 0);   // the world side: cars, props, trees, loose items (fighters are hurt above)
            Mines.BlastNear(at, Mathf.Min(R, 3));
            Turrets.Splash(at, R, cfg.dmg > 0 ? cfg.dmg : 100, src);
        }
    }
}
