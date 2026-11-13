// Everything in Street Life that breaks, falls over or moves, ported from the web game:
//  - glass panes shatter (one bullet), furniture, bins and crates take damage, get shoved when shot or blasted, and break apart;
//    anything stacked on top comes down with them
//  - short furniture chips down (to 60% of its height) under gunfire but stays as cover
//  - fuel drums explode (and set each other off); cars blow up on the 3rd shot or any blast and stay a burnt-out shell
//  - trees topple away from the shot, crash down and sink away, leaving a stump; play frames tip over and sink
//  - loose items get knocked over by bullets, blasts and people walking into them; breakable ones smash
//  - cars drive up and down the road (stopping for people), the windmill turns, the balloon and clouds drift
// Everything comes back at the start of the next round.
// Weapons call Destruction.Hit (a bullet or sword hit on a box) and Destruction.Blast (grenades, rockets).
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class Destruction : MonoBehaviour
    {
        public static Destruction I;
        public const float TREE_HP = 320, PLAY_HP = 420;

        public class Ent
        {
            public WDyn d; public Transform t; public int[] boxes;
            public float hp; public bool alive = true;
            public Vector3 homePos; public Quaternion homeRot; public Vector3 homeScale;
            public Box[] homeBoxes;
            // falling things (trees, play frames)
            public int st; public float stT; public Vector3 axis; public GameObject stump;
            // chips
            public float chipHP, h0;
            // knocks
            public int kst; public Vector3 kv, kav; public float kT, bhp;
            // cars
            public string carState = "park"; public float wait, v, vmax, target, z, homeZ, blocked, hits, smokeT, burnT; public bool burnt;
            public List<Renderer> rends = new List<Renderer>();
        }

        public readonly List<Ent> ents = new List<Ent>();
        readonly Dictionary<int, Ent> byBox = new Dictionary<int, Ent>();
        readonly List<Ent> knocks = new List<Ent>(), cars = new List<Ent>(), anims = new List<Ent>();
        float period = 209.44f, animT;
        MaterialPropertyBlock mpb;
        Fighter[] fighters = new Fighter[0];

        public static void Init(Transform root, DynJson dj, List<Transform> made)
        {
            if (!I) { I = new GameObject("Destruction").AddComponent<Destruction>(); }
            I.Setup(dj, made);
            TownLife.Ensure();
        }

        void Awake() { mpb = new MaterialPropertyBlock(); }
        void OnEnable() { RoundManager.RoundStarted += OnRound; }
        void OnDisable() { RoundManager.RoundStarted -= OnRound; if (I == this) I = null; }
        void OnRound(int r) => ResetAll();

        void Setup(DynJson dj, List<Transform> made)
        {
            ents.Clear(); byBox.Clear(); knocks.Clear(); cars.Clear(); anims.Clear(); NavGrid.Dynamic.Clear();
            period = dj.period > 0 ? dj.period : 209.44f;
            for (int i = 0; i < dj.entities.Length; i++)
            {
                var d = dj.entities[i]; var t = made[i]; if (!t) continue;
                var e = new Ent { d = d, t = t, boxes = d.boxes ?? new int[0], homePos = t.localPosition, homeRot = t.localRotation, homeScale = t.localScale };
                e.homeBoxes = new Box[e.boxes.Length];
                for (int k = 0; k < e.boxes.Length; k++) { if (e.boxes[k] < BoxWorld.Boxes.Count) e.homeBoxes[k] = BoxWorld.Boxes[e.boxes[k]]; byBox[e.boxes[k]] = e; NavGrid.Dynamic.Add(e.boxes[k]); }
                e.rends.AddRange(t.GetComponentsInChildren<Renderer>(true));
                e.hp = d.kind == "tree" ? TREE_HP : d.kind == "play" ? PLAY_HP : d.hp > 0 ? d.hp : 60;
                if (d.kind == "chip") { e.chipHP = e.hp; e.h0 = e.boxes.Length > 0 ? e.homeBoxes[0].y1 - e.homeBoxes[0].y0 : 1; }
                if (d.kind == "knock") { e.bhp = d.brk; knocks.Add(e); }
                if (d.kind == "car") { e.z = e.homeZ = t.position.z; e.wait = 2 + Random.value * 10; cars.Add(e); }
                if (d.kind == "anim") anims.Add(e);
                ents.Add(e);
            }
        }

        public void ResetAll()
        {
            foreach (var e in ents)
            {
                e.alive = true; e.st = 0; e.kst = 0; e.burnt = false; e.hits = 0; e.v = 0; e.carState = "park"; e.wait = 6 + Random.value * 10;
                e.t.gameObject.SetActive(true);
                if (e.d.kind != "anim") { e.t.localPosition = e.homePos; e.t.localRotation = e.homeRot; e.t.localScale = e.homeScale; }
                for (int k = 0; k < e.boxes.Length; k++) { BoxWorld.Restore(e.boxes[k]); BoxWorld.Set(e.boxes[k], e.homeBoxes[k]); }
                e.hp = e.d.kind == "tree" ? TREE_HP : e.d.kind == "play" ? PLAY_HP : e.d.hp > 0 ? e.d.hp : 60;
                e.chipHP = e.hp; e.bhp = e.d.brk; e.z = e.homeZ;
                if (e.stump) e.stump.SetActive(false);
                foreach (var r in e.rends) if (r) r.SetPropertyBlock(null);
            }
        }

        // ------------------------------------------------------------------ public hooks for weapons
        /// A bullet / sword hit on box `boxIndex` (from BoxWorld.Ray). Returns true if something here took the hit.
        /// Something broke: kind is glass, prop, drum, car, tree or play; by is who did it (null if unknown).
        /// For stats and achievements (progression chat).
        public static event System.Action<string, Fighter> Broken;
        static Fighter curBy;
        static void Report(string kind) { try { Broken?.Invoke(kind, curBy); } catch (System.Exception ex) { Debug.LogException(ex); } }

        public static bool Hit(int boxIndex, float dmg, Vector3 at, Vector3 dir, Fighter by = null)
        {
            if (Walls.Hit(boxIndex, dmg, dir)) return true;   // a brick in a built wall
            if (!I || !I.byBox.TryGetValue(boxIndex, out var e) || !e.alive) return false;
            curBy = by; try { I.Damage(e, dmg, at, dir); } finally { curBy = null; }
            return true;
        }

        /// A blast (grenade, rocket, drum, car): hurts fighters in range with line of sight, wrecks cars, breaks and shoves props,
        /// fells trees and play frames, knocks loose items flying. maxDamage 0 = no damage to fighters (just the world).
        public static void Blast(Vector3 at, float radius, Fighter by = null, float maxDamage = 0)
        {
            Walls.Splash(at, radius, 120);   // built walls (the web game's splashBricks)
            if (!I) return;
            var prevBy = curBy; curBy = by ?? curBy;
            try { I.DoBlast(at, radius, by, maxDamage); } finally { curBy = prevBy; }
        }

        /// Bullets passing close to loose items knock them about (and wear down breakable ones).
        public static void KnockRay(Vector3 o, Vector3 d, float dist, float dmg)
        {
            if (!I) return;
            foreach (var e in I.knocks)
            {
                if (!e.alive) continue;
                var p = e.t.position; float t = Vector3.Dot(p - o, d);
                float r = Mathf.Max(.25f, Mathf.Max(e.d.kw, Mathf.Max(e.d.kh, e.d.kd)) * .6f);
                if (t < 0 || t > dist + r) continue;
                if ((o + d * t - p).sqrMagnitude < r * r) { I.KnockIt(e, d, Random.Range(3f, 6f)); if (dmg > 0) I.KnockHurt(e, dmg); }
            }
        }

        // ------------------------------------------------------------------ damage
        void Damage(Ent e, float dmg, Vector3 at, Vector3 dir)
        {
            switch (e.d.kind)
            {
                case "car": HitCar(e, at); return;
                case "chip": Chip(e, dmg, at); return;
                case "tree": case "play":
                    e.hp -= dmg; Bits(at, e.d.kind == "tree" ? new[] { C("#7a4c2a"), C("#c99a5b"), C("#e9d3a8") } : new[] { C("#7fd0d8"), C("#8a8f99") }, 3);
                    if (e.hp <= 0) Fell(e, at - dir);
                    return;
                default:
                    if (e.d.shove && dir != Vector3.zero) Shove(e, dir, Mathf.Min(.4f, dmg * .015f / Mathf.Max(1, Area(e))));
                    e.hp -= dmg;
                    if (e.hp <= 0) Break(e, at - dir * .5f); else Bits(at, new[] { BaseColor(e), C("#d8dbe2"), C("#55585f") }, 3);
                    return;
            }
        }

        static Color C(string h) => Hex.ToColor(h);
        Color BaseColor(Ent e) => !string.IsNullOrEmpty(e.d.color) ? C(e.d.color) : e.rends.Count > 0 && e.rends[0] && e.rends[0].sharedMaterial && e.rends[0].sharedMaterial.HasProperty("_BaseColor") ? e.rends[0].sharedMaterial.GetColor("_BaseColor") : Color.grey;
        float Area(Ent e) { if (e.boxes.Length == 0) return 1; var b = BoxWorld.Boxes[e.boxes[0]]; return (b.x1 - b.x0) * (b.z1 - b.z0); }
        void Bits(Vector3 at, Color[] cols, int n) => BreakFx.I.Chunks(at, Vector3.one * .3f, Vector3.zero, cols, n, at.y - 3, .5f);

        Vector3 Center(Ent e)
        {
            if (e.boxes.Length > 0 && !BoxWorld.IsDisabled(e.boxes[0])) { var b = BoxWorld.Boxes[e.boxes[0]]; return new Vector3((b.x0 + b.x1) / 2, (b.y0 + b.y1) / 2, (b.z0 + b.z1) / 2); }
            return e.t.position;
        }

        void Break(Ent e, Vector3 at)
        {
            if (!e.alive) return;
            var c = Center(e);
            Box b = e.boxes.Length > 0 ? BoxWorld.Boxes[e.boxes[0]] : new Box { x0 = c.x - .5f, x1 = c.x + .5f, y0 = c.y - .5f, y1 = c.y + .5f, z0 = c.z - .5f, z1 = c.z + .5f };
            e.alive = false;
            foreach (var i in e.boxes) BoxWorld.Disable(i);
            e.t.gameObject.SetActive(false);
            bool glass = e.d.kind == "glass" || e.d.prop == "glass";
            Report(glass ? "glass" : e.d.boom ? "drum" : "prop");
            string snd = !string.IsNullOrEmpty(e.d.snd) ? e.d.snd : e.d.prop == "bush" ? "bush" : e.d.prop == "bin" ? "bin" : "crate";
            Sfx.Play(snd, c, glass ? 1.35f : 1f, glass ? 22 : 18);
            var away = new Vector3(c.x - at.x, 0, c.z - at.z); if (away.sqrMagnitude < 1e-4f) away = Random.insideUnitSphere; away.y = 0; away.Normalize();
            var size = new Vector3(b.x1 - b.x0, b.y1 - b.y0, b.z1 - b.z0);
            if (glass)
                BreakFx.I.Chunks(c, size, away, new[] { new Color(.88f, .96f, 1f), new Color(.75f, .9f, 1f), Color.white }, Mathf.Clamp(Mathf.RoundToInt(size.x * size.y * 12), 24, 60), b.y0, 4f, true);
            else
            {
                var col = BaseColor(e);
                BreakFx.I.Chunks(c, size, away, e.d.wood ? new[] { col, C("#b9895a"), C("#8a5a34") } : new[] { col, C("#55585f"), C("#16171c") }, 10, b.y0);
                BreakFx.I.Puffs(c, 8, .4f, .8f, .35f, 1.1f, new Color(.85f, .86f, .88f, .5f));
                Sfx.Play("brick", c, .8f, 16);
            }
            // anything stacked on top comes down with it; loose items on it get knocked off
            foreach (var o in ents)
            {
                if (!o.alive || o == e || o.boxes.Length == 0 || o.d.kind == "car" || o.d.kind == "tree" || o.d.kind == "play") continue;
                var ob = BoxWorld.Boxes[o.boxes[0]];
                if (Mathf.Abs(ob.y0 - b.y1) < .05f && ob.x0 < b.x1 && ob.x1 > b.x0 && ob.z0 < b.z1 && ob.z1 > b.z0) Break(o, at);
            }
            KnockOn(b);
            if (e.d.boom) DrumBoom(e, c);
        }

        void DrumBoom(Ent e, Vector3 c)
        {
            var at = new Vector3(c.x, c.y - (c.y - (e.boxes.Length > 0 ? e.homeBoxes[0].y0 : c.y)) + .6f, c.z);
            DoBlast(at, 4.6f, null, 70);
        }

        void DoBlast(Vector3 at, float R, Fighter by, float maxDamage)
        {
            BreakFx.I.Boom(at, R);
            Sfx.Play("explode", at, 1f, 26);
            if (maxDamage > 0)
            {
                if (fighters.Length == 0 || Time.frameCount % 60 == 0) fighters = FindObjectsByType<Fighter>();
                foreach (var f in fighters)
                {
                    if (!f || !f.alive) continue;
                    var cpos = new Vector3(f.pos.x, f.pos.y + 1.1f * Fighter.SC, f.pos.z);
                    float dd = Vector3.Distance(cpos, at);
                    if (dd > R + .6f || (!NavGrid.Los(at, cpos) && dd > 1.2f)) continue;
                    float dm = Mathf.Round(Mathf.Max(15, maxDamage * (1 - Mathf.Max(0, dd - .6f) / R)));
                    var kb = cpos - at; kb.y = 0; kb.Normalize();
                    f.vel.x += kb.x * 9; f.vel.z += kb.z * 9; f.vel.y = Mathf.Max(f.vel.y, 7); f.grounded = false;
                    f.Damage(dm, by != null && by != f && by.team != f.team ? by : null);
                }
            }
            // the world: cars wreck, props break or get shoved, chips lose height, trees and frames fall, loose items fly
            foreach (var e in ents.ToArray())
            {
                if (!e.alive || e.d.kind == "anim") continue;
                if (e.d.kind == "knock") { var p = e.t.position; float d = Vector3.Distance(p, at); if (d < R * 1.3f) { KnockIt(e, p - at, Mathf.Lerp(9, 3, d / (R * 1.3f))); if (d < R * .6f) KnockHurt(e, 999); } continue; }
                if (e.boxes.Length == 0) continue;
                var b = BoxWorld.Boxes[e.boxes[0]];
                float px = Mathf.Clamp(at.x, b.x0, b.x1), py = Mathf.Clamp(at.y, b.y0, b.y1), pz = Mathf.Clamp(at.z, b.z0, b.z1);
                float dd = Vector3.Distance(new Vector3(px, py, pz), at);
                switch (e.d.kind)
                {
                    case "car": if (dd <= Mathf.Max(1.5f, R * .55f)) Wreck(e); break;
                    case "chip": if (dd < R) Chip(e, 90 * (1 - dd / R) + 20, new Vector3(px, py, pz)); break;
                    case "tree": case "play": if (dd <= Mathf.Max(1.2f, R * .6f)) Fell(e, at); break;
                    default:
                        if (dd <= Mathf.Max(1.2f, R * .6f)) Break(e, at);
                        else if (e.d.shove && dd < R * 1.5f) Shove(e, Center(e) - at, 1.6f * (1 - dd / (R * 1.5f)) / Mathf.Sqrt(Mathf.Max(1, Area(e))));
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ shove, chip
        void Shove(Ent e, Vector3 dir, float dist)
        {
            if (!e.alive || dist < .01f || e.boxes.Length == 0) return;
            dir.y = 0; if (dir.sqrMagnitude < 1e-6f) return; dir.Normalize();
            float dx = dir.x * dist, dz = dir.z * dist; int bi = e.boxes[0]; var b = BoxWorld.Boxes[bi];
            bool Free(float x0, float x1, float z0, float z1)
            {
                for (int i = 0; i < BoxWorld.Boxes.Count; i++)
                {
                    if (i == bi) continue; var o = BoxWorld.Boxes[i];
                    if (o.y1 <= b.y0 + .05f || o.y0 >= b.y1 - .05f) continue;
                    if (x1 > o.x0 && x0 < o.x1 && z1 > o.z0 && z0 < o.z1) return false;
                }
                foreach (var f in fighters) { if (!f || !f.alive) continue; var q = f.pos; if (q.y > b.y1 || q.y + 2.5f < b.y0) continue; if (q.x > x0 - .55f && q.x < x1 + .55f && q.z > z0 - .55f && q.z < z1 + .55f) return false; }
                return true;
            }
            float mx = 0, mz = 0;
            if (Free(b.x0 + dx, b.x1 + dx, b.z0, b.z1)) mx = dx;
            if (Free(b.x0 + mx, b.x1 + mx, b.z0 + dz, b.z1 + dz)) mz = dz;
            if (mx == 0 && mz == 0) return;
            b.x0 += mx; b.x1 += mx; b.z0 += mz; b.z1 += mz; BoxWorld.Set(bi, b);
            e.t.position += new Vector3(mx, 0, mz);
            KnockOn(b);
        }

        void Chip(Ent e, float dmg, Vector3 at)
        {
            if (e.boxes.Length == 0) return;
            e.chipHP = Mathf.Max(0, e.chipHP - dmg);
            float step = Mathf.Ceil((1 - e.chipHP / Mathf.Max(1, e.hp)) * 4) / 4, nh = e.h0 * (1 - .4f * step);
            var b = BoxWorld.Boxes[e.boxes[0]];
            Bits(at, new[] { BaseColor(e), C("#e9d3a8"), C("#8a5a34") }, 3);
            if (nh < b.y1 - b.y0 - .01f)
            {
                float top = b.y1; b.y1 = b.y0 + nh; BoxWorld.Set(e.boxes[0], b);
                var s = e.homeScale; e.t.localScale = new Vector3(s.x, s.y * nh / e.h0, s.z);
                e.t.localPosition = new Vector3(e.t.localPosition.x, e.homePos.y - (e.homeBoxes[0].y0 - e.homeBoxes[0].y0) - (1 - nh / e.h0) * (e.homePos.y - e.homeBoxes[0].y0), e.t.localPosition.z);
                BreakFx.I.Chunks(new Vector3((b.x0 + b.x1) / 2, b.y1, (b.z0 + b.z1) / 2), new Vector3(b.x1 - b.x0, .2f, b.z1 - b.z0), Vector3.zero, new[] { BaseColor(e), C("#c99a5b") }, 6, b.y0, 1.5f);
                Sfx.Play("wood", at, .7f, 14);
                KnockOn(new Box { x0 = b.x0, x1 = b.x1, y0 = b.y0, y1 = top, z0 = b.z0, z1 = b.z1 });
            }
        }

        // ------------------------------------------------------------------ trees and play frames
        void Fell(Ent e, Vector3 at)
        {
            if (!e.alive || e.st != 0) return;
            e.alive = false; e.st = 1; e.stT = 0;
            Report(e.d.kind);
            var p = e.t.position; var d = new Vector3(p.x - at.x, 0, p.z - at.z); if (d.sqrMagnitude < 1e-4f) d = new Vector3(Random.value - .5f, 0, Random.value - .5f); d.Normalize();
            e.axis = Vector3.Cross(Vector3.up, d);   // tips away from the shot
            foreach (var i in e.boxes) BoxWorld.Disable(i);
            if (e.d.kind == "tree")
            {
                Sfx.Play("wood", p + Vector3.up, 1, 24);
                if (!e.stump)
                {
                    e.stump = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    var col = e.stump.GetComponent<Collider>(); if (col) Destroy(col);
                    var hb = e.homeBoxes.Length > 0 ? e.homeBoxes[0] : new Box { x0 = p.x - .35f, x1 = p.x + .35f };
                    float tw = Mathf.Max(.4f, hb.x1 - hb.x0);
                    e.stump.transform.localScale = new Vector3(tw * .9f, .225f, tw * .9f);
                    e.stump.transform.position = new Vector3(p.x, .225f, p.z);
                    mpb.Clear(); mpb.SetColor("_BaseColor", C("#6b4426")); e.stump.GetComponent<Renderer>().SetPropertyBlock(mpb);
                }
                e.stump.SetActive(true);
                BreakFx.I.Chunks(p + Vector3.up * 1.5f, Vector3.one * .6f, d, new[] { C("#7a4c2a"), C("#c99a5b"), C("#e9d3a8") }, 10, 0, 1.2f);
            }
            else
            {
                Sfx.Play("brick", p + Vector3.up * 1.5f, 1, 30);
                BreakFx.I.Chunks(p + Vector3.up * 1.6f, Vector3.one * 2, d, new[] { C("#7fd0d8"), C("#8a8f99"), C("#c8322b"), C("#f4c22b") }, 14, 0, 2f);
                BreakFx.I.Puffs(p + Vector3.up * .3f, 10, 1.5f, .6f, .9f, 1.3f, new Color(.78f, .72f, .63f, .55f));
            }
        }

        void UpdFalling(Ent e, float dt)
        {
            e.stT += dt;
            if (e.d.kind == "tree")
            {
                if (e.st == 1)
                {
                    float a = Mathf.Min(Mathf.PI / 2 * .97f, 1.25f * e.stT * e.stT * 1.6f);
                    e.t.localRotation = Quaternion.AngleAxis(a * Mathf.Rad2Deg, e.axis) * e.homeRot;
                    if (a >= Mathf.PI / 2 * .97f)
                    {
                        e.st = 2; e.stT = 0;
                        var c = e.t.position + e.t.rotation * Quaternion.Inverse(e.homeRot) * (Vector3.up * e.d.treeH * .55f);
                        Sfx.Play("brick", new Vector3(c.x, .5f, c.z), 1, 26);
                        BreakFx.I.Chunks(new Vector3(c.x, .6f, c.z), Vector3.one * 2, Vector3.up, new[] { C("#3f8f3a"), C("#4fae45"), C("#7fd36a"), C("#2f6a1e") }, 18, 0, 1.6f);
                        BreakFx.I.Puffs(new Vector3(c.x, .3f, c.z), 8, 1.5f, .5f, .8f, 1.2f, new Color(.78f, .72f, .63f, .5f));
                    }
                }
                else if (e.st == 2)
                {
                    float k = Mathf.Max(0, 1 - e.stT * 3);
                    e.t.localRotation = Quaternion.AngleAxis((Mathf.PI / 2 * .97f + Mathf.Sin(e.stT * 14) * .04f * k) * Mathf.Rad2Deg, e.axis) * e.homeRot;
                    if (e.stT > 4.5f) { e.st = 3; e.stT = 0; }
                }
                else if (e.st == 3)
                {
                    e.t.localPosition = e.homePos + Vector3.down * e.stT * 1.4f;
                    if (e.stT > 1.4f) { e.t.gameObject.SetActive(false); e.st = 4; }
                }
            }
            else if (e.st == 1)
            {
                float a = Mathf.Min(.5f, .9f * e.stT * e.stT);
                e.t.localRotation = Quaternion.AngleAxis(a * Mathf.Rad2Deg, e.axis) * e.homeRot;
                e.t.localPosition = e.homePos + Vector3.down * Mathf.Max(0, e.stT - .5f) * 1.6f;
                if (e.stT > 3.2f) { e.t.gameObject.SetActive(false); e.st = 4; }
            }
        }

        // ------------------------------------------------------------------ cars
        void HitCar(Ent e, Vector3 at)
        {
            if (e.burnt) return;
            if (Time.time - e.smokeT > .03f) { e.hits++; e.smokeT = Time.time; }
            Bits(at, new[] { C("#ffd23f"), Color.white, BaseColor(e) }, 3);
            if (e.hits >= 3) Wreck(e);
        }

        void Wreck(Ent e)
        {
            if (e.burnt) return;
            e.burnt = true; e.v = 0; e.carState = "dead"; e.burnT = 0;
            Report("car");
            // the shell settles 30 cm (burst tyres) and stays as cover
            foreach (var i in e.boxes) { var bx = BoxWorld.Boxes[i]; bx.y0 -= .3f; bx.y1 -= .3f; BoxWorld.Set(i, bx); }
            e.t.position += Vector3.down * .3f;
            foreach (var r in e.rends)
            {
                if (!r || !r.sharedMaterial) continue;
                var c = r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : Color.grey;
                mpb.Clear(); mpb.SetColor("_BaseColor", Color.Lerp(c, new Color(.08f, .07f, .07f), .85f)); mpb.SetColor("_EmissionColor", Color.black); r.SetPropertyBlock(mpb);
            }
            var cp = Center(e); cp.y = 1.2f;
            BreakFx.I.Boom(cp, 3.4f); Sfx.Play("explode", cp, 1, 26);
            BreakFx.I.Chunks(cp, new Vector3(2, 1, 4), Vector3.up, new[] { BaseColor(e), C("#cfe6f3"), C("#1c1d22"), C("#3a3d45") }, 16, 0, 4);
        }

        void UpdCars(float dt)
        {
            foreach (var e in cars)
            {
                if (e.burnt)
                {
                    // burns for 12 s, then smoulders until 40 s
                    e.burnT += dt; if (e.burnT > 40) continue;
                    float k = e.burnT < 12 ? 1 : .25f; var top = Center(e) + Vector3.up * .6f;
                    if (Random.value < dt * 14 * k) BreakFx.I.Puffs(top + new Vector3(Random.Range(-.9f, .9f), 0, Random.Range(-1.6f, 1.6f)), 1, .1f, 2.2f, .32f, .55f, new Color(1f, Random.Range(.3f, .7f), .1f, .85f), 1f);
                    if (Random.value < dt * 6) BreakFx.I.Puffs(top + Vector3.up * .6f, 1, .8f, 2.6f, .9f, 2.6f, new Color(.17f, .18f, .2f, .5f));
                    continue;
                }
                if (e.carState == "ext") continue;   // driven by StreetTraffic (maps chat)
                if (e.carState == "park")
                {
                    e.wait -= dt;
                    if (e.wait <= 0)
                    {
                        float tz = e.z; for (int k = 0; k < 6; k++) { tz = (Random.value * 2 - 1) * 23f; if (Mathf.Abs(tz - e.z) > 10) break; }
                        e.target = tz; e.vmax = 4 + Random.value * 9; e.carState = "drive";
                    }
                    continue;
                }
                float dir = Mathf.Sign(e.target - e.z), left = Mathf.Abs(e.target - e.z);
                float want = Mathf.Min(e.vmax, Mathf.Sqrt(2 * 5 * left));
                e.v = Mathf.Clamp(e.v + (want > e.v ? 7 : -12) * dt, 0, e.vmax);
                float dz = dir * e.v * dt; if (Mathf.Abs(dz) > left) dz = dir * left;
                if (dz != 0 && (CarHitsFighter(e, dz, out bool fighter) || fighter))
                {
                    if (e.v > 1.5f && fighter && Time.time - e.smokeT > 2.5f) { e.smokeT = Time.time; Sfx.Play("horn", Center(e), 1, 20); }
                    e.v = 0; e.blocked += dt;
                    if (e.blocked > 2.5f) { e.blocked = 0; e.target = Mathf.Clamp(e.z - dir * (6 + Random.value * 20), -23, 23); }
                    continue;
                }
                e.blocked = 0;
                if (dz != 0) MoveCar(e, dz);
                if (left < .05f || (dz == 0 && e.v < .01f)) { e.carState = "park"; e.v = 0; e.wait = 5 + Random.value * 18; }
            }
        }

        bool CarHitsFighter(Ent e, float dz, out bool fighter)
        {
            fighter = false;
            if (e.boxes.Length == 0) return false;
            var b = BoxWorld.Boxes[e.boxes[0]];
            foreach (var f in fighters)
            {
                if (!f || !f.alive) continue; var p = f.pos; const float r = .75f;
                if (p.x > b.x0 - r && p.x < b.x1 + r && p.z > b.z0 + dz - r && p.z < b.z1 + dz + r && p.y < b.y1 + 1.6f && p.y > -1) { fighter = true; return true; }
            }
            for (int i = 0; i < BoxWorld.Boxes.Count; i++)
            {
                if (System.Array.IndexOf(e.boxes, i) >= 0) continue; var x = BoxWorld.Boxes[i];
                if (x.y1 < .4f || x.y0 > b.y1 + 1) continue;
                if (x.x1 > b.x0 && x.x0 < b.x1 && x.z1 > b.z0 + dz - .4f && x.z0 < b.z1 + dz + .4f) return true;
            }
            return false;
        }

        void MoveCar(Ent e, float dz)
        {
            foreach (var i in e.boxes) { var b = BoxWorld.Boxes[i]; b.z0 += dz; b.z1 += dz; BoxWorld.Set(i, b); }
            e.t.position += new Vector3(0, 0, dz); e.z += dz;
            // fighters standing on the roof ride along
            foreach (var f in fighters) { if (!f || !f.alive || !f.grounded) continue; var b = BoxWorld.Boxes[e.boxes[e.boxes.Length - 1]]; if (f.pos.x > b.x0 && f.pos.x < b.x1 && f.pos.z > b.z0 - dz && f.pos.z < b.z1 - dz && Mathf.Abs(f.pos.y - b.y1) < .1f) f.pos.z += dz; }
        }

        // ------------------------------------------------------------------ loose items
        void KnockOn(Box b)
        {
            foreach (var e in knocks)
            {
                if (!e.alive || e.kst != 0) continue;
                var p = e.t.position; float y0 = p.y - e.d.kh / 2;
                if (y0 < b.y0 + .02f || y0 > b.y1 + .1f || p.x < b.x0 - .45f || p.x > b.x1 + .45f || p.z < b.z0 - .45f || p.z > b.z1 + .45f) continue;
                KnockIt(e, new Vector3(p.x - (b.x0 + b.x1) / 2, 0, p.z - (b.z0 + b.z1) / 2), Random.Range(1f, 2.5f));
            }
        }

        void KnockIt(Ent e, Vector3 dir, float pw)
        {
            if (!e.alive || Time.time - e.kT < .25f) return;
            e.kT = Time.time;
            var h = new Vector3(dir.x, 0, dir.z); if (h.sqrMagnitude < 1e-4f) h = new Vector3(Random.value - .5f, 0, Random.value - .5f); h.Normalize();
            pw /= Mathf.Max(.2f, e.d.heavy);
            e.kv = new Vector3(h.x * pw * Random.Range(.6f, 1f), (e.kst != 0 ? Random.Range(1.5f, 3f) : Random.Range(2f, 4.5f)) * Mathf.Min(1, pw / 4), h.z * pw * Random.Range(.6f, 1f));
            e.kav = new Vector3(h.z * Random.Range(6f, 11f), Random.Range(-4f, 4f), -h.x * Random.Range(6f, 11f)) * Mathf.Rad2Deg;
            if (e.d.ball) { e.kv.x += h.x * pw * .4f; e.kv.z += h.z * pw * .4f; e.kav = Vector3.zero; }
            e.kst = 1;
            Sfx.Play(!string.IsNullOrEmpty(e.d.snd) ? e.d.snd : Random.value < .5f ? "bounce1" : "bounce2", e.t.position, .7f, 10, Random.Range(.9f, 1.4f));
        }

        void KnockHurt(Ent e, float dmg)
        {
            if (!e.alive || e.d.brk <= 0) return;
            e.bhp -= dmg;
            if (e.bhp > 0) return;
            e.alive = false; e.t.gameObject.SetActive(false);
            Report("prop");
            var p = e.t.position;
            Sfx.Play("crate", p, .8f, 14);
            BreakFx.I.Chunks(p, new Vector3(e.d.kw, e.d.kh, e.d.kd), Vector3.up, new[] { BaseColor(e), C("#d8dbe2"), C("#55585f") }, 8, p.y - e.d.kh / 2, 1.2f);
        }

        float Ground(float x, float z, float y)
        {
            float s = 0;
            foreach (var b in BoxWorld.Boxes) { if (b.y1 > y + .06f || x < b.x0 || x > b.x1 || z < b.z0 || z > b.z1) continue; if (b.y1 > s) s = b.y1; }
            return s;
        }

        void UpdKnocks(float dt)
        {
            // people walking into things push them over (and kick fallen ones about)
            foreach (var f in fighters)
            {
                if (!f || !f.alive) continue; float sp = f.HorizontalSpeed; if (sp < 1.2f) continue;
                foreach (var e in knocks)
                {
                    if (!e.alive) continue; var p = e.t.position;
                    if (Mathf.Abs(p.x - f.pos.x) > Fighter.R + e.d.kw / 2 + .05f || Mathf.Abs(p.z - f.pos.z) > Fighter.R + e.d.kd / 2 + .05f) continue;
                    if (f.pos.y > p.y + e.d.kh / 2 - .05f || f.pos.y + Fighter.HH < p.y - e.d.kh / 2) continue;
                    KnockIt(e, f.vel, e.kst != 0 ? sp * .6f : Mathf.Max(2.5f, sp * .8f));
                }
            }
            dt = Mathf.Min(dt, .05f);
            foreach (var e in knocks)
            {
                if (e.kst != 1 || !e.alive) continue;
                var p = e.t.position; e.kv.y -= 24 * dt;
                float nx = p.x + e.kv.x * dt, nz = p.z + e.kv.z * dt, hy = Mathf.Min(e.d.kh, Mathf.Max(e.d.kw, e.d.kd)) / 2;
                bool blocked = false;
                foreach (var b in BoxWorld.Boxes) if (nx > b.x0 && nx < b.x1 && nz > b.z0 && nz < b.z1 && b.y1 > p.y - hy + .12f && b.y0 < p.y + hy) { blocked = true; break; }
                if (blocked) { e.kv.x *= e.d.ball ? -.6f : -.3f; e.kv.z *= e.d.ball ? -.6f : -.3f; } else { p.x = nx; p.z = nz; }
                p.y += e.kv.y * dt;
                e.t.Rotate(e.kav * dt, Space.World);
                float g = Ground(p.x, p.z, p.y - hy + .3f);
                if (p.y - hy < g && e.kv.y < 0)
                {
                    p.y = g + hy; e.kv.y *= e.d.ball ? -.5f : -.25f; e.kv.x *= .55f; e.kv.z *= .55f; e.kav *= .5f;
                    if (Mathf.Abs(e.kv.y) < .6f && new Vector2(e.kv.x, e.kv.z).magnitude < .4f) { e.kst = 2; e.kv = Vector3.zero; }
                }
                e.t.position = p;
            }
        }

        // ------------------------------------------------------------------ ambient animation (windmill, balloon, clouds)
        void UpdAnims(float dt)
        {
            animT = (animT + dt) % period;
            foreach (var e in anims)
            {
                var k = e.d.keys; if (k == null || k.Length < 14 || e.d.hz <= 0) continue;
                int n = k.Length / 7; float f = animT * e.d.hz; int i0 = Mathf.FloorToInt(f) % n, i1 = (i0 + 1) % n; float u = f - Mathf.Floor(f);
                var p0 = new Vector3(k[i0 * 7], k[i0 * 7 + 1], k[i0 * 7 + 2]); var p1 = new Vector3(k[i1 * 7], k[i1 * 7 + 1], k[i1 * 7 + 2]);
                var q0 = new Quaternion(k[i0 * 7 + 3], k[i0 * 7 + 4], k[i0 * 7 + 5], k[i0 * 7 + 6]); var q1 = new Quaternion(k[i1 * 7 + 3], k[i1 * 7 + 4], k[i1 * 7 + 5], k[i1 * 7 + 6]);
                if ((p1 - p0).sqrMagnitude > 400) p1 = p0;    // a cloud wrapping round the sky: jump, don't sweep across
                e.t.localPosition = Vector3.Lerp(p0, p1, u);
                e.t.localRotation = Quaternion.Slerp(q0, q1, u);
            }
        }

        // Until the weapons arrive: F6 shoots whatever is under the crosshair (40 damage), F7 sets off a blast there (F3 is the character picker).
        void DebugKeys()
        {
            if (!Application.isEditor) return;   // editor-only test keys now that weapons are in
            var kb = UnityEngine.InputSystem.Keyboard.current; var cam = Camera.main;
            if (kb == null || !cam) return;
            bool shoot = kb.f6Key.wasPressedThisFrame, boom = kb.f7Key.wasPressedThisFrame;
            if (!shoot && !boom) return;
            var o = cam.transform.position; var d = cam.transform.forward;
            float t = BoxWorld.Ray(o, d, 120, out int bi); if (t < 0) return;
            var at = o + d * t;
            if (shoot) { if (!Hit(bi, 40, at, d)) BreakFx.I.Chunks(at, Vector3.one * .2f, -d, new[] { new Color(.85f, .85f, .85f), new Color(.4f, .4f, .4f) }, 3, at.y - 3, .4f); KnockRay(o, d, t, 40); }
            if (boom) Blast(at - d * .3f, 4.6f, null, 0);
        }

        void Update()
        {
            DebugKeys();
            float dt = Time.deltaTime;
            if (Time.frameCount % 30 == 0 || fighters.Length == 0) fighters = FindObjectsByType<Fighter>();
            foreach (var e in ents) if ((e.d.kind == "tree" || e.d.kind == "play") && e.st >= 1 && e.st < 4) UpdFalling(e, dt);
            UpdCars(dt);
            UpdKnocks(dt);
            UpdAnims(dt);
        }
    }
}
