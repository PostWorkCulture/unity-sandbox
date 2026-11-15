// The Backyard's look (MapLooks): the grey-box garden made into somebody's well-kept back garden.
//  - Surfaces: lawn, paving on the patio, boarded fence and shed, felt on the shed roof, timber steps, a leafy hedge.
//  - Fittings: the plain blocks (trampoline, barbecue, cool box, paddling pool, the bench in the shed) are rebuilt as the
//    things themselves in the same places and sizes; their collision boxes are the map's own, untouched. Added: fence posts
//    and rails, shelves in the shed and on the fences, a string of lights over the patio, stepping stones.
//  - Things to smash: flower pots and gnomes round the garden, bottles and cans lined up on the fence, the picnic table
//    laid for lunch, drinks on the patio and by the pool, tins and pots in the shed. All loose; they join Destruction
//    through the kit.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    using Mb = MapProps.Mb;

    public static class YardLook
    {
        public static readonly MapLook Def = new MapLook
        {
            id = "yard", set = "YardLook", stem = "YardLight",
            rules = new Dictionary<string, string>
            {
                { "5fb24b+", "grass" }, { "6cc152+", "grass" },
                { "d5d6da+", "slabs" },
                { "b07a4a", "boards" }, { "8a5a34", "boards" },
                { "5b4a3a", "roofing" },
                { "7a4c2a", "post" },
                { "3f8f3a+", "leaf" },
            },
            tint = new Dictionary<string, string> { { "8a5a34", "#b98a5c" }, { "5b4a3a", "#4a4d52" } },   // a lighter shed under a grey felt roof
            // the blocks that are rebuilt: trampoline, barbecue, shed bench, cool box, pool
            gone = new[]
            {
                MapLooks.Box(4.9f, 0, 4.9f, 9.1f, 1.3f, 9.1f), MapLooks.Box(-7.8f, 0, 5.4f, -6.2f, 1.5f, 6.6f),
                MapLooks.Box(-10.7f, 0, -11.15f, -8.9f, 1.1f, -10.05f), MapLooks.Box(-4.7f, 0, -13.1f, -3.7f, .6f, -12.1f),
                MapLooks.Box(4.4f, 0, -11.5f, 9.6f, .6f, -6.5f),
            },
            strength = 1f,
            probeCentre = new Vector3(0, 2.5f, 0), probeSize = new Vector3(30, 7, 38),
            cloudCover = .45f,
            dress = Dress,
            dressBake = (root, set) => Fittings(root, true),
        };

        static void Dress(MapWorld w, RailLookSet set)
        {
            var k = Fittings(w.transform, false);
            if (!k) return;
            Protos(k); Loose(k);
            MapLooks.Tables(w, k.Surf("post", "#b98a56"));
            k.Lod(.08f);
            Debug.Log("Backyard: " + k.Count + " loose things");
        }

        /// everything that stays put (for the bake: the same, under the bake's root, without the water)
        static TikiBar Fittings(Transform parent, bool baking)
        {
            var k = baking ? TikiBar.Kit(parent, "Yard fittings (bake)", true) : TikiBar.Kit(parent, "Yard fittings");
            if (!k) return null;
            Fence(k); Trampoline(k); Bbq(k); CoolBox(k); Pool(k, baking); Shed(k); Shelves(k); Stones(k);
            // a string of lights over the patio, on four posts
            var post = k.Part(k.Surf("post", "#7a5232")); var wire = k.Part(k.Gloss("#1b1c20", .3f)); var bulb = k.Part(k.Glow("#fff1c9", "#ffc56e", 2.4f));
            var tops = new[] { new Vector3(-3.1f, 3.3f, 10.4f), new Vector3(3.1f, 3.3f, 10.4f), new Vector3(3.1f, 3.3f, 16.6f), new Vector3(-3.1f, 3.3f, 16.6f) };
            foreach (var t in tops) TikiBar.Tube(post, new Vector3(t.x, 0, t.z), t + Vector3.up * .08f, .055f, 8);
            for (int i = 0; i < 4; i++) Festoon(tops[i], tops[(i + 1) % 4], .45f, wire, bulb);
            Festoon(tops[0], tops[2], .7f, wire, bulb); Festoon(tops[1], tops[3], .7f, wire, bulb);
            // a bulb on a flex in the shed (its ceiling is at 3.4)
            TikiBar.Tube(wire, new Vector3(-8f, 3.4f, -9f), new Vector3(-8f, 2.95f, -9f), .008f, 4); TikiBar.Ball(bulb, new Vector3(-8f, 2.88f, -9f), .075f, 10, 6);
            if (!baking) k.AddLamp(new Vector3(-8f, 2.8f, -9f), new Color(1f, .86f, .66f), 2.6f, 6.5f, 0);
            k.Flush();
            return k;
        }

        static void Festoon(Vector3 a, Vector3 b, float sag, Mb wire, Mb bulb)
        {
            int n = Mathf.Max(4, Mathf.RoundToInt(Vector3.Distance(a, b) / .55f)); var last = a;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n; var p = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4 * t * (1 - t));
                TikiBar.Tube(wire, last, p, .007f, 4); last = p;
                if (i < n) TikiBar.Ball(bulb, p + Vector3.down * .06f, .05f, 8, 5);
            }
        }

        // posts, two rails and a capping on the inside of the fence (its inner faces are at x 12.875 and z 16.875, 2.4 high)
        static void Fence(TikiBar k)
        {
            var m = k.Part(k.Surf("post", "#8f5f38")); const float X = 12.875f, Z = 16.875f, H = 2.4f;
            for (int s = -1; s <= 1; s += 2)
            {
                for (float z = -Z + 1.1f; z < Z; z += 2.25f) m.Box(new Vector3(s * (X - .05f), H / 2 + .02f, z), new Vector3(.1f, H + .04f, .13f));
                for (float x = -X + 1.2f; x < X; x += 2.3f) m.Box(new Vector3(x, H / 2 + .02f, s * (Z - .05f)), new Vector3(.13f, H + .04f, .1f));
                foreach (float y in new[] { .45f, 1.95f })
                {
                    m.Box(new Vector3(s * (X - .03f), y, 0), new Vector3(.06f, .11f, 2 * Z));
                    m.Box(new Vector3(0, y, s * (Z - .03f)), new Vector3(2 * X, .11f, .06f));
                }
                m.Box(new Vector3(s * (X + .09f), H + .03f, 0), new Vector3(.4f, .06f, 2 * Z + .5f));
                m.Box(new Vector3(0, H + .035f, s * (Z + .09f)), new Vector3(2 * X + .5f, .06f, .4f));
            }
        }

        // the trampoline (4 m square, bed at 1.1): steel frame and legs, a blue pad round the edge, the black bed
        static void Trampoline(TikiBar k)
        {
            var st = k.Part(k.steel); var pad = k.Part(k.Gloss("#2f6fd0", .45f)); var bed = k.Part(k.Gloss("#15161a", .25f));
            var c = new Vector3(7, 0, 7); const float r = 2f, y = 1.1f;
            for (int i = 0; i < 4; i++)
            {
                var q = Quaternion.Euler(0, i * 90, 0);
                TikiBar.Tube(st, c + q * new Vector3(-r + .08f, y - .06f, r - .08f), c + q * new Vector3(r - .08f, y - .06f, r - .08f), .045f, 8);
                foreach (float s in new[] { -1.1f, 1.1f }) TikiBar.Tube(st, c + q * new Vector3(s, .03f, r - .08f), c + q * new Vector3(s, y - .06f, r - .08f), .04f, 8);
                TikiBar.Tube(st, c + q * new Vector3(-1.4f, .04f, r - .08f), c + q * new Vector3(1.4f, .04f, r - .08f), .04f, 8);
                bool along = i % 2 == 0;
                pad.Box(c + q * new Vector3(0, y + .04f, r - .25f), along ? new Vector3(2 * r, .09f, .5f) : new Vector3(.5f, .09f, 2 * r - 1f), .03f, Quaternion.identity);
            }
            bed.Box(c + Vector3.up * (y + .03f), new Vector3(2 * r - .9f, .03f, 2 * r - .9f));
        }

        // a gas barbecue facing the lawn (the block was 1.2 x 1.0, 1.1 high with a lid to 1.4): cabinet, firebox, lid, shelves
        const float BbqShelf = 1.065f;
        static void Bbq(TikiBar k)
        {
            var black = k.Part(k.Gloss("#17181c", .55f)); var door = k.Part(k.Gloss("#26282e", .6f)); var st = k.Part(k.steel); var wood = k.Part(k.Surf("post", "#b98a56"));
            var c = new Vector3(-7f, 0, 6f);
            foreach (float sx in new[] { -.52f, .52f }) foreach (float sz in new[] { -.42f, .42f }) st.Box(c + new Vector3(sx, .1f, sz), new Vector3(.06f, .2f, .06f));
            black.Box(c + new Vector3(0, .5f, 0), new Vector3(1.16f, .64f, .94f), .02f, Quaternion.identity);
            foreach (float sx in new[] { -.285f, .285f })
            {
                door.Box(c + new Vector3(sx, .5f, -.474f), new Vector3(.54f, .56f, .014f), .005f, Quaternion.identity);
                st.Box(c + new Vector3(sx * .2f, .5f, -.49f), new Vector3(.016f, .26f, .016f));
            }
            st.Box(c + new Vector3(0, .96f, 0), new Vector3(1.2f, .28f, 1f), .03f, Quaternion.identity);
            black.Box(c + new Vector3(0, 1.24f, 0), new Vector3(1.36f, .3f, 1.06f), .13f, Quaternion.identity);
            TikiBar.Tube(st, c + new Vector3(-.4f, 1.2f, -.6f), c + new Vector3(.4f, 1.2f, -.6f), .02f, 8);
            foreach (float sx in new[] { -.4f, .4f }) TikiBar.Tube(st, c + new Vector3(sx, 1.2f, -.6f), c + new Vector3(sx, 1.2f, -.5f), .014f, 6);
            for (int i = 0; i < 4; i++) TikiBar.Tube(black, c + new Vector3(-.36f + i * .24f, .96f, -.5f), c + new Vector3(-.36f + i * .24f, .96f, -.55f), .04f, 10);
            foreach (float sx in new[] { -1f, 1f })
            {
                wood.Box(c + new Vector3(sx * .92f, BbqShelf - .025f, 0), new Vector3(.6f, .05f, .8f), .01f, Quaternion.identity);
                st.Box(c + new Vector3(sx * .92f, BbqShelf - .07f, 0), new Vector3(.56f, .04f, .04f));
            }
        }

        // the cool box (.9 square, .5 high)
        static void CoolBox(TikiBar k)
        {
            var c = new Vector3(-4.2f, 0, -12.6f);
            k.Part(k.Gloss("#2f63c8", .5f)).Box(c + Vector3.up * .2f, new Vector3(.9f, .4f, .9f), .05f, Quaternion.identity);
            var white = k.Part(k.Gloss("#f1efe9", .5f));
            white.Box(c + Vector3.up * .45f, new Vector3(.93f, .1f, .93f), .035f, Quaternion.identity);
            white.Box(c + new Vector3(0, .34f, .455f), new Vector3(.16f, .12f, .03f), .01f, Quaternion.identity);
        }

        // the paddling pool (5 x 4.8 outside, walls .4 thick and .5 high): rounded walls, a pale floor, water
        static void Pool(TikiBar k, bool baking)
        {
            var wall = k.Part(k.Gloss("#2f7fe0", .6f)); var c = new Vector3(7f, 0, -9f);
            foreach (float s in new[] { -1f, 1f })
            {
                wall.Box(c + new Vector3(0, .25f, s * 2.2f), new Vector3(5f, .5f, .4f), .12f, Quaternion.identity);
                wall.Box(c + new Vector3(s * 2.3f, .25f, 0), new Vector3(.4f, .5f, 4f), .12f, Quaternion.identity);
            }
            k.Part(k.Gloss("#bfe9f5", .5f)).Box(c + Vector3.up * .03f, new Vector3(4.3f, .04f, 4.1f));
            if (!baking) k.Part(k.Glass("#49b8e0", .55f)).Box(c + Vector3.up * .38f, new Vector3(4.25f, .02f, 4.05f));
        }

        // in the shed: a workbench where the block was (1.6 x 1.0, top at 1.0) and two shelves along the back wall
        const float ShelfLo = 1.72f, ShelfHi = 2.37f, ShelfZ = -11.2f;
        static void Shed(TikiBar k)
        {
            var wood = k.Part(k.Surf("post", "#b98a56")); var st = k.Part(k.steel); var c = new Vector3(-9.8f, 0, -10.6f);
            wood.Box(c + Vector3.up * .965f, new Vector3(1.6f, .07f, 1f), .01f, Quaternion.identity);
            foreach (float sx in new[] { -.72f, .72f }) foreach (float sz in new[] { -.42f, .42f }) wood.Box(c + new Vector3(sx, .465f, sz), new Vector3(.09f, .93f, .09f));
            wood.Box(c + Vector3.up * .3f, new Vector3(1.44f, .04f, .84f));
            foreach (float y in new[] { ShelfLo, ShelfHi })
            {
                wood.Box(new Vector3(-8.1f, y - .02f, ShelfZ), new Vector3(5.4f, .04f, .3f));
                for (float x = -10.5f; x < -5.5f; x += 1.2f) st.Box(new Vector3(x, y - .1f, ShelfZ - .06f), new Vector3(.03f, .14f, .16f));
            }
        }

        // a shelf on each side fence: bottles and cans to shoot at on the east one, flower pots on the west one
        const float FenceShelf = 1.47f;
        static void Shelves(TikiBar k)
        {
            var wood = k.Part(k.Surf("post", "#b98a56")); var st = k.Part(k.Gloss("#2a2b30", .5f));
            foreach (float s in new[] { -1f, 1f })
            {
                wood.Box(new Vector3(s * 12.7f, FenceShelf - .02f, s * 2.5f), new Vector3(.3f, .04f, 4.2f));
                for (int i = 0; i < 4; i++) st.Box(new Vector3(s * 12.78f, FenceShelf - .11f, s * 2.5f - 1.8f + i * 1.2f), new Vector3(.16f, .16f, .03f));
            }
        }

        // stepping stones from the patio to the table and on to the shed door
        static void Stones(TikiBar k)
        {
            var m = k.Part(k.Surf("rock", "#cfcabd"));
            void Run(Vector3 a, Vector3 b)
            {
                int n = Mathf.RoundToInt(Vector3.Distance(a, b) / .95f);
                for (int i = 0; i <= n; i++)
                {
                    var p = Vector3.Lerp(a, b, i / (float)n) + new Vector3(k.R(-.12f, .12f), .012f, k.R(-.08f, .08f));
                    m.Box(p, new Vector3(k.R(.55f, .7f), .05f, k.R(.45f, .56f)), .02f, Quaternion.Euler(0, k.R(-25, 25), 0));
                }
            }
            Run(new Vector3(0, 0, 9.7f), new Vector3(.3f, 0, 1.4f)); Run(new Vector3(-2.4f, 0, -1.6f), new Vector3(-6.9f, 0, -5.5f));
        }

        // ------------------------------------------------------------------ loose things
        static readonly string[] CanHex = { "#c8261e", "#1f5aa0", "#2f8f4a", "#e0b040" };
        static readonly float[] PotShape = { 0f, 0f, .085f, 0f, .12f, .2f, .135f, .2f, .135f, .24f, .115f, .24f, .11f, .21f, 0f, .21f };

        static void Protos(TikiBar k)
        {
            HouseLook.Protos(k);
            const float S = TikiBar.Small;
            TikiBar.PS = S;
            var terra = k.Gloss("#b5562f", .25f); var soil = k.Gloss("#3a2a20", .1f); var leaf = k.Gloss("#3f8f3a", .3f); var dark = k.Gloss("#2a6a34", .3f);
            Mb Pot(float h) { var m = new Mb(); m.Lathe(0, 0, TikiBar.Pr(h, PotShape), 12); return m; }
            Mb Soil(float h) { var m = new Mb(); m.Lathe(0, 0, TikiBar.Pr(h, .11f, .215f, 0f, .215f), 12); return m; }
            Vector3 At(float x, float y, float z, float h) => new Vector3(x, y - h / 2, z) * S;

            k.New("pot", .27f * S, .24f * S, "#b5562f", true, 1, .9f, (Pot(.24f), terra), (Soil(.24f), soil));
            // a bushy one
            {
                const float h = .52f; var g = new Mb();
                for (int i = 0; i < 6; i++) { float a = i * 1.047f; TikiBar.Ball(g, At(Mathf.Cos(a) * .075f, .33f + (i % 3) * .045f, Mathf.Sin(a) * .075f, h), .095f * S, 8, 5); }
                TikiBar.Ball(g, At(0, .42f, 0, h), .1f * S, 8, 5);
                k.New("plant0", .3f * S, h * S, "#3f8f3a", true, 1, .9f, (Pot(h), terra), (Soil(h), soil), (g, leaf));
            }
            // in flower: pink, then yellow
            string[] bloom = { "#e8508a", "#f2c230" };
            for (int v = 0; v < 2; v++)
            {
                const float h = .5f; var g = new Mb(); var f = new Mb();
                for (int i = 0; i < 4; i++) { float a = i * 1.571f + .4f; TikiBar.Ball(g, At(Mathf.Cos(a) * .06f, .31f, Mathf.Sin(a) * .06f, h), .085f * S, 8, 5); }
                for (int i = 0; i < 7; i++) { float a = i * .9f; float r = i == 6 ? 0 : .085f; TikiBar.Ball(f, At(Mathf.Cos(a) * r, .4f + (i % 2) * .03f + (i == 6 ? .05f : 0), Mathf.Sin(a) * r, h), .042f * S, 7, 4); }
                k.New("plant" + (v + 1), .3f * S, h * S, bloom[v], true, 1, .9f, (Pot(h), terra), (Soil(h), soil), (g, leaf), (f, k.Gloss(bloom[v], .4f)));
            }
            // a clipped cone
            {
                const float h = .85f; var g = new Mb(); g.Lathe(0, 0, TikiBar.Pr(h, 0f, .22f, .125f, .3f, .085f, .58f, 0f, .85f), 10);
                k.New("plant3", .28f * S, h * S, "#2a6a34", true, 1, 1f, (Pot(h), terra), (Soil(h), soil), (g, dark));
            }
            // a garden gnome (he faces his own +z)
            {
                const float h = .54f; var coat = new Mb(); var skin = new Mb(); var beard = new Mb(); var hat = new Mb();
                coat.Lathe(0, 0, TikiBar.Pr(h, 0f, 0f, .1f, 0f, .12f, .06f, .1f, .22f, .06f, .27f, 0f, .27f), 12);
                TikiBar.Ball(skin, At(0, .325f, 0, h), .07f * S, 10, 6); TikiBar.Ball(skin, At(0, .325f, .07f, h), .02f * S, 6, 4);
                TikiBar.Ball(beard, At(0, .262f, .05f, h), .046f * S, 8, 5);
                hat.Lathe(0, 0, TikiBar.Pr(h, 0f, .365f, .082f, .365f, .072f, .39f, 0f, .54f), 12);
                k.New("gnome", .25f * S, h * S, "#c8261e", true, 1, 1f, (coat, k.Gloss("#2f63c8", .6f)), (skin, k.Gloss("#e8b796", .5f)), (beard, k.Gloss("#f4f1ea", .5f)), (hat, k.Gloss("#c8261e", .6f)));
            }
            for (int i = 0; i < CanHex.Length; i++)
            {
                var can = new Mb(); can.Lathe(0, 0, TikiBar.Pr(.125f, 0f, 0f, .028f, 0f, .033f, .008f, .033f, .115f, .027f, .125f, 0f, .125f), 10);
                k.New("can" + i, .07f * S, .125f * S, CanHex[i], false, 1, .3f, (can, k.Gloss(CanHex[i], .75f, .7f)));
                var tin = new Mb(); tin.Lathe(0, 0, TikiBar.Pr(.2f, 0f, 0f, .09f, 0f, .09f, .19f, 0f, .19f), 12);
                var lid = new Mb(); lid.Lathe(0, 0, TikiBar.Pr(.2f, .095f, .185f, .095f, .2f, 0f, .2f), 12);
                k.New("tin" + i, .19f * S, .2f * S, CanHex[i], false, 1, .9f, (tin, k.Gloss(CanHex[i], .5f)), (lid, k.steel));
            }
            TikiBar.PS = 1;
        }

        static TikiBar.Proto AnyBottle(TikiBar k)
        {
            var tint = TikiBar.Tints[k.rng.Next(TikiBar.Tints.Length)];
            return k.Bottle(k.rng.Next(4), k.rng.Next(12), tint.hex, tint.a);
        }
        static TikiBar.Proto AnyPlant(TikiBar k) => k.P("plant" + k.rng.Next(4));

        static void Loose(TikiBar k)
        {
            // the picnic table (top 1.0), laid for four
            foreach (float x in new[] { -.9f, .9f })
                foreach (float z in new[] { -1.45f, -.55f })
                {
                    k.Put(k.P("plate"), new Vector3(x, 1f, z));
                    k.Put(k.G("highball", k.rng.Next(2) == 0 ? "#f08a24" : "#e8d06a"), new Vector3(x + (x < 0 ? .28f : -.28f), 1f, z + (z < -1 ? .1f : -.1f)));
                }
            k.Put(k.P("bowl"), new Vector3(0, 1f, -1f)); k.Put(AnyBottle(k), new Vector3(-.36f, 1f, -1.02f), k.R(0, 360)); k.Put(AnyBottle(k), new Vector3(.38f, 1f, -.94f), k.R(0, 360));
            k.Put(k.P("lantern"), new Vector3(.05f, 1f, -.5f)); k.Put(k.P("plates"), new Vector3(-.05f, 1f, -1.5f));
            // the barbecue's side shelves
            k.Put(k.P("plates"), new Vector3(-7.92f, BbqShelf, 6.15f)); k.Put(AnyBottle(k), new Vector3(-8.05f, BbqShelf, 5.8f)); k.Put(AnyBottle(k), new Vector3(-7.8f, BbqShelf, 5.76f));
            for (int i = 0; i < 3; i++) k.Put(k.P("can" + i), new Vector3(-6.22f + i * .13f, BbqShelf, 5.8f));
            k.Put(k.P("mug1"), new Vector3(-6.05f, BbqShelf, 6.2f));
            // the cool box's lid (top .5)
            for (int i = 0; i < 3; i++) k.Put(AnyBottle(k), new Vector3(-4.42f + i * .22f, .5f, -12.6f + k.R(-.15f, .15f)), k.R(0, 360));
            // the east fence's shelf: bottles and cans in a row; the west one: flower pots
            for (int i = 0; i < 10; i++) k.Put(i % 2 == 0 ? AnyBottle(k) : k.P("can" + k.rng.Next(4)), new Vector3(12.7f, FenceShelf, .6f + i * .42f), k.R(0, 360));
            for (int i = 0; i < 7; i++) k.Put(AnyPlant(k), new Vector3(-12.68f, FenceShelf, -4.3f + i * .6f), k.R(0, 360));
            // the shed: its shelves and the bench (top 1.0)
            for (float x = -10.6f; x < -5.5f; x += k.R(.38f, .5f))
            {
                int pick = k.rng.Next(6);
                k.Put(pick < 2 ? k.P("tin" + k.rng.Next(4)) : pick == 2 ? k.P("pot") : pick == 3 ? k.P("mug" + k.rng.Next(3)) : AnyBottle(k), new Vector3(x, ShelfLo, ShelfZ + k.R(-.03f, .05f)), k.R(0, 360));
            }
            for (float x = -10.5f; x < -5.6f; x += k.R(.55f, .8f))
                k.Put(k.rng.Next(3) == 0 ? k.P("vase" + k.rng.Next(3)) : k.rng.Next(2) == 0 ? k.P("tin" + k.rng.Next(4)) : k.P("pot"), new Vector3(x, ShelfHi, ShelfZ + k.R(-.03f, .05f)), k.R(0, 360));
            k.Put(k.P("plant0"), new Vector3(-10.35f, 1f, -10.75f)); k.Put(k.P("pot"), new Vector3(-9.95f, 1f, -10.85f)); k.Put(k.P("pot"), new Vector3(-9.7f, 1f, -10.5f));
            k.Put(k.P("tin2"), new Vector3(-9.3f, 1f, -10.8f)); k.Put(k.P("mug0"), new Vector3(-9.45f, 1f, -10.35f)); k.Put(k.P("gnome"), new Vector3(-10.25f, 1f, -10.3f), 20);
            // pots on the ground: the front of the patio and behind it, by the shed door, by the pool, along the far fence
            foreach (float x in new[] { -2.85f, -1.7f, 1.7f, 2.85f }) k.Put(AnyPlant(k), new Vector3(x, 0, 10.25f), k.R(0, 360));
            foreach (float x in new[] { -5.3f, -3.9f, 3.9f, 5.3f }) k.Put(AnyPlant(k), new Vector3(x, 0, 16.45f), k.R(0, 360));
            k.Put(k.P("plant3"), new Vector3(-8.6f, 0, -6.05f)); k.Put(k.P("plant3"), new Vector3(-5.8f, 0, -6.05f));
            k.Put(k.P("plant1"), new Vector3(4.15f, 0, -6.3f)); k.Put(k.P("plant2"), new Vector3(9.85f, 0, -6.3f)); k.Put(k.P("plant0"), new Vector3(9.85f, 0, -11.7f));
            for (int i = 0; i < 8; i++)
            {
                var at = new Vector3(1.6f + i * 1.45f, 0, -16.45f + k.R(-.05f, .1f));
                if (i % 3 == 1) k.Put(k.P("gnome"), at, k.R(-25, 25)); else k.Put(AnyPlant(k), at, k.R(0, 360));
            }
            // gnomes: either end of the hedge, under the trees, by the pool, by the patio, on top of the steps, on the shed roof
            k.Put(k.P("gnome"), new Vector3(-3f, 0, -11.4f), 10); k.Put(k.P("gnome"), new Vector3(3f, 0, -11.4f), -15);
            k.Put(k.P("gnome"), new Vector3(-9.9f, 0, 4.1f), 100); k.Put(k.P("gnome"), new Vector3(9.9f, 0, -3.1f), -80);
            k.Put(k.P("gnome"), new Vector3(4.1f, 0, -11.8f), 30); k.Put(k.P("gnome"), new Vector3(-2.9f, 0, 10.9f), 170);
            k.Put(k.P("gnome"), new Vector3(-10.2f, 3.7f, -12.7f), 60); k.Put(k.P("gnome"), new Vector3(-5.2f, 3.7f, -6.7f), 45);
            // drinks: two tables on the patio either side of the way out, one by the pool, glasses on the pool's edge (top .5)
            k.ChampagneTable(new Vector3(-2.15f, 0, 15.4f)); k.TableSet(new Vector3(2.15f, 0, 15.4f)); k.TableSet(new Vector3(3.4f, 0, -9f));
            k.Drink(new Vector3(4.7f, .5f, -7.4f)); k.Drink(new Vector3(6.1f, .5f, -6.8f)); k.Drink(new Vector3(9.3f, .5f, -10.3f));
        }
    }
}
