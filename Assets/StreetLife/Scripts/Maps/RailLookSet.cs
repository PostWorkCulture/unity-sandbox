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
        }
        public Surface[] surfaces;
        [Tooltip("The goods shed's sign, reading just SHED")] public Texture2D shedSign;

        public Surface Get(string n) { if (surfaces != null) foreach (var s in surfaces) if (s != null && s.name == n) return s; return null; }
    }
}
