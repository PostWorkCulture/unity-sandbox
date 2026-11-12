// The 1v1 match: you against one bot, first to 5 rounds, 90-second rounds (the side with more health plus armour
// wins when time runs out), the web game's spawn rules, a fresh secret bot style every round, kill feed and results.
// Added to the Street Life scene automatically when you press Play.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace StreetLife
{
    [DefaultExecutionOrder(-50)]
    public class RoundManager : MonoBehaviour
    {
        public static RoundManager Instance;
        public static event Action<int> RoundStarted;          // round number
        public static event Action<int, int> RoundEnded;       // round number, winning team (-1 draw)
        public static event Action<int> MatchEnded;            // winning team

        public enum State { Boot, Intro, Countdown, Fight, RoundEnd, MatchEnd }

        [Header("Rules (web game: Duels 1v1)")]
        public float roundTime = 90f;
        public int toWin = 5;
        public float countdown = 3f, introTime = 2.4f, roundEndTime = 3.5f, rematchAfter = 15f;
        public string difficulty = "normal";
        public bool simpleRules = true;        // Simple 1v1: M4, sword, grenade, no Sniper-style bots
        public string forceStyle = "";         // leave empty for a random secret style each round

        [Header("Live")]
        public State state = State.Boot;
        public int round;
        public readonly int[] score = new int[2];
        public float timer, stateT;
        public Fighter player, bot;
        public BotBrain botBrain;
        public int lastWinner = -1;
        public bool matchOver;

        public struct FeedItem { public string text; public float t; public bool mine; }
        public readonly List<FeedItem> feed = new List<FeedItem>();
        public readonly List<(Vector3 p, float t)> deathMarks = new List<(Vector3, float)>();

        readonly List<Vector3[]> spawnHistory = new List<Vector3[]>();
        string lastStyle;
        int lastTick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance || !FindFirstObjectByType<MapBuilder>()) return;
            new GameObject("Match").AddComponent<RoundManager>();
        }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; Fighter.Killed -= OnKilled; }

        void Setup()
        {
            if (!NavGrid.Ready && BoxWorld.Loaded) NavGrid.Build();
            foreach (var fi in FindObjectsByType<Fighter>(FindObjectsSortMode.None))
                if (fi.GetComponent<PlayerController>()) player = fi;
            if (!player) { Debug.LogWarning("Street Life: no player found"); enabled = false; return; }
            player.team = 0; player.isBot = false; player.displayName = "You";
            if (!player.GetComponent<FighterAudio>()) player.gameObject.AddComponent<FighterAudio>();
            if (!bot) CreateBot();
            Fighter.Killed -= OnKilled; Fighter.Killed += OnKilled;
            player.Damaged += (amt, by) => { if (amt > 0) Sfx.Play2D("hurt", .55f); };
        }

        void CreateBot()
        {
            var go = new GameObject("Bot");
            bot = go.AddComponent<Fighter>();
            bot.team = 1; bot.isBot = true; bot.displayName = "Bot";
            botBrain = go.AddComponent<BotBrain>();
            botBrain.target = player; botBrain.difficulty = difficulty;
            var pr = player.GetComponent<CharacterRig>();
            if (pr)
            {
                var rig = go.AddComponent<CharacterRig>();
                rig.fighter = bot; rig.library = pr.library; rig.charJson = pr.charJson; rig.charBin = pr.charBin;
                rig.tint = new Color(1f, .55f, .5f);    // until the characters chat adds the other characters
                CharacterSkin.ForBot(rig);              // characters chat: the bot gets its own character (random each match unless picked with F3)
            }
            go.AddComponent<FighterAudio>();
        }

        // ------------------------------------------------------------------ match flow
        public void StartMatch()
        {
            score[0] = score[1] = 0; round = 1; matchOver = false; feed.Clear(); deathMarks.Clear();
            player.ResetStats(); bot.ResetStats();
            StartRound();
        }

        void StartRound()
        {
            var sp = PickSpawns();
            player.ResetForRound(sp[0].p, sp[0].yaw);
            bot.ResetForRound(sp[1].p, sp[1].yaw);
            string style = forceStyle;
            if (string.IsNullOrEmpty(style) || !BotStyles.All.ContainsKey(style))
            {
                var pool = new List<string>(simpleRules ? BotStyles.SimplePool : new List<string>(BotStyles.All.Keys).ToArray());
                if (pool.Count > 1 && lastStyle != null) pool.Remove(lastStyle);
                style = pool[Random.Range(0, pool.Count)];
            }
            lastStyle = style;
            botBrain.difficulty = difficulty;
            botBrain.ResetForRound(style);
            bot.displayName = "Bot";
            timer = roundTime; lastWinner = -1;
            SetFrozen(true);
            state = round == 1 ? State.Intro : State.Countdown;
            stateT = round == 1 ? introTime : countdown;
            lastTick = -1;
            Sfx.Music("rounds");
            RoundStarted?.Invoke(round);
        }

        void SetFrozen(bool on) { player.Frozen = on; bot.Frozen = on; }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (state)
            {
                case State.Boot:
                    Setup(); if (enabled && player && bot) StartMatch();
                    break;
                case State.Intro:
                    stateT -= dt;
                    if (stateT <= 0) { state = State.Countdown; stateT = countdown; }
                    break;
                case State.Countdown:
                    stateT -= dt;
                    { int c = Mathf.CeilToInt(stateT); if (c != lastTick && c > 0) { lastTick = c; Sfx.Play2D("tick", .7f); } }
                    if (stateT <= 0) { state = State.Fight; SetFrozen(false); Sfx.Play2D("gong", .8f); Sfx.Music(null); }
                    break;
                case State.Fight:
                    timer -= dt;
                    botBrain.roundTimeLeft = timer;
                    if (timer <= 0) TimeUp();
                    break;
                case State.RoundEnd:
                    stateT -= dt;
                    if (stateT <= 0)
                    {
                        if (matchOver) EnterMatchEnd();
                        else { round++; StartRound(); }
                    }
                    break;
                case State.MatchEnd:
                    stateT -= dt;
                    var kb = Keyboard.current;
                    if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || stateT <= 0) StartMatch();
                    break;
            }
            // tidy the feed and markers
            feed.RemoveAll(x => Time.time - x.t > 6f);
            deathMarks.RemoveAll(x => Time.time - x.t > 3f);
        }

        void OnKilled(Fighter victim, Fighter killer, bool head)
        {
            string weapon = killer != null ? WeaponName(killer) : "";
            string text = killer != null && killer != victim ? $"{killer.displayName}  [{weapon}{(head ? "  HEADSHOT" : "")}]  {victim.displayName}" : $"{victim.displayName} went down";
            feed.Add(new FeedItem { text = text, t = Time.time, mine = killer == player || victim == player });
            if (feed.Count > 5) feed.RemoveAt(0);
            deathMarks.Add((victim.pos, Time.time));
            if (killer == player) Sfx.Play2D(head ? "headshot" : "kill", .8f);
            else Sfx.Play("down", victim.pos, .9f, 18);
            if (state != State.Fight) return;
            bool pAlive = player.alive, bAlive = bot.alive;
            if (!pAlive || !bAlive) EndRound(pAlive ? player.team : bAlive ? bot.team : -1);
        }

        public static Func<Fighter, string> WeaponNameOf;   // the weapons chat can supply the gun in hand
        static string WeaponName(Fighter f)
        {
            if (WeaponNameOf != null) return WeaponNameOf(f);
            return f.primary == "gar" ? "M4" : f.primary;
        }

        void TimeUp()
        {
            float h(Fighter f) => f.alive ? (f.hp + f.armor) / f.hpMax : 0;
            float a = h(player), b = h(bot);
            EndRound(Mathf.Abs(a - b) < 1e-4f || Mathf.Max(a, b) <= 0 ? -1 : a > b ? player.team : bot.team);
        }

        void EndRound(int winner)
        {
            if (state == State.RoundEnd || state == State.MatchEnd) return;
            lastWinner = winner;
            if (winner >= 0) score[winner]++;
            matchOver = winner >= 0 && score[winner] >= toWin;
            state = State.RoundEnd; stateT = roundEndTime;
            SetFrozen(true);
            Sfx.Music(matchOver ? null : "rounds");
            RoundEnded?.Invoke(round, winner);
        }

        void EnterMatchEnd()
        {
            state = State.MatchEnd; stateT = rematchAfter;
            bool won = score[player.team] >= toWin;
            Sfx.Play2D(won ? "win" : "lose", .9f);
            Sfx.Music("matchend");
            MatchEnded?.Invoke(won ? player.team : bot.team);
        }

        public string StyleThisRound => botBrain ? botBrain.Style.name : "";

        // ------------------------------------------------------------------ spawns
        public struct Spot { public Vector3 p; public float yaw; }

        /// Far-apart pair where neither can see the other at the start, never within 7 m of where either started
        /// in the last two rounds (falls back to the least exposed pair if every pair has a sightline).
        Spot[] PickSpawns()
        {
            var sp = BoxWorld.Spawns;
            if (sp.Count < 2) return new[] { new Spot { p = Vector3.zero }, new Spot { p = new Vector3(10, 0, 0) } };
            var bd = BoxWorld.Bounds;
            float lim = Mathf.Min(45f, Mathf.Max(bd.x1 - bd.x0, bd.z1 - bd.z0) * .5f);
            Vector3 P(WSpawn s) => new Vector3(s.x, s.y, s.z);
            var pairs = new List<(int a, int b)>();
            for (int i = 0; i < sp.Count; i++) for (int j = 0; j < sp.Count; j++) if (i != j) pairs.Add((i, j));
            for (int i = pairs.Count - 1; i > 0; i--) { int k = Random.Range(0, i + 1); var t = pairs[i]; pairs[i] = pairs[k]; pairs[k] = t; }
            bool Recent(int who, Vector3 p) { for (int h = Mathf.Max(0, spawnHistory.Count - 2); h < spawnHistory.Count; h++) if (Flat(spawnHistory[h][who] - p).magnitude < 7f) return true; return false; }
            int Seen(Vector3 a, Vector3 b)
            {
                int n = 0;
                foreach (var (h1, h2) in new[] { (1.9f, 1.9f), (1.9f, 1f), (1f, 1.9f) })
                    if (NavGrid.Los(a + Vector3.up * h1, b + Vector3.up * h2)) n++;
                return n;
            }
            (int a, int b) best = pairs[0]; int bestSeen = 99; bool found = false;
            foreach (var (a, b) in pairs)
            {
                Vector3 pa = P(sp[a]), pb = P(sp[b]);
                if (Flat(pa - pb).magnitude < lim * .6f) continue;
                if (Recent(0, pa) || Recent(1, pb)) continue;
                int s = Seen(pa, pb);
                if (s == 0) { best = (a, b); found = true; break; }
                if (s < bestSeen) { bestSeen = s; best = (a, b); }
            }
            if (!found && bestSeen == 99) best = pairs[0];
            var A = sp[best.a]; var B = sp[best.b];
            spawnHistory.Add(new[] { P(A), P(B) }); if (spawnHistory.Count > 4) spawnHistory.RemoveAt(0);
            return new[] { new Spot { p = P(A), yaw = A.yaw }, new Spot { p = P(B), yaw = B.yaw } };
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    }
}
