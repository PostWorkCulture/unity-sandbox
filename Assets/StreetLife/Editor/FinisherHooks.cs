// Finishers chat: adds the six one-line hooks the downed state needs to the match chat's Fighter.cs and FighterCombat.cs,
// if they aren't there already (so it keeps working when those files are replaced by a newer version). Each hook only
// calls into Scripts/Finishers/FighterDowned.cs. Runs after every script reload; menu: Street Life > Finishers > Re-apply hooks.
// The hooks (also in claude/street-life-interfaces.md):
//   Fighter.Update       if (FinisherHold()) return;                  after the Loaded/alive check
//   Fighter.Step         DownedMove(ref spd);                          after "float spd = ..."
//   Fighter.Physics1     ... && !downed && LadderStep(...)             no ladder climbing while down
//   Fighter.Damage       if (DownedHit(amount, by, head, out var downedTaken)) return downedTaken;   after the friendly-fire check
//   Fighter.Damage       if (hp <= 0.001f && !GoDown(by, head)) Die(by, head);
//   Fighter.TickCombat   TickDowned(dt);
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    static class FinisherHooks
    {
        const string Dir = "Assets/StreetLife/Scripts/";

        struct Hook { public string file, marker, pattern, replace, manual; }

        static readonly Hook[] hooks =
        {
            new Hook { file = "Fighter.cs", marker = "FinisherHold()", pattern = @"(void Update\(\)\s*\{\s*if \(!BoxWorld\.Loaded[^;\n]*;)",
                replace = "$1\n            if (FinisherHold()) return;   // finishers: a finishing move drives this fighter", manual = "Fighter.Update: if (FinisherHold()) return;" },
            new Hook { file = "Fighter.cs", marker = "DownedMove(", pattern = @"(\n(\s*)float spd = [^;\n]*;)",
                replace = "$1\n$2DownedMove(ref spd);   // finishers: crawling pace while down", manual = "Fighter.Step: DownedMove(ref spd); after float spd = ..." },
            new Hook { file = "Fighter.cs", marker = "!downed && LadderStep", pattern = @"BoxWorld\.Ladders\.Count > 0 && LadderStep\(",
                replace = "BoxWorld.Ladders.Count > 0 && !downed && LadderStep(", manual = "Fighter.Physics1: ... && !downed && LadderStep(...)" },
            new Hook { file = "FighterCombat.cs", marker = "DownedHit(", pattern = @"(\n(\s*)if \(by != null && by != this && by\.team == team\) return 0;[^\r\n]*)",
                replace = "$1\n$2if (DownedHit(amount, by, head, out var downedTaken)) return downedTaken;   // finishers: hits while down or being finished", manual = "Fighter.Damage: if (DownedHit(amount, by, head, out var downedTaken)) return downedTaken;" },
            new Hook { file = "FighterCombat.cs", marker = "GoDown(", pattern = @"if \(hp <= 0\.001f\) Die\(by, head\);",
                replace = "if (hp <= 0.001f && !GoDown(by, head)) Die(by, head);   // finishers: down first", manual = "Fighter.Damage: if (hp <= 0.001f && !GoDown(by, head)) Die(by, head);" },
            new Hook { file = "FighterCombat.cs", marker = "TickDowned(", pattern = @"(void TickCombat\(float dt\)\s*\{)",
                replace = "$1 TickDowned(dt); /* finishers */", manual = "Fighter.TickCombat: TickDowned(dt);" },
        };

        static FinisherHooks() { EditorApplication.delayCall += () => Apply(false); }

        [MenuItem("Street Life/Finishers/Re-apply hooks")]
        static void Menu() => Apply(true);

        static void Apply(bool verbose)
        {
            bool changed = false; int ok = 0;
            foreach (var h in hooks)
            {
                var path = Dir + h.file;
                if (!File.Exists(path)) { Debug.LogWarning("Street Life finishers: " + path + " not found; add by hand: " + h.manual); continue; }
                var src = File.ReadAllText(path);
                if (src.Contains(h.marker)) { ok++; continue; }
                var rx = new Regex(h.pattern);
                if (!rx.IsMatch(src)) { Debug.LogWarning("Street Life finishers: couldn't place a hook in " + h.file + "; add by hand: " + h.manual); continue; }
                var nl = src.Contains("\r\n") ? "\r\n" : "\n";        // keep the file's own line endings
                src = rx.Replace(src, h.replace.Replace("\n", nl), 1);
                File.WriteAllText(path, src);
                Debug.Log("Street Life finishers: added hook to " + h.file + " (" + h.manual + ")");
                changed = true; ok++;
            }
            if (changed) AssetDatabase.Refresh();
            else if (verbose) Debug.Log($"Street Life finishers: {ok} of {hooks.Length} hooks in place.");
        }
    }
}
