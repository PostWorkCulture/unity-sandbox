// Lagoon's look (MapLooks): two pavilions on stilts at sunset, with a proper tiki bar on each deck.
//  - Surfaces: deck boards, weathered stilts, bamboo slats, thatch.
//  - The two bars: the plain block counters, tops, back units and block bottles are replaced by a marble-topped counter (it
//    keeps its bamboo front and its collision), a teak back unit with a row of real bottles, a rack of hanging glasses under
//    the canopy, drinks, shakers, champagne on ice and lanterns along the top, and stools in front.
//  - High glass tables on both decks and champagne tables under the pavilion. All loose, all smashable; nothing new blocks.
//  - The torches' light flickers.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public static class LagoonLook
    {
        public static readonly MapLook Def = new MapLook
        {
            id = "lagoon", set = "LagoonLook", stem = "LagoonLight",
            rules = new Dictionary<string, string>
            {
                { "b88250+", "planks" }, { "5a3a22", "planks" }, { "b88c46", "planks" },
                { "6e4a2c", "post" }, { "8a6a3a", "post" },
                { "c8a060", "bamboo" },
                { "d4ad62+", "thatch" },
            },
            // the old bars' blocks: counters, tops, back units and the four colours of block bottle
            hide = new HashSet<string> { "8a7a66", "9a948a", "6e5a46", "3f8f4a", "7a3a1e", "d8c070", "2a5a8a" },
            strength = .9f,
            probeCentre = new Vector3(0, 2.2f, 0), probeSize = new Vector3(40, 7, 52),
            cloudCover = .52f, cloudLit = new Color(1f, .82f, .86f), cloudShade = new Color(.55f, .42f, .62f),
            dress = Dress,
        };

        static void Dress(MapWorld w, RailLookSet set)
        {
            var k = TikiBar.Kit(w.transform, "Lagoon dressing");
            if (!k) return;
            var teak = k.Part(k.Surf("planks", "#5b3a22")); var dark = k.Part(k.Surf("planks", "#3f2716")); var marble = k.Part(k.Surf("marble", "#ece7dd"));
            var br = k.Part(k.brass); var led = k.Part(k.Glow("#ffe2b0", "#ffbe6e", 3.2f)); var shelf = k.Part(k.Glass("#dff3f0", .28f));
            foreach (float s in new[] { -1f, 1f })
            {
                // counter (behind its bamboo slats) and back unit, where the old blocks stood: the collision is the map's own
                teak.Box(new Vector3(0, .6f, s * 11.03f), new Vector3(6f, 1.2f, 1.04f));
                marble.Box(new Vector3(0, 1.235f, s * 11f), new Vector3(6.3f, .09f, 1.4f), .02f, Quaternion.identity);
                br.Box(new Vector3(0, 1.182f, s * 10.34f), new Vector3(6.24f, .016f, .05f));
                led.Box(new Vector3(0, 1.165f, s * 10.4f), new Vector3(6.1f, .02f, .03f));
                dark.Box(new Vector3(0, .95f, s * 13.1f), new Vector3(5.2f, 1.9f, .5f));
                marble.Box(new Vector3(0, 1.945f, s * 13.1f), new Vector3(5.3f, .09f, .6f), .02f, Quaternion.identity);
                br.Box(new Vector3(0, 1.82f, s * 12.845f), new Vector3(5.2f, .02f, .012f));
                led.Box(new Vector3(0, 1.885f, s * 12.83f), new Vector3(5.1f, .016f, .02f));
                // a rack for hanging glasses under the canopy, over the counter
                foreach (float dz in new[] { -.028f, .028f }) TikiBar.Tube(br, new Vector3(-2.5f, 2.5f, s * 11f + dz), new Vector3(2.5f, 2.5f, s * 11f + dz), .009f, 6);
                foreach (float x in new[] { -2.4f, 0f, 2.4f }) TikiBar.Tube(br, new Vector3(x, 2.5f, s * 11f), new Vector3(x, 2.92f, s * 11f), .011f, 6);
            }
            k.Flush();
            foreach (float s in new[] { -1f, 1f })
            {
                float face = s > 0 ? 0 : 180;   // labels towards the customers
                for (float x = -2.3f; x < 2.35f; x += k.R(.33f, .38f))
                {
                    var tint = TikiBar.Tints[k.rng.Next(TikiBar.Tints.Length)];
                    k.Put(k.Bottle(k.rng.Next(4), k.rng.Next(12), tint.hex, tint.a), new Vector3(x, 1.99f, s * (13.12f + k.R(-.03f, .03f))), face + k.R(-18, 18));
                }
                var glass = k.G("wine", null);
                for (float x = -2.3f; x < 2.35f; x += .2f) k.Put(glass, new Vector3(x, 2.512f, s * 11f), 0, true);
                // along the counter: the customers' side is towards the middle of the map
                float zf = s * 10.72f, zb = s * 11.3f; int n = 0;
                for (float x = -2.6f; x < 2.7f; x += .75f, n++)
                {
                    switch (n % 4)
                    {
                        case 0: k.Drink(new Vector3(x - .12f, 1.28f, zf)); k.Drink(new Vector3(x + .16f, 1.28f, zb)); break;
                        case 1: k.Put(k.P("shaker"), new Vector3(x, 1.28f, zb)); k.Drink(new Vector3(x + .08f, 1.28f, zf)); break;
                        case 2: k.Put(k.P("lantern"), new Vector3(x, 1.28f, s * 11f)); break;
                        default: k.Bucket(new Vector3(x, 1.28f, zb)); k.Put(k.G("coupe", "#f3d98a"), new Vector3(x + .26f, 1.28f, zf)); break;
                    }
                }
                foreach (float x in new[] { -2.25f, -.75f, .75f, 2.25f }) k.Put(k.P("stool"), new Vector3(x, 0, s * 9.7f), k.R(0, 360));
                k.AddLamp(new Vector3(0, 2.3f, s * 11.9f), new Color(1f, .82f, .6f), 1.5f, 5f, 0);
                // tables out on the deck
                k.TableSet(new Vector3(-9.5f, 0, s * 9.5f)); k.TableSet(new Vector3(9.8f, 0, s * 13.5f));
            }
            k.ChampagneTable(new Vector3(8.2f, 0, -3f)); k.ChampagneTable(new Vector3(8.2f, 0, 3f));
            k.Lod(.1f);
            // torch light flickers
            var gen = w.builder ? w.builder.transform.Find("__Generated") : null;
            if (gen)
            {
                var f = k.gameObject.AddComponent<MapFlicker>();
                foreach (var l in gen.GetComponentsInChildren<Light>()) if (l.type == LightType.Point && l.range > 12.5f && l.range < 14.5f) f.Add(l);
            }
            Debug.Log("Lagoon: " + k.Count + " loose things");
        }
    }
}
