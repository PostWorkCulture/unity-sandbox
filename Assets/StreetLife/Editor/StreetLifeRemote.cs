// Lets the Cowork chats drive the editor when nobody's at the keyboard (match chat). Every half second the editor looks for
// Logs/streetlife-cmd.txt; each line is a command, the file is deleted, and results go to Logs/streetlife-remote.txt.
// Commands:
//   refresh                     re-import changed files and recompile
//   play | stop                 enter / leave play mode (the game keeps running with the editor in the background)
//   shot <name>                 save the Game view to Logs/<name>.png (play mode)
//   menu <Path/To/Item>         run any editor menu item, e.g. "menu Street Life/Apply look"
//   build-webgl [street|all] [publish]
//   publish                     push Builds/site to gh-pages
//   build-android
//   eval-map <id>               pick the map for the next play (PlayerPrefs StreetLife.map)
// Only reads a file inside this project's Logs folder (git-ignored); nothing is listened for on the network.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    static class StreetLifeRemote
    {
        static readonly string Dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
        static readonly string Cmd = Path.Combine(Dir, "streetlife-cmd.txt"), Out = Path.Combine(Dir, "streetlife-remote.txt");
        static double next;

        static IntPtr hwnd;
        static System.Threading.Thread watcher; static volatile bool stopWatch;

        static StreetLifeRemote()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            PlayerSettings.runInBackground = true;   // play mode keeps going while the editor isn't the front window
            // An unfocused, idle editor stops ticking, so nothing would read the command file. A background thread checks
            // for it every half second and, when there is one, nudges the editor's window (a WM_NULL message) to wake it.
#if UNITY_EDITOR_WIN
            try { hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; } catch { }
            if (watcher == null)
            {
                watcher = new System.Threading.Thread(() =>
                {
                    while (!stopWatch)
                    {
                        try { System.Threading.Thread.Sleep(500); if (hwnd != IntPtr.Zero && File.Exists(Cmd)) PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero); }
                        catch { }
                    }
                }) { IsBackground = true, Name = "StreetLife remote" };
                watcher.Start();
                AssemblyReloadEvents.beforeAssemblyReload += () => { stopWatch = true; watcher = null; };
            }
#endif
        }
#if UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
#endif

        static void Say(string s) { try { File.AppendAllText(Out, $"{DateTime.Now:HH:mm:ss} {s}\n"); } catch { } }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .5;
            if (!File.Exists(Cmd)) return;
            EditorApplication.QueuePlayerLoopUpdate();   // keep ticking until the commands are done
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string[] lines;
            try { lines = File.ReadAllLines(Cmd); File.Delete(Cmd); } catch { return; }
            foreach (var raw in lines)
            {
                var line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
                var sp = line.IndexOf(' '); var verb = sp < 0 ? line : line.Substring(0, sp); var arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                Say("> " + line);
                try { Run(verb, arg); }
                catch (Exception e) { Say("error: " + e.Message); }
            }
        }

        static void Run(string verb, string arg)
        {
            switch (verb)
            {
                case "refresh": AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); Say("refreshed"); break;
                case "play": EditorApplication.isPlaying = true; Say("playing"); break;
                case "stop": EditorApplication.isPlaying = false; Say("stopped"); break;
                case "shot":
                    {
                        var f = Path.Combine(Dir, (arg.Length > 0 ? arg : "shot") + ".png");
                        ScreenCapture.CaptureScreenshot(f); Say("shot " + f);
                        break;
                    }
                case "menu": Say(EditorApplication.ExecuteMenuItem(arg) ? "ran " + arg : "no menu item " + arg); break;
                case "eval-map": PlayerPrefs.SetString("StreetLife.map", arg); PlayerPrefs.Save(); Say("map " + arg); break;
                case "build-webgl":
                    StreetLifeBuild.Quiet = true; StreetLifeBuild.QuietAllMaps = arg.Contains("all"); StreetLifeBuild.QuietPublish = arg.Contains("publish");
                    try { StreetLifeBuild.BuildWebGL(); } finally { StreetLifeBuild.Quiet = false; }
                    Say("webgl: " + StreetLifeBuild.LastResult.Replace('\n', ' '));
                    break;
                case "publish":
                    StreetLifeBuild.Quiet = true;
                    try { StreetLifeBuild.PublishSite(); } finally { StreetLifeBuild.Quiet = false; }
                    Say("publish: " + StreetLifeBuild.LastResult.Replace('\n', ' '));
                    break;
                case "build-windows":
                    StreetLifeBuild.Quiet = true;
                    try { StreetLifeBuild.BuildWindows(); } finally { StreetLifeBuild.Quiet = false; }
                    Say("windows: " + StreetLifeBuild.LastResult.Replace('\n', ' '));
                    break;
                case "build-android":
                    StreetLifeBuild.Quiet = true;
                    try { StreetLifeBuild.BuildAndroid(); } finally { StreetLifeBuild.Quiet = false; }
                    Say("android: " + StreetLifeBuild.LastResult.Replace('\n', ' '));
                    break;
                default: Say("unknown command " + verb); break;
            }
        }
    }
}
