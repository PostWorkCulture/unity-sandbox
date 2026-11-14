// The Tiki Bar on Beach Please, dressed as the main event (BeachLook.Apply calls Build; play mode only).
//  - What stays: the deck, the bamboo posts, the roof, the back wall, the neon signs, the string lights and every collision
//    box of the building. None of that can break.
//  - What goes: the leaning board pillars by the east corner, the two fuel drums on the deck, the two crate tables with
//    their parasols, the block bottles and their shelves, the old counter's skin, the cream lamp shades, the white ladder
//    and the yellow disc under the shop. The shop itself moves 3.2 m towards the sea, clear of the ladder's foot.
//  - What is built: a marble-topped counter with a fluted front, a brass foot rail and beer taps; a back bar with lit onyx
//    panels, glass shelves and a mirror under the COCKTAILS sign; glass globe lamps; ceiling fans; racks of hanging glasses
//    over the counter; a champagne tower; high glass tables and stools; glass screens at both ends of the bartender's
//    aisle; a timber ladder up the outside of the east gable to the ridge.
//  - What smashes: about 400 loose things. Bottles, glasses, the champagne tower, lanterns and the glass screens break
//    like glass; shakers, ice buckets, stools and tables get knocked about. They all join Destruction (Destruction.Add),
//    so bullets, blasts, people walking into them and the reset at each round work as for the map's own props.
// Remote test: call TikiBar.Test status | blast x y z r | ray ox oy oz dx dy dz | climb | old on|off
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetLife
{
    using Mb = MapProps.Mb;

    public class TikiBar : MonoBehaviour
    {
        const float F = .85f, CT = 2.13f, BT = 2.03f;          // deck floor, counter top, back counter top
        const float ShopShift = 3.2f;
        static readonly Vector3 ShopWas = new Vector3(-.5f, .4f, .5f);
        public const string OldKey = "StreetLife.tikiOld";       // 1: leave the bar as the map has it (for before / after pictures)
        static bool Old => PlayerPrefs.GetInt(OldKey, 0) == 1;

        static Bounds B(float x0, float y0, float z0, float x1, float y1, float z1) { var b = new Bounds(); b.SetMinMax(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1)); return b; }
        // everything of the old map inside these is taken out (meshes, outlines)
        static readonly Bounds[] Gone =
        {
            B(-25.6f, .8f, 4.6f, -8.4f, 3.62f, 8.39f),                                                    // counter, shelves, block bottles
            B(-27.6f, .8f, -7.6f, -24.4f, 4.75f, -4.4f), B(-9.6f, .8f, -7.6f, -6.4f, 4.75f, -4.4f),       // the two parasol tables
            B(-24.45f, 3.8f, 1.55f, -23.55f, 6.06f, 2.45f), B(-10.45f, 3.8f, 1.55f, -9.55f, 6.06f, 2.45f),
            B(-24.45f, 3.8f, -4.45f, -23.55f, 6.06f, -3.55f), B(-10.45f, 3.8f, -4.45f, -9.55f, 6.06f, -3.55f),   // lamp shades and cords
            B(-3.75f, .3f, -.6f, -3.55f, 6.7f, .6f),                                                      // the old ladder
            B(-3.2f, .3f, -8.9f, -2.4f, 3.1f, -5.5f),                                                     // the board pillars
            B(-3.6f, .41f, -2.6f, 2.6f, .46f, 3.6f),                                                      // the shop's floor disc
        };
        static readonly Bounds Shop = B(-1.8f, .35f, -.4f, .8f, 4.9f, 1.3f);

        static bool In(Bounds b, Vector3 p, float grow) => p.x >= b.min.x - grow && p.x <= b.max.x + grow && p.y >= b.min.y - grow && p.y <= b.max.y + grow && p.z >= b.min.z - grow && p.z <= b.max.z + grow;
        static bool InGone(Vector3 p, float grow = .02f) { foreach (var b in Gone) if (In(b, p, grow)) return true; return false; }
        static bool Inside(Bounds outer, Bounds inner, float grow = .03f) => In(outer, inner.min, grow) && In(outer, inner.max, grow);

        /// Does this mesh of the map reach into anything that is taken out?
        internal static bool Touches(WMesh rec)
        {
            if (Old || rec == null || rec.bmin == null || rec.bsize == null) return false;
            var mb = new Bounds(); mb.SetMinMax(new Vector3(rec.bmin[0], rec.bmin[1], rec.bmin[2]), new Vector3(rec.bmin[0] + rec.bsize[0], rec.bmin[1] + rec.bsize[1], rec.bmin[2] + rec.bsize[2]));
            foreach (var b in Gone) if (b.Intersects(mb)) return true;
            return false;
        }

        /// BeachLook.Surfaces, for the map's meshes before they are uploaded: drops the triangles of things that are taken out.
        /// 0: nothing of the mesh is left, 1: untouched, 2: some of it went.
        internal static int Cut(Mesh mesh)
        {
            if (Old || !mesh) return 1;
            var mb = mesh.bounds; bool near = false;
            foreach (var b in Gone) if (b.Intersects(mb)) { near = true; break; }
            if (!near) return 1;
            var v = mesh.vertices; var t = mesh.triangles; var keep = new List<int>(t.Length);
            for (int i = 0; i + 2 < t.Length; i += 3)
                if (!(InGone(v[t[i]]) && InGone(v[t[i + 1]]) && InGone(v[t[i + 2]]))) { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
            if (keep.Count == t.Length) return 1;
            if (keep.Count == 0) return 0;
            mesh.SetTriangles(keep, 0, true);
            return 2;
        }

        public static void Build(MapWorld w, RailLookSet set)
        {
            if (Old || !w || !set || !set.glass || !set.gloss || !set.glow) return;
            var gen = w.builder ? w.builder.transform.Find("__Generated") : null;
            if (!gen) return;
            var bar = new GameObject("Tiki Bar").AddComponent<TikiBar>();
            bar.transform.SetParent(w.transform, false);
            bar.set = set;
            try { bar.Clear(gen); } catch (System.Exception ex) { Debug.LogException(ex); }
            try { bar.Make(); } catch (System.Exception ex) { Debug.LogException(ex); }
            NavGrid.Ready = false;   // the boxes changed (boards, shop, back counter, screens, the ladder): the bots' grid is built again
            Debug.Log("Tiki Bar: " + bar.loose.Count + " loose things, " + bar.panes + " glass screens, " + bar.cleared + " old parts taken out");
        }

        RailLookSet set;
        int cleared, panes, serial;
        readonly List<Destruction.Ent> loose = new List<Destruction.Ent>();
        readonly List<(Destruction.Ent item, Destruction.Ent[] on)> stacks = new List<(Destruction.Ent, Destruction.Ent[])>();
        readonly List<Transform> fans = new List<Transform>();
        readonly List<Destruction.Ent> screens = new List<Destruction.Ent>();
        readonly System.Random rng = new System.Random(7);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // ------------------------------------------------------------------ taking the old things out
        void Clear(Transform gen)
        {
            var shift = new Vector3(ShopShift, 0, 0);
            var geo = gen.Find("Geometry");
            if (geo)
                foreach (Transform t in geo)
                {
                    if (!t.gameObject.activeSelf) continue;
                    var mr = t.GetComponent<MeshRenderer>(); if (!mr) continue;
                    var bb = mr.bounds;
                    foreach (var g in Gone) if (Inside(g, bb)) { t.gameObject.SetActive(false); cleared++; break; }
                    if (t.gameObject.activeSelf && Inside(Shop, bb)) t.position += shift;
                }
            foreach (var l in gen.GetComponentsInChildren<Light>(true))
            {
                var p = l.transform.position;
                if (p.x > -1.9f && p.x < .9f && p.z > -.7f && p.z < 1.5f && p.y < 6f) l.transform.position = p + shift;
            }
            var lines = gen.Find("Outlines");
            if (lines)
                foreach (var mf in lines.GetComponentsInChildren<MeshFilter>())
                {
                    var m = mf.sharedMesh; if (!m || !m.isReadable || m.GetTopology(0) != MeshTopology.Lines) continue;
                    var v = m.vertices; var keep = new List<Vector3>(v.Length); bool changed = false;
                    for (int i = 0; i + 1 < v.Length; i += 2)
                    {
                        if (InGone(v[i], .04f) && InGone(v[i + 1], .04f)) { changed = true; continue; }
                        if (In(Shop, v[i], .05f) && In(Shop, v[i + 1], .05f)) { keep.Add(v[i] + shift); keep.Add(v[i + 1] + shift); changed = true; }
                        else { keep.Add(v[i]); keep.Add(v[i + 1]); }
                    }
                    if (!changed) continue;
                    if (keep.Count == 0) { mf.gameObject.SetActive(false); continue; }
                    var ix = new int[keep.Count]; for (int i = 0; i < ix.Length; i++) ix[i] = i;
                    m.Clear(); m.SetVertices(keep); m.SetIndices(ix, MeshTopology.Lines, 0);
                }
            // collision: the board pillars go, the shop moves
            var boxes = BoxWorld.Boxes;
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                if (b.prop) continue;
                if (Mathf.Abs(b.x0 + 3.1f) < .05f && Mathf.Abs(b.x1 + 2.5f) < .05f && Mathf.Abs(b.y1 - 3f) < .05f && b.z0 > -9f && b.z1 < -5.5f) { BoxWorld.Disable(i); cleared++; }
                else if (Mathf.Abs(b.x0 + 1.67f) < .06f && Mathf.Abs(b.z0 + .17f) < .06f && b.y1 > 4f)
                { b.x0 += ShopShift; b.x1 += ShopShift; BoxWorld.Set(i, b); }
            }
            if (MapSet.Current == "wreck" && Kiosk.Here.Length > 0) Kiosk.Here[0] = ShopWas + shift;
            // the drums on the deck and the two crate tables
            if (Destruction.I)
            {
                var gone = new List<Destruction.Ent>();
                foreach (var e in Destruction.I.ents)
                {
                    if (e == null || e.d == null) continue; var p = e.homePos;
                    if (e.d.kind == "drum" && p.x > -6.5f && p.x < -3.5f && p.z > -6f && p.z < -3f) gone.Add(e);
                    else if (e.d.kind == "chip" && (Mathf.Abs(p.x + 26f) < .6f || Mathf.Abs(p.x + 8f) < .6f) && Mathf.Abs(p.z + 6f) < .6f) gone.Add(e);
                }
                foreach (var e in gone) { Destruction.I.Remove(e); cleared++; }
            }
            // the ladder: outside the gable now, and up to the ridge (it stopped under the roof before, inside its boxes)
            float ridge = 0;
            foreach (var b in boxes) if (Mathf.Abs(b.x0 + 31.2f) < .1f && Mathf.Abs(b.y0 - 6.05f) < .05f && b.y1 > ridge) ridge = b.y1;
            foreach (var L in BoxWorld.Ladders)
                if (Mathf.Abs(L.z0 + .55f) < .1f && L.x0 > -4.5f && L.x0 < -2f && ridge > 7f)
                { L.x0 = LadderX - .04f; L.x1 = LadderX + .76f; L.rung = LadderX - .04f; L.y1 = ridge; L.nx = -1; L.nz = 0; ladderTop = ridge; }
        }
        const float LadderX = -2.7f;
        float ladderTop = 8.71f;

        // ------------------------------------------------------------------ materials
        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        readonly Dictionary<Material, RailLookSet.Surface> mapped = new Dictionary<Material, RailLookSet.Surface>();
        readonly Dictionary<Material, Mb> parts = new Dictionary<Material, Mb>();

        Material Gloss(string hex, float smooth, float metal = 0)
        {
            string key = "g" + hex + smooth + "/" + metal;
            if (!mats.TryGetValue(key, out var m))
            {
                m = new Material(set.gloss) { name = "bar " + hex };
                m.SetColor("_BaseColor", Hex.ToColor(hex)); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
                mats[key] = m;
            }
            return m;
        }
        Material Glow(string hex, string lightHex, float power, Texture map = null, float smooth = .6f)
        {
            string key = "e" + hex + lightHex + power + (map ? map.name : "") + smooth;
            if (!mats.TryGetValue(key, out var m))
            {
                m = new Material(set.glow) { name = "bar glow " + hex };
                m.SetColor("_BaseColor", Hex.ToColor(hex)); m.SetFloat("_Smoothness", smooth);
                m.SetColor("_EmissionColor", Hex.ToColor(lightHex).linear * power);
                if (map) m.SetTexture("_EmissionMap", map);
                mats[key] = m;
            }
            return m;
        }
        Material Glass(string hex, float alpha)
        {
            string key = "t" + hex + alpha;
            if (!mats.TryGetValue(key, out var m))
            {
                m = new Material(set.glass) { name = "bar glass " + hex };
                var c = Hex.ToColor(hex); c.a = alpha; m.SetColor("_BaseColor", c);
                mats[key] = m;
            }
            return m;
        }
        Material Surf(string surface, string hex)
        {
            var s = set.Get(surface);
            if (s == null || !s.material) return Gloss(hex, .5f);
            string key = "s" + surface + hex;
            if (!mats.TryGetValue(key, out var m))
            {
                m = new Material(s.material) { name = "bar " + surface + " " + hex };
                m.SetColor("_BaseColor", Hex.ToColor(hex)); m.SetFloat("_Smoothness", 1f); m.enableInstancing = true;
                mats[key] = m; mapped[m] = s;
            }
            return m;
        }
        Material brass, steel, clear, sheet;

        Mb Part(Material m) { if (!parts.TryGetValue(m, out var mb)) { mb = new Mb(); parts[m] = mb; } return mb; }

        MeshRenderer Piece(Transform root, string name, Mesh mesh, Material mat, bool shadows = true)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            mesh.RecalculateBounds(); mesh.UploadMeshData(true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows && mat.renderQueue < 3000 ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return mr;
        }

        // a round bar from a to b
        static void Tube(Mb m, Vector3 a, Vector3 b, float r, int sides = 8)
        {
            Vector3 ax = (b - a).normalized;
            Vector3 u = Vector3.Cross(ax, Mathf.Abs(ax.y) < .9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(ax, u);
            var ia = new int[sides + 1]; var ib = new int[sides + 1];
            for (int k = 0; k <= sides; k++)
            {
                float an = k * Mathf.PI * 2 / sides; Vector3 nr = u * Mathf.Cos(an) + w * Mathf.Sin(an);
                ia[k] = m.Vertex(a + nr * r, nr, Vector2.zero); ib[k] = m.Vertex(b + nr * r, nr, Vector2.zero);
            }
            for (int k = 0; k < sides; k++) m.Face(ia[k], ia[k + 1], ib[k + 1], ib[k]);
        }

        // a ball, turned on the lathe
        static void Ball(Mb m, Vector3 c, float r, int sides = 14, int rings = 8)
        {
            var p = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++) { float a = i * Mathf.PI / rings; p[i] = new Vector2(Mathf.Max(0, r * Mathf.Sin(a)), c.y - r * Mathf.Cos(a)); }
            m.Lathe(c.x, c.z, p, sides);
        }

        // the height of the roof's underside over a spot (the gable: 6.05 at the eaves, 8.65 under the ridge)
        static float RoofY(float z) => 6.05f + (9.8f - Mathf.Min(9.8f, Mathf.Abs(z))) / 9.8f * 2.6f;

        // ------------------------------------------------------------------ the fittings (none of this breaks)
        Transform fittings, looseRoot;
        static readonly (float, float)[] Bays = { (-25.14f, -20.26f), (-13.74f, -8.86f) };     // the lit panels either side of the mirror
        static readonly float[] ShelfY = { 2.72f, 3.41f, 4.10f };
        static readonly float[] Taps = { -21.3f, -12.7f };
        static readonly (float, float)[] RackX = { (-24.2f, -18.8f), (-15.2f, -9.8f) };
        const float RackY = 3.8f;
        static readonly float[] RackZ = { 5.15f, 5.55f };
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        void Make()
        {
            brass = Gloss("#c9a24e", .86f, 1f); steel = Gloss("#c8ccd2", .9f, 1f); clear = Glass("#eef8ff", .14f);
            sheet = new Material(set.gloss) { name = "bar labels" }; sheet.SetColor("_BaseColor", Color.white); sheet.SetFloat("_Smoothness", .45f);
            if (set.barLabels) sheet.SetTexture("_BaseMap", set.barLabels);
            fittings = new GameObject("Fittings").transform; fittings.SetParent(transform, false);
            looseRoot = new GameObject("Loose").transform; looseRoot.SetParent(transform, false);
            Counter(); BackBar(); Lamps(); Racks(); BuildLadder(); Collars(); Screens();
            foreach (var kv in parts)
            {
                var mesh = kv.Value.Build("tiki " + kv.Key.name);
                if (mapped.TryGetValue(kv.Key, out var s)) RailLook.WorldUV(mesh, Mathf.Max(.1f, s.metres), false, s.upright);
                Piece(fittings, kv.Key.name, mesh, kv.Key);
            }
            if (!Destruction.I) return;
            Protos(); Bottles(); CounterTop(); Tower(); Hanging(); Tables();
        }

        void Counter()
        {
            var teak = Surf("planks", "#5b3a22"); var oak = Surf("post", "#ead4aa"); var marble = Surf("marble", "#ece7dd");
            var led = Glow("#ffe2b0", "#ffbe6e", 3.5f);
            Part(teak).Box(V(-17, (F + 2f) / 2, 5.375f), V(16, 2f - F, .85f));
            var fl = Part(oak);                                                             // the fluted front
            for (float x = -24.95f; x < -9.02f; x += .1f) fl.Lathe(x, 4.95f, new[] { new Vector2(.048f, F + .14f), new Vector2(.048f, 1.955f) }, 5, 180, 360);
            var br = Part(brass);
            br.Box(V(-17, F + .07f, 4.915f), V(16.02f, .14f, .05f));                       // kick plate
            br.Box(V(-17, 1.975f, 4.925f), V(16.02f, .04f, .06f));                         // band under the top
            Part(led).Box(V(-17, 1.945f, 4.84f), V(16f, .02f, .03f));                      // light strip under the overhang
            Part(marble).Box(V(-17, 2.065f, 5.35f), V(16.5f, .13f, 1.38f), .025f, Quaternion.identity);
            foreach (float x in new[] { -25.03f, -8.97f }) Part(marble).Box(V(x, (F + 2f) / 2, 5.36f), V(.06f, 2f - F, .94f));
            Tube(br, V(-24.9f, F + .28f, 4.55f), V(-9.1f, F + .28f, 4.55f), .032f, 10);   // foot rail
            for (float x = -24.6f; x < -9.2f; x += 1.9f) Tube(br, V(x, F + .28f, 4.55f), V(x, F + .2f, 4.93f), .016f, 6);
            var black = Part(Gloss("#17181c", .7f));
            foreach (float tx in Taps)
            {   // a brass beer tower with three taps
                br.Lathe(tx, 5.6f, new[] { new Vector2(0, CT), new Vector2(.07f, CT), new Vector2(.07f, CT + .02f), new Vector2(.036f, CT + .045f), new Vector2(.036f, CT + .4f), new Vector2(.05f, CT + .42f), new Vector2(0, CT + .44f) }, 12);
                Tube(br, V(tx - .27f, CT + .4f, 5.6f), V(tx + .27f, CT + .4f, 5.6f), .032f, 10);
                foreach (float o in new[] { -.2f, 0f, .2f })
                {
                    Tube(br, V(tx + o, CT + .4f, 5.6f), V(tx + o, CT + .31f, 5.47f), .014f, 6);
                    black.Box(V(tx + o, CT + .52f, 5.59f), V(.03f, .17f, .03f), .008f, Quaternion.identity);
                }
            }
        }

        void BackBar()
        {
            var dark = Surf("planks", "#3f2716"); var marble = Surf("marble", "#ece7dd");
            var led = Glow("#ffe2b0", "#ffbe6e", 3.5f); var br = Part(brass);
            // the back counter (new, so it gets a collision box of its own)
            Part(dark).Box(V(-17, (F + 1.95f) / 2, 8.125f), V(16.4f, 1.95f - F, .55f));
            Part(marble).Box(V(-17, 1.99f, 8.1f), V(16.5f, .08f, .62f), .02f, Quaternion.identity);
            for (float x = -25.2f + 2.05f; x < -8.9f; x += 2.05f) br.Box(V(x, (F + 1.95f) / 2, 7.845f), V(.02f, 1f, .012f));
            br.Box(V(-17, 1.9f, 7.845f), V(16.4f, .02f, .012f));
            br.Box(V(-17, F + .05f, 7.84f), V(16.4f, .1f, .02f));
            BoxWorld.Boxes.Add(new Box { x0 = -25.2f, x1 = -8.8f, y0 = F, y1 = BT, z0 = 7.82f, z1 = 8.4f, wood = true });
            // lit onyx either side, a mirror in the middle with a dark board behind the neon sign above it
            var onyx = Glow("#f0c890", "#ffffff", .85f, set.onyx, .85f);
            foreach (var (x0, x1) in Bays)
            {
                var p = new Mb(); p.Box(V((x0 + x1) / 2, (BT + 4.9f) / 2, 8.375f), V(x1 - x0, 4.9f - BT, .04f));
                p.PlanarUV(Vector3.right, x0, x1, Vector3.up, BT, 4.9f);
                Piece(fittings, "lit onyx", p.Build("lit onyx"), onyx, false);
            }
            Part(Gloss("#c9b99c", .97f, 1f)).Box(V(-17, (BT + 3.58f) / 2, 8.375f), V(6.36f, 3.58f - BT, .03f));
            Part(Gloss("#161412", .55f)).Box(V(-17, (3.58f + 4.9f) / 2, 8.397f), V(6.36f, 1.32f, .016f));
            foreach (float x in new[] { -25.2f, -20.2f, -13.8f, -8.8f }) br.Box(V(x, (BT + 4.9f) / 2, 8.32f), V(.07f, 4.9f - BT, .07f));
            Part(dark).Box(V(-17, 4.97f, 8.22f), V(16.6f, .14f, .4f));
            br.Box(V(-17, 4.895f, 8.03f), V(16.6f, .025f, .03f));
            Part(led).Box(V(-17, 4.89f, 8.2f), V(16.4f, .015f, .04f));
            foreach (var (x0, x1) in Bays) foreach (float y in ShelfY) Shelf(x0 + .06f, x1 - .06f, y);
            Shelf(-20.1f, -13.9f, 2.8f);
        }

        void Shelf(float x0, float x1, float y)
        {
            Part(Glass("#dff3f0", .28f)).Box(V((x0 + x1) / 2, y - .0125f, 8.165f), V(x1 - x0, .025f, .34f));
            Part(Glow("#ffe2b0", "#ffbe6e", 3.5f)).Box(V((x0 + x1) / 2, y - .034f, 8.325f), V(x1 - x0, .014f, .02f));
            foreach (float x in new[] { x0 + .02f, x1 - .02f }) Part(brass).Box(V(x, y - .042f, 8.2f), V(.03f, .03f, .28f));
        }

        void Lamps()
        {
            var bulb = Glow("#fff3d6", "#ffc880", 9f); var globe = Glass("#fff6e6", .1f); var br = Part(brass);
            foreach (var (x, z) in new[] { (-24f, 2f), (-10f, 2f), (-24f, -4f), (-10f, -4f) })
            {   // glass globes where the block shades hung (the map's own lamps still light them)
                Ball(Part(globe), V(x, 3.95f, z), .32f);
                Ball(Part(bulb), V(x, 3.95f, z), .1f, 10, 6);
                br.Lathe(x, z, new[] { new Vector2(0, 4.2f), new Vector2(.09f, 4.21f), new Vector2(.09f, 4.27f), new Vector2(.03f, 4.33f), new Vector2(.012f, 4.33f) }, 10);
                Tube(br, V(x, 4.33f, z), V(x, RoofY(z) + .02f, z), .012f, 6);
            }
            var blade = Surf("post", "#6b4428");
            foreach (float x in new[] { -23.5f, -17f, -10.5f })
            {   // ceiling fans under the ridge
                Tube(br, V(x, 8.62f, 0), V(x, 6.3f, 0), .022f, 6);
                br.Lathe(x, 0, new[] { new Vector2(0, 6.08f), new Vector2(.1f, 6.1f), new Vector2(.14f, 6.18f), new Vector2(.14f, 6.26f), new Vector2(.05f, 6.32f), new Vector2(.022f, 6.32f) }, 12);
                var b = new Mb();
                for (int k = 0; k < 4; k++)
                {
                    var turn = Quaternion.Euler(0, k * 90, 0);
                    b.Box(turn * V(.7f, 0, 0), V(1.05f, .016f, .16f), .006f, turn * Quaternion.Euler(12, 0, 0));
                    b.Box(turn * V(.2f, 0, 0), V(.22f, .012f, .05f), 0, turn);
                }
                var mesh = b.Build("fan blades"); RailLook.WorldUV(mesh, 1.2f, false, false);
                var mr = Piece(fittings, "fan", mesh, blade, false); mr.transform.position = V(x, 6.14f, 0); fans.Add(mr.transform);
            }
            // real lamps: two over the counter for the glassware, a spot on the champagne tower, two warm ones for the lit stone
            AddLamp(V(-21.5f, 3.45f, 5.3f), new Color(1f, .9f, .76f), 1.3f, 5f, 0);
            AddLamp(V(-12.5f, 3.45f, 5.3f), new Color(1f, .9f, .76f), 1.3f, 5f, 0);
            AddLamp(V(-17f, 4.9f, 4.9f), new Color(1f, .95f, .86f), 5f, 5f, 55);
            AddLamp(V(-22.7f, 3.2f, 7.2f), new Color(1f, .72f, .42f), 2.2f, 5.5f, 0);
            AddLamp(V(-11.3f, 3.2f, 7.2f), new Color(1f, .72f, .42f), 2.2f, 5.5f, 0);
        }

        void AddLamp(Vector3 at, Color c, float power, float range, float spot)
        {
            var go = new GameObject("Bar lamp"); go.transform.SetParent(fittings, false); go.transform.position = at;
            var l = go.AddComponent<Light>(); l.color = c; l.intensity = power; l.range = range; l.shadows = LightShadows.None;
            if (spot > 0) { l.type = LightType.Spot; l.spotAngle = spot; l.innerSpotAngle = spot * .5f; go.transform.rotation = Quaternion.LookRotation(new Vector3(0, -1, .12f).normalized); }
            else l.type = LightType.Point;
        }

        void Racks()
        {
            var br = Part(brass);
            foreach (var (x0, x1) in RackX)
            {
                foreach (float z in RackZ) { Tube(br, V(x0, RackY, z - .028f), V(x1, RackY, z - .028f), .009f, 6); Tube(br, V(x0, RackY, z + .028f), V(x1, RackY, z + .028f), .009f, 6); }
                foreach (float z in new[] { 5.05f, 5.65f }) Tube(br, V(x0, RackY + .03f, z), V(x1, RackY + .03f, z), .014f, 6);
                for (float x = x0; x < x1 + .01f; x += (x1 - x0) / 4) Tube(br, V(x, RackY + .015f, 5.05f), V(x, RackY + .015f, 5.65f), .012f, 6);
                foreach (float x in new[] { x0 + .1f, x1 - .1f })
                    foreach (float z in new[] { 5.05f, 5.65f }) Tube(br, V(x, RackY + .03f, z), V(x, RoofY(z) + .02f, z), .011f, 6);
                Part(Glow("#ffe2b0", "#ffbe6e", 3.5f)).Box(V((x0 + x1) / 2, RackY + .05f, 5.35f), V(x1 - x0 - .2f, .016f, .03f));
            }
        }

        void BuildLadder()
        {
            var t = Part(Surf("post", "#b98a56")); var br = Part(brass); float top = ladderTop + 1.05f;
            foreach (float z in new[] { -.43f, .43f })
            {
                t.Box(V(LadderX, (.36f + top) / 2, z), V(.07f, top - .36f, .05f), .012f, Quaternion.identity);
                foreach (float y in new[] { 6.5f, ladderTop - .35f }) br.Box(V(LadderX - .26f, y, z), V(.5f, .035f, .035f));   // stays to the gable
                Ball(br, V(LadderX, top + .03f, z), .05f, 8, 5);
            }
            for (float y = .74f; y < ladderTop + .2f; y += .34f) Tube(br, V(LadderX, y, -.43f), V(LadderX, y, .43f), .02f, 8);
        }

        void Collars()
        {   // brass bands at the foot and the head of every bamboo post
            foreach (var b in BoxWorld.Boxes)
                if (b.wood && Mathf.Abs(b.x1 - b.x0 - .56f) < .03f && Mathf.Abs(b.z1 - b.z0 - .56f) < .03f && Mathf.Abs(b.y0 - F) < .03f && b.y1 > 6f && b.x0 > -31f && b.x1 < -3f && Mathf.Abs(b.z0) < 9.5f)
                {
                    float cx = (b.x0 + b.x1) / 2, cz = (b.z0 + b.z1) / 2;
                    Part(brass).Box(V(cx, F + .08f, cz), V(.62f, .16f, .62f), .012f, Quaternion.identity);
                    Part(brass).Box(V(cx, 5.9f, cz), V(.62f, .1f, .62f), .012f, Quaternion.identity);
                }
        }

        void Screens()
        {   // frameless glass at both ends of the bartender's aisle: they break like the map's other glass
            if (!Destruction.I) return;
            // the same glass as the map's own panes, so these read like the rest of the beach club's glazing
            Material pane = null;
            foreach (var e in Destruction.I.ents) if (e != null && e.d != null && e.d.kind == "glass" && e.rends.Count > 0 && e.rends[0] && e.rends[0].sharedMaterial) { pane = e.rends[0].sharedMaterial; break; }
            if (!pane) pane = Glass("#cfeeff", .12f);
            foreach (float x in new[] { -29.6f, -4.4f })
            {
                Part(brass).Box(V(x, F + .04f, 4.325f), V(.1f, .08f, 7.75f));
                for (int k = 0; k < 4; k++)
                {
                    float z0 = .45f + k * 1.95f, z1 = z0 + 1.9f, y0 = F + .06f, y1 = 3.2f;
                    int bi = BoxWorld.Boxes.Count;
                    BoxWorld.Boxes.Add(new Box { x0 = x - .04f, x1 = x + .04f, y0 = y0, y1 = y1, z0 = z0, z1 = z1, glass = true, prop = true });
                    var mb = new Mb(); mb.Box(Vector3.zero, V(.05f, y1 - y0, 1.9f), .008f, Quaternion.identity);
                    var mr = Piece(fittings, "glass screen", mb.Build("glass screen"), pane, false);
                    mr.transform.position = V(x, (y0 + y1) / 2, (z0 + z1) / 2);
                    var d = new WDyn { id = 9000 + serial++, kind = "glass", prop = "glass", snd = "frost", color = "#bfe6ff", hp = 1, heavy = 1, boxes = new[] { bi } };
                    screens.Add(Destruction.I.Add(d, mr.transform)); panes++;
                }
            }
        }

        // ------------------------------------------------------------------ loose things (they all join Destruction)
        class Proto { public string name, color; public Mesh mesh; public Material[] mats; public float w, h, brk, heavy; public bool glass; }
        readonly Dictionary<string, Proto> protos = new Dictionary<string, Proto>();
        Proto P(string name) => protos[name];

        // a lathe profile from (radius, height) pairs, moved down so the thing's middle is at its own origin
        static Vector2[] Pr(float h, params float[] a)
        {
            var p = new Vector2[a.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new Vector2(a[i * 2] * PS, (a[i * 2 + 1] - h / 2) * PS);
            return p;
        }
        // Bottles, glasses and the like are drawn at life size and built a quarter bigger, as the fighters are (2.25 m tall)
        const float Small = 1.25f;
        static float PS = 1f;

        // one mesh with a sub-mesh per material
        Proto New(string name, float w, float h, string color, bool glass, float brk, float heavy, params (Mb mb, Material mat)[] bits)
        {
            var ci = new CombineInstance[bits.Length]; var ms = new Material[bits.Length];
            for (int i = 0; i < bits.Length; i++) { ci[i] = new CombineInstance { mesh = bits[i].mb.Build(name), transform = Matrix4x4.identity }; ms[i] = bits[i].mat; }
            var mesh = new Mesh { name = name }; mesh.CombineMeshes(ci, false, false);
            foreach (var c in ci) Destroy(c.mesh);
            mesh.RecalculateBounds(); mesh.UploadMeshData(true);
            var p = new Proto { name = name, color = color, mesh = mesh, mats = ms, w = w, h = h, brk = brk, heavy = heavy, glass = glass };
            protos[name] = p;
            return p;
        }

        // stands on `foot` (hang: hangs from it, upside down)
        Destruction.Ent Put(Proto p, Vector3 foot, float yaw = 0, bool hang = false, float tilt = 0)
        {
            var go = new GameObject(p.name); go.transform.SetParent(looseRoot, false);
            go.transform.position = hang ? foot - Vector3.up * (p.h / 2) : foot + Vector3.up * (p.h / 2);
            go.transform.rotation = Quaternion.Euler(hang ? 180 : tilt, yaw, 0);
            go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = p.mats; mr.shadowCastingMode = ShadowCastingMode.Off;
            var d = new WDyn { id = 9000 + serial++, kind = "knock", prop = p.glass ? "glass" : "", snd = p.glass ? "none" : "", color = p.color, kw = p.w, kh = p.h, kd = p.w, brk = p.brk, heavy = p.heavy, hp = 1 };
            var e = Destruction.I.Add(d, go.transform);
            loose.Add(e);
            return e;
        }

        // glasses: (radius, height) up the outside and back down the inside, then the drink in it
        static readonly Dictionary<string, (float h, float[] shell, float[] drink)> Glasses = new Dictionary<string, (float, float[], float[])>
        {
            { "coupe", (.195f, new[] { 0f, 0f, .045f, 0f, .045f, .006f, .008f, .016f, .008f, .11f, .03f, .125f, .06f, .15f, .078f, .19f, .078f, .195f, .074f, .195f, .057f, .153f, .028f, .13f, 0f, .127f },
                               new[] { 0f, .132f, .028f, .134f, .056f, .156f, .068f, .18f, 0f, .18f }) },
            { "martini", (.24f, new[] { 0f, 0f, .045f, 0f, .045f, .006f, .007f, .014f, .007f, .13f, .09f, .235f, .09f, .24f, .086f, .24f, .006f, .138f, 0f, .138f },
                                new[] { 0f, .142f, .068f, .218f, 0f, .218f }) },
            { "highball", (.195f, new[] { 0f, 0f, .04f, 0f, .043f, .19f, .043f, .195f, .039f, .195f, .036f, .012f, 0f, .012f },
                                  new[] { 0f, .014f, .0355f, .014f, .038f, .16f, 0f, .16f }) },
            { "tumbler", (.115f, new[] { 0f, 0f, .046f, 0f, .05f, .11f, .05f, .115f, .046f, .115f, .042f, .015f, 0f, .015f },
                                 new[] { 0f, .017f, .0415f, .017f, .044f, .06f, 0f, .06f }) },
            { "wine", (.25f, new[] { 0f, 0f, .042f, 0f, .042f, .005f, .007f, .014f, .007f, .11f, .035f, .135f, .05f, .17f, .048f, .21f, .04f, .245f, .04f, .25f, .037f, .25f, .045f, .21f, .047f, .17f, .033f, .138f, 0f, .125f },
                             new[] { 0f, .128f, .032f, .14f, .046f, .17f, .0455f, .19f, 0f, .19f }) },
            { "hurricane", (.235f, new[] { 0f, 0f, .04f, 0f, .04f, .006f, .01f, .016f, .01f, .045f, .035f, .07f, .048f, .11f, .036f, .17f, .046f, .23f, .046f, .235f, .042f, .235f, .032f, .17f, .044f, .11f, .031f, .073f, 0f, .06f },
                                   new[] { 0f, .063f, .03f, .076f, .043f, .11f, .0315f, .168f, .037f, .2f, 0f, .2f }) },
        };

        Proto G(string kind, string drinkHex)
        {
            string key = kind + (drinkHex ?? "");
            if (protos.TryGetValue(key, out var p)) return p;
            PS = Small;
            var def = Glasses[kind]; var g = new Mb(); g.Lathe(0, 0, Pr(def.h, def.shell), 10);
            float w = 0; for (int i = 0; i < def.shell.Length; i += 2) w = Mathf.Max(w, def.shell[i] * 2);
            if (drinkHex == null) return New(key, w * Small, def.h * Small, "#e8f6ff", true, 1, .6f, (g, clear));
            var l = new Mb(); l.Lathe(0, 0, Pr(def.h, def.drink), 10);
            return New(key, w * Small, def.h * Small, drinkHex, true, 1, .6f, (g, clear), (l, Glow(drinkHex, drinkHex, .45f, null, .9f)));
        }

        // bottles, bottom to top: wine or champagne, spirit, squat, tall, and a decanter with its stopper
        static readonly float[][] BottleShapes =
        {
            new[] { 0f, 0f, .045f, 0f, .047f, .012f, .047f, .2f, .034f, .255f, .016f, .285f, .0155f, .355f, .019f, .36f, .019f, .375f, 0f, .375f },
            new[] { 0f, 0f, .05f, 0f, .052f, .01f, .052f, .2f, .045f, .222f, .02f, .235f, .019f, .325f, .023f, .33f, .023f, .345f, 0f, .345f },
            new[] { 0f, 0f, .066f, 0f, .07f, .015f, .07f, .125f, .058f, .165f, .03f, .195f, .021f, .205f, .021f, .25f, .026f, .255f, .026f, .27f, 0f, .27f },
            new[] { 0f, 0f, .036f, 0f, .038f, .01f, .038f, .27f, .03f, .31f, .015f, .335f, .015f, .415f, .018f, .42f, .018f, .432f, 0f, .432f },
            new[] { 0f, 0f, .04f, 0f, .075f, .07f, .078f, .11f, .06f, .16f, .024f, .205f, .022f, .265f, .03f, .27f, .036f, .3f, .028f, .33f, 0f, .335f },
        };
        // each bottle's label: from, to, the body's radius there; then where its cap starts (no label: radius 0)
        static readonly float[][] BottleBits = { new[] { .07f, .17f, .047f, .29f }, new[] { .06f, .17f, .052f, .265f }, new[] { .035f, .11f, .07f, .215f }, new[] { .09f, .21f, .038f, .35f }, new[] { 0f, 0f, 0f, 0f } };
        static readonly (string hex, float a)[] Tints = { ("#1f5a2c", .82f), ("#8a4612", .8f), ("#eaf6f6", .25f), ("#1f5aa0", .72f), ("#26262c", .85f), ("#c8566a", .65f), ("#178a86", .7f), ("#d9a83a", .6f), ("#8a4612", .8f), ("#1f5a2c", .82f) };

        Proto Bottle(int shape, int label, string glassHex, float alpha)
        {
            string key = "bottle" + shape + "/" + label + glassHex;
            if (protos.TryGetValue(key, out var p)) return p;
            var a = BottleShapes[shape]; float h = a[a.Length - 1], w = 0;
            for (int i = 0; i < a.Length; i += 2) w = Mathf.Max(w, a[i] * 2);
            PS = Small;
            var g = new Mb(); g.Lathe(0, 0, Pr(h, a), 10);
            var bits = BottleBits[shape];
            if (bits[2] <= 0) return New(key, w * Small, h * Small, glassHex, true, 1, .7f, (g, Glass(glassHex, alpha)));
            // the label, a third of the way round the front, and the cap: both on the label sheet
            var s = new Mb(); const int seg = 6; int col = label % 4, row = label / 4; float r = bits[2] + .0015f;
            var lo = new int[seg + 1]; var hi = new int[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg, an = Mathf.Lerp(215, 325, t) * Mathf.Deg2Rad; var o = new Vector3(Mathf.Cos(an), 0, Mathf.Sin(an));
                float u = (col + .03f + .94f * t) / 4f;
                lo[i] = s.Vertex((o * r + Vector3.up * (bits[0] - h / 2)) * Small, o, new Vector2(u, 1 - (row + .97f) / 4f));
                hi[i] = s.Vertex((o * r + Vector3.up * (bits[1] - h / 2)) * Small, o, new Vector2(u, 1 - (row + .03f) / 4f));
            }
            for (int i = 0; i < seg; i++) s.Face(lo[i], lo[i + 1], hi[i + 1], hi[i]);
            int first = s.v.Count; float topR = a[a.Length - 4] + .003f;
            s.Lathe(0, 0, Pr(h, topR, bits[3], topR + .001f, h + .004f, 0f, h + .004f), 8);
            var swatch = new Vector2(((shape == 0 ? 0 : label % 4) + .5f) / 4f, .125f);
            for (int i = first; i < s.v.Count; i++) s.uv[i] = swatch;
            return New(key, w * Small, h * Small, glassHex, true, 1, .7f, (g, Glass(glassHex, alpha)), (s, sheet));
        }

        void Protos()
        {
            PS = Small;
            var shaker = new Mb(); shaker.Lathe(0, 0, Pr(.27f, 0f, 0f, .04f, 0f, .048f, .17f, .045f, .18f, .04f, .19f, .03f, .235f, .022f, .24f, .022f, .27f, 0f, .27f), 10);
            New("shaker", .096f * Small, .27f * Small, "#c8ccd2", false, 0, 1f, (shaker, steel));

            var bucket = new Mb(); bucket.Lathe(0, 0, Pr(.21f, 0f, 0f, .08f, 0f, .11f, .2f, .115f, .2f, .115f, .21f, .105f, .21f, .076f, .012f, 0f, .012f), 12);
            var ice = new Mb(); ice.Lathe(0, 0, Pr(.21f, .096f, .15f, 0f, .15f), 12);
            for (int i = 0; i < 9; i++) ice.Box(V(R(-.06f, .06f), .15f - .105f + R(0, .02f), R(-.06f, .06f)) * Small, Vector3.one * R(.035f, .05f), .006f, Quaternion.Euler(R(0, 90), R(0, 90), R(0, 90)));
            New("bucket", .23f * Small, .21f * Small, "#c8ccd2", false, 0, 1.5f, (bucket, steel), (ice, Gloss("#eaf6ff", .95f)));

            const float lh = .225f;
            var lb = new Mb(); lb.Lathe(0, 0, Pr(lh, 0f, 0f, .07f, 0f, .07f, .02f, .06f, .03f, 0f, .03f), 12); lb.Lathe(0, 0, Pr(lh, .058f, .205f, .066f, .205f, .066f, .222f, .058f, .222f), 12);
            var lg = new Mb(); lg.Lathe(0, 0, Pr(lh, .062f, .03f, .062f, .21f, .058f, .21f, .058f, .03f), 12);
            var lc = new Mb(); lc.Lathe(0, 0, Pr(lh, .028f, .03f, .028f, .115f, 0f, .115f), 8);
            var lf = new Mb(); lf.Lathe(0, 0, Pr(lh, 0f, .122f, .012f, .134f, .014f, .15f, .006f, .175f, 0f, .19f), 6);
            New("lantern", .14f * Small, lh * Small, "#ffd9a0", true, 1, .8f, (lb, brass), (lg, clear), (lc, Gloss("#f4ead2", .4f)), (lf, Glow("#ffd9a0", "#ffb060", 8f)));

            string[] glaze = { "#1f9a9a", "#e8763a", "#f1e6c8" };
            for (int i = 0; i < glaze.Length; i++)
            {
                var mug = new Mb(); mug.Lathe(0, 0, Pr(.2f, 0f, 0f, .045f, 0f, .05f, .04f, .042f, .08f, .052f, .12f, .045f, .16f, .05f, .19f, .05f, .2f, .044f, .2f, .04f, .03f, 0f, .03f), 10);
                New("mug" + i, .104f * Small, .2f * Small, glaze[i], false, 25, .8f, (mug, Gloss(glaze[i], .78f)));
            }

            PS = 1;
            const float sh = .865f;
            var frame = new Mb();
            foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f }) Tube(frame, V(sx * .13f, .78f - sh / 2, sz * .13f), V(sx * .2f, -sh / 2, sz * .2f), .014f, 6);
            frame.Lathe(0, 0, Pr(sh, .235f, .29f, .255f, .29f, .255f, .31f, .235f, .31f, .235f, .29f), 14);
            var seat = new Mb(); seat.Lathe(0, 0, Pr(sh, 0f, .78f, .17f, .78f, .2f, .8f, .2f, .84f, .17f, .865f, 0f, .865f), 14);
            New("stool", .44f, sh, "#e9dcc0", false, 0, 2f, (frame, brass), (seat, Gloss("#efe6d2", .55f)));

            const float th = 1.36f;
            var ped = new Mb(); ped.Lathe(0, 0, Pr(th, 0f, 0f, .25f, 0f, .25f, .02f, .05f, .05f, .03f, .1f, .03f, 1.28f, .1f, 1.325f, 0f, 1.325f), 14);
            ped.Lathe(0, 0, Pr(th, .45f, 1.322f, .463f, 1.322f, .463f, 1.36f, .45f, 1.36f), 20);
            var topGlass = new Mb(); topGlass.Lathe(0, 0, Pr(th, 0f, 1.33f, .45f, 1.33f, .45f, 1.355f, 0f, 1.355f), 20);
            New("table", .92f, th, "#c9a24e", false, 0, 3f, (ped, brass), (topGlass, Glass("#bfe6e0", .3f)));
        }

        void Bottles()
        {
            float[] rows = { BT, ShelfY[0], ShelfY[1], ShelfY[2] };
            foreach (var (x0, x1) in Bays)
                foreach (float y in rows)
                    for (float x = x0 + .28f; x < x1 - .22f; x += R(.33f, .38f))
                    {
                        int shape = rng.Next(4), label = rng.Next(12); var tint = Tints[rng.Next(Tints.Length)];
                        Put(Bottle(shape, label, tint.hex, tint.a), V(x, y, 8.16f + R(-.03f, .03f)), R(-18, 18));
                    }
            // the middle: decanters on the back counter, cocktail glasses on the shelf over them
            for (float x = -19.7f; x < -14.2f; x += .56f)
            {
                bool amber = rng.Next(2) == 0;
                Put(Bottle(4, 0, amber ? "#a8601c" : "#e0b040", amber ? .75f : .6f), V(x, BT, 8.14f), R(0, 360));
            }
            for (float x = -19.8f; x < -14.1f; x += .47f) Put(G(rng.Next(2) == 0 ? "martini" : "coupe", null), V(x, 2.8f, 8.16f));
        }

        void Bucket(Vector3 at)
        {
            var b = Put(P("bucket"), at, R(0, 360));
            var bottle = Put(Bottle(0, 10, "#2f6b3a", .8f), at + Vector3.up * .025f, R(0, 360), false, 15);
            stacks.Add((bottle, new[] { b }));
        }

        void CounterTop()
        {
            int k = 0;
            for (float x = -24.6f; x < -9.3f; x += .78f, k++)
            {
                if (Mathf.Abs(x + 17f) < 1.75f) continue;                                  // the champagne tower's stretch
                bool tap = false; foreach (float tx in Taps) if (Mathf.Abs(x - tx) < .5f) tap = true;
                if (tap) continue;
                float zf = R(5.02f, 5.2f), zb = R(5.5f, 5.72f);
                switch (k % 6)
                {
                    case 0: Put(G("martini", "#ff7fa8"), V(x - .13f, CT, zf)); Put(G("coupe", "#f3d98a"), V(x + .17f, CT, zb)); break;
                    case 1: Put(P("shaker"), V(x, CT, zb)); Put(G("tumbler", "#c9781e"), V(x + .05f, CT, zf)); break;
                    case 2: Put(G("hurricane", "#ff7a2a"), V(x - .08f, CT, zf)); Put(G("highball", "#2f9fe0"), V(x + .12f, CT, zb)); break;
                    case 3: Put(P("lantern"), V(x, CT, 5.35f)); break;
                    case 4: Put(P("mug" + (k / 6 % 3)), V(x - .1f, CT, zb)); Put(G("highball", "#9fdc6a"), V(x + .1f, CT, zf)); break;
                    default: Bucket(V(x, CT, 5.45f)); Put(G("coupe", "#f3d98a"), V(x + .27f, CT, 5.08f)); break;
                }
            }
        }

        void Tower()
        {   // four tiers of champagne coupes on a brass tray, in the middle of the counter under the sign
            var tray = new Mb(); tray.Lathe(-17, 5.35f, new[] { new Vector2(0, CT), new Vector2(.6f, CT), new Vector2(.6f, CT + .014f), new Vector2(0, CT + .014f) }, 24);
            Piece(fittings, "tray", tray.Build("tray"), brass, false);
            var coupe = G("coupe", "#f3d98a"); const float step = .165f * Small; var c = V(-17, CT + .014f, 5.35f);
            Destruction.Ent[,] below = null;
            for (int tier = 0; tier < 4; tier++)
            {
                int m = 4 - tier; var row = new Destruction.Ent[m, m];
                for (int i = 0; i < m; i++)
                    for (int j = 0; j < m; j++)
                    {
                        var e = Put(coupe, c + V((i - (m - 1) / 2f) * step, tier * .193f * Small, (j - (m - 1) / 2f) * step));
                        row[i, j] = e;
                        if (below != null) stacks.Add((e, new[] { below[i, j], below[i + 1, j], below[i, j + 1], below[i + 1, j + 1] }));
                    }
                below = row;
            }
            Bucket(V(-18.1f, CT, 5.4f)); Bucket(V(-15.9f, CT, 5.4f));
        }

        void Hanging()
        {
            var g = G("wine", null);
            foreach (var (x0, x1) in RackX)
                foreach (float z in RackZ)
                    for (float x = x0 + .15f; x < x1 - .1f; x += .2f) Put(g, V(x, RackY + .012f, z), 0, true);
        }

        void Tables()
        {
            var spots = new[] { V(-26.5f, F, -5.5f), V(-21.5f, F, -6.8f), V(-12.5f, F, -6.8f), V(-7.5f, F, -5.5f), V(-27f, F, 1f), V(-7f, F, 1f) };
            string[] kinds = { "martini", "coupe", "hurricane", "highball", "highball", "tumbler" };
            string[] drinks = { "#ff7fa8", "#f3d98a", "#ff7a2a", "#2f9fe0", "#9fdc6a", "#c9781e" };
            foreach (var spot in spots)
            {
                var on = new[] { Put(P("table"), spot, R(0, 360)) };
                Vector3 top = spot + Vector3.up * 1.356f;
                stacks.Add((Put(P("lantern"), top), on));
                int n = 2 + rng.Next(2); float a0 = R(0, 6.28f);
                for (int i = 0; i < n; i++)
                {
                    int d = rng.Next(kinds.Length); float a = a0 + i * 6.28f / n;
                    stacks.Add((Put(G(kinds[d], drinks[d]), top + V(Mathf.Cos(a) * .3f, 0, Mathf.Sin(a) * .3f)), on));
                }
                float sa = R(0, 6.28f);
                for (int i = 0; i < 2; i++) Put(P("stool"), spot + V(Mathf.Cos(sa + i * 2.6f) * .85f, 0, Mathf.Sin(sa + i * 2.6f) * .85f), R(0, 360));
            }
            for (float x = -24.2f; x < -9.5f; x += 1.45f) Put(P("stool"), V(x, F, 4.15f), R(0, 360));
            // nothing this small needs drawing from across the beach
            var rends = looseRoot.GetComponentsInChildren<Renderer>();
            var lod = looseRoot.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.16f, rends) }); lod.RecalculateBounds();
        }

        // ------------------------------------------------------------------ while playing
        float pollT;
        void Update()
        {
            foreach (var f in fans) if (f) f.Rotate(0, 150f * Time.deltaTime, 0, Space.World);
            if ((pollT -= Time.deltaTime) > 0) return;
            pollT = .12f;
            // whatever stood on something that has been knocked away or smashed comes down too (the tower, drinks on a table)
            foreach (var (item, on) in stacks)
            {
                if (item == null || !item.alive || item.kst != 0) continue;
                bool held = true;
                foreach (var s in on) if (s == null || !s.alive || s.kst != 0) { held = false; break; }
                if (!held) Destruction.KnockRay(item.t.position + Vector3.up * .3f, Vector3.down, .6f, 0);
            }
        }

        // ------------------------------------------------------------------ remote test
        public static string Test(string arg)
        {
            var a = (arg ?? "").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (a.Length > 0 && a[0] == "old")
            {
                PlayerPrefs.SetInt(OldKey, a.Length > 1 && a[1] == "on" ? 1 : 0); PlayerPrefs.Save();
                return "old bar " + (Old ? "on" : "off") + " from the next start of the map";
            }
            var bar = FindAnyObjectByType<TikiBar>();
            if (!bar) return "no Tiki Bar (not on Beach Please, or the old bar is on)";
            float Fl(int i) => i < a.Length && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            switch (a.Length > 0 ? a[0] : "status")
            {
                case "blast": Destruction.Blast(new Vector3(Fl(1), Fl(2), Fl(3)), Fl(4) > 0 ? Fl(4) : 3f); return bar.Status();
                case "ray": Destruction.KnockRay(new Vector3(Fl(1), Fl(2), Fl(3)), new Vector3(Fl(4), Fl(5), Fl(6)).normalized, 40, 40); return bar.Status();
                case "climb": bar.StartCoroutine(bar.Climb()); return "climbing: see Logs/tiki-climb.txt";
                default: return bar.Status();
            }
        }

        string Status()
        {
            int whole = 0, home = 0, glass = 0; string moved = "";
            foreach (var e in loose) { if (e.alive) whole++; if (e.alive && e.kst == 0) home++; else if (moved.Length < 160) moved += " " + e.t.name + (e.alive ? "" : "(broken)"); }
            foreach (var e in screens) if (e.alive) glass++;
            return loose.Count + " loose things, " + whole + " whole, " + home + " still in place; " + glass + " of " + panes + " screens whole; ladder to " + ladderTop.ToString("0.00") + (loose.Count - home <= 12 ? "; moved:" + moved : "");
        }

        // walks the player up the ladder and on along the ridge, noting where they are four times a second
        System.Collections.IEnumerator Climb()
        {
            var rm = RoundManager.Instance; var f = rm ? rm.player : null;
            Ladder L = null;
            foreach (var l in BoxWorld.Ladders) if (Mathf.Abs(l.rung - (LadderX - .04f)) < .01f) L = l;
            if (!f || L == null) yield break;
            var pc = f.GetComponent<PlayerController>(); if (pc) pc.enabled = false;
            f.Place(new Vector3(L.rung + .9f, L.y0 + .02f, 0), Mathf.Atan2(L.nx, L.nz));
            var log = new System.Text.StringBuilder(); float next = 0;
            for (float t = 0; t < 4.5f; t += Time.deltaTime)
            {
                f.Frozen = false; f.inFwd = 1; f.inSide = 0; f.yaw = Mathf.Atan2(L.nx, L.nz);
                if (t >= next) { log.AppendLine(t.ToString("0.00") + "s  x " + f.pos.x.ToString("0.00") + " y " + f.pos.y.ToString("0.00") + " z " + f.pos.z.ToString("0.00") + (f.onLadder != null ? "  on the ladder" : f.grounded ? "  on the ground" : "  in the air")); next += .25f; }
                yield return null;
            }
            f.inFwd = 0; if (pc) pc.enabled = true;
#if UNITY_EDITOR
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs", "tiki-climb.txt"), log.ToString());
#endif
        }
    }
}
