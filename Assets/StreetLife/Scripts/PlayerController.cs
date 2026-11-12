// Keyboard and mouse controls, same bindings and look sensitivity as the web game:
// WASD move, mouse look, Space jump (press again in the air to double jump), right mouse aim, C crouch,
// left mouse fire (snipers: hold to scope, let go to fire), R reload, 1 / 2 / wheel switch, G lethal.
// Extra for testing: Backspace respawns at another spawn point, Tab opens the character and weapon picker, Esc frees the mouse.
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
        [Tooltip("Sniper scope sensitivity (web game default 1)")]
        [Range(.1f, 2f)] public float scopeSensitivity = 1f;
        Fighter f; Arsenal arsenal;
        int spawnIx = -1;

        void Awake() { f = GetComponent<Fighter>(); arsenal = GetComponent<Arsenal>(); }

        void Start() { Respawn(); }

        public void Respawn()
        {
            var sp = BoxWorld.Spawns;
            if (sp.Count == 0) f.Place(new Vector3(0, 0, 0), 0);
            else
            {
                spawnIx = spawnIx < 0 ? Random.Range(0, sp.Count) : (spawnIx + 1 + Random.Range(0, sp.Count - 1)) % sp.Count;
                var s = sp[spawnIx];
                f.Place(new Vector3(s.x, s.y, s.z), s.yaw);
            }
            if (arsenal) arsenal.Refill();
        }

        void Update()
        {
            var kb = Keyboard.current; var ms = Mouse.current;
            if (kb == null || ms == null) return;
            if (!f.alive && kb.backspaceKey.wasPressedThisFrame) Respawn();

            if (Hud.PickerOpen)
            {
                // menu open: stand still, hands off the gun
                f.inFwd = f.inSide = 0; f.inJump = false; f.ads = false;
                if (arsenal) arsenal.trigger = false;
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            bool justLocked = false;
            if (ms.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; justLocked = true; }
            bool locked = Cursor.lockState == CursorLockMode.Locked;

            if (locked)
            {
                var d = ms.delta.ReadValue();
                if (Mathf.Abs(d.x) < 250 && Mathf.Abs(d.y) < 250)
                {
                    bool scoped = arsenal && arsenal.Scoped;
                    float sc = sensitivity * (scoped ? scopeSensitivity * .35f : f.ads ? adsSensitivity * .65f : 1f);
                    f.yaw += d.x * .0022f * sc;
                    f.pitch = Mathf.Clamp(f.pitch + d.y * .0022f * sc, -1.3f, 1.3f);
                }
            }

            f.inFwd = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1 : 0) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1 : 0);
            f.inSide = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0);
            f.inJump = kb.spaceKey.isPressed;
            if (kb.spaceKey.wasPressedThisFrame) f.inJumpPress = true;   // held until the next sim step uses it
            f.ads = locked && ms.rightButton.isPressed && f.onLadder == null;
            if (kb.cKey.wasPressedThisFrame) f.crouchOn = !f.crouchOn;
            if ((kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) && f.crouchOn && kb.wKey.isPressed) f.crouchOn = false;
            if (kb.backspaceKey.wasPressedThisFrame) Respawn();

            if (arsenal)
            {
                arsenal.trigger = locked && !justLocked && ms.leftButton.isPressed;
                arsenal.cancelSnipe = ms.rightButton.wasPressedThisFrame;
                if (kb.rKey.wasPressedThisFrame) arsenal.reloadPress = true;
                if (kb.gKey.wasPressedThisFrame) arsenal.throwPress = true;
                if (kb.digit1Key.wasPressedThisFrame) arsenal.switchTo = 0;
                if (kb.digit2Key.wasPressedThisFrame) arsenal.switchTo = 1;
                float wheel = ms.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > .01f) arsenal.switchTo = 1 - arsenal.slot;
            }
        }
    }
}
