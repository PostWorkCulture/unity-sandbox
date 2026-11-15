// Map looks: bakes a map's bounce light into Adaptive Probe Volumes (not for WebGL: it has none). Railed and Beach Please.
//   Menu: Street Life > Maps > Bake Railed lighting / Bake Beach Please lighting.
//   Remote: call RailBake.Run [rail|wreck|skyhigh|lagoon|house|yard]   (progress in Logs/rail/bake.txt)
// The map is built at play time, so there is nothing in the game's scene to bake. This opens a scene of its own
// (RailLook.LightScene), builds the yard's solid geometry there with the surfaces and the sun the game uses, bakes, removes
// the geometry and the sun again and saves: what is left in that scene is just the probe volumes and their baked data.
// RailLook loads it on top of the game's scene when Railed is played. Bake again after the map, its surfaces or its sun change.
// The first run switches the PC quality level's Light Probe System to Adaptive Probe Volumes (maps with no baked data are
// lit by the ambient light exactly as before).   call RailBake.Off puts that setting back.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    public static class RailBake
    {
        const string Key = "StreetLife.RailBake";
        static string Map => SessionState.GetString(Key + ".map", "rail");
        static bool Beach => Map == "wreck";
        static string Dir => "Assets/StreetLife/Maps/" + Map + "/Light";
        static MapLook Look => MapLooks.Get(Map);   // a small map (MapLooks): everything below comes from its description and its own sky
        static string LightScene => Look != null ? Look.LightScene : Beach ? BeachLook.LightScene : RailLook.LightScene;
        static Vector3 lookSun = Vector3.up; static string lookHex = "#ffffff"; static float lookPower = 2.5f;
        static Vector3 Sun => Look != null ? lookSun : Beach ? BeachLook.Sun : RailLook.Sun;
        static string SunHex => Look != null ? lookHex : Beach ? BeachLook.SunHex : RailLook.SunHex;
        static float SunPower => Look != null ? lookPower : Beach ? BeachLook.SunPower : RailLook.SunPower;
        static readonly string LogFile = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "rail", "bake.txt");

        static RailBake()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= OnPlayMode; EditorApplication.playModeStateChanged += OnPlayMode;
        }

        // like a build, the bake has the editor to itself for a couple of minutes: a play request in the middle is held off
        static void OnPlayMode(PlayModeStateChange c)
        {
            if (c != PlayModeStateChange.ExitingEditMode || (Phase != 1 && Phase != 2)) return;
            EditorApplication.isPlaying = false;
            Log("held off a play request: the bake is still running");
        }

        static int Phase { get => SessionState.GetInt(Key + ".phase", 0); set => SessionState.SetInt(Key + ".phase", value); }
        static double Mark { get => SessionState.GetFloat(Key + ".t", 0); set => SessionState.SetFloat(Key + ".t", (float)value); }
        static double Now => EditorApplication.timeSinceStartup;

        static void Log(string s) { try { Directory.CreateDirectory(Path.GetDirectoryName(LogFile)); File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss} {s}\n"); } catch { } }

        [MenuItem("Street Life/Maps/Bake Railed lighting")]
        static void Menu() { Debug.Log("Railed lighting: " + Run("rail")); }

        [MenuItem("Street Life/Maps/Bake Beach Please lighting")]
        static void MenuBeach() { Debug.Log("Beach Please lighting: " + Run("wreck")); }

        public static string Run(string arg)
        {
            if (Phase != 0) return "busy: a bake is already going or waiting";
            SessionState.SetString(Key + ".arg", arg ?? "");
            string first = (arg ?? "").Trim().Split(' ')[0];
            SessionState.SetString(Key + ".map", MapLooks.Get(first) != null ? first : (arg ?? "").Contains("wreck") || (arg ?? "").Contains("beach") ? "wreck" : "rail");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { Phase = 9; Log("waiting for play mode to end"); return "queued: the bake starts when play mode ends"; }
            return Begin();
        }

        static string Begin()
        {
            var set = MapSet.Load(); var e = set ? set.Get(Map) : null;
            if (e == null || !e.mapJson || !e.mapBin || !e.library || !e.envJson || !set.sky) return "no data for " + Map;
            var asset = UniversalRenderPipeline.asset;
            if (!asset) return "no URP asset";
            if (asset.lightProbeSystem != LightProbeSystem.ProbeVolumes)
            {
                if (!SetProbeSystem(asset, 1)) return "couldn't switch the Light Probe System";
                Log("Adaptive Probe Volumes switched on in " + AssetDatabase.GetAssetPath(asset));
            }
            SessionState.SetString(Key + ".prev", SceneManager.GetActiveScene().path);
            Phase = 1; Mark = Now;
            Log("bake of " + Map + " asked for, quality level " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            return "bake started (progress in Logs/rail/bake.txt)";
        }

        public static string Off(string arg)
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (a && a.lightProbeSystem == LightProbeSystem.ProbeVolumes && SetProbeSystem(a, 0)) n++;
            }
            return "Light Probe System back to light probe groups on " + n + " asset(s)";
        }

        /// the light scene has to be in the build's scene list to be loaded by the game (the editor's list here; StreetLifeBuild adds it to PC builds)
        public static string Register(string arg)
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int added = 0;
            foreach (var path in MapLooks.LightScenes())
                if (File.Exists(path) && !list.Exists(x => x.path == path)) { list.Add(new EditorBuildSettingsScene(path, true)); added++; }
            if (added == 0) return "already in the scene list";
            EditorBuildSettings.scenes = list.ToArray();
            EditorApplication.ExecuteMenuItem("File/Save Project");
            return added + " added to the scene list";
        }

        static bool SetProbeSystem(UniversalRenderPipelineAsset asset, int v)
        {
            var so = new SerializedObject(asset); var p = so.FindProperty("m_LightProbeSystem");
            if (p == null) return false;
            p.intValue = v; so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
            return true;
        }

        static void Tick()
        {
            int phase = Phase; if (phase == 0) return;
            try { Step(phase); }
            catch (Exception ex) { Finish(false, "error: " + ex.Message + "\n" + ex.StackTrace); }
        }

        static void Step(int phase)
        {
            if (phase == 9)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                Phase = 0; string r = Begin(); Log(r);
                return;
            }
            if (EditorApplication.isPlaying) { Finish(false, "play mode started: bake dropped"); return; }
            switch (phase)
            {
                case 1:   // the render pipeline restarts with probe volumes on: wait until it has drawn a frame
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); EditorApplication.QueuePlayerLoopUpdate();
                    if (Now - Mark < 2) return;
                    if (!ProbeReferenceVolume.instance.isInitialized)
                    {
                        if (Now - Mark > 40) Finish(false, "probe volumes never came up (is the Scene or Game view open?)");
                        return;
                    }
                    Setup();
                    if (!AdaptiveProbeVolumes.BakeAsync()) { Finish(false, "the bake would not start"); return; }
                    Log("baking");
                    Phase = 2; Mark = Now;
                    return;
                case 2:
                    if (AdaptiveProbeVolumes.isRunning) { if (Now - Mark > 1800) { AdaptiveProbeVolumes.Cancel(); Finish(false, "gave up after 30 minutes"); } return; }
                    Finish(true, $"baked in {Now - Mark:F0} s");
                    return;
            }
        }

        static void Setup()
        {
            var active = SceneManager.GetActiveScene();
            if (active.isDirty && !string.IsNullOrEmpty(active.path)) EditorSceneManager.SaveScene(active);
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/StreetLife/Maps/" + Map, "Light");
            var set = MapSet.Load(); var e = set.Get(Map);
            string stem = Look != null ? Look.stem : Beach ? "BeachLight" : "RailLight";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // the sky and the sun as the game has them on Railed (MapWorld.ApplyEnv with RailLook.Env)
            var env = JsonUtility.FromJson<EnvJson>(e.envJson.text);
            if (Look != null)
            {   // the sun is the first directional light of the map's own lights, after the look has had its say
                MapLooks.Env(Map, env);
                if (env.global != null)
                    foreach (var g in env.global)
                        if (g != null && g.type == "DirectionalLight" && g.pos != null)
                        {
                            var to = g.target != null && g.target.Length >= 3 ? new Vector3(g.target[0], g.target[1], g.target[2]) : Vector3.zero;
                            lookSun = (new Vector3(g.pos[0], g.pos[1], g.pos[2]) - to).normalized; lookHex = g.color; lookPower = g.intensity;
                            break;
                        }
            }
            else if (Beach) BeachLook.Env(env); else RailLook.Env(env);
            string skyPath = Dir + (Look != null ? "/" + stem + "BakeSky.mat" : Beach ? "/BeachBakeSky.mat" : "/RailBakeSky.mat");
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (!sky) { sky = new Material(set.sky); AssetDatabase.CreateAsset(sky, skyPath); } else sky.shader = set.sky;
            var s = env.env;
            // the sky as a light is paler than the sky you see: at full strength its blue floods every shadow
            sky.SetColor("_Top", Pale(Hex.ToColor(s.top))); sky.SetColor("_Mid", Pale(Hex.ToColor(s.mid))); sky.SetColor("_Bot", Pale(Hex.ToColor(s.bot)));
            sky.SetFloat("_Ex", s.ex); sky.SetColor("_Glow", Hex.ToColor(s.glow)); sky.SetFloat("_GlowA", s.glowA);
            sky.SetColor("_Disc", Color.black); sky.SetFloat("_DiscCos", 1.01f);   // no sun disc: the sun itself is the light below
            sky.SetVector("_SunDir", new Vector4(Sun.x, Sun.y, Sun.z, 0));
            sky.SetColor("_SunCol", Hex.ToColor(SunHex));
            if (Look != null) { sky.SetFloat("_CloudCover", Look.cloudCover); sky.SetColor("_CloudLit", Look.cloudLit); sky.SetColor("_CloudShade", Look.cloudShade); }
            else if (Beach) { sky.SetFloat("_CloudCover", .35f); sky.SetColor("_CloudLit", Color.white); sky.SetColor("_CloudShade", new Color(.62f, .68f, .78f)); }
            else { sky.SetFloat("_CloudCover", .5f); sky.SetColor("_CloudLit", new Color(1f, .84f, .66f)); sky.SetColor("_CloudShade", new Color(.52f, .44f, .56f)); }
            sky.SetFloat("_CloudAlpha", .95f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky; RenderSettings.ambientMode = AmbientMode.Skybox; RenderSettings.ambientIntensity = 1.15f; RenderSettings.fog = false;

            var sunGo = new GameObject("Bake sun");
            var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional;
            sun.color = Hex.ToColor(SunHex); sun.intensity = SunPower / Mathf.PI; sun.bounceIntensity = 1;
            sun.shadows = LightShadows.Soft; sun.lightmapBakeType = LightmapBakeType.Mixed;
            sunGo.transform.rotation = Quaternion.LookRotation(-Sun.normalized);
            RenderSettings.sun = sun;

            // probes: close-set over the yard and the tunnel mouths, coarse over the fields and hills around
            // (Beach Please: close-set over the beach, the bar and the jetties; coarse over the cliffs and rocks around)
            var inner = new GameObject("Map probes").AddComponent<ProbeVolume>();
            inner.mode = ProbeVolume.Mode.Local; inner.fillEmptySpaces = true;
            if (Look != null) { inner.transform.position = Look.probeCentre; inner.size = Look.probeSize; }
            else if (Beach) { inner.transform.position = new Vector3(0, 6.5f, 0); inner.size = new Vector3(92, 15, 120); }
            else { inner.transform.position = new Vector3(0, 9, 0); inner.size = new Vector3(66, 22, 150); }
            if (!SessionState.GetString(Key + ".arg", "").Contains("inner") && (Look == null || Look.farSize.sqrMagnitude > 1))
            {
                var outer = new GameObject("Map probes (far)").AddComponent<ProbeVolume>();
                outer.mode = ProbeVolume.Mode.Local;
                if (Look != null) { outer.transform.position = Look.farCentre; outer.size = Look.farSize; }
                else if (Beach) { outer.transform.position = new Vector3(2, 10, 0); outer.size = new Vector3(144, 34, 160); }
                else { outer.transform.position = new Vector3(0, 6, 0); outer.size = new Vector3(284, 56, 304); }
                outer.overridesSubdivLevels = true; outer.lowestSubdivLevelOverride = 1; outer.highestSubdivLevelOverride = 5;
            }

            string lsPath = Dir + "/" + stem + ".lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (!ls) { ls = new LightingSettings { name = stem }; AssetDatabase.CreateAsset(ls, lsPath); }
            ls.bakedGI = true; ls.realtimeGI = false; ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.directSampleCount = 32; ls.indirectSampleCount = 512; ls.environmentSampleCount = 512; ls.lightProbeSampleCountMultiplier = 4; ls.maxBounces = 3;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;

            EditorSceneManager.SaveScene(scene, LightScene);
            AssetDatabase.SaveAssets();

            // the yard itself: there for the bake only, never saved
            var root = new GameObject("Bake geometry");
            if (Look != null) MapLooks.BuildForBake(root.transform, e, Look);
            else if (Beach) BeachLook.BuildForBake(root.transform, e, Resources.Load<RailLookSet>("BeachLook"));
            else RailLook.BuildForBake(root.transform, e, Resources.Load<RailLookSet>("RailLook"));
            int n = 0;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
            {
                GameObjectUtility.SetStaticEditorFlags(mr.gameObject, StaticEditorFlags.ContributeGI);
                mr.receiveGI = ReceiveGI.LightProbes; mr.shadowCastingMode = ShadowCastingMode.On;
                n++;
            }
            Log($"scene ready: {n} renderers, sun {sun.intensity:F2} {SunHex}, ambient x{RenderSettings.ambientIntensity}");
        }

        static Color Pale(Color c) { float g = c.grayscale; return Color.Lerp(c, new Color(g, g, g, 1), .5f); }

        static void Finish(bool ok, string why)
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.path == LightScene)
                {
                    var root = GameObject.Find("Bake geometry");
                    if (root)
                    {
                        foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) UnityEngine.Object.DestroyImmediate(mf.sharedMesh);
                        UnityEngine.Object.DestroyImmediate(root);
                    }
                    var sun = GameObject.Find("Bake sun"); if (sun) UnityEngine.Object.DestroyImmediate(sun);
                    if (ok)
                    {
                        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
                        Register("");
                    }
                }
                string prev = SessionState.GetString(Key + ".prev", "");
                if (!string.IsNullOrEmpty(prev) && !MapLooks.LightScenes().Contains(prev) && File.Exists(prev)) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
            }
            catch (Exception ex) { Log("while finishing: " + ex.Message); }
            Phase = 0;
            Log((ok ? "done: " : "FAILED: ") + why);
        }
    }
}
