// The House's look (MapLooks): the grey-box rooms made into a lived-in home.
//  - Surfaces: painted plaster on the walls, boards in the living room, study and dining room, ceramic tiles in the kitchen,
//    bathroom and hall, carpet in the bedrooms, concrete in the garage, grass outside.
//  - Furniture: the plain coloured blocks (wardrobes, bookcases, kitchen counter and island, fridge, sideboard, TV unit and
//    TV, washer and dryer, toy chest, garage shelving) are rebuilt as furniture in the same places and sizes. Their collision
//    boxes are the map's own, untouched.
//  - Things to smash: crockery and glasses in the kitchen and on the dining table, a glass cabinet full of them, vases on
//    the bookcases, drinks on the coffee table, bottles round the bath. All loose; they join Destruction through the kit.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    using Mb = MapProps.Mb;

    public static class HouseLook
    {
        // the blocks that are rebuilt: kind, x0, x1, height, z0, z1, and which way the front faces (x, z)
        static readonly (string kind, float x0, float x1, float h, float z0, float z1, int fx, int fz)[] Items =
        {
            ("shelving", -14.6f, -12.6f, 2.2f, -23.6f, -20.4f, 1, 0),
            ("wardrobe", 1.2f, 4.8f, 2.6f, -13.8f, -12.6f, 0, -1),
            ("chest", 11.7f, 14.3f, 1.1f, -13.8f, -12.6f, 0, -1),
            ("media", -14.6f, -10f, 1.1f, -9.45f, -8.35f, 0, 1),
            ("bookcase", 2f, 3.2f, 2.8f, 1.2f, 4.8f, -1, 0),
            ("bookcase", 13.4f, 14.4f, 2.6f, -1.5f, 3.5f, -1, 0),
            ("counter", -14.8f, -13.6f, 1.1f, 6.4f, 10.8f, 1, 0),
            ("island", -7.5f, -3.5f, 1.1f, 8.2f, 9.8f, 0, -1),
            ("fridge", -13.3f, -11.9f, 2.6f, 6.2f, 7.5f, 0, 1),
            ("display", 13.2f, 14.4f, 2.4f, 7f, 11f, -1, 0),
            ("washer", -3.8f, -2.2f, 1.4f, 21.9f, 23.3f, 0, -1),
            ("washer", -2.2f, -.6f, 1.4f, 21.9f, 23.3f, 0, -1),
            ("wardrobe", 12.8f, 14.4f, 2.8f, 12.4f, 15.6f, -1, 0),
            ("bath", -14.5f, -9.5f, 1f, 20.6f, 23f, 0, -1),
            ("vanity", -8.7f, -7.3f, 1f, 14.3f, 15.7f, 0, -1),
        };

        static Bounds[] Gone()
        {
            var list = new List<Bounds>();
            foreach (var it in Items) list.Add(MapLooks.Box(it.x0, 0, it.z0, it.x1, it.h, it.z1));
            list.Add(MapLooks.Box(-14f, 1.1f, -9.3f, -10.6f, 3f, -9.1f));   // the old TV
            return list.ToArray();
        }

        public static readonly MapLook Def = new MapLook
        {
            id = "house", set = "HouseLook", stem = "HouseLight",
            rules = new Dictionary<string, string>
            {
                { "c99a5b+", "planks" }, { "b9895a+", "planks" },
                { "9aa0a8+", "concrete" },
                { "eeeeea+", "tiles" }, { "bfe6ea+", "tiles" }, { "dedede+", "tiles" },
                { "a8c8e8+", "carpet" }, { "e8c7d8+", "carpet" },
                { "5fb24b+", "grass" },
                { "f3efe6", "plaster" }, { "e6dccb", "plaster" }, { "d6e3ee", "plaster" }, { "efe3c8", "plaster" }, { "dfe9e2", "plaster" }, { "cfe6ea", "plaster" }, { "ead8e2", "plaster" },
                { "ffffff-", "paint" }, { "3a3d45", "paint" },
                { "8a5a34", "post" }, { "7a4c2a", "post" },
            },
            gone = Gone(),
            strength = 1f,
            probeCentre = new Vector3(0, 3f, 0), probeSize = new Vector3(32, 7, 50),
            cloudCover = .5f,
            dress = Dress,
            dressBake = (root, set) => Furniture(root, true),
        };

        // a piece of furniture's own frame: x along its front, y up, z out of its front; its foot is the middle of its floor area
        struct Fr
        {
            public Vector3 c; public Quaternion rot; public float W, D, H;
            public Vector3 At(float x, float y, float z) => c + rot * new Vector3(x, y, z);
        }
        static void B(Mb m, Fr f, float x, float y, float z, float sx, float sy, float sz, float r = 0) => m.Box(f.At(x, y, z), new Vector3(sx, sy, sz), r, f.rot);

        static TikiBar kit;
        static void Dress(MapWorld w, RailLookSet set)
        {
            kit = Furniture(w.transform, false);
            if (!kit) return;
            Protos(kit); Loose(kit);
            MapLooks.Tables(w, kit.Surf("post", "#a8743c"));
            kit.Lod(.06f);
            Debug.Log("The House: " + kit.Count + " loose things");
        }

        /// the furniture (for the bake: just the solid parts, under the bake's root)
        static TikiBar Furniture(Transform parent, bool baking)
        {
            var k = baking ? TikiBar.Kit(parent, "House furniture (bake)", true) : TikiBar.Kit(parent, "House furniture");
            if (!k) return null;
            var white = k.Gloss("#f1efe9", .55f); var oak = k.Surf("post", "#c9a878"); var walnut = k.Surf("post", "#6b4428"); var navy = k.Gloss("#27405e", .5f);
            var marble = k.Surf("marble", "#ece7dd"); var dark = k.Gloss("#1b1c20", .6f); var sage = k.Gloss("#8fa58c", .5f);
            foreach (var it in Items)
            {
                var f = new Fr { c = new Vector3((it.x0 + it.x1) / 2, 0, (it.z0 + it.z1) / 2), rot = Quaternion.LookRotation(new Vector3(it.fx, 0, it.fz)), H = it.h };
                f.W = it.fx != 0 ? it.z1 - it.z0 : it.x1 - it.x0; f.D = it.fx != 0 ? it.x1 - it.x0 : it.z1 - it.z0;
                switch (it.kind)
                {
                    case "wardrobe": Cupboard(k, f, white, white, Mathf.Max(2, Mathf.RoundToInt(f.W / .85f)), null, true, it.z1 < -12); break;   // the children's one stands clear of the wall
                    case "chest": Cupboard(k, f, k.Gloss("#f0c64a", .5f), k.Gloss("#f6d466", .5f), 3, null, false); break;
                    case "media": Cupboard(k, f, walnut, walnut, 4, null, false); TV(k, f); break;
                    case "counter": Cupboard(k, f, sage, sage, 5, marble, false); Sink(k, f); break;
                    case "island": Cupboard(k, f, navy, navy, 4, marble, false, true); break;
                    case "bookcase": Bookcase(k, f, it.x0 > 10 ? oak : walnut); break;
                    case "display": Display(k, f, walnut); break;
                    case "fridge": Fridge(k, f); break;
                    case "washer": Washer(k, f, white, dark); break;
                    case "shelving": Shelving(k, f); break;
                    case "bath": Bath(k, f, white); break;
                    case "vanity": Cupboard(k, f, white, white, 2, marble, false); B(k.Part(k.Gloss("#dfe6ea", .95f)), f, 0, f.H + .004f, 0, .8f, .012f, .7f, .004f); TikiBar.Tube(k.Part(k.steel), f.At(0, f.H, -.5f), f.At(0, f.H + .26f, -.5f), .02f, 8); break;
                }
            }
            Decor(k, baking);
            k.Flush();
            return k;
        }

        // rugs, standing lamps, a desk lamp and the lights under the loft
        static void Decor(TikiBar k, bool baking)
        {
            void Rug(float x0, float z0, float x1, float z1, string hex, string edge)
            {
                var c = new Vector3((x0 + x1) / 2, .012f, (z0 + z1) / 2);
                k.Part(k.Surf("carpet", edge)).Box(c, new Vector3(x1 - x0, .024f, z1 - z0), .01f, Quaternion.identity);
                k.Part(k.Surf("carpet", hex)).Box(c + Vector3.up * .004f, new Vector3(x1 - x0 - .5f, .028f, z1 - z0 - .5f), .01f, Quaternion.identity);
            }
            Rug(-10.6f, -7f, -2.4f, -2.5f, "#5a6f8a", "#e6e0d2");
            Rug(3.6f, 6.9f, 10f, 11.1f, "#8a3b2e", "#d8c9a8");
            Rug(3.6f, 15.2f, 11.4f, 19.6f, "#b9a48c", "#efe8dc");
            Rug(6.6f, -19.3f, 13.4f, -15.2f, "#e8b84a", "#3f7fb8");

            var shade = k.Glow("#f5e6c8", "#ffd9a0", 1.5f); var warm = new Color(1f, .85f, .65f);
            void Standing(float x, float z)
            {
                var m = k.Part(k.brass);
                m.Lathe(x, z, new[] { new Vector2(0, 0), new Vector2(.2f, 0), new Vector2(.2f, .03f), new Vector2(.02f, .05f), new Vector2(.02f, 1.86f), new Vector2(0, 1.86f) }, 14);
                k.Part(shade).Lathe(x, z, new[] { new Vector2(.27f, 1.7f), new Vector2(.19f, 2.14f), new Vector2(.17f, 2.14f), new Vector2(.25f, 1.7f) }, 16);
                if (!baking) k.AddLamp(new Vector3(x, 2f, z), warm, 2.2f, 6f, 0);
            }
            Standing(-14f, 5f); Standing(4f, -9f); Standing(4.2f, 22.9f); Standing(10.8f, 22.9f); Standing(7.1f, -22.9f);

            // under the loft (its underside is at 3.1): four lights let into the ceiling, two real lamps between them
            foreach (float x in new[] { 7.6f, 12.2f })
                foreach (float z in new[] { -2.8f, 2.6f })
                {
                    k.Part(k.brass).Lathe(x, z, new[] { new Vector2(.2f, 3.04f), new Vector2(.26f, 3.05f), new Vector2(.26f, 3.1f) }, 16);
                    k.Part(shade).Lathe(x, z, new[] { new Vector2(0, 3.045f), new Vector2(.2f, 3.045f) }, 16);
                }
            if (!baking) { k.AddLamp(new Vector3(7.8f, 2.7f, 0), warm, 2.6f, 7f, 0); k.AddLamp(new Vector3(12.2f, 2.7f, 0), warm, 2.6f, 7f, 0); }

            // a lamp on the study desk (top 1.05)
            var at = new Vector3(9.85f, 1.05f, -1.75f); var dark = k.Part(k.Gloss("#1b1c20", .6f));
            dark.Lathe(at.x, at.z, new[] { new Vector2(0, 1.05f), new Vector2(.1f, 1.05f), new Vector2(.1f, 1.07f), new Vector2(0, 1.075f) }, 12);
            TikiBar.Tube(dark, at + Vector3.up * .02f, at + new Vector3(0, .42f, 0), .012f, 6); TikiBar.Tube(dark, at + new Vector3(0, .42f, 0), at + new Vector3(.2f, .5f, .16f), .012f, 6);
            k.Part(shade).Lathe(at.x + .22f, at.z + .18f, new[] { new Vector2(.11f, 1.45f), new Vector2(.05f, 1.56f), new Vector2(0, 1.56f) }, 12);
        }

        // doors across the front, a plinth, an optional stone top (it keeps the block's height) and, on a wardrobe, a cornice
        static void Cupboard(TikiBar k, Fr f, Material body, Material door, int doors, Material top, bool tall, bool bothSides = false)
        {
            float topT = top ? .07f : 0, h = f.H - topT;
            B(k.Part(k.Gloss("#2a2b30", .4f)), f, 0, .05f, 0, f.W - .06f, .1f, f.D - .1f);
            B(k.Part(body), f, 0, (h + .1f) / 2, 0, f.W, h - .1f, f.D - .03f);
            float dw = f.W / doors;
            for (int side = 0; side < (bothSides ? 2 : 1); side++)
            {
                float z = (f.D / 2 - .004f) * (side == 0 ? 1 : -1);
                for (int i = 0; i < doors; i++)
                {
                    float x = -f.W / 2 + dw * (i + .5f);
                    B(k.Part(door), f, x, (h + .1f) / 2, z, dw - .014f, h - .14f, .022f, .006f);
                    float hx = x + (i % 2 == 0 ? 1 : -1) * (dw / 2 - .06f);
                    if (tall) B(k.Part(k.brass), f, hx, h * .5f, z + (side == 0 ? .028f : -.028f), .016f, .34f, .016f);
                    else B(k.Part(k.brass), f, x, h - .16f, z + (side == 0 ? .028f : -.028f), Mathf.Min(.3f, dw * .4f), .016f, .016f);
                }
            }
            if (top) B(k.Part(top), f, 0, f.H - topT / 2, 0, f.W + .06f, topT, f.D + .06f, .018f);
            if (tall) B(k.Part(body), f, 0, f.H - .04f, .01f, f.W + .04f, .08f, f.D + .02f);
        }

        static void TV(TikiBar k, Fr f)
        {   // on the unit, where the flat block stood: a thin set with a dim picture
            B(k.Part(k.Gloss("#111216", .8f)), f, 0, f.H + 1.02f, -.2f, 3.3f, 1.84f, .06f, .015f);
            B(k.Part(k.Glow("#0c1220", "#5a8fd0", .55f, null, .9f)), f, 0, f.H + 1.02f, -.166f, 3.18f, 1.72f, .01f);
            B(k.Part(k.Gloss("#111216", .8f)), f, 0, f.H + .06f, -.2f, .9f, .12f, .3f, .01f);
        }

        static void Sink(TikiBar k, Fr f)
        {   // a sink and a tap in the worktop, a black glass hob
            B(k.Part(k.steel), f, -1.1f, f.H + .002f, 0, .9f, .012f, .56f, .004f);
            B(k.Part(k.Gloss("#50545a", .8f, 1f)), f, -1.1f, f.H + .006f, 0, .74f, .012f, .42f);
            TikiBar.Tube(k.Part(k.steel), f.At(-1.1f, f.H, -.36f), f.At(-1.1f, f.H + .34f, -.36f), .02f, 8);
            TikiBar.Tube(k.Part(k.steel), f.At(-1.1f, f.H + .33f, -.36f), f.At(-1.1f, f.H + .3f, -.12f), .016f, 8);
            B(k.Part(k.Gloss("#0d0e11", .95f)), f, 1.1f, f.H + .004f, 0, .9f, .01f, .62f, .004f);
        }

        static readonly string[] BookHex = { "#8a2f2a", "#27405e", "#2f6b3a", "#c9a14a", "#e9e2d0", "#3a2a4a", "#b85a2a", "#1f4f55" };
        static void Bookcase(TikiBar k, Fr f, Material wood)
        {
            var m = k.Part(wood);
            foreach (float x in new[] { -f.W / 2 + .025f, f.W / 2 - .025f }) B(m, f, x, f.H / 2, 0, .05f, f.H, f.D);
            B(m, f, 0, f.H - .025f, 0, f.W, .05f, f.D); B(m, f, 0, .06f, 0, f.W, .12f, f.D); B(m, f, 0, f.H / 2, -f.D / 2 + .015f, f.W, f.H, .03f);
            int shelves = Mathf.Max(3, Mathf.RoundToInt((f.H - .2f) / .5f)); float gap = (f.H - .17f) / shelves;
            for (int s = 0; s < shelves; s++)
            {
                float y = .12f + s * gap;
                if (s > 0) B(m, f, 0, y, 0, f.W - .1f, .035f, f.D - .04f);
                // books standing in runs, with gaps where the vases go (Loose)
                float x = -f.W / 2 + .08f, end = f.W / 2 - .08f;
                while (x < end)
                {
                    if (k.rng.NextDouble() < .1) { x += k.R(.25f, .5f); continue; }
                    float bw = k.R(.035f, .075f), bh = Mathf.Min(gap - .08f, k.R(.24f, .36f)), bd = k.R(.17f, .22f);
                    if (x + bw > end) break;
                    B(k.Part(k.Gloss(BookHex[k.rng.Next(BookHex.Length)], .35f)), f, x + bw / 2, y + .018f + bh / 2, f.D / 2 - .06f - bd / 2, bw - .004f, bh, bd);
                    x += bw;
                }
            }
        }

        // a glass cabinet: a wooden frame, a mirror back, glass shelves (what stands on them is put there by Loose)
        static readonly float[] DisplayY = { .92f, 1.38f, 1.84f };
        static void Display(TikiBar k, Fr f, Material wood)
        {
            var m = k.Part(wood);
            B(m, f, 0, .4f, 0, f.W, .8f, f.D);                                        // a base cupboard
            for (int i = 0; i < 4; i++) B(k.Part(k.brass), f, -f.W / 2 + f.W / 4 * (i + .5f), .62f, f.D / 2 + .012f, .2f, .016f, .016f);
            foreach (float x in new[] { -f.W / 2 + .03f, f.W / 2 - .03f }) B(m, f, x, (f.H + .8f) / 2, 0, .06f, f.H - .8f, f.D);
            B(m, f, 0, f.H - .04f, 0, f.W, .08f, f.D);
            B(k.Part(k.Gloss("#c9b99c", .97f, 1f)), f, 0, (f.H + .8f) / 2, -f.D / 2 + .02f, f.W - .1f, f.H - .86f, .02f);
            for (int i = 0; i < 2; i++) B(k.Part(k.Glass("#dff3f0", .28f)), f, 0, DisplayY[i] + .49f, 0, f.W - .12f, .02f, f.D - .1f);
            B(k.Part(k.Glow("#ffe2b0", "#ffbe6e", 2.6f)), f, 0, f.H - .09f, f.D / 2 - .06f, f.W - .16f, .014f, .02f);
        }

        // a bath: a rim all round, a wide shelf at the tap end, water in it
        static void Bath(TikiBar k, Fr f, Material white)
        {
            var m = k.Part(white); float t = .12f, shelf = .55f;
            B(m, f, 0, .12f, 0, f.W, .24f, f.D, .02f);
            B(m, f, 0, f.H / 2, f.D / 2 - t / 2, f.W, f.H, t, .03f); B(m, f, 0, f.H / 2, -f.D / 2 + t / 2, f.W, f.H, t, .03f);
            B(m, f, f.W / 2 - t / 2, f.H / 2, 0, t, f.H, f.D, .03f); B(m, f, -f.W / 2 + shelf / 2, f.H / 2, 0, shelf, f.H, f.D, .03f);
            B(k.Part(k.Glass("#7fd0e6", .55f)), f, shelf / 2 - t / 2, f.H - .2f, 0, f.W - shelf - t, .02f, f.D - 2 * t);
            TikiBar.Tube(k.Part(k.steel), f.At(-f.W / 2 + .3f, f.H, 0), f.At(-f.W / 2 + .3f, f.H + .22f, 0), .022f, 8);
            TikiBar.Tube(k.Part(k.steel), f.At(-f.W / 2 + .3f, f.H + .21f, 0), f.At(-f.W / 2 + .62f, f.H + .17f, 0), .018f, 8);
        }

        static void Fridge(TikiBar k, Fr f)
        {
            var st = k.Part(k.Gloss("#c4c8ce", .82f, .9f));
            B(st, f, 0, f.H / 2, -.02f, f.W, f.H, f.D - .04f, .02f);
            B(st, f, 0, .5f, f.D / 2 - .012f, f.W - .02f, .94f, .03f, .012f);                 // freezer drawer
            B(st, f, 0, 1.0f + (f.H - 1.02f) / 2, f.D / 2 - .012f, f.W - .02f, f.H - 1.06f, .03f, .012f);
            B(k.Part(k.steel), f, -f.W / 2 + .1f, 1.75f, f.D / 2 + .03f, .022f, .7f, .022f);
            B(k.Part(k.steel), f, 0, .86f, f.D / 2 + .03f, f.W * .6f, .022f, .022f);
        }

        static void Washer(TikiBar k, Fr f, Material white, Material dark)
        {
            B(k.Part(white), f, 0, f.H / 2, -.01f, f.W - .03f, f.H, f.D - .02f, .02f);
            B(k.Part(dark), f, 0, f.H - .13f, f.D / 2 - .008f, f.W - .1f, .16f, .012f, .004f);     // the control strip
            TikiBar.Tube(k.Part(k.steel), f.At(0, f.H * .43f, f.D / 2 - .02f), f.At(0, f.H * .43f, f.D / 2 + .035f), .36f, 20);
            B(k.Part(k.Gloss("#15181d", .95f)), f, 0, f.H * .43f, f.D / 2 - .004f, .5f, .5f, .02f, .008f);
            B(k.Part(k.Glow("#0c1a12", "#3dff9a", 2f)), f, f.W / 2 - .16f, f.H - .13f, f.D / 2 + .002f, .06f, .03f, .004f);
        }

        static readonly string[] CanHex = { "#d7263d", "#2f7fe0", "#f4c22b", "#3bb54a", "#e9e4da", "#f08a24" };
        static void Shelving(TikiBar k, Fr f)
        {   // steel racking with paint tins and boxes
            var st = k.Part(k.Gloss("#5b6070", .6f, .6f));
            foreach (float x in new[] { -f.W / 2 + .03f, f.W / 2 - .03f }) foreach (float z in new[] { -f.D / 2 + .03f, f.D / 2 - .03f }) B(st, f, x, f.H / 2, z, .05f, f.H, .05f);
            for (int s = 0; s < 4; s++)
            {
                float y = .25f + s * (f.H - .3f) / 3.6f;
                B(st, f, 0, y, 0, f.W, .035f, f.D);
                for (float x = -f.W / 2 + .3f; x < f.W / 2 - .25f; x += k.R(.38f, .6f))
                {
                    if (k.rng.Next(3) == 0) B(k.Part(k.Surf("planks", "#c9a070")), f, x, y + .02f + .16f, k.R(-.2f, .2f), .4f, .32f, .5f);
                    else
                    {
                        var at = f.At(x, 0, k.R(-.3f, .3f));
                        k.Part(k.Gloss(CanHex[k.rng.Next(CanHex.Length)], .7f)).Lathe(at.x, at.z, new[] { new Vector2(0, y + .018f), new Vector2(.11f, y + .018f), new Vector2(.11f, y + .27f), new Vector2(0, y + .27f) }, 12);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ loose things
        static void Protos(TikiBar k)
        {
            TikiBar.PS = TikiBar.Small;
            var china = k.Gloss("#f4f1ea", .85f);
            var plate = new Mb(); plate.Lathe(0, 0, TikiBar.Pr(.026f, 0f, 0f, .07f, 0f, .13f, .018f, .135f, .022f, .13f, .026f, .07f, .012f, 0f, .012f), 14);
            k.New("plate", .27f * TikiBar.Small, .026f * TikiBar.Small, "#f4f1ea", true, 1, .5f, (plate, china));
            var stack = new Mb();
            for (int i = 0; i < 6; i++) stack.Lathe(0, 0, TikiBar.Pr(.1f, 0f, i * .015f, .07f, i * .015f, .13f, .018f + i * .015f, .135f, .022f + i * .015f, .13f, .026f + i * .015f, .07f, .012f + i * .015f, 0f, .012f + i * .015f), 14);
            k.New("plates", .27f * TikiBar.Small, .1f * TikiBar.Small, "#f4f1ea", true, 1, .8f, (stack, china));
            string[] glaze = { "#e9e4da", "#2a6f8f", "#c8563a" };
            for (int i = 0; i < glaze.Length; i++)
            {
                var vase = new Mb(); vase.Lathe(0, 0, TikiBar.Pr(.34f, 0f, 0f, .05f, 0f, .085f, .08f, .09f, .16f, .05f, .26f, .04f, .31f, .055f, .34f, .05f, .34f, .035f, .31f, .04f, .02f, 0f, .02f), 12);
                k.New("vase" + i, .18f * TikiBar.Small, .34f * TikiBar.Small, glaze[i], true, 1, .9f, (vase, k.Gloss(glaze[i], .8f)));
            }
            var bowl = new Mb(); bowl.Lathe(0, 0, TikiBar.Pr(.12f, 0f, 0f, .06f, 0f, .15f, .09f, .155f, .095f, .15f, .1f, .06f, .015f, 0f, .015f), 14);
            var fruitA = new Mb(); var fruitB = new Mb();
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.05f; var c = new Vector3(Mathf.Cos(a) * .075f, (.075f - .06f + (i % 2) * .03f), Mathf.Sin(a) * .075f) * TikiBar.Small;
                TikiBar.Ball(i % 2 == 0 ? fruitA : fruitB, c, .05f * TikiBar.Small, 8, 5);
            }
            k.New("bowl", .31f * TikiBar.Small, .12f * TikiBar.Small, "#f08a24", true, 1, .9f, (bowl, china), (fruitA, k.Gloss("#f08a24", .5f)), (fruitB, k.Gloss("#c8322b", .6f)));
            TikiBar.PS = 1;
        }

        // a framed picture that hangs flat on a wall (it faces its own +z) and smashes: bands of colour on a white mount
        static void Picture(TikiBar k, string name, float W, float H, Material frame, params string[] art)
        {
            var fr = new Mb(); var mount = new Mb(); float t = .05f;
            fr.Box(new Vector3(0, H / 2 - t / 2, 0), new Vector3(W, t, .05f)); fr.Box(new Vector3(0, -H / 2 + t / 2, 0), new Vector3(W, t, .05f));
            fr.Box(new Vector3(-W / 2 + t / 2, 0, 0), new Vector3(t, H - 2 * t, .05f)); fr.Box(new Vector3(W / 2 - t / 2, 0, 0), new Vector3(t, H - 2 * t, .05f));
            mount.Box(new Vector3(0, 0, -.005f), new Vector3(W - 2 * t, H - 2 * t, .02f));
            var bits = new List<(Mb, Material)> { (fr, frame), (mount, k.Gloss("#f3f0e8", .3f)) };
            float iw = W - 2 * t - .18f, ih = H - 2 * t - .18f, y = -ih / 2; float[] share = { .34f, .12f, .54f };
            for (int i = 0; i < art.Length && i < 3; i++)
            {
                var b = new Mb(); float h = ih * share[i]; b.Box(new Vector3(0, y + h / 2, .006f), new Vector3(iw, h, .012f)); y += h;
                if (i == 2) TikiBar.Ball(b, new Vector3(iw * .22f, y - h * .45f, .004f), Mathf.Min(iw, ih) * .13f, 14, 6);
                bits.Add((b, k.Gloss(art[i], .35f)));
            }
            k.New(name, .6f, H, art[0], true, 1, 1.2f, bits.ToArray());
        }

        static void Bottles(TikiBar k, Vector3 from, Vector3 step, int n, float yaw)
        {
            for (int i = 0; i < n; i++)
            {
                var tint = TikiBar.Tints[k.rng.Next(TikiBar.Tints.Length)];
                k.Put(k.Bottle(k.rng.Next(4), k.rng.Next(12), tint.hex, tint.a), from + step * i, yaw + k.R(-20, 20));
            }
        }

        static void Loose(TikiBar k)
        {
            // kitchen: along the worktop by the wall (x -14.2, top 1.1) and on the island (z 9, top 1.1)
            k.Put(k.P("plates"), new Vector3(-14.25f, 1.1f, 9.9f)); k.Put(k.P("plates"), new Vector3(-14.25f, 1.1f, 10.35f));
            Bottles(k, new Vector3(-14.45f, 1.1f, 8.3f), new Vector3(0, 0, .26f), 4, 90);
            for (int i = 0; i < 4; i++) k.Put(k.G(i % 2 == 0 ? "highball" : "tumbler", null), new Vector3(-14.05f, 1.1f, 8.35f + i * .22f));
            k.Put(k.P("mug0"), new Vector3(-14.2f, 1.1f, 6.9f)); k.Put(k.P("mug2"), new Vector3(-14.0f, 1.1f, 7.15f));
            k.Put(k.P("bowl"), new Vector3(-5.5f, 1.1f, 9f));
            Bottles(k, new Vector3(-7.1f, 1.1f, 8.85f), new Vector3(.24f, 0, 0), 3, 180);
            for (int i = 0; i < 4; i++) k.Put(k.G("wine", i < 2 ? "#7a1028" : null), new Vector3(-6.2f + i * .22f, 1.1f, 9.35f));
            k.Put(k.P("plates"), new Vector3(-4.2f, 1.1f, 8.75f)); k.Put(k.P("lantern"), new Vector3(-4.6f, 1.1f, 9.35f));
            for (int i = 0; i < 3; i++) k.Put(k.P("stool"), new Vector3(-6.8f + i * 1.3f, 0, 10.35f), k.R(0, 360));
            // dining table (top 1.05): six places, a bottle and candles down the middle
            foreach (float x in new[] { 5.4f, 6.8f, 8.2f })
                foreach (float z in new[] { 8.68f, 9.32f })
                {
                    k.Put(k.P("plate"), new Vector3(x, 1.05f, z));
                    k.Put(k.G("wine", k.rng.Next(2) == 0 ? "#7a1028" : "#f3d98a"), new Vector3(x + .24f, 1.05f, z + (z < 9 ? .12f : -.12f)));
                }
            Bottles(k, new Vector3(6.1f, 1.05f, 9f), new Vector3(1.4f, 0, 0), 2, 0);
            k.Put(k.P("lantern"), new Vector3(6.8f, 1.05f, 9f)); k.Put(k.P("bowl"), new Vector3(5.2f, 1.05f, 9f));
            // the glass cabinet (x 13.8, z 7..11): crockery and glasses on its three shelves, decanters on top of the base
            for (int s = 0; s < 3; s++)
            {
                for (float z = 7.3f; z < 10.8f; z += .34f)
                {
                    int pick = k.rng.Next(5);
                    var at = new Vector3(13.82f, s == 0 ? .8f : DisplayY[s - 1] + .5f, z);
                    if (s == 0) k.Put(k.Bottle(4, 0, pick % 2 == 0 ? "#a8601c" : "#e0b040", .7f), at, k.R(0, 360));
                    else if (pick == 0) k.Put(k.P("plates"), at);
                    else if (pick == 1) k.Put(k.P("vase" + k.rng.Next(3)), at);
                    else k.Put(k.G(pick == 2 ? "coupe" : pick == 3 ? "martini" : "wine", null), at);
                }
            }
            // living room: the coffee table (top .5), the TV unit (top 1.1), vases on the bookcases
            k.Drink(new Vector3(-7.2f, .5f, -4.5f)); k.Drink(new Vector3(-6.7f, .5f, -4.0f)); k.Drink(new Vector3(-5.7f, .5f, -4.6f)); k.Put(k.P("bowl"), new Vector3(-6.3f, .5f, -4.3f));
            k.Put(k.P("vase1"), new Vector3(-14.2f, 1.1f, -8.9f)); k.Put(k.P("lantern"), new Vector3(-10.4f, 1.1f, -8.9f));
            k.Put(k.P("vase0"), new Vector3(2.6f, 2.8f, 1.7f)); k.Put(k.P("vase2"), new Vector3(2.6f, 2.8f, 4.3f)); k.Put(k.P("vase1"), new Vector3(13.9f, 2.6f, 1f));
            // study desk (top 1.05), bedroom table (top 1), toy chest and wardrobes
            k.Put(k.P("mug1"), new Vector3(10.2f, 1.05f, -1.3f)); k.Put(k.Bottle(4, 0, "#a8601c", .7f), new Vector3(11.8f, 1.05f, -1.5f)); k.Put(k.G("tumbler", "#c9781e"), new Vector3(11.5f, 1.05f, -1.1f));
            k.Put(k.P("vase0"), new Vector3(2f, 1f, 14.8f)); k.Put(k.G("wine", "#f3d98a"), new Vector3(2.35f, 1f, 14.5f));
            k.Put(k.P("vase2"), new Vector3(12.2f, 1.1f, -13.2f));
            // bathroom: bottles on the end of the bath (top 1), on the washer and dryer (top 1.4)
            Bottles(k, new Vector3(-9.72f, 1f, 21.2f), new Vector3(0, 0, .26f), 5, -90);
            Bottles(k, new Vector3(-3.4f, 1.4f, 22.9f), new Vector3(.3f, 0, 0), 3, 180); k.Put(k.P("plates"), new Vector3(-1.4f, 1.4f, 22.6f));
            // pictures on the walls
            Picture(k, "picA", 1.4f, .9f, k.Surf("post", "#6b4428"), "#2f5d7c", "#e9c46a", "#f4a261");
            Picture(k, "picB", .85f, 1.15f, k.Gloss("#1b1c20", .6f), "#264653", "#2a9d8f", "#e9ecef");
            Picture(k, "picC", 1.1f, 1.1f, k.brass, "#7a3b2e", "#d9b382", "#f1e3c8");
            foreach (var (pic, x, y, z, yaw) in new[] {
                ("picA", -3.6f, 1.7f, -9.77f, 0f), ("picB", 2.6f, 1.6f, -9.77f, 0f), ("picC", -3.2f, 1.7f, 5.77f, 180f),
                ("picB", 2.5f, 1.6f, 11.77f, 180f), ("picA", 11f, 1.7f, 11.77f, 180f), ("picC", -7.9f, 1.7f, 11.77f, 180f),
                ("picA", 10.6f, 1.7f, 12.23f, 0f), ("picC", 7.5f, 2f, 23.77f, 180f), ("picB", 10f, 1.9f, -23.77f, 0f),
                ("picB", 5.23f, 1.45f, 2.6f, 90f), ("picA", -6.23f, 1.7f, 21.6f, 270f) })
                k.Put(k.P(pic), new Vector3(x, y, z), yaw);
            // garage: tins on the crate side of the shelving are part of the shelving; a few loose mugs on the bonnet side
            k.Put(k.P("mug2"), new Vector3(-12.9f, 2.2f, -22.9f)); k.Put(k.P("mug1"), new Vector3(-13.3f, 2.2f, -21.2f));
        }
    }
}
