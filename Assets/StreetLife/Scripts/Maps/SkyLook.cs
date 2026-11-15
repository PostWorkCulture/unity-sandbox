// Sky High's look (MapLooks): a helipad on a tower at golden hour, made to feel like the roof of an expensive building.
//  - Surfaces: paving slabs on the deck, cast concrete on the stair heads and the helipad, painted and bare steel on the
//    air-con units, tanks, frames and rails, roofing felt on the stair-head roofs.
//  - Light strips: a warm line round the whole roof edge inside the glass, and one under the lip of the helipad.
//  - Red warning lights that blink on the stair heads and the water tanks.
//  - A roof-terrace lounge along both long edges: high glass tables with cocktails, champagne on ice, lanterns and stools,
//    all loose (walk into them and they go over) and all smashable. Nothing new blocks the way.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public static class SkyLook
    {
        public static readonly MapLook Def = new MapLook
        {
            id = "skyhigh", set = "SkyLook", stem = "SkyLight",
            rules = new Dictionary<string, string>
            {
                { "b9b2a8+", "slabs" }, { "cfcac0+", "slabs" },
                { "7d7f86", "concrete" }, { "8d8f95", "concrete" }, { "5f6269", "concrete" }, { "6b7079", "concrete" }, { "5a5f67", "concrete" },
                { "e6decf+", "concrete" }, { "bdb4a5+", "concrete" }, { "55575d", "concrete" }, { "8a8c92", "concrete" }, { "9a9ea4", "concrete" }, { "9aa0a8", "concrete" },
                { "c3c9d0", "metal" }, { "c9cdd2", "metal" }, { "eef0f3", "metal" }, { "a9adb3", "metal" }, { "7b8088", "metal" }, { "5d6168", "metal" }, { "d6d9de", "metal" },
                { "202328", "metal" }, { "2c2f35", "metal" }, { "16171c", "metal" }, { "d9dde3", "metal" }, { "bfc3c9", "metal" }, { "a3a9b1", "metal" }, { "151619", "metal" },
                { "5e7f99", "metal" }, { "4e6b82", "metal" }, { "39434d", "metal" }, { "b8733d", "metal" }, { "f4c22b", "metal" }, { "ff6a1a", "metal" }, { "c4c8ce+", "metal" },
                { "8e9096+", "roofing" }, { "4a4d54+", "roofing" },
            },
            strength = .9f,
            probeCentre = new Vector3(0, 4f, 0), probeSize = new Vector3(40, 10, 48),
            cloudCover = .55f, cloudLit = new Color(1f, .8f, .62f), cloudShade = new Color(.55f, .38f, .45f),
            dress = Dress,
        };

        static void Dress(MapWorld w, RailLookSet set)
        {
            var k = TikiBar.Kit(w.transform, "Sky High dressing");
            if (!k) return;
            var led = k.Part(k.Glow("#ffe2b0", "#ffbe6e", 3.2f));
            // a warm line round the roof edge, inside the glass
            foreach (float x in new[] { -18.22f, 18.22f }) led.Box(new Vector3(x, .045f, 0), new Vector3(.07f, .05f, 44.5f));
            foreach (float z in new[] { -22.22f, 22.22f }) led.Box(new Vector3(0, .045f, z), new Vector3(36.4f, .05f, .07f));
            // and under the lip of the helipad, between its four flights of steps
            foreach (float s in new[] { -1f, 1f })
                foreach (float a in new[] { -3.5f, 3.5f })
                {
                    led.Box(new Vector3(a, 1.4f, s * 5.27f), new Vector3(3.4f, .035f, .03f));
                    led.Box(new Vector3(s * 5.27f, 1.4f, a), new Vector3(.03f, .035f, 3.4f));
                }
            // red warning lights: a pole at each corner of the stair heads, one on each water tank
            var red = k.Glow("#5a0c0c", "#ff2a1a", 1f); var steel = k.Part(k.steel);
            var blink = k.gameObject.AddComponent<MapBlink>(); blink.glow = red; blink.on = new Color(1f, .12f, .06f) * 9f; blink.power = 3f;
            var poles = new List<Vector3>();
            foreach (float z in new[] { -16.2f, -22f, 16.2f, 22f }) foreach (float x in new[] { -4.4f, 4.4f }) poles.Add(new Vector3(x, 7.4f, z));
            poles.Add(new Vector3(12.5f, 4.7f, -15.5f)); poles.Add(new Vector3(-12.5f, 4.7f, 15.5f));
            foreach (var p in poles)
            {
                TikiBar.Tube(steel, p, p + Vector3.up * .55f, .025f, 6);
                TikiBar.Ball(k.Part(red), p + Vector3.up * .66f, .12f, 10, 6);
            }
            foreach (var p in new[] { poles[8], poles[9], new Vector3(0, 8.1f, -19f), new Vector3(0, 8.1f, 19f) })
            {
                var go = new GameObject("Warning light"); go.transform.SetParent(k.fittings, false); go.transform.position = p + Vector3.up * .8f;
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, .16f, .1f); l.range = 9f; l.intensity = 0; l.shadows = LightShadows.None;
                blink.lamps.Add(l);
            }
            k.Flush();
            // the lounge: tables along the west and the east glass, clear of the spawns and the ways round the units
            k.TableSet(new Vector3(-16.2f, 0, 2.4f)); k.ChampagneTable(new Vector3(-16.4f, 0, 5.6f)); k.TableSet(new Vector3(-15.8f, 0, -6.2f));
            k.TableSet(new Vector3(16.2f, 0, -2.4f)); k.ChampagneTable(new Vector3(16.4f, 0, -5.6f)); k.TableSet(new Vector3(15.8f, 0, 6.2f));
            k.AddLamp(new Vector3(-16f, 2.7f, 4f), new Color(1f, .86f, .7f), 1.3f, 5f, 0);
            k.AddLamp(new Vector3(16f, 2.7f, -4f), new Color(1f, .86f, .7f), 1.3f, 5f, 0);
            k.Lod(.1f);
            Debug.Log("Sky High: " + k.Count + " loose things");
        }
    }
}
