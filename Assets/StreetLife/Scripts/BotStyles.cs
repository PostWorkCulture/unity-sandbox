// Bot play styles and difficulty levels, numbers taken straight from the web game (STYLES and DIFF in index.html).
using System.Collections.Generic;

namespace StreetLife
{
    public class BotStyle
    {
        public string id, name, desc;
        public float aggr, range, retreat, strafe, jump, react, err;
        public int shots;        // sniper: shots before moving
        public float spring;     // lurker: ambush distance
        public float hold;       // turtle: how long it holds a peek
    }

    public class BotDifficulty
    {
        public string name;
        public float react, err, head, turn, strafe, settle, sway, flinch, recoil, lead, tol, rof, hes, miss;
        public int burst; public float pauseMin, pauseMax;
    }

    public static class BotStyles
    {
        public static readonly Dictionary<string, BotStyle> All = new Dictionary<string, BotStyle>
        {
            ["rusher"] = new BotStyle { id = "rusher", name = "Rusher", desc = "Charges straight at you, jukes and jumps, never backs off.", aggr = 1, range = .35f, retreat = 0, strafe = 1.3f, jump = .35f, react = .75f, err = 1.25f },
            ["duelist"] = new BotStyle { id = "duelist", name = "Duelist", desc = "Strafes and trades shots at mid range.", aggr = .5f, range = 1, retreat = .25f, strafe = 1, jump = .15f, react = 1, err = 1 },
            ["sniper"] = new BotStyle { id = "sniper", name = "Sniper", desc = "Finds high ground far away, scopes in, moves after every couple of shots.", aggr = .1f, range = 1.3f, retreat = .45f, strafe = .5f, jump = .05f, react = 1.15f, err = .7f, shots = 2 },
            ["lurker"] = new BotStyle { id = "lurker", name = "Lurker", desc = "Hides out of sight near you and waits to ambush you up close.", aggr = .5f, range = .6f, retreat = .35f, strafe = .9f, jump = .1f, react = .85f, err = 1, spring = 13 },
            ["flanker"] = new BotStyle { id = "flanker", name = "Flanker", desc = "Circles round out of sight and hits you from the side.", aggr = .65f, range = .8f, retreat = .3f, strafe = 1.15f, jump = .2f, react = .95f, err = 1 },
            ["turtle"] = new BotStyle { id = "turtle", name = "Turtle", desc = "Walls up the moment it sees you, peeks from cover, backs off when hurt.", aggr = .2f, range = 1.1f, retreat = .55f, strafe = .6f, jump = .05f, react = 1, err = .85f, hold = 1.6f },
            ["hunter"] = new BotStyle { id = "hunter", name = "Hunter", desc = "Always has a rough idea where you are and keeps tracking you down at mid range.", aggr = .7f, range = .85f, retreat = .2f, strafe = 1.1f, jump = .2f, react = .9f, err = 1 },
            ["breacher"] = new BotStyle { id = "breacher", name = "Breacher", desc = "Shotgun. Hops between hiding spots, each one closer, then bursts in.", aggr = .6f, range = 1, retreat = .3f, strafe = 1.2f, jump = .3f, react = .8f, err = 1.1f },
            ["kiter"] = new BotStyle { id = "kiter", name = "Kiter", desc = "Holds its favourite range: backs off as you push, follows as you run.", aggr = .35f, range = 1, retreat = .4f, strafe = 1.4f, jump = .25f, react = .95f, err = .95f },
            ["camper"] = new BotStyle { id = "camper", name = "Camper", desc = "Picks one spot with a view, walls up and holds the angle all round.", aggr = .05f, range = 1, retreat = .6f, strafe = .3f, jump = 0, react = 1.05f, err = .8f },
        };

        /// Simple 1v1 rules in the web game don't use Sniper-style bots.
        public static readonly string[] SimplePool = { "rusher", "duelist", "lurker", "flanker", "turtle", "hunter", "breacher", "kiter", "camper" };

        public static readonly Dictionary<string, BotDifficulty> Levels = new Dictionary<string, BotDifficulty>
        {
            ["easy"] = new BotDifficulty { name = "Easy", react = 1.5f, err = 7, head = .01f, turn = 2.6f, strafe = .25f, settle = .7f, sway = 1.1f, flinch = 8, recoil = 2.8f, lead = 0, tol = .2f, rof = 1.6f, burst = 3, pauseMin = .9f, pauseMax = 1.6f, hes = .4f, miss = .6f },
            ["normal"] = new BotDifficulty { name = "Normal", react = .8f, err = 3.6f, head = .08f, turn = 5.2f, strafe = .5f, settle = 1.2f, sway = .65f, flinch = 4, recoil = 1.4f, lead = .35f, tol = .12f, rof = 1.4f, burst = 5, pauseMin = .5f, pauseMax = 1f, hes = .2f, miss = .35f },
            ["hard"] = new BotDifficulty { name = "Hard", react = .4f, err = 1.6f, head = .26f, turn = 10, strafe = .9f, settle = 1.9f, sway = .33f, flinch = 2, recoil = .5f, lead = .85f, tol = .07f, rof = 1.15f, burst = 8, pauseMin = .2f, pauseMax = .45f, hes = .05f, miss = .15f },
            ["extreme"] = new BotDifficulty { name = "Extreme", react = .3f, err = 1.15f, head = .34f, turn = 12.5f, strafe = 1, settle = 2.3f, sway = .26f, flinch = 1.6f, recoil = .38f, lead = 1, tol = .06f, rof = 1.08f, burst = 10, pauseMin = .15f, pauseMax = .35f, hes = .03f, miss = .1f },
        };
    }
}
