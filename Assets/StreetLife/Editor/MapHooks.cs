// Maps chat: makes sure the one-line hooks the maps need are in the match chat's files (like FinisherHooks does for the finishers).
// Each hook only calls into the maps chat's code (MapSet.cs, FighterEdge.cs, MapRules.cs) and does nothing on Street Life.
// Runs after every script reload and only writes a file when a hook is missing; menu: Street Life > Maps > Re-apply hooks.
//  MapBuilder.OnEnable  MapSet.Apply(this);                                  swap in the chosen map's data (play mode)
//  Fighter.Physics      if (EdgeFallStep(dt)) return;                        falling off an open edge
//                       if (EdgeAfterMove()) return;                         open edges aren't clamped to the map
//  Fighter.Physics1     if (MapDropHere()) sup = -1e9f;                      no floor past the edge / over deep water
//  Fighter.MoveAxis     if (EdgeBlock(pos.x + dx, pos.z + dz)) return false; Lagoon bots stay on the boards
//                       if (EdgeFree()) { ...; return true; }                not clamped while free of the edge
//  NavGrid.Build        if (!MapRules.Drop(cx, cz)) cand.Add(0);             no ground nodes over the void or the water
//                       blocked = null;                                     a rebuild for another map starts clean
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    public static class MapHooks
    {
        const string S = "Assets/StreetLife/Scripts/";
        static MapHooks() { EditorApplication.delayCall += () => Apply(false); }

        [MenuItem("Street Life/Maps/Re-apply hooks")]
        public static void ApplyMenu() => Apply(true);

        public static void Apply(bool verbose)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            int n = 0;
            n += Patch(S + "MapBuilder.cs", "MapSet.Apply(this)",
                "        void OnEnable()\n        {\n",
                "        void OnEnable()\n        {\n            MapSet.Apply(this);   // maps chat: swap in the chosen map's data (play mode only)\n", verbose);
            n += Patch(S + "Fighter.cs", "EdgeFallStep(dt)",
                "            if (stamLock) speed *= .75f;\n",
                "            if (EdgeFallStep(dt)) return;   // maps chat: falling off an open edge\n            if (stamLock) speed *= .75f;\n", verbose);
            n += Patch(S + "Fighter.cs", "EdgeAfterMove()",
                "            var bd = BoxWorld.Bounds;\n            pos.x = Mathf.Clamp(pos.x, bd.x0 + R, bd.x1 - R);\n",
                "            if (EdgeAfterMove()) return;   // maps chat: open edges aren't clamped to the map\n            var bd = BoxWorld.Bounds;\n            pos.x = Mathf.Clamp(pos.x, bd.x0 + R, bd.x1 - R);\n", verbose);
            n += Patch(S + "Fighter.cs", "MapDropHere()",
                "            float ny = pos.y + (grounded ? vel.y : (vy0 + vel.y) / 2) * dt, sup = 0;\n",
                "            float ny = pos.y + (grounded ? vel.y : (vy0 + vel.y) / 2) * dt, sup = 0;\n            if (MapDropHere()) sup = -1e9f;   // maps chat: no floor here\n", verbose);
            n += Patch(S + "Fighter.cs", "EdgeBlock(",
                "            float nx = pos.x + dx, nz = pos.z + dz, lift = 0, H = Hh;\n",
                "            if (EdgeBlock(pos.x + dx, pos.z + dz)) return false;   // maps chat: Lagoon bots stay on the boards\n            float nx = pos.x + dx, nz = pos.z + dz, lift = 0, H = Hh;\n", verbose);
            n += Patch(S + "Fighter.cs", "if (EdgeFree())",
                "            var bd = BoxWorld.Bounds;\n            pos.x = Mathf.Clamp(nx, bd.x0 + R, bd.x1 - R);\n",
                "            if (EdgeFree()) { pos.x = nx; pos.z = nz; pos.y += lift; return true; }   // maps chat: open edges\n            var bd = BoxWorld.Bounds;\n            pos.x = Mathf.Clamp(nx, bd.x0 + R, bd.x1 - R);\n", verbose);
            n += Patch(S + "NavGrid.cs", "MapRules.Drop(cx, cz)",
                "cand.Clear(); cand.Add(0);",
                "cand.Clear(); if (!MapRules.Drop(cx, cz)) cand.Add(0);   // maps chat: no ground over open edges or deep water\n", verbose, replaceOnly: true);
            n += Patch(S + "NavGrid.cs", "blocked = null;",
                "Nodes.Clear(); Ready = false;",
                "Nodes.Clear(); Ready = false; blocked = null;   // maps chat: a rebuild for another map must not read the old map's flags\n", verbose, replaceOnly: true);
            if (n > 0) { Debug.Log("Street Life maps: added " + n + " hook line(s) to the match chat's scripts."); AssetDatabase.Refresh(); }
            else if (verbose) Debug.Log("Street Life maps: all hooks already in place.");
        }

        static int Patch(string path, string marker, string anchor, string with, bool verbose, bool replaceOnly = false)
        {
            if (!File.Exists(path)) return 0;
            var src = File.ReadAllText(path);
            if (src.Contains(marker)) return 0;
            var nl = src.Contains("\r\n");
            var s = nl ? src.Replace("\r\n", "\n") : src;
            int at = s.IndexOf(anchor);
            if (at < 0 || s.IndexOf(anchor, at + 1) >= 0) { Debug.LogWarning("Street Life maps: couldn't place the hook '" + marker + "' in " + path + " (the code round it changed); see claude/street-life-interfaces.md"); return 0; }
            if (replaceOnly && with.EndsWith("\n") && !anchor.EndsWith("\n")) with = with.TrimEnd('\n');
            s = s.Substring(0, at) + with + s.Substring(at + anchor.Length);
            if (nl) s = s.Replace("\n", "\r\n");
            File.WriteAllText(path, s);
            return 1;
        }
    }
}
