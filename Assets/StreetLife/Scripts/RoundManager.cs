// The match (match chat): the web game's Duels in all three player setups, and Rumble.
//  - 1v1: you against one bot, first to 5 rounds of 90 s (more health plus armour wins on time).
//  - 2v2: you and a bot team-mate against two bots, first to 5 rounds of 120 s; a team-mate standing over you revives
//    you twice as fast; if you're down and your whole team is out, you're out too.
//  - Free for all: you and 1, 3 or 5 bots, everyone respawns 3 s after going out, first to 10 kills (10 minutes).
// Spawns follow the web game: far-apart sides that can't see each other, never where you started in the last two rounds;
// team-mates start together; deathmatch respawns go to the spot furthest from everyone still standing.
// Every bot gets a fresh secret style each round (Rumble has its own pool). Added to the scene automatically.
// `player`, `bot` (the first enemy bot) and `botBrain` stay for the code written for the 1v1 version.
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
        /// The main menu holds the match here (fighters placed and frozen) until Play.
        public static bool HoldAtMenu;
        bool held;
        public static event Action<int> RoundStarted;          // round number
        public static event Action<int, int> RoundEnded;       // round number, winning team (-1 draw)
        public static event Action<int> MatchEnded;            // winning team

        public enum State { Boot, Intro, Countdown, Fight, RoundEnd, MatchEnd }

        [Header("Rules (web game: Duels)")]
        public float roundTime = 90f;
        public int toWin = 5;
        public float countdown = 3f, introTime = 2.4f, roundEndTime = 3.5f, rematchAfter = 15f;
        public string difficulty = "normal";
        public bool simpleRules = true;        // Simple 1v1: M4, sword, grenade, no Sniper-style bots
        public string forceStyle = "";         // leave empty for a random secret style each round

        [Header("Live")]
        public State state = State.Boot;
        public int round;
        public readonly int[] score = new int[8];
        public float timer, stateT;
        public Fighter player, bot;
        public BotBrain botBrain;
        public int lastWinner = -1;
        public bool matchOver;
        /// everyone in the match, you first
        public readonly List<Fighter> fighters = new List<Fighter>();
        readonly List<Fighter> pool = new List<Fighter>();          // every bot made so far (spares are switched off)
        readonly Dictionary<Fighter, float> respawnT = new Dictionary<Fighter, float>();
        public bool Deathmatch { get; private set; }
        public string PlayersMode { get; private set; } = "1v1";
        public Fighter LastWinnerFighter { get; private set; }

        public struct FeedItem { public string text; public float t; public bool mine; }
        public readonly List<FeedItem> feed = new List<FeedItem>();
        public readonly List<(Vector3 p, float t)> deathMarks = new List<(Vector3, float)>();

        readonly List<Vector3[]> spawnHistory = new List<Vector3[]>();
        readonly Dictionary<Fighter, string> lastStyle = new Dictionary<Fighter, string>();
        int lastTick; float targetT;

        static readonly string[] RumbleStyles = { "rusher", "lurker", "flanker", "duelist", "hunter", "breacher" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!rebootHooked) { rebootHooked = true; UnityEngine.SceneManagement.SceneManager.sceneLoaded += Reboot; }
            if (Instance || !FindAnyObjectByType<MapBuilder>()) return;
            new GameObject("Match").AddComponent<RoundManager>();
        }

        // Unity runs [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] once, for the first scene only. Picking a map or a character reloads
        // the scene, which takes the match, weapons, finishers and the rest with it: run every Street Life boot again for the new scene.
        // Each boot checks for its own instance first, so nothing is made twice (MapPicker kept its own copy of this until now).
        static bool rebootHooked;
        static void Reboot(UnityEngine.SceneManagement.Scene sc, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (mode != UnityEngine.SceneManagement.LoadSceneMode.Single) return;
            foreach (var t in typeof(RoundManager).Assembly.GetTypes())
            {
                if (t.Namespace == null || !t.Namespace.StartsWith("StreetLife")) continue;
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (m.GetParameters().Length != 0) continue;
                    foreach (var a in m.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false))
                        if (((RuntimeInitializeOnLoadMethodAttribute)a).loadType == RuntimeInitializeLoadType.AfterSceneLoad)
                        { try { m.Invoke(null, null); } catch (Exception e) { Debug.LogException(e); } }
                }
            }
        }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; Fighter.Killed -= OnKilled; }

        void Setup()
        {
            if (!NavGrid.Ready && BoxWorld.Loaded) NavGrid.Build();
            foreach (var fi in FindObjectsByType<Fighter>())
                if (fi.GetComponent<PlayerController>()) player = fi;
            if (!player) { Debug.LogWarning("Street Life: no player found"); enabled = false; return; }
            player.team = 0; player.isBot = false; player.displayName = "You";
            if (!player.GetComponent<FighterAudio>()) player.gameObject.AddComponent<FighterAudio>();
            Configure();
            Fighter.Killed -= OnKilled; Fighter.Killed += OnKilled;
            player.Damaged += (amt, by) => { if (amt > 0) Sfx.Play2D("hurt", .55f); };
        }

        Fighter CreateBot()
        {
            var go = new GameObject("Bot " + (pool.Count + 1));
            var b = go.AddComponent<Fighter>();
            b.team = 1; b.isBot = true; b.displayName = "Bot";
            var brain = go.AddComponent<BotBrain>();
            brain.target = player; brain.difficulty = difficulty;
            var pr = player.GetComponent<CharacterRig>();
            if (pr)
            {
                var rig = go.AddComponent<CharacterRig>();
                rig.fighter = b; rig.library = pr.library; rig.charJson = pr.charJson; rig.charBin = pr.charBin;
                CharacterSkin.ForBot(rig);              // characters chat: a random character other than yours
            }
            go.AddComponent<FighterAudio>();
            pool.Add(b);
            return b;
        }

        /// make / switch on the bots this setup needs and give everyone their team
        void Configure()
        {
            PlayersMode = Rules.Players; Deathmatch = Rules.Deathmatch;
            int n = Rules.FighterCount;
            while (pool.Count < n - 1) CreateBot();
            fighters.Clear(); fighters.Add(player);
            for (int i = 0; i < pool.Count; i++)
            {
                var b = pool[i]; bool on = i < n - 1;
                if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
                if (!on) continue;
                fighters.Add(b);
                b.team = Rules.TwoVTwo ? (i == 0 ? 0 : 1) : i + 1;
                var br = b.GetComponent<BotBrain>(); br.difficulty = difficulty;
            }
            player.team = 0;
            bot = null; botBrain = null;
            foreach (var f in fighters) if (f != player && f.team != player.team) { bot = f; botBrain = f.GetComponent<BotBrain>(); break; }
            roundTime = Rules.RoundTime;
            toWin = Deathmatch ? Rules.DM_KILLS : Rules.ROUNDS_TO_WIN;
            PickTargets(true);
        }

        /// Play pressed in the main menu.
        public void BeginFromMenu() { HoldAtMenu = false; held = false; Settings.Apply(); if (state == State.Boot && player) { Configure(); StartMatch(); } }

        // ------------------------------------------------------------------ match flow
        public void StartMatch()
        {
            if (player && (Rules.Players != PlayersMode || Rules.FighterCount != fighters.Count)) Configure();
            Array.Clear(score, 0, score.Length); round = 1; matchOver = false; feed.Clear(); deathMarks.Clear(); LastWinnerFighter = null;
            foreach (var f in fighters) f.ResetStats();
            StartRound();
        }

        void StartRound()
        {
            respawnT.Clear();
            var sp = PickSpawns();
            for (int i = 0; i < fighters.Count; i++) fighters[i].ResetForRound(sp[i].p, sp[i].yaw);
            foreach (var f in fighters)
            {
                if (f == player) continue;
                var br = f.GetComponent<BotBrain>(); br.difficulty = difficulty;
                br.ResetForRound(PickStyle(f));
                f.displayName = string.IsNullOrEmpty(f.characterName) ? "Bot" : f.characterName;   // a bot goes by its character
            }
            // two bots can roll the same character: number the repeats so the feed and leaderboard stay readable
            var seen = new Dictionary<string, int>();
            foreach (var f in fighters)
            {
                if (f == player) continue;
                seen.TryGetValue(f.displayName, out int n); seen[f.displayName] = n + 1;
                if (n > 0) f.displayName += " " + (n + 1);
            }
            PickTargets(true);
            timer = roundTime; lastWinner = -1;
            SetFrozen(true);
            state = round == 1 ? State.Intro : State.Countdown;
            stateT = round == 1 ? introTime : countdown;
            lastTick = -1;
            Sfx.Music("rounds");
            RoundStarted?.Invoke(round);
        }

        string PickStyle(Fighter f)
        {
            if (!string.IsNullOrEmpty(forceStyle) && BotStyles.All.ContainsKey(forceStyle)) return forceStyle;
            var pool2 = new List<string>(Rules.Rumble ? RumbleStyles : simpleRules && Rules.OneVOne ? BotStyles.SimplePool : new List<string>(BotStyles.All.Keys).ToArray());
            if (lastStyle.TryGetValue(f, out var ls) && pool2.Count > 1) pool2.Remove(ls);
            var s = pool2[Random.Range(0, pool2.Count)];
            lastStyle[f] = s; return s;
        }

        void SetFrozen(bool on) { foreach (var f in fighters) f.Frozen = on; }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (state)
            {
                case State.Boot:
                    if (!player) Setup();
                    if (!enabled || !player) break;
                    if (HoldAtMenu)
                    {
                        if (!held)
                        {
                            held = true; var sp = PickSpawns();
                            for (int i = 0; i < fighters.Count; i++) fighters[i].ResetForRound(sp[i].p, sp[i].yaw);
                            SetFrozen(true);
                        }
                        break;
                    }
                    Settings.Apply(); Configure(); StartMatch();
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
                    foreach (var f in fighters) { var br = f.GetComponent<BotBrain>(); if (br) br.roundTimeLeft = timer; }
                    targetT -= dt; if (targetT <= 0) PickTargets(false);
                    if (Deathmatch) TickRespawns(dt);
                    else TeamWipe();
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

        // ------------------------------------------------------------------ who each bot goes for (the web game's target score)
        void PickTargets(bool now)
        {
            targetT = .35f;
            foreach (var f in fighters)
            {
                if (f == player || !f.gameObject.activeInHierarchy) continue;
                var br = f.GetComponent<BotBrain>(); if (!br) continue;
                Fighter best = null; float bs = 1e9f;
                foreach (var o in fighters)
                {
                    if (o == f || o.team == f.team || !o.alive) continue;
                    var d = o.pos - f.pos; d.y = 0; float sc = d.magnitude;
                    bool sees = NavGrid.Los(f.Head, o.Head) || NavGrid.Los(f.Head, o.pos + Vector3.up * (1.25f * Fighter.SC));
                    if (!sees) sc += 18;
                    if (o.downed) sc -= 12;
                    if (o == br.target) sc -= 8;
                    if (o.Tagged) sc -= 10;
                    sc += KillCalls.TargetBias(f, o, this);   // grudges, lean towards the leader
                    if (sc < bs) { bs = sc; best = o; }
                }
                if (best) br.target = best;
            }
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
            if (Deathmatch)
            {
                foreach (var f in fighters) score[f.team] = f.kills;
                if (killer && killer != victim && killer.kills >= toWin) { DMWin(killer); return; }
                respawnT[victim] = 3;
                return;
            }
            CheckRound();
        }

        /// the round ends when only one team has anyone left (down counts as still in)
        void CheckRound()
        {
            int alive = 0, team = -1;
            var seen = new HashSet<int>();
            foreach (var f in fighters) if (f.alive && seen.Add(f.team)) { alive++; team = f.team; }
            if (alive <= 1) EndRound(alive == 1 ? team : -1);
        }

        /// 2v2: down with your whole team out means you're out too
        void TeamWipe()
        {
            if (!Rules.TwoVTwo) return;
            foreach (var x in fighters)
            {
                if (!x.alive || !x.downed || x.InFinisher) continue;
                bool mates = false, mateIn = false;
                foreach (var m in fighters) if (m != x && m.team == x.team) { mates = true; if (m.alive) mateIn = true; }
                if (mates && !mateIn) { x.Die(x.lastBy ? x.lastBy : x.downedBy, false); return; }
            }
        }

        public static Func<Fighter, string> WeaponNameOf;   // the weapons chat can supply the gun in hand
        static string WeaponName(Fighter f)
        {
            if (WeaponNameOf != null) return WeaponNameOf(f);
            return f.primary == "gar" ? "M4" : f.primary;
        }

        void TimeUp()
        {
            if (Deathmatch)
            {
                Fighter best = null; foreach (var f in fighters) if (!best || f.kills > best.kills) best = f;
                DMWin(best); return;
            }
            var tot = new Dictionary<int, float>();
            foreach (var f in fighters) { float h = f.alive && !f.downed ? (f.hp + f.armor) / f.hpMax : 0; tot[f.team] = (tot.TryGetValue(f.team, out var v) ? v : 0) + h; }
            int best2 = -1; float bv = -1; bool tie = false;
            foreach (var kv in tot) { if (kv.Value > bv + 1e-6f) { bv = kv.Value; best2 = kv.Key; tie = false; } else if (Mathf.Abs(kv.Value - bv) <= 1e-6f) tie = true; }
            EndRound(tie || bv <= 0 ? -1 : best2);
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

        void DMWin(Fighter w)
        {
            if (state != State.Fight) return;
            foreach (var f in fighters) score[f.team] = f.kills;
            LastWinnerFighter = w; lastWinner = w ? w.team : -1;
            matchOver = true; state = State.RoundEnd; stateT = roundEndTime;
            SetFrozen(true);
            RoundEnded?.Invoke(round, lastWinner);
        }

        void EnterMatchEnd()
        {
            state = State.MatchEnd; stateT = rematchAfter;
            bool won = lastWinner == player.team;
            Sfx.Play2D(won ? "win" : "lose", .9f);
            Sfx.Music("matchend");
            MatchEnded?.Invoke(lastWinner);
        }

        public bool PlayerWonMatch => lastWinner == (player ? player.team : 0);
        public string StyleThisRound => botBrain ? botBrain.Style.name : "";

        // ------------------------------------------------------------------ deathmatch respawns
        void TickRespawns(float dt)
        {
            foreach (var f in fighters)
            {
                if (f.alive || !respawnT.TryGetValue(f, out var t)) continue;
                if (f.InFinisher) continue;   // a finishing move plays out first
                t -= dt; respawnT[f] = t;
                if (t > 0) continue;
                respawnT.Remove(f);
                var s = DMSpawn(f);
                f.ResetDowned(); f.ResetForRound(s.p, s.yaw); f.Frozen = false; f.lastHurt = f.simTime;
                var fp = f.GetComponent<FinisherPose>(); if (fp) fp.ResetVisual();
                var w = f.GetComponent<Weapons>(); if (w) w.ResetKit();
                var br = f.GetComponent<BotBrain>(); if (br) br.ResetForRound(PickStyle(f));
                var bx = f.GetComponent<BotExtras>(); if (bx) bx.Rekit();
                Sfx.Play("respawn", f.pos, 1, 20);
                var o = GunFx.E(30, 4, .2f, .6f, -1, "#ffffff", "#7fd3ff"); o.up = 3; GunFx.Emit(f.pos + Vector3.up * 1.2f, o);
            }
        }

        public float RespawnIn(Fighter f) => respawnT.TryGetValue(f, out var t) ? Mathf.Max(0, t) : 0;

        Spot DMSpawn(Fighter f)
        {
            Spot best = default; float bv = -1;
            foreach (var s in BoxWorld.Spawns)
            {
                var p = new Vector3(s.x, s.y, s.z); float m = 1e9f;
                foreach (var o in fighters) if (o != f && o.alive) m = Mathf.Min(m, Flat(o.pos - p).magnitude);
                if (m > bv) { bv = m; best = new Spot { p = p, yaw = s.yaw }; }
            }
            if (bv < 0) best = new Spot { p = Vector3.zero };
            return best;
        }

        // ------------------------------------------------------------------ spawns
        public struct Spot { public Vector3 p; public float yaw; }
        Vector3 P(WSpawn s) => new Vector3(s.x, s.y, s.z);

        Spot[] PickSpawns()
        {
            int n = Mathf.Max(2, fighters.Count);
            var res = new Spot[n];
            var sp = BoxWorld.Spawns;
            if (sp.Count < 2) { for (int i = 0; i < n; i++) res[i] = new Spot { p = new Vector3(i * 4, 0, 0) }; return res; }
            if (Rules.Ffa) return FfaSpawns(n);
            var pair = PickPair();
            if (!Rules.TwoVTwo) { res[0] = pair[0]; res[1] = pair[1]; return res; }
            // 2v2: you and your team-mate at one end, the other two at the other
            var a = pair[0]; var b = pair[1];
            int k = 0;
            foreach (var f in fighters)
            {
                bool mine = f.team == player.team;
                var basis = mine ? a : b;
                int nth = 0; for (int j = 0; j < k; j++) if ((fighters[j].team == player.team) == mine) nth++;
                res[k] = nth == 0 ? basis : Beside(basis, nth);
                k++;
            }
            return res;
        }

        /// a free spot next to a spawn for a team-mate (another spawn point close by, or 1.6 m to the side)
        Spot Beside(Spot s, int nth)
        {
            foreach (var o in BoxWorld.Spawns) { var p = P(o); float d = Flat(p - s.p).magnitude; if (d > 1.2f && d < 9 && Mathf.Abs(p.y - s.p.y) < 1) return new Spot { p = p, yaw = s.yaw }; }
            var right = new Vector3(Mathf.Cos(s.yaw), 0, -Mathf.Sin(s.yaw));
            for (int t = 0; t < 8; t++)
            {
                var dir = Quaternion.Euler(0, t * 45, 0) * right; var p = s.p + dir * 1.6f * nth;
                if (Free(p)) return new Spot { p = p, yaw = s.yaw };
            }
            return new Spot { p = s.p + right * 1.6f, yaw = s.yaw };
        }

        static bool Free(Vector3 p)
        {
            foreach (var b in BoxWorld.Boxes)
                if (b.x0 < p.x + Fighter.R && b.x1 > p.x - Fighter.R && b.z0 < p.z + Fighter.R && b.z1 > p.z - Fighter.R && b.y1 > p.y + Fighter.STEP && b.y0 < p.y + Fighter.HH) return false;
            var bd = BoxWorld.Bounds;
            return p.x > bd.x0 + 1 && p.x < bd.x1 - 1 && p.z > bd.z0 + 1 && p.z < bd.z1 - 1;
        }

        /// free-for-all: spread everyone out, each as far as possible from the ones already placed
        Spot[] FfaSpawns(int n)
        {
            var sp = BoxWorld.Spawns; var cand = new List<Spot>();
            foreach (var s in sp) cand.Add(new Spot { p = P(s), yaw = s.yaw });
            var outp = new List<Spot> { cand[Random.Range(0, cand.Count)] };
            while (outp.Count < n)
            {
                Spot best = default; float bv = -1;
                foreach (var c in cand)
                {
                    bool used = false; float m = 1e9f;
                    foreach (var o in outp) { float d = Flat(o.p - c.p).magnitude; if (d < .5f) used = true; m = Mathf.Min(m, d); }
                    if (!used && m > bv) { bv = m; best = c; }
                }
                if (bv < 0) best = Beside(outp[Random.Range(0, outp.Count)], 1);
                outp.Add(best);
            }
            for (int i = outp.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var t = outp[i]; outp[i] = outp[j]; outp[j] = t; }
            return outp.ToArray();
        }

        /// Far-apart pair where neither can see the other at the start, never within 7 m of where either started
        /// in the last two rounds (falls back to the least exposed pair if every pair has a sightline).
        Spot[] PickPair()
        {
            var sp = BoxWorld.Spawns;
            var bd = BoxWorld.Bounds;
            float lim = Mathf.Min(45f, Mathf.Max(bd.x1 - bd.x0, bd.z1 - bd.z0) * .5f);
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
