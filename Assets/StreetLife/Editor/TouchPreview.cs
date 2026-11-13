// Street Life > Touch controls preview: draws the phone controls in the editor's Game view without a touch screen,
// to check the layout (and for screenshots from Cowork: `menu Street Life/Touch controls preview`).
using UnityEditor;
using UnityEngine;

namespace StreetLife
{
    static class TouchPreviewMenu
    {
        const string M = "Street Life/Touch controls preview";
        [MenuItem(M)] static void Toggle() { TouchControls.Preview = !TouchControls.Preview; EditorPrefs.SetBool("StreetLife.touchPreview", TouchControls.Preview); }
        [MenuItem(M, true)] static bool Check() { Menu.SetChecked(M, TouchControls.Preview); return true; }
        // play mode: press the main menu's Play (for screenshots of a match from Cowork)
        [MenuItem("Street Life/Start match from menu")] static void StartMatch()
        {
            if (!EditorApplication.isPlaying) return;
            var m = UnityEngine.Object.FindAnyObjectByType<MainMenu>();
            if (m) m.SendMessage("Play", SendMessageOptions.DontRequireReceiver);
        }
        [InitializeOnLoadMethod] static void Load() { TouchControls.Preview = EditorPrefs.GetBool("StreetLife.touchPreview", false); }
    }
}
