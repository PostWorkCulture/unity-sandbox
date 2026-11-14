// Every character from the imok 1v1 web game (Data/Characters, exported by the StreetLife exporter's exportall.js):
// the model data, one material table for all of them, and the CHARS table (name, ability, speed, health, colour).
// Lives in Resources so anything can load it: CharacterSet.Load(). Built by Editor/CharacterSetup.cs.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [CreateAssetMenu(menuName = "Street Life/Character Set")]
    public class CharacterSet : ScriptableObject
    {
        [Serializable] public class Entry { public string id; public TextAsset json, bin; }

        [Serializable]
        public class Info
        {
            public string id, name, desc, ability, color;
            public float cooldown, hp, speed;
            public string[] killFx;
            public bool hasModel;
            public Color Colour => Hex.ToColor(color);
        }

        /// Arm lengths and hand offsets for two-bone IK (web-game space), per character.
        [Serializable] public class Arm { public float up, fore; public float[] hand; }
        [Serializable] public class ArmSet { public Arm L, R; public bool elbows; }

        [Serializable] class CatalogJson { public Info[] characters; }
        [Serializable] class ArmsJson { public ArmSet arms; public float handY; }

        public TextAsset catalog;
        public Entry[] characters;
        public Material[] materials;
        [Tooltip("Fingerprint of the export these were made from; a new export rebuilds them")]
        public string stamp;

        [NonSerialized] Info[] infos;
        [NonSerialized] Dictionary<string, ArmsJson> arms;

        static CharacterSet cached;
        public static CharacterSet Load() { if (!cached) cached = Resources.Load<CharacterSet>("StreetLifeCharacters"); return cached; }

        void OnEnable() { infos = null; arms = null; playable = null; }

        /// All characters in the web game's menu order (Wednesday first).
        public Info[] All
        {
            get
            {
                if (infos == null) infos = catalog ? (JsonUtility.FromJson<CatalogJson>(catalog.text)?.characters ?? new Info[0]) : new Info[0];
                return infos;
            }
        }

        /// Characters only bots play (Pete, 14 Nov): not offered to players in the menu or F3, never a player's character.
        public static readonly HashSet<string> BotOnly = new HashSet<string> { "dida", "lia" };
        public static bool IsBotOnly(string id) => id != null && BotOnly.Contains(id);

        [NonSerialized] Info[] playable;
        /// The characters a player can pick (All without the bot-only ones), in menu order.
        public Info[] Playable
        {
            get
            {
                if (playable == null || playable.Length == 0) { var l = new List<Info>(); foreach (var i in All) if (!IsBotOnly(i.id)) l.Add(i); playable = l.ToArray(); }
                return playable;
            }
        }

        public Info Get(string id) { foreach (var i in All) if (i.id == id) return i; return null; }
        public Entry EntryOf(string id) { if (characters != null) foreach (var e in characters) if (e != null && e.id == id) return e; return null; }
        public bool Has(string id) { var e = EntryOf(id); return e != null && e.json && e.bin; }

        /// Arm measurements for character <id> (null for the blocky Isaac, who has no elbows).
        public ArmSet Arms(string id)
        {
            if (arms == null) arms = new Dictionary<string, ArmsJson>();
            if (!arms.TryGetValue(id, out var a)) { var e = EntryOf(id); a = e != null && e.json ? JsonUtility.FromJson<ArmsJson>(e.json.text) : null; arms[id] = a; }
            return a != null && a.arms != null && a.arms.elbows ? a.arms : null;
        }
    }
}
