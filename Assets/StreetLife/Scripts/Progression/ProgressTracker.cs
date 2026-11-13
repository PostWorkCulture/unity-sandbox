// Progression (progression chat): listens to the match and writes progress. Added by itself at every scene load
// where there's a map, together with the end-screen XP panel and the main menu's Profile.
//   Round over: +10 XP if you took it, +5 otherwise (the web game's rule); saved straight away.
//   Match over: +20 XP for a win, the record for this map / difficulty / rules, lifetime stats, win streaks.
//   Finishing moves you land are counted by move.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(-40)]
    public class ProgressTracker : MonoBehaviour
    {
        public static ProgressTracker I;

        /// What this match earned, for the end screen.
        public class MatchSummary
        {
            public bool active, won, flawless;
            public int xp, levelBefore, xpBefore;
            public readonly List<string> unlocked = new List<string>();
            public void Clear() { active = won = flawless = false; xp = 0; unlocked.Clear(); levelBefore = Progress.Level; xpBefore = Progress.Data.xp; }
        }
        public readonly MatchSummary match = new MatchSummary();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I || !FindAnyObjectByType<MapBuilder>()) return;
            var go = new GameObject("Progression");
            go.AddComponent<ProgressTracker>();
            go.AddComponent<ProgressHud>();
            go.AddComponent<ProfileMenu>();
        }

        void Awake() { I = this; Progress.Load(); match.Clear(); }

        void OnEnable()
        {
            RoundManager.RoundStarted += OnRoundStarted;
            RoundManager.RoundEnded += OnRoundEnded;
            RoundManager.MatchEnded += OnMatchEnded;
            Finishers.Ended += OnFinisher;
            Progress.XpGained += OnXp;
            Progress.Unlocked += OnUnlocked;
        }

        void OnDisable()
        {
            RoundManager.RoundStarted -= OnRoundStarted;
            RoundManager.RoundEnded -= OnRoundEnded;
            RoundManager.MatchEnded -= OnMatchEnded;
            Finishers.Ended -= OnFinisher;
            Progress.XpGained -= OnXp;
            Progress.Unlocked -= OnUnlocked;
            Progress.Save();
        }

        void OnDestroy() { if (I == this) I = null; }
        void OnApplicationPause(bool paused) { if (paused) Progress.Save(); }
        void OnApplicationQuit() { Progress.Save(); }

        static Fighter Player => RoundManager.Instance ? RoundManager.Instance.player : null;

        void OnXp(int n, string why) { match.xp += n; }
        void OnUnlocked(string id) { match.unlocked.Add(id); }

        void OnRoundStarted(int round)
        {
            if (round == 1) match.Clear();   // a new match (also a rematch)
            match.active = true;
        }

        void OnRoundEnded(int round, int winner)
        {
            var p = Player; if (!p) return;
            var d = Progress.Data;
            if (winner == p.team) { d.roundsWon++; Progress.AddXp(ProgressRewards.RoundWon, "round won"); }
            else
            {
                if (winner < 0) d.roundsDrawn++; else d.roundsLost++;
                Progress.AddXp(ProgressRewards.RoundOther, winner < 0 ? "round drawn" : "round lost");
            }
            Progress.Save();
            Debug.Log($"Progression: round {round} {(winner == p.team ? "won" : winner < 0 ? "drawn" : "lost")}, level {Progress.Level} ({Progress.Data.xp} XP)");
        }

        void OnMatchEnded(int winnerTeam)
        {
            var rm = RoundManager.Instance; var p = Player; if (!rm || !p) return;
            var d = Progress.Data;
            bool won = winnerTeam == p.team;
            bool hard = rm.difficulty == "hard" || rm.difficulty == "extreme";
            match.won = won;
            match.flawless = won && rm.score[1 - p.team] == 0;

            d.matches++;
            var rec = Progress.RecordFor(MapSet.Current, rm.difficulty, rm.simpleRules ? "simple" : "normal");
            if (won) { d.matchesWon++; rec.won++; } else rec.lost++;
            if (match.flawless) d.flawless++;
            d.streak = won ? d.streak + 1 : 0;
            d.bestStreak = Mathf.Max(d.bestStreak, d.streak);
            if (hard) { d.hardStreak = won ? d.hardStreak + 1 : 0; d.bestHardStreak = Mathf.Max(d.bestHardStreak, d.hardStreak); }
            else if (!won) d.hardStreak = 0;

            // the fighter's stats are totals for the whole match (RoundManager resets them at the start of each match)
            d.kills += p.kills; d.deaths += p.deaths; d.headshots += p.headshots;
            d.shots += p.shots; d.hits += p.hits; d.damage += p.damageDealt;

            if (won) Progress.AddXp(ProgressRewards.MatchWon, "match won");
            Progress.Save();
            Debug.Log($"Progression: match {(won ? "won" : "lost")} {rm.score[p.team]}-{rm.score[1 - p.team]} on {MapSet.Current} ({rm.difficulty}), +{match.xp} XP this match, level {Progress.Level}");
        }

        void OnFinisher(Finisher f)
        {
            var p = Player;
            if (f == null || !p || f.a != p || string.IsNullOrEmpty(f.fx)) return;
            Progress.Bump(Progress.Data.finishers, f.fx);
            Progress.Save();
        }
    }
}
