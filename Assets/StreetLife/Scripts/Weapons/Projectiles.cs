// Everything that flies or sits in the world after it leaves a hand, ported from the web game's updProjs, mines and
// Sentry code: rockets (direct hit + splash), Swarm micro-missiles that curve onto their lock, crossbow bolts with drop,
// bouncing grenades (fuse starts on first landing), stun and flash grenades, cluster grenades and their bomblets,
// proximity mines, and the Sentry turret.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public static class Projectiles
    {
        public class Proj
        {
            public Fighter f; public string id; public GunStat g; public Vector3 pos, vel; public float life, age, bT = -9; public bool armed, prox, gold;
            public Fighter lockOn; public Vector3? tgt; public Transform mesh, flame;
        }
        public static readonly List<Proj> List = new List<Proj>();
        public static readonly HashSet<string> Thrown = new HashSet<string> { "grenade", "stun", "flash", "cluster", "bomblet" };
        public static readonly HashSet<string> Aimed = new HashSet<string> { "grenade", "stun", "flash", "cluster" };
        static Transform root;
        static Transform Root { get { if (!root) root = new GameObject("Projectiles").transform; return root; } }

        public static Proj Spawn(Fighter f, string id, Vector3 from, Vector3 dir)
        {
            var g = GunsDb.Gun(id); if (g == null) return null;
            var p = new Proj { f = f, id = id, g = g, pos = from, vel = dir * g.speed, life = g.landFuse > 0 ? 8 : g.Fuse, armed = !(g.landFuse > 0) };
            var pj = GunsDb.Proj(id);
            if (pj != null)
            {
                p.mesh = WeaponModels.Build(pj.nodes, Root, id);
                p.mesh.position = from;
                p.flame = WeaponModels.Find(p.mesh, "flame");
                if (id == "rocket" || id == "swarm" || id == "crossbow") p.mesh.rotation = Quaternion.LookRotation(-dir);
            }
            List.Add(p);
            return p;
        }

        public static void MakeGold(Proj p)
        {
            p.gold = true; if (!p.mesh) return;
            var m = WeaponModels.Mat(GunsDb.Data.goldMat);
            foreach (var r in p.mesh.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = m;
        }

        public static void Clear() { foreach (var p in List) if (p.mesh) Object.Destroy(p.mesh.gameObject); List.Clear(); Mines.Clear(); Turrets.Clear(); }

        /// rocketNear: rockets and missiles burst on anyone they pass within 0.55 m of (not only a dead-on hit)
        static bool RocketNear(Proj p, Vector3 st, float L, out float t, out Fighter who)
        {
            t = 0; who = null; bool any = false;
            foreach (var o in Combat.FoesOf(p.f))
            {
                float sc = Fighter.SC;
                for (int k = 1; k <= 5; k++)
                {
                    float u = k / 5f; var q = p.pos + st * u; float h = Mathf.Sqrt((q.x - o.pos.x) * (q.x - o.pos.x) + (q.z - o.pos.z) * (q.z - o.pos.z));
                    if (h < .55f && q.y > o.pos.y + .1f && q.y < o.pos.y + 2.7f * sc) { float tt = L * u; if (!any || tt < t) { t = tt; who = o; any = true; } break; }
                }
            }
            return any;
        }

        static readonly string[] Boom5 = { "#ffd23f", "#ff7a1f", "#ff3b1f", "#3a3d45", "#ffffff" };
        static readonly string[] Nade = { "#ffd23f", "#ff7a1f", "#3a3d45", "#ffffff" };

        public static void Update(float dt)
        {
            for (int i = List.Count - 1; i >= 0; i--)
            {
                if (i >= List.Count) continue;
                var p = List[i]; var g = p.g; p.life -= dt;
                if (p.id == "swarm")
                {
                    p.age += dt;
                    if (p.age > .07f)
                    {
                        Vector3? L0 = p.lockOn && p.lockOn.alive ? p.lockOn.pos + Vector3.up * (1.1f * Fighter.SC) : p.tgt;
                        if (L0.HasValue)
                        {
                            float sp = p.vel.magnitude; var want = L0.Value - p.pos;
                            if (want.sqrMagnitude > .01f) { want = want.normalized * sp; p.vel = Vector3.Lerp(p.vel, want, 1 - Mathf.Exp(-dt * (p.age < .22f ? 7 : 15))); p.vel = p.vel.normalized * sp; }
                            if (p.lockOn && Vector3.Distance(p.pos, L0.Value) < .95f) p.prox = true;
                        }
                    }
                }
                if (g.accel > 0) { float sp = p.vel.magnitude; if (sp < g.maxSpeed && sp > 1e-5f) p.vel *= Mathf.Min(g.maxSpeed, sp + g.accel * dt) / sp; }
                p.vel.y -= g.grav * dt;
                var st = p.vel * dt; float Ls = st.magnitude; if (Ls < 1e-6f) continue;
                var d = st / Ls;
                bool hb = Combat.RayBoxes(p.pos, d, Ls, out var bh);
                bool hf = Combat.RayFoes(p.pos, d, p.f, out var fh);
                bool thrown = Thrown.Contains(p.id);
                if (thrown && p.mesh) { p.mesh.Rotate(Vector3.right, dt * 8 * Mathf.Rad2Deg, Space.Self); if (Random.value < .35f) GunFx.Emit(p.pos + Vector3.up * .2f, GunFx.E(1, .3f, .08f, .3f, -1, "#ffd23f")); }

                // what it runs into this step: 0 none, 1 fighter, 2 box, 3 ground / proximity
                int kind = 0; float ht = 0; Fighter who = null; bool head = false;
                if ((p.id == "rocket" || p.id == "swarm") && RocketNear(p, st, Ls, out var nt, out var nw) && (!hf || fh.t > Ls || nt < fh.t) && (!hb || nt < bh.t)) { kind = 1; ht = nt; who = nw; }
                if (kind == 0 && p.prox) { kind = 3; ht = 0; }
                if (kind == 0)
                {
                    if (hf && fh.t <= Ls && (!hb || fh.t < bh.t)) { kind = 1; ht = fh.t; who = fh.who; head = fh.head; }
                    else if (hb) { kind = 2; ht = bh.t; }
                    else if (p.pos.y + st.y < 0) { kind = 3; ht = Ls * p.pos.y / Mathf.Max(1e-6f, -st.y); }
                }
                // anything thrown goes straight through a pane of glass and smashes it (16 Nov)
                if (thrown && kind == 2 && Destruction.IsGlass(bh.box) && p.vel.magnitude > 3f)
                { Destruction.Hit(bh.box, 200, p.pos + d * ht, d, p.f); kind = 0; p.vel *= .85f; }
                // thrown things bounce (and hit people like a wall), the fuse starts on the first landing
                if (thrown && p.life > 0 && kind != 0 && (kind != 1 || p.id == "bomblet"))
                {
                    var n = kind == 2 ? bh.n : Vector3.up;
                    if (kind == 1) kind = 3;
                    var at = p.pos + d * ht + n * .06f;
                    { float v = p.vel.magnitude; if (v > 2.5f && Combat.Time - p.bT > .12f) { p.bT = Combat.Time; Sfx.Play(Random.value < .5f ? "bounce1" : "bounce2", at, Mathf.Clamp(v / 14, .3f, 1), 12); } }
                    if (!p.armed && n.y > .5f) { p.armed = true; p.life = g.fuse; }
                    p.vel = (p.vel - 2 * Vector3.Dot(p.vel, n) * n) * .45f;
                    if (n.y > .5f && Mathf.Abs(p.vel.y) < 1.5f) { p.vel.y = 0; p.vel.x *= .8f; p.vel.z *= .8f; }
                    p.pos = at; if (p.mesh) p.mesh.position = at;
                    continue;
                }
                // (a thrown grenade that hits a person goes off on them; bomblets bounce off people like the floor)
                if (thrown && p.life <= 0) kind = 0;
                if (kind != 0 || p.life <= 0)
                {
                    var at = kind != 0 ? p.pos + d * ht : p.pos;
                    if (p.mesh) Object.Destroy(p.mesh.gameObject);
                    List.RemoveAt(i);
                    Vector3? nrm = kind == 2 ? bh.n : (Vector3?)null;
                    switch (p.id)
                    {
                        case "rocket":
                            if (kind == 1 && who)
                            {
                                float dm = g.direct; bool downs = dm >= who.hp + who.armor;
                                if (p.f == Combat.Me) Combat.Feedback(who, dm, false, downs);
                                if (p.f) p.f.NoteHit(false);
                                var kb = p.vel; kb.y = 0; kb.Normalize(); who.vel.x += kb.x * 10; who.vel.z += kb.z * 10; who.vel.y = Mathf.Max(who.vel.y, 6); who.grounded = false;
                                Combat.Damage(who, dm, p.f, false, "rocket"); Combat.RocketSkip = who;
                            }
                            Combat.Explode(p.f, at, nrm, null); Combat.RocketSkip = null; break;
                        case "grenade":
                            Combat.Explode(p.f, at + Vector3.up * .2f, null, p.gold
                                ? new Combat.Blast { dmg = g.dmg * 2, radius = g.radius * 1.25f, gun = "grenade", colors = new[] { "#ffd23f", "#ffc61a", "#fff3b0", "#ffffff" } }
                                : new Combat.Blast { dmg = g.dmg, radius = g.radius, gun = "grenade", colors = Nade }); break;
                        case "swarm": Combat.Explode(p.f, at, nrm, new Combat.Blast { dmg = g.dmg, radius = g.radius, gun = "swarm", colors = Boom5 }); break;
                        case "stun": Lethals.StunBlast(p.f, at + Vector3.up * .3f); break;
                        case "flash": Lethals.FlashBang(p.f, at + Vector3.up * .3f); break;
                        case "cluster":
                            at += Vector3.up * .2f;
                            Combat.Explode(p.f, at, null, new Combat.Blast { dmg = g.dmg, radius = g.radius, gun = "grenade", colors = Nade });
                            for (int k = 0; k < 5; k++)
                            {
                                float a = k / 5f * Mathf.PI * 2 + Random.Range(-.3f, .3f), sp = Random.Range(4f, 7f);
                                var b = Spawn(p.f, "bomblet", at + Vector3.up * .25f, new Vector3(Mathf.Cos(a) * sp / 8, Random.Range(.7f, 1.1f) * 7 / 8, Mathf.Sin(a) * sp / 8));
                                if (b != null) b.life = Random.Range(.7f, 1.2f);
                            }
                            break;
                        case "bomblet": Combat.Explode(p.f, at + Vector3.up * .15f, null, new Combat.Blast { dmg = g.dmg, radius = g.radius, gun = "grenade", small = true, colors = Nade }); break;
                        default:   // crossbow bolt (and ninja stars): one hit, 2x on the head
                            if (kind == 1 && who)
                            {
                                float dm = Mathf.Round(g.dmg * (head ? g.head : 1));
                                if (p.f == Combat.Me) Combat.Feedback(who, dm, head, head);
                                if (p.f) p.f.NoteHit(head);
                                GunFx.Emit(at, GunFx.E(8, 4, .12f, .5f, 12, "#ff4a3d", "#ffffff"));
                                Combat.Damage(who, dm, p.f, head, p.id);
                            }
                            else
                            {
                                if (kind == 2) { Turrets.HitBox(bh.box, p.id == "crossbow" ? 90 : 40, p.f); Destruction.Hit(bh.box, p.id == "crossbow" ? 70 : 40, at, p.vel.normalized, p.f); }
                                GunFx.Emit(at, p.id == "crossbow" ? GunFx.E(6, 3, .08f, .3f, 12, "#3bff7a", "#ffffff") : GunFx.E(6, 3, .08f, .3f, 12, "#ffffff", "#c9ced8"));
                            }
                            break;
                    }
                    continue;
                }
                p.pos += st; if (p.mesh) p.mesh.position = p.pos;
                if (p.id == "swarm")
                {
                    if (p.mesh) p.mesh.rotation = Quaternion.LookRotation(-d);
                    if (p.flame) p.flame.localScale = new Vector3(1, 1, .7f + Random.value * .6f);
                    if (p.f != Combat.Me || p.age > .1f) GunFx.Emit(p.pos - d * .25f, GunFx.E(1, .3f, .11f, .45f, -1, "#e6e8ec", "#c4c8cf"));
                }
                else if (p.id == "rocket")
                {
                    if (p.mesh) p.mesh.rotation = Quaternion.LookRotation(-d);
                    if (p.flame) p.flame.localScale = new Vector3(1, 1, .7f + Random.value * .6f);
                    p.age += dt;
                    if (p.f != Combat.Me || p.age > .15f) GunFx.Emit(p.pos - d * .6f, GunFx.E(2, .5f, .2f, .7f, -1.2f, "#d8dbe0", "#a9adb5", "#f4f4f4"));
                    if (Random.value < .5f) GunFx.Emit(p.pos - d * .5f, GunFx.E(1, .8f, .14f, .18f, 0, "#ffb347", "#ffd23f"));
                }
                else if (p.id == "crossbow")
                {
                    if (p.mesh) p.mesh.rotation = Quaternion.LookRotation(-d);
                    if (Random.value < .6f) GunFx.Emit(p.pos, GunFx.E(1, .2f, .07f, .22f, 0, "#3bff7a", "#b8ffd0"));
                }
            }
        }

        /// simThrow: the same flight as Update (gravity, bounces, fuse) for the dotted throw preview. first = no bounce.
        public static Vector3 SimThrow(string id, Vector3 from, Vector3 dir, List<Vector3> pts, out Vector3 n)
        {
            var g = GunsDb.Gun(id); n = Vector3.up; pts.Clear();
            bool bouncy = Thrown.Contains(id) && false;   // the web game previews only the first flight (first = true)
            var pos = from; var vel = dir * g.speed; float life = g.landFuse > 0 ? 8 : (g.fuse > 0 ? g.fuse : 2.2f); bool armed = !(g.landFuse > 0);
            pts.Add(pos); float dt = 1 / 60f;
            for (int k = 0; k < 400 && life > 0; k++)
            {
                life -= dt; vel.y -= g.grav * dt; var st = vel * dt; float L = st.magnitude; if (L < 1e-6f) continue; var d = st / L;
                float? ht = null; var hn = Vector3.up;
                if (Combat.RayBoxes(pos, d, L, out var hb)) { ht = hb.t; hn = hb.n; }
                else if (pos.y + st.y < 0) { ht = L * pos.y / Mathf.Max(1e-6f, -st.y); hn = Vector3.up; }
                if (ht.HasValue)
                {
                    var at = pos + d * ht.Value;
                    if (!bouncy) { n = hn; pts.Add(at); return at; }
                    if (!armed && hn.y > .5f) { armed = true; life = g.fuse; }
                    pos = at + hn * .06f; vel = (vel - 2 * Vector3.Dot(vel, hn) * hn) * .45f; n = hn;
                }
                else pos += st;
                if (k % 2 == 0) pts.Add(pos);
            }
            return pos;
        }
    }

    // ---------------------------------------------------------------------- stun and flash grenades
    public static class Lethals
    {
        /// stun grenade: everyone in 6.5 m with line of sight is dazed (moves at 25%) for 1 s (thrower 0.5 s) and drops their aim
        public static void StunBlast(Fighter src, Vector3 at)
        {
            Sfx.Play("stun", at, 1, 20);
            GunFx.Emit(at, GunFx.E(60, 11, .24f, .45f, 0, "#ffffff", "#dff0ff", "#7fc4ff"));
            GunFx.Emit(at, GunFx.E(14, 2, .6f, .9f, -.5f, "#e8ecf2", "#c9ced8"));
            float R = GunsDb.Gun("stun").radius; bool any = false;
            foreach (var f in Combat.Fighters)
            {
                if (!f || !f.alive || (f != src && !Combat.IsFoe(src, f))) continue;
                var c = f.pos + Vector3.up * (1.4f * Fighter.SC); float d = Vector3.Distance(c, at);
                if (d > R || !Combat.Los(at, c)) continue;
                float k = 1 - d / R; if (f != src) any = true;
                f.dazeT = Mathf.Max(f.dazeT, f == src ? .5f : 1); f.ads = false;
                if (f == Combat.Me)
                {
                    var cam = Camera.main; float facing = cam ? Vector3.Dot(cam.transform.forward, (at - cam.transform.position).normalized) : 0;
                    f.flashA = Mathf.Max(f.flashA, Mathf.Clamp(.55f + .45f * k + (facing > .3f ? .3f : 0), 0, 1)); GunFx.Shake = Mathf.Max(GunFx.Shake, .3f);
                }
            }
            if (src == Combat.Me && any) Combat.Feedback(null, 0, false, false);
        }

        /// flash bang: anyone facing it within 13 m is whited out for 1 s (0.6 s looking away)
        public static void FlashBang(Fighter src, Vector3 at)
        {
            Sfx.Play("stun", at, 1, 24, 1.25f);
            GunFx.Emit(at, GunFx.E(70, 13, .26f, .4f, 0, "#ffffff", "#fffbe0"));
            float R = GunsDb.Gun("flash").radius; bool any = false;
            foreach (var f in Combat.Fighters)
            {
                if (!f || !f.alive || (f != src && !Combat.IsFoe(src, f))) continue;
                var c = f.Eye; float d = Vector3.Distance(c, at);
                if (d > R || !Combat.Los(at, c)) continue;
                var fw = f == Combat.Me ? f.Aim : f.Forward; var to = (at - c).normalized;
                bool facing = Vector3.Dot(fw, to) > -.2f || d < 3; float t = facing ? 1 : .6f; if (f != src) any = true;
                f.blindT = Mathf.Max(f.blindT, t); f.ads = false;
                if (f == Combat.Me) { f.flashHold = Mathf.Max(f.flashHold, t - .6f); f.flashA = 1; }
            }
            if (src == Combat.Me && any) Combat.Feedback(null, 0, false, false);
        }
    }

    // ---------------------------------------------------------------------- proximity mines
    public static class Mines
    {
        public class Mine { public Fighter f; public Vector3 p; public float arm = 1, t, vy, fl, ck; public Transform mesh, led; }
        public static readonly List<Mine> List = new List<Mine>();

        static float Floor(float x, float z, float y)
        {
            if (Combat.RayBoxes(new Vector3(x, y + .35f, z), Vector3.down, 60, out var h)) return y + .35f - h.t;
            return 0;
        }

        public static bool Lay(Fighter f)
        {
            var fw = f.Forward; float x = f.pos.x + fw.x * 1.1f, z = f.pos.z + fw.z * 1.1f; float y = Floor(x, z, f.pos.y + .25f);
            if (Mathf.Abs(y - f.pos.y) > .6f) { x = f.pos.x; z = f.pos.z; y = Floor(x, z, f.pos.y); }
            foreach (var b in BoxWorld.Boxes)
                if (x > b.x0 - .2f && x < b.x1 + .2f && z > b.z0 - .2f && z < b.z1 + .2f && b.y1 > y + .1f && b.y0 < y + .4f) { x = f.pos.x; z = f.pos.z; y = Floor(x, z, f.pos.y); break; }
            var m = new Mine { f = f, p = new Vector3(x, y, z) };
            var pj = GunsDb.Proj("mine");
            if (pj != null) { m.mesh = WeaponModels.Build(pj.nodes, null, "mine"); m.mesh.position = m.p; m.mesh.rotation = Quaternion.Euler(0, f.yaw * Mathf.Rad2Deg + 180, 0); m.led = WeaponModels.Find(m.mesh, "led"); }
            List.Add(m); Sfx.Play("wall", m.p, 1, 8, 1.6f); return true;
        }

        static void Boom(int i, Fighter by = null)
        {
            var m = List[i]; if (m.mesh) Object.Destroy(m.mesh.gameObject); List.RemoveAt(i);
            var g = GunsDb.Gun("mine");
            Combat.Explode(by && by.alive ? by : m.f, m.p + Vector3.up * .35f, null, new Combat.Blast { dmg = g.dmg, radius = g.radius, gun = "grenade", colors = new[] { "#ffd23f", "#ff7a1f", "#3a3d45", "#9aa0a8", "#ffffff" } });
        }

        public static void Update(float dt)
        {
            for (int i = List.Count - 1; i >= 0; i--)
            {
                if (i >= List.Count) continue;
                var m = List[i]; m.t += dt;
                // mines always sit on something: if what they sit on goes, they drop
                m.ck -= dt; if (m.ck <= 0 || m.vy != 0) { m.ck = .15f; m.fl = Floor(m.p.x, m.p.z, m.p.y); }
                if (m.p.y > m.fl + .02f || m.vy != 0) { m.vy -= 24 * dt; m.p.y = Mathf.Max(m.fl, m.p.y + m.vy * dt); if (m.p.y <= m.fl + .001f) { m.p.y = m.fl; m.vy = 0; } }
                if (m.mesh) m.mesh.position = m.p;
                if (m.arm > 0) { m.arm -= dt; if (m.led) m.led.gameObject.SetActive(Mathf.Sin(m.t * 30) > 0); continue; }
                if (m.led) m.led.gameObject.SetActive(m.t % 2 < .12f);
                if (!m.f) continue;
                foreach (var o in Combat.Fighters)
                    if (o && o.alive && Combat.IsFoe(m.f, o) && Mathf.Sqrt((o.pos.x - m.p.x) * (o.pos.x - m.p.x) + (o.pos.z - m.p.z) * (o.pos.z - m.p.z)) < 1.7f && Mathf.Abs(o.pos.y - m.p.y) < 1.1f)
                    { Sfx.Play("dry", m.p, 1, 10); Boom(i); break; }
            }
        }

        /// a blast sets off other mines within r
        public static void BlastNear(Vector3 at, float r)
        {
            for (int i = List.Count - 1; i >= 0; i--)
            {
                if (i >= List.Count) continue;
                var m = List[i]; float h = Mathf.Sqrt((m.p.x - at.x) * (m.p.x - at.x) + (m.p.z - at.z) * (m.p.z - at.z));
                if (Vector3.Distance(m.p, at) < r && h > .05f) Boom(i);
            }
        }

        /// shooting a mine sets it off (credited to the shooter)
        public static bool Ray(Vector3 o, Vector3 d, float maxT, out float t, out int idx)
        {
            t = 0; idx = -1;
            for (int i = 0; i < List.Count; i++)
            {
                var c = List[i].p + Vector3.up * .12f - o; float tt = Vector3.Dot(c, d);
                if (tt < 0 || tt > maxT || (idx >= 0 && tt >= t)) continue;
                if (c.sqrMagnitude - tt * tt < .16f) { t = tt; idx = i; }
            }
            return idx >= 0;
        }
        public static void Pop(int idx, Fighter by) { if (idx >= 0 && idx < List.Count) Boom(idx, by); }

        public static void Clear() { foreach (var m in List) if (m.mesh) Object.Destroy(m.mesh.gameObject); List.Clear(); }
    }

    // ---------------------------------------------------------------------- the Sentry
    public static class Turrets
    {
        public class Turret
        {
            public Fighter owner; public Vector3 pos; public float hp, age, fireT = .6f, scanT, yaw, pitch; public int shots; public bool dead; public Box box;
            public Fighter tgt; public Transform root, yawT, pitchT, lens; public Transform[] flashes = new Transform[2]; public Vector3[] muz = new Vector3[2]; public LineRenderer laser;
        }
        public static readonly List<Turret> List = new List<Turret>();
        static TurretCfg C => GunsDb.Data.turret.cfg;

        static float GroundAt(float x, float z, float y)
        {
            float best = 0; foreach (var b in BoxWorld.Boxes) if (b.x0 < x && b.x1 > x && b.z0 < z && b.z1 > z && b.y1 <= y + .5f && b.y1 > best) best = b.y1;
            return best;
        }

        public static bool Deploy(Fighter f)
        {
            var fw = f.Forward; Vector3? spot = null;
            foreach (var d in new[] { 1.8f, 1.3f, 2.4f })
            {
                float x = f.pos.x + fw.x * d, z = f.pos.z + fw.z * d, gy = GroundAt(x, z, f.pos.y + .6f);
                if (Mathf.Abs(gy - f.pos.y) > 1.2f) continue;
                bool blocked = false;
                foreach (var b in BoxWorld.Boxes) if (b.x0 < x + .45f && b.x1 > x - .45f && b.z0 < z + .45f && b.z1 > z - .45f && b.y1 > gy + .15f && b.y0 < gy + 1.9f) { blocked = true; break; }
                if (blocked) continue;
                foreach (var o in Combat.Fighters) if (o && o.alive && Mathf.Sqrt((o.pos.x - x) * (o.pos.x - x) + (o.pos.z - z) * (o.pos.z - z)) < .9f) { blocked = true; break; }
                foreach (var t in List) if (Mathf.Sqrt((t.pos.x - x) * (t.pos.x - x) + (t.pos.z - z) * (t.pos.z - z)) < 1.4f) { blocked = true; break; }
                if (blocked) continue;
                spot = new Vector3(x, gy, z); break;
            }
            if (!spot.HasValue) return false;
            var tu = new Turret { owner = f, pos = spot.Value, hp = C.hp, yaw = f.yaw };
            tu.root = WeaponModels.Build(GunsDb.Data.turret.nodes, null, "Sentry");
            tu.root.position = tu.pos; tu.root.localScale = Vector3.one * .2f;
            tu.yawT = WeaponModels.Find(tu.root, "yaw"); tu.pitchT = WeaponModels.Find(tu.root, "pitch"); tu.lens = WeaponModels.Find(tu.root, "lens");
            var mz = GunsDb.Data.turret.muz;
            for (int k = 0; k < 2; k++)
            {
                tu.muz[k] = new Vector3(mz[k * 3], mz[k * 3 + 1], mz[k * 3 + 2]);
                tu.flashes[k] = WeaponModels.MakeFlash(tu.pitchT ? tu.pitchT : tu.root, tu.muz[k] + new Vector3(0, 0, -.06f), .3f, -1, null);
            }
            tu.laser = new GameObject("laser").AddComponent<LineRenderer>(); tu.laser.transform.SetParent(tu.root, false);
            tu.laser.material = GunFx.MakeMat(true, null); tu.laser.widthMultiplier = .02f; tu.laser.startColor = tu.laser.endColor = new Color(1, .1f, .15f); tu.laser.enabled = false;
            tu.box = new Box { x0 = tu.pos.x - .4f, x1 = tu.pos.x + .4f, y0 = tu.pos.y, y1 = tu.pos.y + 1.85f, z0 = tu.pos.z - .4f, z1 = tu.pos.z + .4f };
            BoxWorld.Boxes.Add(tu.box);
            Sfx.Play("wall", tu.pos, 1, 14, 1.3f);
            List.Add(tu);
            GunFx.Emit(tu.pos + Vector3.up * .2f, GunFx.E(18, 3.5f, .18f, .5f, 12, "#2f7fe0", "#ffc61a", "#ffffff"));
            return true;
        }

        public static void Hit(Turret t, float dmg, Fighter src)
        {
            if (t == null || t.dead) return; if (src && !Combat.IsFoe(src, t.owner)) return;
            t.hp -= dmg; GunFx.Emit(t.pos + new Vector3(Random.Range(-.3f, .3f), 1.4f, Random.Range(-.3f, .3f)), GunFx.E(4, 3, .08f, .3f, 12, "#ffd23f", "#ffffff", "#9aa0a8"));
            if (src == Combat.Me && t.owner != Combat.Me) Combat.Feedback(null, 0, false, t.hp <= 0);
            if (t.hp <= 0) Destroy(t, true);
        }

        /// a bullet or bolt that stops on a box: if that box is a Sentry, it takes the damage
        public static void HitBox(int box, float dmg, Fighter src)
        {
            if (box < 0 || box >= BoxWorld.Boxes.Count || List.Count == 0) return; var b = BoxWorld.Boxes[box];
            foreach (var t in List) if (Same(t.box, b)) { Hit(t, dmg, src); return; }
        }

        static bool Same(Box a, Box b) => a.x0 == b.x0 && a.x1 == b.x1 && a.y0 == b.y0 && a.y1 == b.y1 && a.z0 == b.z0 && a.z1 == b.z1;
        public static bool IsTurretBox(int box) { if (box < 0 || box >= BoxWorld.Boxes.Count) return false; foreach (var t in List) if (Same(t.box, BoxWorld.Boxes[box])) return true; return false; }

        public static void Splash(Vector3 at, float R, float dmg, Fighter src)
        {
            foreach (var t in List.ToArray()) { float dd = Vector3.Distance(t.pos + Vector3.up * 1.2f, at); if (dd < R + .6f) Hit(t, Mathf.Max(20, dmg * 1.3f * (1 - dd / (R + .6f))), src); }
        }

        static void Destroy(Turret t, bool boom)
        {
            if (t.dead) return; t.dead = true; List.Remove(t);
            // park the box rather than remove it, so every other box keeps its index (the destruction code keys on them)
            for (int i = 0; i < BoxWorld.Boxes.Count; i++) if (Same(BoxWorld.Boxes[i], t.box)) { BoxWorld.Disable(i); break; }
            var p = t.pos + Vector3.up * 1.3f;
            if (boom) { GunFx.Emit(p, new EmitOpts { n = 40, speed = 8, up = 3, size = .25f, life = .7f, grav = 6, colors = new[] { Web.Hex("#ffc61a"), Web.Hex("#16171c"), Web.Hex("#ff7a1f"), Color.white } }); GunFx.Shake = Mathf.Max(GunFx.Shake, .1f); }
            if (t.root) Object.Destroy(t.root.gameObject);
        }

        public static void Clear() { foreach (var t in List.ToArray()) Destroy(t, false); List.Clear(); }

        public static void Update(float dt)
        {
            foreach (var t in List.ToArray())
            {
                t.age += dt; float s = t.root.localScale.x; if (s < 1) t.root.localScale = Vector3.one * Mathf.Min(1, s + dt * 3.5f);
                if (t.age > C.life) { Destroy(t, false); continue; }
                var eye = t.pos + Vector3.up * 1.55f;
                t.scanT -= dt;
                if (t.scanT <= 0)
                {
                    t.scanT = .15f; Fighter best = null; float bd = C.range;
                    foreach (var o in Combat.Fighters)
                    {
                        if (!o || !o.alive || o.downed || o.InFinisher || !Combat.IsFoe(t.owner, o)) continue;
                        float dd = Mathf.Sqrt((o.pos.x - t.pos.x) * (o.pos.x - t.pos.x) + (o.pos.z - t.pos.z) * (o.pos.z - t.pos.z)); if (dd > bd) continue;
                        var c = o.pos + Vector3.up * (1.15f * Fighter.SC);
                        if (Combat.RayBoxes(eye, (c - eye).normalized, Vector3.Distance(eye, c), out var hb) && !Same(BoxWorld.Boxes[hb.box], t.box)) continue;
                        best = o; bd = dd;
                    }
                    t.tgt = best;
                }
                var T = t.tgt; bool aimed = false;
                if (T)
                {
                    var c = T.pos + Vector3.up * (1.15f * Fighter.SC); float dx = c.x - t.pos.x, dz = c.z - t.pos.z;
                    float wantY = Mathf.Atan2(dx, dz), wantP = Mathf.Atan2(c.y - eye.y, Mathf.Sqrt(dx * dx + dz * dz));
                    float dy = Mathf.Atan2(Mathf.Sin(wantY - t.yaw), Mathf.Cos(wantY - t.yaw)), st = C.turn * dt;
                    t.yaw += Mathf.Clamp(dy, -st, st); t.pitch += Mathf.Clamp(wantP - t.pitch, -st, st);
                    aimed = Mathf.Abs(dy) < .1f && Mathf.Abs(wantP - t.pitch) < .12f;
                }
                else { t.yaw += Mathf.Sin(t.age * .9f) * dt * .6f; t.pitch += (0 - t.pitch) * Mathf.Min(1, dt * 3); }
                // pitch here is "up positive" in Unity terms (the web game stores it negated)
                if (t.yawT) t.yawT.localRotation = Quaternion.Euler(0, t.yaw * Mathf.Rad2Deg + 180, 0);
                if (t.pitchT) t.pitchT.localRotation = Quaternion.Euler(t.pitch * Mathf.Rad2Deg, 0, 0);
                if (T && t.lens) { t.laser.enabled = true; t.laser.useWorldSpace = true; t.laser.SetPosition(0, t.lens.position); t.laser.SetPosition(1, T.pos + Vector3.up * (1.15f * Fighter.SC)); }
                else t.laser.enabled = false;
                t.fireT -= dt; foreach (var fl in t.flashes) if (fl) fl.gameObject.SetActive(false);
                if (T && aimed && t.fireT <= 0)
                {
                    t.shots++; t.fireT = t.shots % C.burst == 0 ? C.pause : C.rate;
                    int bi = t.shots % 2; var mp = t.pitchT ? t.pitchT.TransformPoint(t.muz[bi]) : t.pos + Vector3.up * 1.5f;
                    if (t.flashes[bi]) { t.flashes[bi].gameObject.SetActive(true); t.flashes[bi].localScale = Vector3.one * Random.Range(.8f, 1.3f); }
                    Sfx.Play("smg", mp, .42f, 24, Random.Range(1.3f, 1.45f));
                    var c = T.pos + Vector3.up * (1.15f * Fighter.SC); var dir = (c - mp).normalized; float sp = C.spread * Mathf.Deg2Rad;
                    dir.x += Random.Range(-sp, sp); dir.y += Random.Range(-sp, sp) * .6f; dir.z += Random.Range(-sp, sp); dir.Normalize();
                    float dist = Vector3.Distance(mp, c) + 1; Vector3 end;
                    bool hb = Combat.RayBoxes(mp, dir, dist, out var bh);
                    if (Combat.RayFoes(mp, dir, t.owner, out var fh) && fh.t < dist && (!hb || fh.t < bh.t))
                    { end = mp + dir * fh.t; Combat.Damage(fh.who, C.dmg, t.owner, false, "turret"); GunFx.Emit(end, GunFx.E(4, 3, .08f, .5f, 12, "#ff4a3d", "#ffffff")); }
                    else { end = mp + dir * (hb ? bh.t : dist); if (hb) HitBox(bh.box, C.dmg, t.owner); GunFx.Emit(end, GunFx.E(3, 2.5f, .07f, .3f, 12, "#d8d4cc", "#ffffff")); }
                    GunFx.Tracer(mp, end, Web.Hex("#ffd27a"), .036f);
                }
            }
        }
    }
}
