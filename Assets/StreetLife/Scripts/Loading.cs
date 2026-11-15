// The loading screen (progression chat, 15 Nov). Every map change, rematch on a new map and return to the menu reloads the
// scene, and the first frames of a new scene take a couple of seconds (building the map, the menu, the first render). Done
// straight from a button, the old picture just froze for that long. Loading.ReloadScene() instead puts up a black "Loading"
// screen first, reloads on the next frame, and keeps the screen up until the new scene is running smoothly, so the wait
// reads as loading rather than as the game hanging. It's a UI Toolkit panel above everything (the menu is UI Toolkit too).
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace StreetLife
{
    public class Loading : MonoBehaviour
    {
        static Loading I;
        bool on, loaded; int smooth; float since;
        VisualElement screen; Label text;

        public static bool Busy => I && I.on;
        /// remote test: call Loading.Test
        public static string Test(string a) { ReloadScene(); return "reloading behind the loading screen"; }

        /// Reload the current scene behind the loading screen (a second call while one is under way is ignored).
        public static void ReloadScene()
        {
            if (!I) { var go = new GameObject("Loading screen"); DontDestroyOnLoad(go); I = go.AddComponent<Loading>(); I.Build(); }
            if (I.on) return;
            I.on = true; I.loaded = false; I.smooth = 0; I.since = Time.realtimeSinceStartup;
            I.screen.style.display = DisplayStyle.Flex;
            I.StartCoroutine(I.Go());
        }

        void Build()
        {
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize; ps.referenceResolution = new Vector2Int(1280, 720);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; ps.match = .5f; ps.sortingOrder = 1000;   // above the menu (100)
            var doc = gameObject.AddComponent<UIDocument>(); doc.panelSettings = ps;
            var root = doc.rootVisualElement;
            root.style.position = Position.Absolute; root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.pickingMode = PickingMode.Ignore;
            screen = new VisualElement();
            screen.style.position = Position.Absolute; screen.style.left = screen.style.top = screen.style.right = screen.style.bottom = 0;
            screen.style.backgroundColor = new Color(.035f, .035f, .04f);
            screen.style.alignItems = Align.Center; screen.style.justifyContent = Justify.Center;
            text = new Label("LOADING");
            text.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            text.style.fontSize = 28; text.style.color = new Color(1, 1, 1, .85f); text.style.unityFontStyleAndWeight = FontStyle.Bold;
            text.style.letterSpacing = 6;
            screen.Add(text); root.Add(screen);
            screen.style.display = DisplayStyle.None;
        }

        IEnumerator Go()
        {
            yield return null;   // let one frame with the loading screen reach the display first
            yield return null;
            Time.timeScale = 1;
            var sc = SceneManager.GetActiveScene();
            if (sc.buildIndex >= 0) SceneManager.LoadScene(sc.buildIndex); else SceneManager.LoadScene(sc.name);
        }

        void OnEnable() { SceneManager.sceneLoaded += OnLoaded; }
        void OnDisable() { SceneManager.sceneLoaded -= OnLoaded; }
        void OnLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) loaded = true; }

        void Update()
        {
            if (!on) return;
            int dots = 1 + (int)((Time.realtimeSinceStartup - since) * 3) % 3;
            text.text = "LOADING" + new string('.', dots);
            if (!loaded) return;
            // up until five frames in a row come quickly (the new scene's heavy first frames are over), or 8 s at most
            smooth = Time.unscaledDeltaTime < .08f ? smooth + 1 : 0;
            if (smooth >= 5 || Time.realtimeSinceStartup - since > 8) { on = false; screen.style.display = DisplayStyle.None; }
        }
    }
}
