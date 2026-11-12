// Shared combat rules ported from the web game: fighter hitboxes (rayFighter), shot spread (spreadDir), explosions,
// projectiles (rockets, Swarm missiles, crossbow bolts, ninja stars, grenades, mines) and the simple effects that go with them.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public struct FighterHit { public Fighter who; public float t; public bool head; }

    public static class Combat
    {
        static float Slab(Vector3 o, Vector3 d, float hx, float y0, float y1, float hz)
        {
            float t0 = 0, t1 = 1e9f;
            if (!Axis(o.x, d.x, -hx, hx, ref t0, ref t1)) return -1;
            if (!Axis(o.y, d.y, y0, y1, ref t0, ref t1)) return -1;
            if (!Axis(o.z, d.z, -hz, hz, ref t0, ref t1)) return -1;
            return t0;
        }
        static bool Axis(float o, float d, float mn, float mx, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-9f) return o >= mn && o <= mx;
            float a = (mn - o) / d, b = (mx - o) / d; if (a > b) { var q = a; a = b; b = q; }
            if (a > t0) t0 = a; if (b < t1) t1 = b; return t0 <= t1;
        }

        /// The web game's hitboxes: a head box over a body box, turned with the fighter, squashed when crouching.
        public static bool RayFighter(Vector3 o, Vector3 d, Fighter f, out float t, out bool head)
        {
            t = -1; head = false;
            if (!f || !f.alive) return false;
            float c = Mathf.Cos(f.yaw), s = Mathf.Sin(f.yaw);
            Vector3 w = o - f.pos;
            // into the fighter's frame: x across (right), z along the facing
            var lo = new Vector3(w.x * c - w.z * s, w.y, w.x * s + w.z * c);
            var ld = new Vector3(d.x * c - d.z * s, d.y, d.x * s + d.z * c);
            float cy = 1 - .27f * f.cv;
            float th = Slab(lo, ld, .38f, 2.0f * cy, 2.68f * cy, .36f), tb = Slab(lo, ld, 1.0f, 0, 2.02f * cy, .34f);
            if (th >= 0 && (tb < 0 || th <= tb + .05f)) { t = th; head = true; return true; }
            if (tb >= 0) { t = tb; return true; }
            return false;
        }

        public static bool IsFoe(Fighter a, Fighter b) => a && b && a != b && a.team != b.team;

        /// Nearest enemy of <from> along the ray.
        public static bool RayFoes(Vector3 o, Vector3 d, Fighter from, out FighterHit hit, float maxT = 1e9f)
        {
            hit = default; bool any = false;
            foreach (var f in Fighter.All)
            {
                if (!IsFoe(from, f)) continue;
                if (RayFighter(o, d, f, out float t, out bool head) && t <= maxT && (!any || t < hit.t)) { hit = new FighterHit { who = f, t = t, head = head }; any = true; }
            }
            return any;
        }

        public static Vector3 SpreadDir(Vector3 d, float deg)
        {
            if (deg <= 0) return d;
            float a = deg * Mathf.Deg2Rad * Mathf.Sqrt(Random.value), t = Random.value * Mathf.PI * 2;
            var up = Mathf.Abs(d.y) > .95f ? Vector3.right : Vector3.up;
            var u = Vector3.Cross(d, up).normalized; var v = Vector3.Cross(d, u);
            return (d * Mathf.Cos(a) + u * (Mathf.Sin(a) * Mathf.Cos(t)) + v * (Mathf.Sin(a) * Mathf.Sin(t))).normalized;
        }

        public static Vector3 Eye(Fighter f) => new Vector3(f.pos.x, f.pos.y + 2.35f * (1 - .27f * f.cv), f.pos.z);
        public static Vector3 Chest(Fighter f) => new Vector3(f.pos.x, f.pos.y + 1.1f * Fighter.SC, f.pos.z);

        /// Hurt someone and report it to the HUD when it was you.
        public static bool Hurt(Fighter v, float dmg, Fighter from, bool head, bool showNumber = true)
        {
            if (!v || !v.alive) return false;
            float before = v.hp;
            bool down = v.TakeDamage(dmg, head, from);
            if (from && from.team == 0 && v != from && showNumber) { Hud.DamageNumber(Chest(v) + Vector3.up * .9f, Mathf.RoundToInt(dmg), head); Hud.HitMarker(head || down); }
            return down;
        }

        /// The web game's explode(): full damage within .6 m, falling off to the edge (never under 20% or 18), half to yourself,
        /// walls block it beyond 1.2 m, and everyone caught is knocked back.
        public static void Explode(Fighter src, Vector3 at, float dmg, float radius, Color col, Fighter skip = null)
        {
            Fx.Blast(at, radius, col);
            foreach (var o in Fighter.All.ToArray())
            {
                if (!o.alive || o == skip || (o != src && !IsFoe(src, o))) continue;
                var c = Chest(o); float dd = Vector3.Distance(c, at);
                if (dd > radius + .6f) continue;
                if (!BoxWorld.Los(at, c) && dd > 1.2f) continue;
                float dm = Mathf.Round(Mathf.Max(Mathf.Min(18, dmg * .2f), dmg * (1 - Mathf.Max(0, dd - .6f) / radius)));
                if (o == src) dm = Mathf.Round(dm * .5f);
                var kb = c - at; kb.y = 0; kb = kb.sqrMagnitude > 1e-6f ? kb.normalized : Vector3.zero;
                if (o == src) { o.vel += kb * 3; }
                else { o.vel += kb * 9; o.vel.y = Mathf.Max(o.vel.y, 7); o.grounded = false; }
                Hurt(o, dm, src, false);
            }
        }
    }

    /// Projectiles and mines in flight, and the short-lived effects (tracers, impacts, blasts, fire).
    public class CombatWorld : MonoBehaviour
    {
        static CombatWorld inst;
        public static CombatWorld I { get { if (!inst) inst = new GameObject("Combat").AddComponent<CombatWorld>(); return inst; } }
        public static Armory armory;

        class Proj { public Fighter f; public string id; public WeaponInfo g; public Vector3 pos, vel; public float life, age; public bool armed, prox; public Fighter lockOn; public Vector3? tgt; public Transform mesh; }
        class Mine { public Fighter f; public Vector3 pos; public float age; public Transform mesh; }
        readonly List<Proj> projs = new List<Proj>();
        readonly List<Mine> mines = new List<Mine>();
        static readonly HashSet<string> Thrown = new HashSet<string> { "grenade", "stun", "flash", "cluster", "bomblet" };

        public void Spawn(Fighter f, string id, Vector3 from, Vector3 dir, Vector3? tgt = null, Fighter lockOn = null)
        {
            var g = armory ? armory.Weapon(id) : null; if (g == null) return;
            if (id == "mine") { LayMine(f, from); return; }
            var p = new Proj { f = f, id = id, g = g, pos = from, vel = dir * g.speed, life = g.landFuse > 0 ? 8 : (g.fuse > 0 ? g.fuse : 4), armed = g.landFuse <= 0, tgt = tgt, lockOn = lockOn };
            if (id == "swarm") p.life = 3;
            p.mesh = MakeMesh(id, g);
            p.mesh.position = from; if (dir.sqrMagnitude > 0) p.mesh.rotation = Quaternion.LookRotation(dir);
            projs.Add(p);
        }

        Transform MakeMesh(string id, WeaponInfo g)
        {
            Transform t;
            if (Thrown.Contains(id) || id == "stars")
            {
                t = armory.MakeWeapon(id == "bomblet" ? "bomblet" : id, null).transform;
                foreach (var r in t.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            else
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule); Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = Fx.Mat(id == "crossbow" ? new Color(.12f, .84f, .33f) : id == "swarm" ? new Color(.95f, .95f, .97f) : new Color(.2f, .22f, .25f));
                t = new GameObject("proj " + id).transform; go.transform.SetParent(t, false);
                float len = id == "rocket" ? .7f : id == "swarm" ? .26f : .6f, w = id == "rocket" ? .16f : id == "swarm" ? .06f : .035f;
                go.transform.localRotation = Quaternion.Euler(90, 0, 0); go.transform.localScale = new Vector3(w, len / 2, w);
                if (id == "rocket" || id == "swarm") Fx.Trail(t, id == "rocket" ? .5f : .25f);
            }
            return t;
        }

        void LayMine(Fighter f, Vector3 at)
        {
            at.y = BoxWorld.FloorUnder(at + Vector3.up * .5f);
            var m = new Mine { f = f, pos = at };
            m.mesh = armory.MakeWeapon("mine", null).transform; m.mesh.position = at + Vector3.up * .03f;
            mines.Add(m);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = projs.Count - 1; i >= 0; i--) if (StepProj(projs[i], dt)) { if (projs[i].mesh) Destroy(projs[i].mesh.gameObject); projs.RemoveAt(i); }
            for (int i = mines.Count - 1; i >= 0; i--)
            {
                var m = mines[i]; m.age += dt;
                bool boom = false;
                if (m.age > 1)
                    foreach (var o in Fighter.All) if (Combat.IsFoe(m.f, o) && o.alive && Vector2.Distance(new Vector2(o.pos.x, o.pos.z), new Vector2(m.pos.x, m.pos.z)) < 1.7f && Mathf.Abs(o.pos.y - m.pos.y) < 1.5f) { boom = true; break; }
                if (!boom) continue;
                var g = armory.Weapon("mine");
                Combat.Explode(m.f, m.pos + Vector3.up * .3f, g.dmg, g.radius, new Color(1, .45f, .2f));
                if (m.mesh) Destroy(m.mesh.gameObject); mines.RemoveAt(i);
            }
        }

        /// One step of the web game's updProjs. Returns true when the projectile is gone.
        bool StepProj(Proj p, float dt)
        {
            var g = p.g; p.life -= dt; p.age += dt;
            if (p.id == "swarm" && p.age > .07f)
            {
                Vector3? L = p.lockOn && p.lockOn.alive ? Combat.Chest(p.lockOn) : p.tgt;
                if (L.HasValue)
                {
                    float sp = p.vel.magnitude; var want = L.Value - p.pos;
                    if (want.sqrMagnitude > .01f) { want = want.normalized * sp; p.vel = Vector3.Lerp(p.vel, want, 1 - Mathf.Exp(-dt * (p.age < .22f ? 7 : 15))).normalized * sp; }
                    if (p.lockOn && Vector3.Distance(p.pos, L.Value) < .95f) p.prox = true;
                }
            }
            if (g.accel > 0) { float sp = p.vel.magnitude; if (sp < g.maxSpeed && sp > 0) p.vel *= Mathf.Min(g.maxSpeed, sp + g.accel * dt) / sp; }
            p.vel.y -= g.grav * dt;
            var st = p.vel * dt; float Ls = st.magnitude;
            if (Ls < 1e-6f) { if (p.life <= 0 && Thrown.Contains(p.id)) { Detonate(p, p.pos, null, Vector3.up); return true; } return false; }
            var d = st / Ls;
            float hb = BoxWorld.RayHit(p.pos, d, Ls, out var n);
            bool hf = Combat.RayFoes(p.pos, d, p.f, out var fh, Ls);
            bool thrown = Thrown.Contains(p.id);
            if (thrown && p.mesh) p.mesh.Rotate(dt * 460, 0, 0, Space.Self);

            // rockets also go off when they pass close by someone
            if ((p.id == "rocket" || p.id == "swarm") && !hf)
                foreach (var o in Fighter.All)
                {
                    if (!Combat.IsFoe(p.f, o) || !o.alive) continue;
                    for (int k = 1; k <= 5; k++) { var q = p.pos + st * (k / 5f); if (new Vector2(q.x - o.pos.x, q.z - o.pos.z).magnitude < .55f && q.y > o.pos.y + .1f && q.y < o.pos.y + 2.7f) { hf = true; fh = new FighterHit { who = o, t = Ls * k / 5f }; break; } }
                    if (hf) break;
                }

            float hitT = -1; Fighter who = null; bool head = false; bool ground = false;
            if (p.prox) { hitT = 0; ground = true; }
            else if (hf && (hb < 0 || fh.t < hb)) { hitT = fh.t; who = fh.who; head = fh.head; }
            else if (hb >= 0) hitT = hb;
            else if (p.pos.y + st.y < 0) { hitT = Ls * p.pos.y / Mathf.Max(1e-6f, -st.y); ground = true; n = Vector3.up; }

            if (thrown && p.life > 0 && hitT >= 0 && (who == null || p.id == "bomblet"))
            {
                // bounce: grenades skip off walls and floors and arm on the first landing
                if (who != null) n = Vector3.up;
                var at = p.pos + d * hitT + n * .06f;
                if (!p.armed && n.y > .5f) { p.armed = true; p.life = g.fuse; }
                p.vel = (p.vel - 2 * Vector3.Dot(p.vel, n) * n) * .45f;
                if (n.y > .5f && Mathf.Abs(p.vel.y) < 1.5f) { p.vel.y = 0; p.vel.x *= .8f; p.vel.z *= .8f; }
                p.pos = at; if (p.mesh) p.mesh.position = at; return false;
            }
            if (thrown && p.life <= 0) { Detonate(p, p.pos, null, Vector3.up); return true; }
            if (hitT >= 0 || p.life <= 0)
            {
                var at = hitT >= 0 ? p.pos + d * hitT : p.pos;
                Detonate(p, at, who, n, head);
                return true;
            }
            p.pos += st; if (p.mesh) { p.mesh.position = p.pos; if (!thrown && p.vel.sqrMagnitude > 0) p.mesh.rotation = Quaternion.LookRotation(p.vel); }
            return false;
        }

        void Detonate(Proj p, Vector3 at, Fighter who, Vector3 n, bool head = false)
        {
            var g = p.g;
            switch (p.id)
            {
                case "rocket":
                    if (who) { var kb = p.vel; kb.y = 0; kb.Normalize(); who.vel += kb * 10; who.vel.y = Mathf.Max(who.vel.y, 6); who.grounded = false; Combat.Hurt(who, g.direct, p.f, false); }
                    Combat.Explode(p.f, at + n * .2f, g.dmg, g.radius, new Color(1, .5f, .15f), who); break;
                case "swarm": Combat.Explode(p.f, at + n * .2f, g.dmg, g.radius, new Color(1, .55f, .2f)); break;
                case "grenade": Combat.Explode(p.f, at + Vector3.up * .2f, g.dmg, g.radius, new Color(1, .5f, .15f)); break;
                case "cluster":
                    at += Vector3.up * .2f; Combat.Explode(p.f, at, g.dmg, g.radius, new Color(1, .55f, .2f));
                    float bs = Mathf.Max(1, armory.Weapon("bomblet").speed);   // Spawn multiplies by the bomblet's speed
                    for (int k = 0; k < 5; k++) { float a = k / 5f * Mathf.PI * 2 + Random.Range(-.3f, .3f), sp = Random.Range(4f, 7f); Spawn(p.f, "bomblet", at, new Vector3(Mathf.Sin(a) * sp, 6, Mathf.Cos(a) * sp) / bs); }
                    break;
                case "bomblet": Combat.Explode(p.f, at + Vector3.up * .15f, g.dmg, g.radius, new Color(1, .6f, .25f)); break;
                case "stun": Fx.Blast(at + Vector3.up * .3f, g.radius * .5f, new Color(.25f, .5f, 1f)); StunBlast(p.f, at + Vector3.up * .3f, g.radius); break;
                case "flash": Fx.Blast(at + Vector3.up * .3f, 1.2f, Color.white); FlashBang(p.f, at + Vector3.up * .3f, g.radius); break;
                default:   // crossbow bolts and ninja stars: a straight hit, double on the head
                    if (who) Combat.Hurt(who, g.dmg * (head ? g.head : 1), p.f, head);
                    else Fx.Impact(at, n, new Color(.85f, .83f, .8f));
                    break;
            }
        }

        static void StunBlast(Fighter src, Vector3 at, float radius)
        {
            foreach (var o in Fighter.All)
                if (o.alive && (o == src || Combat.IsFoe(src, o)) && Vector3.Distance(Combat.Chest(o), at) < radius && BoxWorld.Los(at, Combat.Chest(o)))
                    o.stunT = o == src ? 1.5f : 3f;
        }

        static void FlashBang(Fighter src, Vector3 at, float radius)
        {
            foreach (var o in Fighter.All)
            {
                if (!o.alive || (o != src && !Combat.IsFoe(src, o))) continue;
                var c = Combat.Eye(o); float d = Vector3.Distance(c, at);
                if (d > radius || !BoxWorld.Los(at, c)) continue;
                bool facing = Vector3.Dot(o.Aim, (at - c).normalized) > -.2f || d < 3;
                o.ads = false;
                if (o.team == 0) Hud.Flash(facing ? 1f : .6f);
            }
        }
    }

    /// Small, cheap effects: unlit materials by colour, tracers, impact chips, blast balls, flame puffs.
    public static class Fx
    {
        static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
        static Shader unlit;
        public static Material Mat(Color c)
        {
            if (mats.TryGetValue(c, out var m) && m) return m;
            if (!unlit) unlit = Shader.Find("Universal Render Pipeline/Unlit");
            m = new Material(unlit); m.SetColor("_BaseColor", c); mats[c] = m; return m;
        }

        class Live { public Transform t; public float life, max; public System.Action<Live, float> tick; public LineRenderer lr; public Light light; }
        static readonly List<Live> live = new List<Live>();
        static FxRunner runner;
        static void Add(Live l) { if (!runner) runner = new GameObject("Fx").AddComponent<FxRunner>(); live.Add(l); }

        internal static void Tick(float dt)
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var l = live[i]; l.life -= dt;
                if (l.life <= 0 || !l.t) { if (l.t) Object.Destroy(l.t.gameObject); live.RemoveAt(i); continue; }
                l.tick?.Invoke(l, 1 - l.life / l.max);
            }
        }

        public static void Tracer(Vector3 a, Vector3 b, Color c, float width = .045f)
        {
            var go = new GameObject("tracer"); var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Mat(c); lr.positionCount = 2; lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.startWidth = width * .5f; lr.endWidth = width; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Add(new Live { t = go.transform, life = .06f, max = .06f, lr = lr, tick = (l, u) => { l.lr.SetPosition(0, Vector3.Lerp(a, b, u)); } });
        }

        public static void Impact(Vector3 at, Vector3 n, Color c)
        {
            for (int i = 0; i < 3; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = Mat(i == 0 ? Color.white : c);
                go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.transform.position = at + n * .05f; go.transform.localScale = Vector3.one * .07f;
                var v = (n * 2 + Random.insideUnitSphere * 1.6f + Vector3.up) * 1.5f;
                Add(new Live { t = go.transform, life = .4f, max = .4f, tick = (l, u) => { v.y -= 9 * Time.deltaTime; l.t.position += v * Time.deltaTime; l.t.localScale = Vector3.one * .07f * (1 - u); } });
            }
        }

        public static void Blast(Vector3 at, float radius, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Mat(c); go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.position = at; go.transform.localScale = Vector3.zero;
            var lg = new GameObject("blast light").AddComponent<Light>(); lg.transform.SetParent(go.transform, false);
            lg.type = LightType.Point; lg.color = c; lg.range = radius * 4; lg.intensity = 8;
            float R = radius * 1.1f;
            Add(new Live { t = go.transform, life = .45f, max = .45f, light = lg, tick = (l, u) => { float k = u < .3f ? u / .3f : 1 - (u - .3f) / .7f; l.t.localScale = Vector3.one * R * 2 * Mathf.Max(.05f, k); l.light.intensity = 8 * (1 - u); } });
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.Destroy(ring.GetComponent<Collider>());
            ring.GetComponent<Renderer>().sharedMaterial = Mat(new Color(.92f, .9f, .85f)); ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.transform.position = new Vector3(at.x, BoxWorld.FloorUnder(at) + .03f, at.z);
            Add(new Live { t = ring.transform, life = .35f, max = .35f, tick = (l, u) => { l.t.localScale = new Vector3(R * 2.4f * u, .01f, R * 2.4f * u); } });
        }

        public static void FlamePuff(Vector3 from, Vector3 dir, float stop)
        {
            for (int i = 0; i < 2; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.Destroy(go.GetComponent<Collider>());
                var r = go.GetComponent<Renderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.sharedMaterial = Mat(Random.value < .5f ? new Color(1, .55f, .1f) : new Color(1, .8f, .25f));
                go.transform.position = from;
                var v = (dir + Random.insideUnitSphere * .08f).normalized * (stop / .35f);
                Add(new Live { t = go.transform, life = .35f, max = .35f, tick = (l, u) => { l.t.position += v * Time.deltaTime; v.y += 3 * Time.deltaTime; l.t.localScale = Vector3.one * Mathf.Lerp(.15f, .9f, u) * (1 - u * .6f); } });
            }
        }

        public static void Trail(Transform parent, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Mat(new Color(1, .7f, .25f)); go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetParent(parent, false); go.transform.localPosition = new Vector3(0, 0, -size * .7f); go.transform.localScale = Vector3.one * size * .45f;
        }
    }

    public class FxRunner : MonoBehaviour { void Update() { Fx.Tick(Time.deltaTime); } }
}
