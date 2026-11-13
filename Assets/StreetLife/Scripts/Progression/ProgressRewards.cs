// Progression (progression chat): XP amounts and what each level unlocks. Everything is cosmetic: no character,
// map or finisher is ever locked (Pete, 13 Nov). Values move to Remote Config in P5.
using System.Collections.Generic;

namespace StreetLife
{
    public static class ProgressRewards
    {
        // ---- XP, as in the web game's Duels (endRound: +10 a round won, +5 otherwise), plus a match-win bonus
        public const int RoundWon = 10, RoundOther = 5, MatchWon = 20;

        // ---- titles (shown on the end screen, the profile and later the leaderboards)
        public struct Title { public string id, name; public int level; }
        public static readonly Title[] Titles =
        {
            new Title { id = "title:rookie",     name = "Rookie",           level = 2 },
            new Title { id = "title:streetrat",  name = "Street Rat",       level = 4 },
            new Title { id = "title:roofrunner", name = "Roof Runner",      level = 6 },
            new Title { id = "title:sharp",      name = "Sharpshooter",     level = 8 },
            new Title { id = "title:brawler",    name = "Brawler",          level = 10 },
            new Title { id = "title:menace",     name = "Menace",           level = 13 },
            new Title { id = "title:finisher",   name = "The Finisher",     level = 16 },
            new Title { id = "title:wcatcher",   name = "W Catcher",        level = 20 },
            new Title { id = "title:topdog",     name = "Top Dog",          level = 25 },
            new Title { id = "title:untouch",    name = "Untouchable",      level = 30 },
            new Title { id = "title:legend",     name = "Legend",           level = 40 },
            new Title { id = "title:icon",       name = "Street Life Icon", level = 50 },
        };

        static readonly List<string> none = new List<string>();
        static Dictionary<int, List<string>> byLevel;

        public static List<string> ForLevel(int level)
        {
            if (byLevel == null)
            {
                byLevel = new Dictionary<int, List<string>>();
                foreach (var t in Titles) { if (!byLevel.TryGetValue(t.level, out var l)) byLevel[t.level] = l = new List<string>(); l.Add(t.id); }
            }
            return byLevel.TryGetValue(level, out var r) ? r : none;
        }

        public static string NameOf(string id)
        {
            foreach (var t in Titles) if (t.id == id) return t.name;
            return id;
        }

        /// The next level that unlocks something, or 0 when there's nothing left.
        public static int NextRewardLevel(int level)
        {
            int best = 0;
            foreach (var t in Titles) if (t.level > level && (best == 0 || t.level < best)) best = t.level;
            return best;
        }
    }
}
