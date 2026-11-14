// Railed look: the surfaces Railed's own materials are swapped for at play time (see RailLook.cs). The asset
// (Resources/RailLook.asset) and its materials are made by Street Life > Maps > Rebuild Railed surfaces (Editor/RailSetup.cs).
using System;
using UnityEngine;

namespace StreetLife
{
    public class RailLookSet : ScriptableObject
    {
        [Serializable]
        public class Surface
        {
            public string name;
            public Material material;
            [Tooltip("How many metres one repeat of the texture covers")] public float metres = 2;
            [Tooltip("The texture is near white and takes the colour of the material it replaces")] public bool tinted;
            [Tooltip("Not tinted: the colour the texture was painted in, so a darker or lighter original still comes out darker or lighter")] public Color reference = Color.white;
            [Tooltip("Smoothness follows the original material (painted metal)")] public bool keepGloss;
            [Tooltip("At least this metallic, whatever the original had (bare ribbed steel)")] public float metallic;
            [Tooltip("Multiplied into the colour: ribbed steel is cooled a little so grey sheds stay steel in the evening sun and don't turn to timber")] public Color shift = Color.white;
            [Tooltip("On walls the texture runs up and down instead of along (posts and piles: the grain follows the timber)")] public bool upright;
        }
        public Surface[] surfaces;
        [Tooltip("Beach Please: the sand (the map's own colours drawn into a texture, with ripples as a detail map)")] public Material ground;
        [Tooltip("Striped cushions: their own picture goes in, a canvas weave lies over it as a detail map")] public Material cushion;
        [Tooltip("The fuel drums' hazard label")] public Texture2D drumLabel;
        [Tooltip("The goods shed's sign, reading just SHED")] public Texture2D shedSign;

        public Surface Get(string n) { if (surfaces != null) foreach (var s in surfaces) if (s != null && s.name == n) return s; return null; }
    }
}
