// Building walls (match chat), the web game's buildWall / damageBrick / wallSupport / toppleWall / updWalls:
// Q (or the Wall button) puts up a 3.6 x 3.1 m wall of 6 x 5 bricks 2.3 m in front of you, in your team colour. Four a
// round, 0.6 s apart. Bricks have 24 hp: a hit nudges a brick back along the shot, a broken one is knocked out and bursts
// when it lands, a brick with nothing under it falls too, and losing two from the bottom row brings the whole wall down.
// Walls rise in a moment and sink away after 6 s. Bricks are real collision boxes (bullets stop, bots path round them).
// Not in Rumble. Bots build them by their style (BotExtras).
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter
    {
        [System.NonSerialized] public int walls = 4;
        [System.NonSerialized] public float buildT;
    }

    public class Walls : MonoBehaviour
    {
        public const float BRICK_HP = 24, WALL_LIFE = 6, W = 3.6f, T = .5f, HGT = 3.1f;
        public const int COLS = 6, ROWS = 5, PER_ROUND = 4, MAX_WALLS = 8;
        public static readonly string[] TeamColours = { "#2f7fe0", "#e8463a", "#3bb54a", "#f0a020", "#a05ad6", "#1fb5b0", "#ff6fae", "#8a5a34" };

        static Walls I;
        public static bool On => !Rules.Rumble;

        class Brick { public int slot; public Box b; public Transform t; public float hp, col, wd; public int row; public bool alive; public Vector3 home; public Wall w; }
        class Wall { public Fighter owner; public Color colour; public float born, y0; public Box bb; public Transform root; public List<Brick> bricks = new List<Brick>(); public bool alive, sank; public Vector3 hitDir; }
        readonly List<Wall> walls = new List<Wall>();
        readonly Dictionary<int, Brick> bySlot = new Dictionary<int, Brick>();
        readonly Stack<int> free = new Stack<int>();
        class Chunk { public Transform t; public Vector3 v, w; public float floor; public Color c; public float age; }
        readonly List<Chunk> chunks = new List<Chunk>();
        static Mesh cube; static Material mat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Ensure(); Ensure(); }
        static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("Walls").AddComponent<Walls>();
        }
        void OnEnable() { RoundManager.RoundStarted += OnRound; }
        void OnDisable() { RoundManager.RoundStarted -= OnRound; if (I == this) I = null; }
        void OnRound(int r) { ClearAll(); }

        // ------------------------------------------------------------------ collision slots: parked boxes the walls borrow
        void MakeSlots()
        {
            if (free.Count > 0 || bySlot.Count > 0 || !BoxWorld.Loaded) return;
            int n = MAX_WALLS * (COLS + 1) * ROWS;
            for (int i = 0; i < n; i++)
            {
                int idx = BoxWorld.Boxes.Count;
                BoxWorld.Boxes.Add(new Box { x0 = -9999, x1 = -9998, y0 = -9999, y1 = -9998, z0 = -9999, z1 = -9998 });
                NavGrid.Dynamic.Add(idx);
                free.Push(idx);
            }
        }

        static Box Far => new Box { x0 = -9999, x1 = -9998, y0 = -9999, y1 = -9998, z0 = -9999, z1 = -9998 };
        static void Put(int slot, Box b) { if (BoxWorld.IsDisabled(slot)) BoxWorld.Restore(slot); BoxWorld.Set(slot, b); }
        static void Take(int slot) { if (BoxWorld.IsDisabled(slot)) BoxWorld.Restore(slot); BoxWorld.Set(slot, Far); }

        // ------------------------------------------------------------------ building
        public static bool Build(Fighter f)
        {
            if (!I) Ensure(); if (!I) return false;
            return I.DoBuild(f);
        }

        bool DoBuild(Fighter f)
        {
            if (!On || !f || f.walls <= 0 || f.buildT > 0 || !f.alive || f.downed || f.InFinisher || f.Frozen) return false;
            MakeSlots();
            float fx = Mathf.Sin(f.yaw), fz = Mathf.Cos(f.yaw); bool alongZ = Mathf.Abs(fx) > Mathf.Abs(fz);
            float cx = f.pos.x + fx * 2.3f, cz = f.pos.z + fz * 2.3f;
            float x0 = cx - (alongZ ? T : W) / 2, x1 = cx + (alongZ ? T : W) / 2, z0 = cz - (alongZ ? W : T) / 2, z1 = cz + (alongZ ? W : T) / 2;
            var bd = BoxWorld.Bounds;
            if (cx < bd.x0 + 1 || cx > bd.x1 - 1 || cz < bd.z0 + 1 || cz > bd.z1 - 1) return false;
            foreach (var o in Combat.Fighters) if (o && o.alive && o.pos.x + Fighter.R > x0 && o.pos.x - Fighter.R < x1 && o.pos.z + Fighter.R > z0 && o.pos.z - Fighter.R < z1) return false;
            if (free.Count < (COLS + 1) * ROWS) { var old = walls.Find(w => w.alive); if (old != null) Remove(old, false); }
            if (free.Count < (COLS + 1) * ROWS) return false;

            var colour = Web.Hex(TeamColours[Mathf.Clamp(f.team, 0, TeamColours.Length - 1)]);
            var w = new Wall { owner = f, colour = colour, born = Time.time, y0 = f.pos.y, alive = true, bb = new Box { x0 = x0, x1 = x1, z0 = z0, z1 = z1, y0 = f.pos.y, y1 = f.pos.y + HGT } };
            w.root = new GameObject("wall").transform; w.root.SetParent(transform, false); w.root.position = new Vector3(cx, w.y0, cz);
            float bw = W / COLS, bh = HGT / ROWS, g = .03f;
            for (int r = 0; r < ROWS; r++)
                for (int c = -1; c < COLS; c++)
                {
                    float off = (r % 2 == 1 ? .5f : 0) * bw, a0, a1;
                    if (c == -1) { if (r % 2 == 0) continue; a0 = -W / 2; a1 = -W / 2 + off; }
                    else { a0 = -W / 2 + c * bw + off; a1 = Mathf.Min(W / 2, a0 + bw); }
                    if (a1 - a0 < .05f) continue;
                    var b = alongZ ? new Box { x0 = x0, x1 = x1, z0 = cz + a0 + g, z1 = cz + a1 - g } : new Box { x0 = cx + a0 + g, x1 = cx + a1 - g, z0 = z0, z1 = z1 };
                    b.y0 = w.y0 + r * bh; b.y1 = w.y0 + (r + 1) * bh - g;
                    var br = new Brick { slot = free.Pop(), b = b, hp = BRICK_HP, alive = true, col = (a0 + a1) / 2, wd = a1 - a0, row = r, w = w };
                    Put(br.slot, b); bySlot[br.slot] = br;
                    Color.RGBToHSV(colour, out var hh, out var ss, out var vv);
                    var bc = Color.HSVToRGB(hh, ss, Mathf.Clamp01(vv + Random.Range(-.04f, .04f)));
                    br.t = MakeCube(w.root, bc);
                    br.home = new Vector3((b.x0 + b.x1) / 2, (b.y0 + b.y1) / 2, (b.z0 + b.z1) / 2);
                    br.t.position = br.home; br.t.localScale = new Vector3(b.x1 - b.x0, b.y1 - b.y0, b.z1 - b.z0);
                    w.bricks.Add(br);
                }
            w.root.localScale = new Vector3(1, .05f, 1);
            walls.Add(w);
            f.walls--; f.buildT = .6f;
            var o2 = GunFx.E(18, 4, .2f, .5f, 12, "#" + ColorUtility.ToHtmlStringRGB(colour), "#ffffff"); o2.up = 2; GunFx.Emit(new Vector3(cx, f.pos.y + .2f, cz), o2);
            Sfx.Play("wall", new Vector3(cx, f.pos.y, cz), 1, 14);
            return true;
        }

        Transform MakeCube(Transform parent, Color c)
        {
            if (!cube) { var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp); }
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "brick", enableInstancing = true }; mat.SetFloat("_Smoothness", .25f); }
            var go = new GameObject("brick"); go.transform.SetParent(parent, true);
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", c); mr.SetPropertyBlock(mpb);
            return go.transform;
        }

        // ------------------------------------------------------------------ damage (Destruction.Hit / Blast call these)
        public static bool Hit(int box, float dmg, Vector3 dir)
        {
            if (!I || !I.bySlot.TryGetValue(box, out var br) || !br.alive) return false;
            I.Damage(br, dmg, dir); return true;
        }

        public static void Splash(Vector3 at, float R, float dmg)
        {
            if (!I) return;
            foreach (var w in I.walls.ToArray())
            {
                if (!w.alive) continue;
                foreach (var br in w.bricks.ToArray())
                {
                    if (!br.alive) continue; var b = br.b;
                    var q = new Vector3(Mathf.Clamp(at.x, b.x0, b.x1), Mathf.Clamp(at.y, b.y0, b.y1), Mathf.Clamp(at.z, b.z0, b.z1));
                    float d = Vector3.Distance(q, at);
                    if (d < R) I.Damage(br, dmg * (1 - d / R) + 10, q - at);
                }
            }
        }

        float brickSnd;
        void Damage(Brick br, float dm, Vector3 dir)
        {
            if (!br.alive) return; var w = br.w;
            br.hp -= dm;
            var hd = new Vector3(dir.x, 0, dir.z); if (hd.sqrMagnitude > 1e-6f) hd.Normalize();
            if (br.hp > 0)
            {
                if (hd.sqrMagnitude > .5f) { float k = Mathf.Min(.16f, .05f + dm * .003f); br.t.position += hd * k; }
                return;
            }
            if (hd.sqrMagnitude > .5f) w.hitDir = hd;
            Pop(br, hd, 1, null);
            if (Time.time - brickSnd > .15f) { brickSnd = Time.time; Sfx.Play("brick", br.home, 1, 14); }
            Support(w);
        }

        void Pop(Brick br, Vector3 dir, float k, Vector3? vx)
        {
            br.alive = false; Take(br.slot); bySlot.Remove(br.slot); free.Push(br.slot);
            var d = dir.sqrMagnitude > .5f ? dir : new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)).normalized;
            var v = vx ?? new Vector3(d.x * Random.Range(2.5f, 4.5f) * k + Random.Range(-.6f, .6f), Random.Range(1f, 3f) * k, d.z * Random.Range(2.5f, 4.5f) * k + Random.Range(-.6f, .6f));
            br.t.SetParent(transform, true);
            chunks.Add(new Chunk { t = br.t, v = v, w = new Vector3(Random.Range(-7f, 7f), Random.Range(-3f, 3f), Random.Range(-7f, 7f)) * Mathf.Rad2Deg, floor = br.w.y0 + (br.b.y1 - br.b.y0) * .5f, c = br.w.colour });
        }

        void Support(Wall w)
        {
            if (!w.alive) return;
            int lost = 0; foreach (var o in w.bricks) if (o.row == 0 && !o.alive) lost++;
            if (lost >= 2) { Topple(w); return; }
            for (int r = 1; r < ROWS; r++)
                foreach (var br in w.bricks)
                {
                    if (!br.alive || br.row != r) continue;
                    bool held = false;
                    foreach (var o in w.bricks) if (o.alive && o.row == r - 1 && Mathf.Abs(o.col - br.col) < (o.wd + br.wd) / 2 - .06f) { held = true; break; }
                    if (!held) Pop(br, w.hitDir, .45f, null);
                }
            bool any = false; foreach (var o in w.bricks) if (o.alive) { any = true; break; }
            if (!any) Remove(w, true);
        }

        void Topple(Wall w)
        {
            var d = w.hitDir.sqrMagnitude > .5f ? w.hitDir : new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)).normalized;
            foreach (var br in w.bricks)
            {
                if (!br.alive) continue; float h = (br.b.y0 + br.b.y1) / 2 - w.y0;
                Pop(br, d, 1, new Vector3(d.x * (1.2f + h * 2.2f) + Random.Range(-.5f, .5f), Random.Range(.3f, 1.5f) + h * .4f, d.z * (1.2f + h * 2.2f) + Random.Range(-.5f, .5f)));
            }
            Sfx.Play("brick", new Vector3((w.bb.x0 + w.bb.x1) / 2, w.y0 + 1, (w.bb.z0 + w.bb.z1) / 2), 1, 18);
            GunFx.Shake = Mathf.Max(GunFx.Shake, .12f);
            Remove(w, true);
        }

        void Remove(Wall w, bool keepChunks)
        {
            w.alive = false;
            foreach (var br in w.bricks)
                if (br.alive) { br.alive = false; Take(br.slot); bySlot.Remove(br.slot); free.Push(br.slot); if (br.t) Destroy(br.t.gameObject); }
            if (w.root) Destroy(w.root.gameObject);
            walls.Remove(w);
        }

        void ClearAll()
        {
            foreach (var w in walls.ToArray()) Remove(w, false);
            foreach (var c in chunks) if (c.t) Destroy(c.t.gameObject);
            chunks.Clear();
        }

        // ------------------------------------------------------------------ per frame: rise, age, sink, chunks
        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var f in Combat.Fighters) if (f && f.buildT > 0) f.buildT -= dt;
            foreach (var w in walls.ToArray())
            {
                if (!w.alive || !w.root) continue;
                float age = Time.time - w.born;
                if (age >= WALL_LIFE)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        var p = new Vector3(Mathf.Lerp(w.bb.x0, w.bb.x1, Random.value), w.y0 + Random.Range(.2f, 2.6f), Mathf.Lerp(w.bb.z0, w.bb.z1, Random.value));
                        var o = GunFx.E(4, 2, .25f, .6f, 2, "#" + ColorUtility.ToHtmlStringRGB(w.colour), "#d8dbe2"); o.up = 1; GunFx.Emit(p, o);
                    }
                    Remove(w, false); continue;
                }
                var s = w.root.localScale;
                if (age > WALL_LIFE - .45f)
                {
                    if (!w.sank) { w.sank = true; Sfx.Play("sink", new Vector3((w.bb.x0 + w.bb.x1) / 2, w.y0 + 1, (w.bb.z0 + w.bb.z1) / 2), 1, 14); }
                    s.y = Mathf.Max(.03f, (WALL_LIFE - age) / .45f);
                }
                else if (s.y < 1) s.y = Mathf.Min(1, s.y + dt * 5);
                w.root.localScale = s;
            }
            for (int i = chunks.Count - 1; i >= 0; i--)
            {
                var c = chunks[i]; c.age += dt;
                if (!c.t || c.age > 4) { if (c.t) Destroy(c.t.gameObject); chunks.RemoveAt(i); continue; }
                c.v.y -= 26 * dt; c.t.position += c.v * dt; c.t.Rotate(c.w * dt, Space.Self);
                if (c.t.position.y <= c.floor && c.v.y < 0)
                {
                    var p = c.t.position; Destroy(c.t.gameObject); chunks.RemoveAt(i);
                    var o = GunFx.E(10, 4.5f, .16f, .55f, 8, "#" + ColorUtility.ToHtmlStringRGB(c.c), "#ffffff", "#9aa0a8"); o.up = 2.5f; GunFx.Emit(p, o);
                    if (Time.time - brickSnd > .08f) { brickSnd = Time.time; Sfx.Play("brick", p, .6f, 12, Random.Range(.9f, 1.2f)); }
                }
            }
        }
    }
}
