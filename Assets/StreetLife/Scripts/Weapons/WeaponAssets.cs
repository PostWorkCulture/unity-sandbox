using UnityEngine;

namespace StreetLife
{
    /// The weapon data the game loads at runtime (kept in a Resources folder so builds include it).
    /// Made by the weapon setup (Editor/StreetLifeWeaponsSetup.cs) whenever guns_materials.json changes.
    public class WeaponAssets : ScriptableObject
    {
        public TextAsset gunsJson, gunsBin;
        public Material[] materials;
        public Shader fxShader;
        [Tooltip("Fingerprint of the material table these were made from")] public string dataStamp;
    }
}
