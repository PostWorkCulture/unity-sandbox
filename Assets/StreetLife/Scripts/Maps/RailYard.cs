// Maps chat: Railed's live line, ported from the web game's railTrain / railPos / railStep and the rail glow:
//  - a two-unit passenger train runs through the yard on a timetable (11 m/s, 10 s gap), in and out of the tunnels;
//    it's a set of moving collision boxes, so you can ride on its roof; anyone standing in front is stepped up onto
//    a low deck or flung clear of the line (45 damage once a second); a horn as it comes into the yard;
//  - the rails glow red while a train is in the section or due within 4 s, and then they're live: standing on them
//    costs 4 health every .45 s with a spark and a zap.
// Runs at the same fixed 60 Hz as the fighters, before them (MapWorld's order), so riders move with the train.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class RailYard : MonoBehaviour
    {
        class Train
        {
            public XTrain d; public Transform root; public int[] boxes; public float z = -9999, zWeb = -9999; public bool shown, horned;
            public readonly List<float> lastHit = new List<float>();
        }
        class Glow { public XGlow d; public List<Material> mats = new List<Material>(); public float k; public bool hot; public Color emit; public float alpha; }

        MapWorld w; float Z = 30, t, acc;
        readonly List<Train> trains = new List<Train>();
        readonly List<Glow> glows = new List<Glow>();
        readonly Dictionary<Fighter, float> trainHit = new Dictionary<Fighter, float>(), railHit = new Dictionary<Fighter, float>();
        Fighter[] fighters = new Fighter[0];
        const float SIM = 1f / 60f;

        public void Init(MapWorld world)
        {
            w = world; var x = w.extras; if (x.rl != null && x.rl.Z > 0) Z = x.rl.Z;
            if (x.trains != null)
                foreach (var d in x.trains)
                {
                    var tr = new Train { d = d };
                    tr.root = new GameObject("Train").transform; tr.root.SetParent(transform, false);
                    foreach (var id in d.meshes) w.MeshObject(id, tr.root, null);
                    tr.root.gameObject.SetActive(false);
                    // its boxes join the world parked out of sight; BoxWorld.Set moves them, and the nav grid treats them as moving
                    tr.boxes = new int[d.parts.Length];
                    for (int i = 0; i < d.parts.Length; i++)
                    {
                        tr.boxes[i] = BoxWorld.Boxes.Count;
                        BoxWorld.Boxes.Add(Parked(d));
                        NavGrid.Dynamic.Add(tr.boxes[i]);
                    }
                    trains.Add(tr);
                }
            if (x.glow != null)
                foreach (var g in x.glow)
                {
                    var gl = new Glow { d = g };
                    foreach (var id in g.meshes)
                    {
                        var src = w.MapMat(x.meshes[id].mat); if (!src) continue;
                        var m = new Material(src);
                        if (!g.plane) { m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; m.SetColor("_EmissionColor", Color.black); }
                        gl.mats.Add(m); w.MeshObject(id, transform, m);
                    }
                    glows.Add(gl);
                }
        }

        static Box Parked(XTrain d) => new Box { x0 = d.x - .1f, x1 = d.x + .1f, y0 = -60, y1 = -59.9f, z0 = -.1f, z1 = .1f };

        // the web game's railPos: where the nose is along the line (web z), or null while the train is off in the tunnels
        float? RailPos(XTrain d, float time)
        {
            float span = 2 * Z + 92 + d.len, cyc = span / d.speed + d.gap, u = ((time + d.ph) % cyc + cyc) % cyc;
            if (u > span / d.speed) return null;
            return -d.dir * (Z + 46) + d.dir * u * d.speed;
        }

        Box PartBox(XTrain d, XPart p, float zWeb)
        {
            float xa = d.x + d.dir * p.lx0, xb = d.x + d.dir * p.lx1, za = zWeb + d.dir * p.s0, zb = zWeb + d.dir * p.s1;
            return new Box { x0 = Mathf.Min(xa, xb), x1 = Mathf.Max(xa, xb), y0 = p.y0, y1 = p.y1, z0 = -Mathf.Max(za, zb), z1 = -Mathf.Min(za, zb) };
        }

        void Update()
        {
            if (Time.frameCount % 30 == 0 || fighters.Length == 0) fighters = FindObjectsByType<Fighter>(FindObjectsInactive.Exclude);
            acc = Mathf.Min(acc + Time.deltaTime, SIM * 4);
            while (acc >= SIM - 1e-6f) { acc -= SIM; t += SIM; foreach (var tr in trains) Step(tr, SIM); Rails(SIM); }
            foreach (var tr in trains) if (tr.shown) Place(tr);
        }

        void Place(Train tr) { tr.root.localPosition = new Vector3(tr.d.x, 0, -tr.zWeb); tr.root.localRotation = Quaternion.Euler(0, tr.d.dir > 0 ? 0 : 180, 0); }

        void Step(Train tr, float dt)
        {
            var d = tr.d; var zp = RailPos(d, t);
            if (zp == null)
            {
                if (tr.shown) { tr.shown = false; tr.root.gameObject.SetActive(false); for (int i = 0; i < tr.boxes.Length; i++) BoxWorld.Set(tr.boxes[i], Parked(d)); }
                tr.horned = false; return;
            }
            float z = zp.Value;
            if (!tr.shown || Mathf.Abs(z - tr.zWeb) > 20)
            {
                tr.shown = true; tr.zWeb = z; tr.root.gameObject.SetActive(true); Place(tr);
                for (int i = 0; i < tr.boxes.Length; i++) BoxWorld.Set(tr.boxes[i], PartBox(d, d.parts[i], z));
                return;
            }
            float dzWeb = z - tr.zWeb; var carry = new Vector3(0, 0, -dzWeb);
            // riders: anyone standing on top of a wagon moves with it
            var riders = new List<Fighter>(); const float R = Fighter.R;
            foreach (var f in fighters)
            {
                if (!f || !f.alive || !f.grounded) continue;
                foreach (var bi in tr.boxes)
                {
                    var b = BoxWorld.Boxes[bi];
                    if (Mathf.Abs(f.pos.y - b.y1) < .08f && f.pos.x > b.x0 - R * .8f && f.pos.x < b.x1 + R * .8f && f.pos.z > b.z0 - R * .8f && f.pos.z < b.z1 + R * .8f) { riders.Add(f); break; }
                }
            }
            tr.zWeb = z;
            for (int i = 0; i < tr.boxes.Length; i++) BoxWorld.Set(tr.boxes[i], PartBox(d, d.parts[i], z));
            foreach (var f in riders) f.Carry(carry);
            // anyone in front gets stepped up onto a low deck or flung clear of the line
            foreach (var f in fighters)
            {
                if (!f || !f.alive || riders.Contains(f)) continue;
                float H = f.Hh;
                foreach (var bi in tr.boxes)
                {
                    var b = BoxWorld.Boxes[bi];
                    if (f.pos.x <= b.x0 - R || f.pos.x >= b.x1 + R || f.pos.z <= b.z0 - R || f.pos.z >= b.z1 + R || f.pos.y >= b.y1 - .02f || f.pos.y + H <= b.y0) continue;
                    if (b.y1 - f.pos.y < .6f) { f.Carry(new Vector3(0, b.y1 - f.pos.y, 0)); f.vel.y = Mathf.Max(0, f.vel.y); f.grounded = true; break; }
                    float side = Mathf.Sign(f.pos.x - d.x); if (side == 0) side = Random.value < .5f ? -1 : 1;
                    const float hw = 1.75f;
                    f.Carry(new Vector3(d.x + side * (hw + R + .05f) - f.pos.x, 0, -dzWeb));
                    f.vel = new Vector3(side * Random.Range(11f, 15f), Random.Range(8f, 11f), -d.dir * d.speed * .5f);
                    f.grounded = false; f.thrownT = .55f;
                    trainHit.TryGetValue(f, out var last);
                    if (Time.time - last > 1 || last == 0)
                    {
                        trainHit[f] = Time.time;
                        Sfx.Play("punch", f.pos, 1, 20, .8f);
                        if (!f.Frozen) f.Damage(45, null, false);
                    }
                    break;
                }
            }
            // horn as it comes into the yard
            if (!tr.horned && Mathf.Abs(z) < Z + 12) { tr.horned = true; Sfx.Play("trainhorn", new Vector3(d.x, 3, -z), .5f, 90); }
        }

        void Rails(float dt)
        {
            float pulse = .75f + .25f * Mathf.Sin(t * 10);
            foreach (var g in glows)
            {
                Train tr = g.d.ln >= 0 && g.d.ln < trains.Count ? trains[g.d.ln] : null;
                bool hot = tr != null && (tr.shown || RailPos(tr.d, t + 4) != null);
                g.k = Mathf.Clamp01(g.k + (hot ? dt * 2.5f : -dt * 1.5f)); g.hot = g.k > .5f;
                foreach (var m in g.mats)
                {
                    if (g.d.plane) { var c = m.GetColor("_BaseColor"); c.a = g.k * .3f * pulse; m.SetColor("_BaseColor", c); }
                    else m.SetColor("_EmissionColor", Hex.ToColor("#ff1a0a").linear * (g.k * 1.6f * pulse));
                }
            }
            // live rails: 4 health every .45 s while you stand on them
            foreach (var f in fighters)
            {
                if (!f || !f.alive || f.Frozen || f.pos.y > .45f || Mathf.Abs(f.pos.z) > Z) continue;
                foreach (var g in glows)
                {
                    if (!g.hot || g.d.plane) continue;
                    float dx = Mathf.Abs(f.pos.x - g.d.x);
                    if (Mathf.Abs(dx - .75f) > Fighter.R * .7f + .08f) continue;
                    railHit.TryGetValue(f, out var last);
                    if (last != 0 && Time.time - last < .45f) break;
                    railHit[f] = Time.time;
                    Sfx.Play("volt", f.pos, .45f, 10);
                    f.Damage(4, null, false);
                    BreakFx.I.Puffs(new Vector3(f.pos.x, .25f, f.pos.z), 10, .3f, 2.5f, .1f, .3f, Hex.ToColor("#ffd23f"));
                    break;
                }
            }
        }
    }
}
