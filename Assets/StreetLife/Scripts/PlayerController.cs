// Keyboard and mouse controls, same bindings and look sensitivity as the web game:
// WASD move, mouse look, Space jump (press again in the air to double jump), right mouse aim, C crouch.
// Extra for testing: R respawns at another spawn point, Esc frees the mouse (click to lock it again).
using UnityEngine;
using UnityEngine.InputSystem;

namespace StreetLife
{
    [RequireComponent(typeof(Fighter)), DefaultExecutionOrder(-10)]
    public class PlayerController : MonoBehaviour
    {
        [Tooltip("Camera sensitivity (the web game's Options slider, 1 = default)")]
        [Range(.1f, 6f)] public float sensitivity = 1f;
        [Tooltip("Aim sensitivity for non-sniper guns (web game default 1)")]
        [Range(.1f, 1f)] public float adsSensitivity = 1f;
        Fighter f;
        int spawnIx = -1;
        bool lockClick;

        void Awake() { f = GetComponent<Fighter>(); TouchControls.Ensure(f); }

        void Start() { if (!RoundManager.Instance) Respawn(); }

        public void Respawn()
        {
            var sp = BoxWorld.Spawns;
            if (sp.Count == 0) { f.Place(new Vector3(0, 0, 0), 0); return; }
            spawnIx = spawnIx < 0 ? Random.Range(0, sp.Count) : (spawnIx + 1 + Random.Range(0, sp.Count - 1)) % sp.Count;
            var s = sp[spawnIx];
            f.Place(new Vector3(s.x, s.y, s.z), s.yaw);
        }

        void Update()
        {
            var kb = Keyboard.current; var ms = Mouse.current;
            if (kb == null || ms == null) return;

            if (kb.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (ms.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; lockClick = true; }
            bool locked = Cursor.lockState == CursorLockMode.Locked;

            if (locked)
            {
                var d = ms.delta.ReadValue();
                if (Mathf.Abs(d.x) < 250 && Mathf.Abs(d.y) < 250)
                {
                    float sc = sensitivity * (f.ads ? adsSensitivity * .65f : 1f);
                    f.yaw += d.x * .0022f * sc;
                    f.pitch = Mathf.Clamp(f.pitch + d.y * .0022f * sc, -1.3f, 1.3f);
                }
            }

            f.inFwd = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1 : 0) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1 : 0);
            f.inSide = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0);
            f.inJump = kb.spaceKey.isPressed;
            if (kb.spaceKey.wasPressedThisFrame) f.inJumpPress = true;   // held until the next sim step uses it
            f.ads = locked && ms.rightButton.isPressed && f.onLadder == null;
            if (!ms.leftButton.isPressed) lockClick = false;                   // the click that grabbed the mouse doesn't fire
            f.inFire = locked && ms.leftButton.isPressed && !lockClick;
            if (kb.cKey.wasPressedThisFrame) f.crouchOn = !f.crouchOn;
            f.abilityHeld = kb.gKey.isPressed;
            if (kb.qKey.wasPressedThisFrame) Walls.Build(f);   // the web game's Q: a brick wall
            if (kb.gKey.wasPressedThisFrame) f.UseAbility();   // the web game's G: character ability
            if ((kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) && f.crouchOn && kb.wKey.isPressed) f.crouchOn = false;
            if (kb.rKey.wasPressedThisFrame && !RoundManager.Instance) Respawn();   // free-roam testing only
        }
    }
}
