// Builds for the web-vs-Unity comparison (Street Life menu):
//  - WebGL for GitHub Pages: gzip with the in-browser fallback (Pages can't send Content-Encoding headers), the Street Life
//    page template, into Builds/site/unity, with the comparison page as Builds/site/index.html.
//  - Publish site: pushes Builds/site to the repo's gh-pages branch (replacing it), so it appears at
//    https://<owner>.github.io/<repo>/ next to the web version at https://<owner>.github.io/1v1/.
//  - Android APK for the S24: IL2CPP, ARM64, landscape, into Builds/Android/StreetLife.apk (needs Android Build Support).
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace StreetLife.EditorTools
{
    public static class StreetLifeBuild
    {
        const string Scene = "Assets/StreetLife/Scenes/StreetLife.unity";
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);
        static string SiteDir => Path.Combine(ProjectDir, "Builds", "site");

        // set by StreetLifeRemote: no dialogs; questions take these answers and messages go to the log
        public static bool Quiet, QuietAllMaps, QuietPublish;
        public static string LastResult = "";
        static bool Dlg(string title, string msg, string ok, string cancel = null)
        {
            if (Quiet) { LastResult = msg; Debug.Log("Street Life build: " + msg.Replace('\n', ' ')); return true; }
            return cancel == null ? EditorUtility.DisplayDialog(title, msg, ok) : EditorUtility.DisplayDialog(title, msg, ok, cancel);
        }

        [MenuItem("Street Life/Build/WebGL for GitHub Pages")]
        public static void BuildWebGL()
        {
            if (!File.Exists(Scene)) { Dlg("Street Life", "The Street Life scene is missing: run Street Life > Rebuild materials and scene first.", "OK"); return; }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            { Dlg("Street Life", "WebGL Build Support isn't installed. Add it to this Unity version in Unity Hub (Installs > Add modules).", "OK"); return; }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL &&
                !Dlg("Street Life", "Switching the project to WebGL re-imports the textures once (a few minutes). Carry on?", "Build", "Cancel")) return;

            int which = Quiet ? (QuietAllMaps ? 2 : 0) : EditorUtility.DisplayDialogComplex("Street Life", "Which maps go in the browser build?\n\nStreet Life only is the fair comparison with the web version (and a much smaller download).", "Street Life only", "Cancel", "All eight maps");
            if (which == 1) return;
            bool streetOnly = which == 0;

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;          // GitHub Pages serves .gz without Content-Encoding
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:StreetLife";
            PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;
            PlayerSettings.productName = "Street Life";
            PlayerSettings.companyName = "PostWorkCulture";

            var outDir = Path.Combine(SiteDir, "unity");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            BuildReport report;
            const string mapsAsset = "Assets/StreetLife/Resources/StreetLifeMaps.asset";
            string backup = streetOnly && File.Exists(mapsAsset) ? File.ReadAllText(mapsAsset) : null;
            try
            {
                if (backup != null) LeaveOutOtherMaps(mapsAsset);
                report = Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, outDir);
            }
            finally
            {
                if (backup != null) { File.WriteAllText(mapsAsset, backup); AssetDatabase.ImportAsset(mapsAsset); }
            }
            if (report == null) return;

            // the comparison page and Pages housekeeping
            var cmp = Path.Combine(ProjectDir, "Comparison", "index.html");
            if (File.Exists(cmp)) File.Copy(cmp, Path.Combine(SiteDir, "index.html"), true);
            File.WriteAllText(Path.Combine(SiteDir, ".nojekyll"), "");
            long bytes = Directory.GetFiles(Path.Combine(outDir, "Build")).Sum(f => new FileInfo(f).Length);
            int secs = Mathf.RoundToInt((float)report.summary.totalTime.TotalSeconds);   // (a format spec right before "}}" came out as "F1184")
            File.WriteAllText(Path.Combine(outDir, "build-info.json"),
                $"{{\"unity\":\"{Application.unityVersion}\",\"built\":\"{System.DateTime.UtcNow:O}\",\"buildBytes\":{bytes},\"seconds\":{secs}}}");
            Debug.Log("Street Life build: " + SplitData(""));
            if (Quiet ? QuietPublish : Dlg("Street Life", $"WebGL build done in {report.summary.totalTime.TotalMinutes:F1} min.\nDownload: {bytes / 1048576f:F1} MB (compressed).\n\nPublish it to GitHub Pages now?", "Publish", "Not now"))
                PublishSite();
        }

        /// GitHub refuses any file over 100 MB, and the data file with every map in is bigger. Past 95 MB it is cut into 45 MB
        /// parts here; the page (the template's dataParts) fetches them and hands Unity's loader one blob.
        /// Remote: call StreetLifeBuild.SplitData   (on the build in Builds/site; does nothing if it is small or already cut)
        public static string SplitData(string arg)
        {
            string dir = Path.Combine(SiteDir, "unity", "Build"), page = Path.Combine(SiteDir, "unity", "index.html");
            if (!Directory.Exists(dir) || !File.Exists(page)) return "no web build";
            string html = File.ReadAllText(page);
            if (!html.Contains("/*SL_DATA_PARTS*/[]")) return html.Contains("/*SL_DATA_PARTS*/") ? "the data file is already in parts" : "this page can't take parts (built from the old template)";
            string data = Directory.GetFiles(dir, "*.data*").FirstOrDefault(f => !f.Contains(".part"));
            if (data == null) return "no data file";
            long len = new FileInfo(data).Length; const long limit = 95L << 20, size = 45L << 20;
            if (len <= limit) return $"data file is {len >> 20} MB: left whole";
            var names = new System.Collections.Generic.List<string>(); var buf = new byte[1 << 20];
            using (var src = File.OpenRead(data))
            {
                for (int k = 0; src.Position < len; k++)
                {
                    string name = Path.GetFileName(data) + ".part" + k; names.Add(name);
                    using (var dst = File.Create(Path.Combine(dir, name)))
                    {
                        long left = size; int n;
                        while (left > 0 && (n = src.Read(buf, 0, (int)System.Math.Min(buf.Length, left))) > 0) { dst.Write(buf, 0, n); left -= n; }
                    }
                }
            }
            File.Delete(data);
            html = html.Replace("/*SL_DATA_PARTS*/[]", "/*SL_DATA_PARTS*/[" + string.Join(",", names.Select(n => "\"" + n + "\"")) + "]").Replace("/*SL_DATA_BYTES*/0", "/*SL_DATA_BYTES*/" + len);
            File.WriteAllText(page, html);
            return $"data file of {len >> 20} MB cut into {names.Count} parts";
        }

        [MenuItem("Street Life/Build/Publish site to GitHub Pages")]
        public static void PublishSite()
        {
            if (!File.Exists(Path.Combine(SiteDir, "unity", "index.html"))) { Dlg("Street Life", "Build the WebGL version first.", "OK"); return; }
            var origin = Git(ProjectDir, "config --get remote.origin.url").Trim();
            if (string.IsNullOrEmpty(origin)) { Dlg("Street Life", "This project has no git remote called origin.", "OK"); return; }
            // a throwaway repo in Builds/site, force-pushed to gh-pages: the branch only ever holds the latest build
            var gitDir = Path.Combine(SiteDir, ".git");
            if (Directory.Exists(gitDir))
            {   // git's object files are read-only on Windows, and Directory.Delete won't remove read-only files
                foreach (var f in Directory.GetFiles(gitDir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(gitDir, true);
            }
            string log = "";
            log += Git(SiteDir, "init -q");
            log += Git(SiteDir, "checkout -q -b gh-pages");
            log += Git(SiteDir, "add -A");
            log += Git(SiteDir, "-c user.name=PostWorkCulture -c user.email=postworkculture@users.noreply.github.com commit -q -m \"Street Life comparison site\"");
            EditorUtility.DisplayProgressBar("Street Life", "Pushing to GitHub (a sign-in window may open)", .5f);
            try { log += Git(SiteDir, $"push -f \"{origin}\" gh-pages", 900); }
            finally { EditorUtility.ClearProgressBar(); }
            Debug.Log("Street Life publish:\n" + log);
            if (log.Contains("rejected]") || log.Contains("error: failed to push") || log.Contains("fatal:"))
            {   // git says why on the line starting "remote: error: File" or "fatal:"
                string why = log.Split('\n').FirstOrDefault(l => l.Contains("exceeds GitHub's file size limit") || l.StartsWith("fatal:")) ?? "see the Console";
                Dlg("Street Life", "NOT published: the push to gh-pages was refused. " + why.Trim(), "OK");
                return;
            }
            var site = PagesUrl(origin);
            Dlg("Street Life", $"Pushed to the gh-pages branch.\n\nIf this is the first time: on GitHub open the repo's Settings > Pages and choose 'Deploy from a branch', gh-pages, / (root).\n\nThen: {site}", "OK");
        }

        /// Windows desktop: 64-bit, the "PC" quality level, into Builds/Windows/StreetLife.exe (Mono, so no extra module is needed).
        [MenuItem("Street Life/Build/Windows (desktop)")]
        public static void BuildWindows()
        {
            // a running copy holds its data files open: building or installing under it leaves files the game then reads as corrupt
            if (System.Diagnostics.Process.GetProcessesByName("StreetLife").Length > 0) { Dlg("Street Life", "Close Street Life first: the game is still running, and building under it corrupts its data files.", "OK"); return; }
            var nb = NamedBuildTarget.Standalone;
            PlayerSettings.productName = "Street Life"; PlayerSettings.companyName = "PostWorkCulture";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;   // borderless full screen: exclusive at launch hangs on this laptop (hybrid GPU); Settings > Display offers it
            PlayerSettings.defaultIsNativeResolution = true; PlayerSettings.runInBackground = true; PlayerSettings.enableFrameTimingStats = true;   // PerfProbe reads main/render/GPU times
            // Windows.Gaming.Input hangs the player at startup when it's launched from Explorer on this PC (a shortcut double-click
            // never got past "Using Windows.Gaming.Input"); XInput covers Xbox-style pads and the Input System still sees the rest
            PlayerSettings.windowsGamepadBackendHint = WindowsGamepadBackendHint.WindowsGamepadBackendHintXInput;
            PlayerSettings.SetScriptingBackend(nb, ScriptingImplementation.Mono2x);
            var outDir = Path.Combine(ProjectDir, "Builds", "Windows"); Directory.CreateDirectory(outDir);
            var exe = Path.Combine(outDir, "StreetLife.exe");
            var data = Path.Combine(outDir, "StreetLife_Data");   // a full write every time, never a patch over old data files
            if (Directory.Exists(data)) { foreach (var f in Directory.GetFiles(data, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(data, true); }
            var report = Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, exe);
            if (report == null) return;
            long bytes = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            var inst = InstallWindows();
            Dlg("Street Life", $"Windows build: {bytes / 1048576f:F0} MB in Builds/Windows, {report.summary.totalTime.TotalMinutes:F1} min ({PlayerSettings.GetScriptingBackend(nb)}). {inst}", "OK");
        }

        /// remote: call StreetLifeBuild.BuildWindowsTrace : a development build in Builds/WindowsTrace, only for recording the
        /// graphics warm-up (GfxWarmup -gfxtrace): release players don't record pipeline states
        public static string BuildWindowsTrace(string a)
        {
            var outDir = Path.Combine(ProjectDir, "Builds", "WindowsTrace"); Directory.CreateDirectory(outDir);
            var report = Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, Path.Combine(outDir, "StreetLife.exe"), BuildOptions.Development);
            return report == null ? "trace build failed" : $"trace build in Builds/WindowsTrace, {report.summary.totalTime.TotalMinutes:F1} min";
        }

        // ------------------------------------------------------------------ the playable copy outside the project (match chat)
        // Every Windows build is copied to C:\Games\Street Life (without Unity's debug folders) and the desktop gets a "Street Life"
        // shortcut to it, so the game is always one double-click away and a build in progress never touches the copy you play.
        public const string InstallDir = @"C:\Games\Street Life";

        [MenuItem("Street Life/Build/Install Windows build to C:\\Games")]
        public static void InstallWindowsMenu() => Dlg("Street Life", InstallWindows(), "OK");

        /// remote: call StreetLifeBuild.Install
        public static string Install(string a) => InstallWindows();

        public static string InstallWindows()
        {
            if (System.Diagnostics.Process.GetProcessesByName("StreetLife").Length > 0) return "Not installed: close Street Life first.";
            var src = Path.Combine(ProjectDir, "Builds", "Windows");
            if (!File.Exists(Path.Combine(src, "StreetLife.exe"))) return "No Windows build to install yet.";
            try
            {
                if (Directory.Exists(InstallDir))
                {
                    foreach (var f in Directory.GetFiles(InstallDir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                    Directory.Delete(InstallDir, true);
                }
                long bytes = 0;
                foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                {
                    if (dir.Contains("_ButDontShipItWithYourGame") || dir.Contains("_DoNotShip")) continue;
                    Directory.CreateDirectory(dir.Replace(src, InstallDir));
                }
                Directory.CreateDirectory(InstallDir);
                foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    if (file.Contains("_ButDontShipItWithYourGame") || file.Contains("_DoNotShip")) continue;
                    var to = file.Replace(src, InstallDir); Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(file, to, true); bytes += new FileInfo(file).Length;
                }
                // the game hangs on load when Windows starts it straight from a .lnk on this PC (it never gets past the input setup),
                // yet starts fine any other way: so the shortcut runs a one-line launcher that starts the game itself
                File.WriteAllText(Path.Combine(InstallDir, "Launch.vbs"), "Set s = CreateObject(\"WScript.Shell\")\r\ns.CurrentDirectory = \"" + InstallDir + "\"\r\ns.Run \"\"\"\" & s.CurrentDirectory & \"\\StreetLife.exe\"\"\", 1, False\r\n");
                string sc = DesktopShortcut(Path.Combine(InstallDir, "StreetLife.exe"));
                return $"Installed to {InstallDir} ({bytes / 1048576f:F0} MB){sc}.";
            }
            catch (System.Exception e) { return $"Couldn't install to {InstallDir} (is the game still running?): {e.Message}"; }
        }

        /// remote: call StreetLifeBuild.GameProbe [kill|run]: the running copies of the game, or close them, or start the installed one windowed with its log in Logs/game.log
        public static string GameProbe(string a)
        {
            a = (a ?? "").Trim(); var sb = new System.Text.StringBuilder();
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("StreetLife"))
            {
                string path = "?"; try { path = p.MainModule.FileName; } catch { }
                sb.Append($"pid {p.Id} {path} started {p.StartTime:HH:mm:ss} responding {p.Responding} window '{p.MainWindowTitle}' mem {p.WorkingSet64 / 1048576} MB; ");
                if (a == "kill") { try { p.Kill(); sb.Append("killed; "); } catch (System.Exception e) { sb.Append(e.Message + "; "); } }
            }
            if (a.StartsWith("trace "))   // call StreetLifeBuild.GameProbe trace <args>: the development build in Builds/WindowsTrace
            {
                var dir = Path.Combine(ProjectDir, "Builds", "WindowsTrace");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(dir, "StreetLife.exe"), a.Substring(6)) { WorkingDirectory = dir, UseShellExecute = false });
                sb.Append("started the trace build with " + a.Substring(6));
            }
            if (a.StartsWith("args "))   // call StreetLifeBuild.GameProbe args -screen-fullscreen 0
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(InstallDir, "StreetLife.exe"), a.Substring(5)) { WorkingDirectory = InstallDir, UseShellExecute = false });
                sb.Append("started with " + a.Substring(5));
            }
            if (a.StartsWith("via "))   // via cmd | via explorer | via env: how it's started, to find what a shortcut launch does differently
            {
                var exe = Path.Combine(InstallDir, "StreetLife.exe"); var how = a.Substring(4).Trim();
                System.Diagnostics.ProcessStartInfo psi;
                if (how == "cmd") psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c start \"\" \"{exe}\"");
                else if (how == "explorer") psi = new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{exe}\"");
                else psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = InstallDir };
                if (how != "shell") psi.UseShellExecute = false;
                System.Diagnostics.Process.Start(psi); sb.Append("started via " + how);
            }
            if (a == "env")
            {
                foreach (System.Collections.DictionaryEntry e in System.Environment.GetEnvironmentVariables()) sb.Append(e.Key + "=" + (e.Value + "").Substring(0, System.Math.Min(80, (e.Value + "").Length)) + " | ");
            }
            if (a == "shortcut")   // exactly what a double-click on the desktop shortcut does
            {
                var lnk = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory), "Street Life.lnk");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(lnk) { UseShellExecute = true });
                sb.Append("started from the desktop shortcut");
            }
            if (a == "run")
            {
                var log = Path.Combine(ProjectDir, "Logs", "game.log");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(InstallDir, "StreetLife.exe"), $"-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile \"{log}\"") { WorkingDirectory = InstallDir, UseShellExecute = false });
                sb.Append("started windowed, log in Logs/game.log");
            }
            return sb.Length == 0 ? "not running" : sb.ToString();
        }

        static string DesktopShortcut(string exe)
        {
            try
            {
                var desk = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
                var lnk = Path.Combine(desk, "Street Life.lnk");
                var ps = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{lnk}');$s.TargetPath=$env:WINDIR+'\\System32\\wscript.exe';$s.Arguments=[char]34+'{InstallDir}\\Launch.vbs'+[char]34;$s.WorkingDirectory='{InstallDir}';$s.IconLocation='{exe},0';$s.Description='Street Life';$s.Save()";
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + ps + "\"") { UseShellExecute = false, CreateNoWindow = true });
                p.WaitForExit(20000);
                return File.Exists(lnk) ? ", with a Street Life shortcut on the desktop" : " (couldn't make the desktop shortcut)";
            }
            catch (System.Exception e) { return " (no desktop shortcut: " + e.Message + ")"; }
        }

        [MenuItem("Street Life/Build/Android APK (S24)")]
        public static void BuildAndroid()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            { Dlg("Street Life", "Android Build Support isn't installed. In Unity Hub: Installs > this version > Add modules > Android Build Support (with OpenJDK and the Android SDK & NDK). Then run this again.", "OK"); return; }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !Dlg("Street Life", "Switching the project to Android re-imports the textures once (a few minutes). Carry on?", "Build", "Cancel")) return;

            var nb = NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(nb, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApplicationIdentifier(nb, "com.postworkculture.streetlife");
            PlayerSettings.productName = "Street Life";
            PlayerSettings.companyName = "PostWorkCulture";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true; PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false; PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            EditorUserBuildSettings.buildAppBundle = false;

            var outDir = Path.Combine(ProjectDir, "Builds", "Android"); Directory.CreateDirectory(outDir);
            var apk = Path.Combine(outDir, "StreetLife.apk");
            var report = Build(BuildTarget.Android, BuildTargetGroup.Android, apk);
            if (report == null) return;
            var mb = new FileInfo(apk).Length / 1048576f;
            Dlg("Street Life", $"APK built: {mb:F0} MB, in {report.summary.totalTime.TotalMinutes:F1} min.\n\nCopy Builds/Android/StreetLife.apk to the S24 (USB, Drive or Quick Share), open it and allow installs from that app.", "OK");
            if (!Quiet) EditorUtility.RevealInFinder(apk);
        }

        // the maps chat's registry keeps every map's data; for a Street Life only build, blank the others' references
        // for the length of the build (the file is put back byte for byte afterwards)
        static void LeaveOutOtherMaps(string path)
        {
            var set = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path); if (!set) return;
            var so = new SerializedObject(set); var maps = so.FindProperty("maps"); if (maps == null) return;
            for (int i = 0; i < maps.arraySize; i++)
            {
                var e = maps.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("id").stringValue == "street") continue;
                foreach (var f in new[] { "mapJson", "mapBin", "dynJson", "dynBin", "extrasJson", "extrasBin", "envJson", "library" })
                { var p = e.FindPropertyRelative(f); if (p != null) p.objectReferenceValue = null; }
            }
            so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
        }

        // Railed's and Beach Please's baked light each live in a scene of their own (Editor/RailBake.cs), loaded on top of the game's when Railed is played.
        // Not on WebGL: it has no probe volumes, and the baked data would only make the download bigger.
        static string[] Scenes(BuildTarget target)
        {
            var list = new System.Collections.Generic.List<string> { Scene };
            if (target != BuildTarget.WebGL) foreach (var s in new[] { RailLook.LightScene, BeachLook.LightScene }) if (File.Exists(s)) list.Add(s);
            return list.ToArray();
        }

        static BuildReport Build(BuildTarget target, BuildTargetGroup group, string path, BuildOptions extra = BuildOptions.None)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target && !EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
            { Dlg("Street Life", "Couldn't switch the project to " + target + ".", "OK"); return null; }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = Scenes(target), locationPathName = path, target = target, targetGroup = group, options = extra });
            if (report.summary.result != BuildResult.Succeeded)
            {
                var errs = string.Join("\n", report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(6));
                Dlg("Street Life", $"The {target} build failed ({report.summary.totalErrors} errors).\n\n{errs}", "OK");
                return null;
            }
            return report;
        }

        static string PagesUrl(string origin)
        {
            // https://github.com/Owner/repo(.git) or git@github.com:Owner/repo(.git)
            var s = origin.Replace("git@github.com:", "https://github.com/");
            if (s.EndsWith(".git")) s = s.Substring(0, s.Length - 4);
            var parts = s.TrimEnd('/').Split('/');
            return parts.Length >= 2 ? $"https://{parts[parts.Length - 2].ToLowerInvariant()}.github.io/{parts[parts.Length - 1]}/" : origin;
        }

        static string Git(string dir, string args, int timeoutSec = 120)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args) { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using var p = Process.Start(psi);
                var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutSec * 1000)) { p.Kill(); return $"git {args}: timed out\n"; }
                if (p.ExitCode != 0) Debug.LogWarning($"git {args} failed: {e.Result}");
                return o.Result + e.Result;
            }
            catch (System.Exception ex) { Debug.LogError("Couldn't run git (is Git for Windows installed?): " + ex.Message); return ""; }
        }
    }
}
