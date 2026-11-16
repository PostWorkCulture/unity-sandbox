// Small per-match jobs that have no other home: the shop kiosk's timer and reset, and the Wall button on phones.
// (This file used to run the character abilities, which were removed on 16 Nov.)
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StreetLife
{
    [DefaultExecutionOrder(60)]
    public class MatchExtras : MonoBehaviour
    {
        public static MatchExtras I;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; Ensure(); }
        static void OnScene(Scene s, LoadSceneMode m) => Ensure();
        public static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("MatchExtras").AddComponent<MatchExtras>();
        }

        void OnEnable() { RoundManager.RoundStarted += OnRound; }
        void OnDisable() { RoundManager.RoundStarted -= OnRound; if (I == this) I = null; }
        void OnRound(int r) { Kiosk.ResetRound(); }

        int wallsShown = -2; bool wallWas; TouchControls wallTc;
        void Update()
        {
            Kiosk.Tick(Time.deltaTime);
            // the Wall button on phones, at the web game's Q place; rebuilt only when what it shows changes
            var me = Combat.Me;
            if (me && TouchControls.I)
            {
                bool wallOn = Walls.On && me.alive && !MainMenu.Open && me.walls > 0;
                if (wallOn != wallWas || me.walls != wallsShown || wallTc != TouchControls.I)
                {
                    wallWas = wallOn; wallsShown = me.walls; wallTc = TouchControls.I;
                    TouchControls.AddButton("wall", 236, 164, wallOn ? 48 : 0, "Wall\n" + me.walls, d => { var p = Combat.Me; if (d && p) Walls.Build(p); });
                }
            }
        }
    }
}
