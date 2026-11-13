// Progression (progression chat): the player's saved progress — XP and level, win/loss record, lifetime stats,
// unlocks and achievements. Saved on the device after every round (a JSON file; PlayerPrefs on WebGL, which keeps it
// in the browser's storage). Works with no account; cloud sync comes later (see claude/street-life-progression.md).
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace StreetLife
{
    [Serializable]
    public class ProgressData
    {
        public int version = 1;
        public int xp;
        public string title = "";            // equipped title id ("" = none)
        public string favFinisher = "";      // "" = Random
        public long updated;                 // unix seconds of the last change

        // lifetime stats
        public int matches, matchesWon, roundsWon, roundsLost, roundsDrawn;
        public int kills, deaths, headshots, shots, hits;
        public float damage;
        public int flawless, streak, bestStreak, bestHardStreak, hardStreak;
        public int glassBroken, propsBroken;  // filled once the match chat's Destruction.Broken event exists

        public List<Record> record = new List<Record>();
        public List<Count> finishers = new List<Count>();
        public List<string> unlocks = new List<string>();
        public List<string> found = new List<string>();          // collectables (P2)
        public List<string> achievements = new List<string>();   // (P2)

        [Serializable] public class Record { public string map, difficulty, rules; public int won, lost; }
        [Serializable] public class Count { public string id; public int n; }
    }

    public static class Progress
    {
        public const int XpPerLevel = 100;
        const string File_ = "streetlife-progress.json", PrefKey = "StreetLife.progress";

        static ProgressData data;
        public static ProgressData Data { get { if (data == null) Load(); return data; } }

        /// Fires after XP is added: (amount, reason).
        public static event Action<int, string> XpGained;
        /// Fires when something new is unlocked: (unlock id).
        public static event Action<string> Unlocked;

        public static int Level => LevelOf(Data.xp);
        public static int LevelOf(int xp) => Mathf.Max(0, xp) / XpPerLevel + 1;
        public static int XpIntoLevel => Mathf.Max(0, Data.xp) % XpPerLevel;

        // ------------------------------------------------------------------ changes
        public static void AddXp(int n, string why)
        {
            if (n <= 0) return;
            int before = Level;
            Data.xp += n;
            Touch();
            XpGained?.Invoke(n, why);
            for (int lv = before + 1; lv <= Level; lv++)
                foreach (var id in ProgressRewards.ForLevel(lv)) Unlock(id);
        }

        public static bool Has(string id) => Data.unlocks.Contains(id);

        public static bool Unlock(string id)
        {
            if (string.IsNullOrEmpty(id) || Has(id)) return false;
            Data.unlocks.Add(id); Touch();
            Debug.Log("Progression: unlocked " + id);
            Unlocked?.Invoke(id);
            return true;
        }

        public static ProgressData.Record RecordFor(string map, string difficulty, string rules)
        {
            foreach (var r in Data.record) if (r.map == map && r.difficulty == difficulty && r.rules == rules) return r;
            var n = new ProgressData.Record { map = map, difficulty = difficulty, rules = rules };
            Data.record.Add(n); return n;
        }

        public static void Bump(List<ProgressData.Count> list, string id, int by = 1)
        {
            foreach (var c in list) if (c.id == id) { c.n += by; Touch(); return; }
            list.Add(new ProgressData.Count { id = id, n = by }); Touch();
        }

        public static int CountOf(List<ProgressData.Count> list, string id) { foreach (var c in list) if (c.id == id) return c.n; return 0; }

        static void Touch() => Data.updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ------------------------------------------------------------------ disk
        static string PathOf => System.IO.Path.Combine(Application.persistentDataPath, File_);
        static bool UsePrefs => Application.platform == RuntimePlatform.WebGLPlayer;

        public static void Load()
        {
            data = null;
            try
            {
                string json = null;
                if (UsePrefs) json = PlayerPrefs.GetString(PrefKey, null);
                else
                {
                    if (File.Exists(PathOf)) json = File.ReadAllText(PathOf);
                    if (string.IsNullOrEmpty(json) && File.Exists(PathOf + ".bak")) json = File.ReadAllText(PathOf + ".bak");
                }
                if (!string.IsNullOrEmpty(json)) data = JsonUtility.FromJson<ProgressData>(json);
            }
            catch (Exception e) { Debug.LogWarning("Progression: couldn't read the save, starting fresh (" + e.Message + ")"); }
            if (data == null) data = new ProgressData();
            if (data.record == null) data.record = new List<ProgressData.Record>();
            if (data.finishers == null) data.finishers = new List<ProgressData.Count>();
            if (data.unlocks == null) data.unlocks = new List<string>();
            if (data.found == null) data.found = new List<string>();
            if (data.achievements == null) data.achievements = new List<string>();
            // anything a level already earned (also fills in rewards added after the save was made)
            for (int lv = 2; lv <= LevelOf(data.xp); lv++) foreach (var id in ProgressRewards.ForLevel(lv)) if (!data.unlocks.Contains(id)) data.unlocks.Add(id);
            Debug.Log($"Progression: loaded (level {LevelOf(data.xp)}, {data.xp} XP, {data.matches} matches)");
        }

        public static void Save()
        {
            if (data == null) return;
            try
            {
                var json = JsonUtility.ToJson(data, true);
                if (UsePrefs) { PlayerPrefs.SetString(PrefKey, json); PlayerPrefs.Save(); return; }
                // write beside, then swap, keeping the last good copy as .bak
                var tmp = PathOf + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(PathOf)) File.Copy(PathOf, PathOf + ".bak", true);
                File.Copy(tmp, PathOf, true);
                File.Delete(tmp);
            }
            catch (Exception e) { Debug.LogWarning("Progression: couldn't save (" + e.Message + ")"); }
        }

        /// Editor / testing: wipe everything.
        public static void ResetAll() { data = new ProgressData(); Save(); Debug.Log("Progression: progress reset"); }
    }
}
