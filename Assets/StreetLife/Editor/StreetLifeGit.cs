// Lets any Cowork chat commit and push the project without Pete (progression chat, 13 Nov), through the editor's remote
// control (Logs/streetlife-cmd.txt, see StreetLifeRemote.cs). Git runs here on Windows with Pete's own git sign-in, the same
// way "Publish site" already pushes gh-pages, so no token is stored anywhere.
//   call StreetLifeGit.Save <message>     stage everything, commit, then push main in the background
//                                         (use \n for new lines; refused while scripts don't compile; during play mode it waits
//                                         and commits when play mode ends)
//   call StreetLifeGit.Push               push main again (e.g. after a failed push)
//   call StreetLifeGit.Status             branch, changed files, commits not yet pushed, last push result
// Results also go to Logs/streetlife-git.txt. The push never forces; if GitHub has commits this copy doesn't, it stops and says so.
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StreetLife.EditorTools
{
    public static class StreetLifeGit
    {
        static readonly string Root = Path.GetDirectoryName(Application.dataPath);
        static readonly string LogFile = Path.Combine(Root, "Logs", "streetlife-git.txt");
        static readonly string MsgFile = Path.Combine(Root, "Logs", "streetlife-commit-msg.txt");
        static volatile bool pushing;
        static volatile string lastPush = "none yet";

        static string waiting;   // a Save asked for during play mode: runs when play mode ends (another chat may be testing)

        public static string Save(string message)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (string.IsNullOrWhiteSpace(message)) return Log("refused: no commit message");
                waiting = waiting == null ? message : waiting + "\\n\\n" + message;
                EditorApplication.playModeStateChanged -= AfterPlay; EditorApplication.playModeStateChanged += AfterPlay;
                return Log("in play mode: the commit will be made when play mode ends");
            }
            if (BuildPipeline.isBuildingPlayer) return Log("refused: a build is running (a Street-Life-only WebGL build blanks the other maps until it ends); try again after");   // match chat
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return Log("refused: the scripts don't compile right now");
            if (pushing) return Log("refused: a push is still running");
            if (string.IsNullOrWhiteSpace(message)) return Log("refused: no commit message");
            AssetDatabase.SaveAssets();
            Git("add -A", 120, out _);
            Git("diff --cached --quiet", 60, out int changed);
            if (changed == 0) { Log("nothing to commit"); StartPush(); return "nothing to commit; pushing anything unpushed"; }
            File.WriteAllText(MsgFile, message.Replace("\\n", "\n").Trim() + "\n");
            var outp = Git($"commit -F \"{MsgFile}\"", 120, out int code);
            if (code != 0) return Log("commit failed: " + outp.Trim());
            var head = Git("log -1 --format=%h", 30, out _).Trim();
            Log("committed " + head + ": " + message.Split(new[] { "\\n" }, StringSplitOptions.None)[0]);
            StartPush();
            return "committed " + head + "; pushing in the background (see Logs/streetlife-git.txt)";
        }

        static void AfterPlay(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredEditMode || waiting == null) return;
            EditorApplication.playModeStateChanged -= AfterPlay;
            var m = waiting; waiting = null;
            EditorApplication.delayCall += () => Log("after play mode: " + Save(m));
        }

        public static string Push(string unused = "")
        {
            if (pushing) return "a push is already running";
            StartPush(); return "pushing in the background (see Logs/streetlife-git.txt)";
        }

        public static string Status(string unused = "")
        {
            var branch = Git("rev-parse --abbrev-ref HEAD", 30, out _).Trim();
            var dirty = Git("status --porcelain", 60, out _).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            var ahead = Git("rev-list --count @{u}..HEAD", 30, out int c).Trim();
            return $"branch {branch}, {dirty} changed files, {(c == 0 ? ahead : "?")} commits not pushed, last push: {lastPush}";
        }

        static void StartPush()
        {
            pushing = true;
            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    var o = Git("push origin HEAD:main", 600, out int code);
                    lastPush = (code == 0 ? "ok " : "FAILED ") + DateTime.Now.ToString("HH:mm:ss");
                    Log(code == 0 ? "pushed to GitHub (main)" : "push FAILED (nothing was forced): " + o.Trim().Replace('\n', ' '));
                }
                catch (Exception e) { lastPush = "FAILED"; Log("push FAILED: " + e.Message); }
                finally { pushing = false; }
            }) { IsBackground = true };
            t.Start();
        }

        static string Git(string args, int timeoutSec, out int exitCode)
        {
            exitCode = -1;
            try
            {
                var psi = new ProcessStartInfo("git", args) { WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";   // never sit waiting for a password nobody will type
                using var p = Process.Start(psi);
                var err = p.StandardError.ReadToEndAsync();
                var o = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(timeoutSec * 1000)) { try { p.Kill(); } catch { } return $"git {args}: timed out"; }
                exitCode = p.ExitCode;
                return o + err.Result;
            }
            catch (Exception e) { return "couldn't run git: " + e.Message; }
        }

        static string Log(string s)
        {
            try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); } catch { }
            return s;
        }
    }
}
