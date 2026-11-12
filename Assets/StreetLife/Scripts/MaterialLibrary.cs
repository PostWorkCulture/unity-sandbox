using UnityEngine;

namespace StreetLife
{
    /// Materials made from the web game's material table (one per exported material id).
    [CreateAssetMenu(menuName = "Street Life/Material Library")]
    public class MaterialLibrary : ScriptableObject
    {
        public Material[] map;
        public Material[] character;
        public Material[] guns;   // weapons chat
        public Material unlitBase;
        public Material lineBase;
        [Tooltip("Fingerprint of the material tables these were made from; a new export rebuilds them automatically")]
        public string dataStamp;
    }
}
