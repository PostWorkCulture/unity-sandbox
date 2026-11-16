// Developer tools switch. On in the Unity editor, and in a build started with -dev on the command line; off for
// players. Behind it: F2 bot info, F3 character picker, F4 map picker (and the MAPS button on phones), the F9 / F10
// finisher test keys, and the Tab screen's weapon swapping and free shop.
using UnityEngine;

namespace StreetLife
{
    public static class Dev
    {
        static bool looked, on;
        public static bool On
        {
            get
            {
                if (!looked)
                {
                    looked = true; on = Application.isEditor;
                    if (!on) foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-dev") on = true;
                }
                return on;
            }
        }
        /// remote test: call Dev.Test off | on  (see the game as a player does, in the editor)
        public static string Test(string a) { looked = true; on = (a ?? "").Trim() != "off"; return "developer tools " + (on ? "on" : "off"); }
    }
}
