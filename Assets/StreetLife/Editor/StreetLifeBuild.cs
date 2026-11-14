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
            if (Quiet ? QuietPublish : Dlg("Street Life", $"WebGL build done in {report.summary.totalTime.TotalMinutes:F1} min.\nDownload: {bytes / 1048576f:F1} MB (compressed).\n\nPublish it to GitHub Pages now?", "Publish", "Not now"))
                PublishSite();
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
            var site = PagesUrl(origin);
            Dlg("Street Life", $"Pushed to the gh-pages branch.\n\nIf this is the first time: on GitHub open the repo's Settings > Pages and choose 'Deploy from a branch', gh-pages, / (root).\n\nThen: {site}", "OK");
        }

        /// Windows desktop: 64-bit, the "PC" quality level, into Builds/Windows/StreetLife.exe (Mono, so no extra module is needed).
        [MenuItem("Street Life/Build/Windows (desktop)")]
        public static void BuildWindows()
        {
            var nb = NamedBuildTarget.Standalone;
            PlayerSettings.productName = "Street Life"; PlayerSettings.companyName = "PostWorkCulture";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true; PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(nb, ScriptingImplementation.Mono2x);
            var outDir = Path.Combine(ProjectDir, "Builds", "Windows"); Directory.CreateDirectory(outDir);
            var exe = Path.Combine(outDir, "StreetLife.exe");
            var report = Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, exe);
            if (report == null) return;
            long bytes = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            Dlg("Street Life", $"Windows build: {bytes / 1048576f:F0} MB in Builds/Windows, {report.summary.totalTime.TotalMinutes:F1} min ({PlayerSettings.GetScriptingBackend(nb)}). Run StreetLife.exe.", "OK");
            if (!Quiet) EditorUtility.RevealInFinder(exe);
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

        static BuildReport Build(BuildTarget target, BuildTargetGroup group, string path)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target && !EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
            { Dlg("Street Life", "Couldn't switch the project to " + target + ".", "OK"); return null; }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = Scenes(target), locationPathName = path, target = target, targetGroup = group, options = BuildOptions.None });
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
