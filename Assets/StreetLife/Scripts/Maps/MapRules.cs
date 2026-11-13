// Maps chat: the per-map ground rules from the web game, read by the movement and nav-grid hooks (FighterEdge.cs, NavGrid):
//  Sky High: an open edge. There's no floor past the roof (|x| > 18.6 or |z| > 22.6): walk or get knocked off and you fall.
//  Lagoon:   deep water round the boardwalks (the web game's lgSd > .3): fall in and you sink. Bots on their feet won't step off.
//  Beach Please: the sea past the shoreline slows you to 80% while you wade (Waterside.cs).
// Formulas are in the web game's axes (z negated) so they match index.html line for line.
using UnityEngine;

namespace StreetLife
{
    public static class MapRules
    {
        public static string Id { get; private set; } = "street";
        /// The map has places with no floor (Sky High, Lagoon).
        public static bool HasEdge { get; private set; }
        /// Falling off means landing in water (Lagoon): splash and sink rather than tumble.
        public static bool DeepWater { get; private set; }
        /// The map has a sea you wade into (Beach Please).
        public static bool Wading { get; private set; }

        public static void Set(string id)
        {
            Id = id; HasEdge = id == "skyhigh" || id == "lagoon"; DeepWater = id == "lagoon"; Wading = id == "wreck";
        }

        /// No floor here (Unity x, z).
        public static bool Drop(float x, float z)
        {
            if (!HasEdge) return false;
            if (Id == "skyhigh") return Mathf.Abs(x) > 18.6f || Mathf.Abs(z) > 22.6f;
            return LgSd(x, -z) > .3f;
        }

        /// Beach Please: out past the shoreline, in the shallows (Unity x, z).
        public static bool Wade(float x, float z) => Wading && x > Shore(-z) + .4f;

        public static float Shore(float zWeb) => 18f + 3f * Mathf.Sin(zWeb * .11f) + 1.5f * Mathf.Sin(zWeb * .27f + 1f);

        /// Beach Please water height (Unity x, z), the web game's wcWave, for splashes.
        public static float BeachWaterY(float x, float z, float t)
        {
            float zw = -z, d = x - Shore(zw);
            float ph = Mathf.PI * 2 / 13f * d + Mathf.PI * 2 / 6f * t + .9f * Mathf.Sin(zw * .045f) + .5f * Mathf.Sin(zw * .11f + 1.3f);
            float c = .5f + .5f * Mathf.Sin(ph);
            float A = (.08f + .2f * Mathf.Exp(-Mathf.Pow((d - 6f) / 7f, 2f))) * SS(-.3f, 3f, d);
            return .28f + A * (Mathf.Pow(c, 2.5f) * 1.7f - .5f);
        }

        /// Lagoon water height (Unity x, z), the web game's WY + lgH.
        public static float LagoonWaterY(float x, float z, float t)
        {
            float px = x, pz = -z;
            float h = .16f * Mathf.Sin(px * .21f + pz * .13f + t * .9f) + .11f * Mathf.Sin(-px * .14f + pz * .27f + t * 1.15f) + .05f * Mathf.Sin(px * .9f + pz * .7f + t * 2.3f) + .03f * Mathf.Sin(px * 1.7f - pz * 1.3f + t * 3.1f);
            return -2.4f + h;
        }

        static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        // the web game's LG = {X:16, PZ0:6, PZ1:22, W0:5, W1:11, NX0:11, NX1:14, NZ:1.75}: two pavilions, two crossovers, the shop nook
        static float SdB(float px, float pz, float cx, float cz, float hx, float hz)
        {
            float qx = Mathf.Abs(px - cx) - hx, qz = Mathf.Abs(pz - cz) - hz;
            return Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qz, 0) * Mathf.Max(qz, 0)) + Mathf.Min(Mathf.Max(qx, qz), 0);
        }
        public static float LgSd(float px, float pz)
        {
            float a = Mathf.Min(SdB(px, pz, 0, 14, 16, 8), SdB(px, pz, 0, -14, 16, 8));
            float b = Mathf.Min(SdB(px, pz, 8, 0, 3, 6.2f), SdB(px, pz, -8, 0, 3, 6.2f));
            return Mathf.Min(Mathf.Min(a, b), SdB(px, pz, 12.5f, 0, 1.5f, 1.75f));
        }
    }
}
