// Puts the chosen web-game character on a CharacterRig before it builds: the player gets the one picked in the
// character picker (F3, remembered between sessions; Wednesday by default) and the bot gets a random other one each match
// (or a fixed one picked in the picker). Speed and health come from the web game's CHARS table.
//
// Works alongside the rest of the project without changing it: it swaps the rig's charJson / charBin and gives it a copy of
// its MaterialLibrary whose 'character' materials are the shared character table. RoundManager calls ForBot() for the bot.
// Other code can read CharacterSkin.Of(rig) and listen to CharacterSkin.Changed.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StreetLife
{
    public static class CharacterSkin
    {
        const string PlayerPref = "StreetLife.character", BotPref = "StreetLife.botCharacter";

        /// The player's character id (web game id: wednesday, crusty, angel, antihero, jet, ghost, dracula, astronaut, lilpink,
        /// brute, blaze, blink, tank, frost, patch, thorn, thor, isaac).
        public static string PlayerChoice
        {
            get { var id = PlayerPrefs.GetString(PlayerPref, "wednesday"); return CharacterSet.IsBotOnly(id) ? "wednesday" : id; }   // bot-only characters are never yours
            set { if (CharacterSet.IsBotOnly(value)) return; PlayerPrefs.SetString(PlayerPref, value); PlayerPrefs.Save(); }
        }

        /// The bot's character, or empty for a random one each match.
        public static string BotChoice
        {
            get => PlayerPrefs.GetString(BotPref, "");
            set { PlayerPrefs.SetString(BotPref, value ?? ""); PlayerPrefs.Save(); }
        }

        /// A rig got a new character (rig, character id).
        public static event Action<CharacterRig, string> Changed;

        static readonly Dictionary<CharacterRig, string> applied = new Dictionary<CharacterRig, string>();
        static readonly Dictionary<MaterialLibrary, MaterialLibrary> clones = new Dictionary<MaterialLibrary, MaterialLibrary>();

        /// The character id a rig is wearing (null if it still has the scene's own).
        public static string Of(CharacterRig rig) => rig && applied.TryGetValue(rig, out var id) ? id : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // after the scene's Awake, before any Start: the player's rig hasn't built yet
        static void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            applied.Clear();
            var set = CharacterSet.Load(); if (!set) return;
            foreach (var rig in UnityEngine.Object.FindObjectsByType<CharacterRig>())
                if (rig.GetComponent<PlayerController>()) Apply(rig, PlayerChoice);
            if (!UnityEngine.Object.FindAnyObjectByType<CharacterPicker>()) new GameObject("Character picker").AddComponent<CharacterPicker>();
            if (!UnityEngine.Object.FindAnyObjectByType<CharacterShowcase>()) new GameObject("Character showcase").AddComponent<CharacterShowcase>();
            if (!UnityEngine.Object.FindAnyObjectByType<CharacterClimb>()) new GameObject("Character climbing").AddComponent<CharacterClimb>();
        }

        /// Called by RoundManager when it makes the bot: a random character other than yours (or the one picked for it).
        public static void ForBot(CharacterRig rig)
        {
            var set = CharacterSet.Load(); if (!set || !rig) return;
            string want = BotChoice;
            if (string.IsNullOrEmpty(want) || !set.Has(want))
            {
                var mine = PlayerChoice; var pool = new List<string>();
                // no two fighters in a match wear the same character (while there are enough to go round)
                var taken = new HashSet<string>(); foreach (var kv in applied) if (kv.Key && kv.Key != rig) taken.Add(kv.Value);
                foreach (var c in set.All) if (c.id != mine && set.Has(c.id) && !taken.Contains(c.id)) pool.Add(c.id);
                if (pool.Count == 0) foreach (var c in set.All) if (c.id != mine && set.Has(c.id)) pool.Add(c.id);
                if (pool.Count == 0) return;
                want = pool[UnityEngine.Random.Range(0, pool.Count)];
            }
            Apply(rig, want);
        }

        /// Dress a rig as character <id>. Before the rig has built this is free; after, the model is rebuilt in place.
        public static bool Apply(CharacterRig rig, string id)
        {
            var set = CharacterSet.Load();
            if (!set || !rig) return false;
            var e = set.EntryOf(id);
            if (e == null || !e.json || !e.bin) { Debug.LogWarning("Street Life: no exported character '" + id + "'"); return false; }

            rig.charJson = e.json; rig.charBin = e.bin;
            if (rig.library && set.materials != null && set.materials.Length > 0 && !clones.ContainsValue(rig.library))
            {
                if (!clones.TryGetValue(rig.library, out var lib) || !lib)
                {
                    lib = UnityEngine.Object.Instantiate(rig.library);
                    lib.name = rig.library.name + " (characters)";
                    lib.character = set.materials;
                    clones[rig.library] = lib;
                }
                rig.library = lib;
            }
            // the match chat tints its stand-in bot; a real character needs no tint
            var tint = typeof(CharacterRig).GetField("tint");
            if (tint != null && tint.FieldType == typeof(Color)) tint.SetValue(rig, Color.white);

            var info = set.Get(id); var f = rig.fighter ? rig.fighter : rig.GetComponent<Fighter>();
            if (f && info != null)
            {
                f.characterName = info.name;
                f.charSpeed = info.speed > 0 ? info.speed : 1f;
                float frac = f.hpMax > 0 ? f.hp / f.hpMax : 1; f.hpMax = info.hp > 0 ? info.hp : 100f; f.hp = f.hpMax * frac;
            }
            applied[rig] = id;
            if (rig.Built)
            {
                var old = rig.transform.Find("root");
                if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
                rig.Build();
            }
            Changed?.Invoke(rig, id);
            return true;
        }
    }
}
