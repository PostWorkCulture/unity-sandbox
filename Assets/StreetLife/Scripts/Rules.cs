// Match rules chosen in the menu (match chat): mode (Duels or Rumble), players (1v1, 2v2, free-for-all with 2, 4 or 6),
// and whether Simple 1v1 rules apply. Read by everything that behaves differently by mode. Saved in PlayerPrefs.
using UnityEngine;

namespace StreetLife
{
    public static class Rules
    {
        const string P = "StreetLife.";
        /// "duels" or "rumble"
        public static string Mode { get => PlayerPrefs.GetString(P + "gameMode", "duels"); set { PlayerPrefs.SetString(P + "gameMode", value); } }
        /// "1v1", "2v2" or "ffa"
        public static string Players { get => PlayerPrefs.GetString(P + "players", "1v1"); set { PlayerPrefs.SetString(P + "players", value); } }
        /// fighters in a free-for-all: 2, 4 or 6 (you plus bots)
        public static int FfaN { get { int n = PlayerPrefs.GetInt(P + "ffaN", 4); return n == 2 || n == 4 || n == 6 ? n : 4; } set => PlayerPrefs.SetInt(P + "ffaN", value); }

        public static bool Rumble => Mode == "rumble";
        public static bool TwoVTwo => Players == "2v2";
        public static bool Ffa => Players == "ffa";
        public static bool OneVOne => !TwoVTwo && !Ffa;
        /// deathmatch: free-for-all, respawns, first to 10 kills
        public static bool Deathmatch => Ffa;
        public const int DM_KILLS = 10, ROUNDS_TO_WIN = 5;
        public static int FighterCount => TwoVTwo ? 4 : Ffa ? FfaN : 2;
        public static float RoundTime => Deathmatch ? 600 : OneVOne ? 90 : 120;
        public static string Tag => Rumble ? "Rumble" + (OneVOne ? "" : Ffa ? " · free for all" : " · 2v2") : OneVOne ? "1v1" : Ffa ? "Free for all" : "2v2";
    }
}
