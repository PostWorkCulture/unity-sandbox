// Railed look work: hands-off screenshots of a map from fixed viewpoints, without the HUD (an extra camera drawn to a texture).
//   remote: call RailShots.Run <tag> [map] [high|smooth]     (map defaults to rail; the last one picks the Graphics setting for the run)
// Plays the map, starts the match, takes every view listed in Logs/railshots.txt and leaves play mode again, restoring the
// map pick. Shots go to Logs/rail/<tag>_<name>.png, a line per run to Logs/rail/log.txt. It refuses while play mode is on,
// so it never cuts into another chat's test.
// Logs/railshots.txt, one view per line:   name x y z yaw pitch [fov]     |   top name cx cz halfHeight   |   player x y z yawDeg
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    public static class RailShots
    {
        const string Key = "StreetLife.RailShots";
        static readonly string Root = Path.GetDirectoryName(Application.dataPath);
        static readonly string OutDir = Path.Combine(Root, "Logs", "rail"), ListFile = Path.Combine(Root, "Logs", "railshots.txt");
        const int W = 1600, H = 900;

        static List<string[]> views; static int index, shotFrame; static Camera cam; static RenderTexture rt;

        static RailShots() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }

        static int Phase { get => SessionState.GetInt(Key + ".phase", 0); set => SessionState.SetInt(Key + ".phase", value); }
        static double Mark { get => SessionState.GetFloat(Key + ".t", 0); set => SessionState.SetFloat(Key + ".t", (float)value); }
        static double Now => EditorApplication.timeSinceStartup;

        public static string Run(string arg)
        {
            if (Phase != 0) return "busy: a shot run is already going or waiting";
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {   // take the next gap: starts by itself as soon as play mode ends
                SessionState.SetString(Key + ".arg", arg ?? ""); Phase = 9; Mark = Now;
                return "queued: the shots start when play mode ends";
            }
            return Begin(arg);
        }

        static string Begin(string arg)
        {
            var parts = (arg ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string tag = parts.Length > 0 ? parts[0] : "shot", map = parts.Length > 1 ? parts[1] : "rail";
            SessionState.SetString(Key + ".tag", tag);
            SessionState.SetString(Key + ".gfx", parts.Length > 2 ? Settings.Graphics : "");
            if (parts.Length > 2) Settings.Graphics = parts[2];
            SessionState.SetString(Key + ".prev", PlayerPrefs.GetString(MapSet.Pref, MapSet.Default));
            PlayerPrefs.SetString(MapSet.Pref, map); PlayerPrefs.Save();
            Directory.CreateDirectory(OutDir);
            Phase = 1; Mark = Now;
            EditorApplication.isPlaying = true;
            return "shots " + tag + " on " + map + " started";
        }

        static void Log(string s) { try { Directory.CreateDirectory(OutDir); File.AppendAllText(Path.Combine(OutDir, "log.txt"), $"{DateTime.Now:HH:mm:ss} {SessionState.GetString(Key + ".tag", "")}: {s}\n"); } catch { } }

        static void Finish(string why)
        {
            if (cam) UnityEngine.Object.Destroy(cam.gameObject); cam = null;
            if (rt) { rt.Release(); UnityEngine.Object.Destroy(rt); } rt = null;
            views = null;
            Log(why);
            if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; Phase = 5; Mark = Now; }
            else Restore();
        }

        static void Restore()
        {
            PlayerPrefs.SetString(MapSet.Pref, SessionState.GetString(Key + ".prev", MapSet.Default));
            string g = SessionState.GetString(Key + ".gfx", ""); if (g != "") Settings.Graphics = g;
            PlayerPrefs.Save();
            Phase = 0;
            Log("finished, map pick back to " + PlayerPrefs.GetString(MapSet.Pref, MapSet.Default));
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static void Tick()
        {
            int phase = Phase; if (phase == 0) return;
            try { Step(phase); }
            catch (Exception e) { Finish("error: " + e.Message); }
        }

        static void Step(int phase)
        {
            switch (phase)
            {
                case 9:
                    if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { if (Now - Mark > 900) { Phase = 0; Log("gave up waiting for a gap"); } return; }
                    Phase = 0; Begin(SessionState.GetString(Key + ".arg", ""));
                    return;
                case 1:   // waiting for play mode and the map
                    if (Now - Mark > 60) { Finish("gave up: play mode never started"); return; }
                    if (!EditorApplication.isPlaying || Time.frameCount < 20 || !RoundManager.Instance) return;
                    {
                        var mm = UnityEngine.Object.FindAnyObjectByType<MainMenu>();
                        var play = mm ? typeof(MainMenu).GetMethod("Play", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) : null;
                        if (play != null) play.Invoke(mm, null); else RoundManager.Instance.BeginFromMenu();
                        Phase = 2; Mark = Now;
                    }
                    return;
                case 2:   // the match is starting: let the reflections and the first frames settle, then set the views up
                    if (!EditorApplication.isPlaying) { Finish("stopped by someone else before the shots"); return; }
                    if (Now - Mark < 3.5) return;
                    views = new List<string[]>();
                    if (File.Exists(ListFile))
                        foreach (var raw in File.ReadAllLines(ListFile))
                        {
                            var l = raw.Trim(); if (l.Length == 0 || l.StartsWith("#")) continue;
                            views.Add(l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                        }
                    if (views.Count == 0) views.Add(new[] { "view", "0", "1.8", "-20", "0", "0" });
                    rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Rail shots" };
                    var go = new GameObject("Rail shots camera");
                    cam = go.AddComponent<Camera>(); cam.depth = -50; cam.fieldOfView = 60; cam.nearClipPlane = .1f; cam.farClipPlane = 800; cam.targetTexture = rt;
                    go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = true;
                    index = -1; shotFrame = 0;
                    Log($"map {MapSet.Current}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, {views.Count} lines");
                    Phase = 3;
                    return;
                case 3:
                    if (!EditorApplication.isPlaying) { Finish("stopped by someone else during the shots"); return; }
                    if (index >= 0 && Time.frameCount < shotFrame) return;
                    if (index >= 0) Save(views[index][0] == "top" ? views[index][1] : views[index][0]);
                    // next view (player lines are applied on the way)
                    while (++index < views.Count)
                    {
                        var v = views[index];
                        if (v[0] == "player")
                        {
                            var p = RoundManager.Instance ? RoundManager.Instance.player : null;
                            if (p && v.Length >= 5) { p.Place(new Vector3(F(v[1]), F(v[2]), F(v[3])), F(v[4]) * Mathf.Deg2Rad); p.Frozen = true; }
                            continue;
                        }
                        if (v[0] == "top" && v.Length >= 5)
                        {
                            cam.orthographic = true; cam.orthographicSize = F(v[4]);
                            cam.transform.position = new Vector3(F(v[2]), 120, F(v[3])); cam.transform.rotation = Quaternion.Euler(90, 0, 0);
                        }
                        else if (v.Length >= 6)
                        {
                            cam.orthographic = false; cam.fieldOfView = v.Length > 6 ? F(v[6]) : 60;
                            cam.transform.position = new Vector3(F(v[1]), F(v[2]), F(v[3])); cam.transform.rotation = Quaternion.Euler(F(v[5]), F(v[4]), 0);
                        }
                        else continue;
                        shotFrame = Time.frameCount + 4;
                        return;
                    }
                    Finish("took the shots");
                    return;
                case 5:   // leaving play mode
                    if (EditorApplication.isPlaying && Now - Mark < 20) return;
                    Restore();
                    return;
            }
        }

        static void Save(string name)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutDir, SessionState.GetString(Key + ".tag", "shot") + "_" + name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }
    }
}
