// The player's weapon keys (the trigger and aim come from PlayerController through the fighter's inFire / ads):
// 1 / 2 / mouse wheel swap, R reload, E lethal (hold to see the arc, release to throw; a mine goes down at once),
// T grenade from the shop kit (same), V put the Sentry down.
using UnityEngine;
using UnityEngine.InputSystem;

namespace StreetLife
{
    [RequireComponent(typeof(Weapons)), DefaultExecutionOrder(-6)]
    public class WeaponInput : MonoBehaviour
    {
        Weapons w;
        void Awake() { w = GetComponent<Weapons>(); }

        void Update()
        {
            var kb = Keyboard.current; var ms = Mouse.current; if (kb == null || ms == null || !GunsDb.Loaded) return;
            var f = w.F;
            bool locked = Cursor.lockState == CursorLockMode.Locked && !WeaponHud.MenuOpen;
            if (w.Busy || WeaponHud.MenuOpen) { w.aimThrow = w.aimNade = false; return; }
            if (!locked) return;   // phones: the touch buttons drive the same calls (WeaponWorld adds them)
            if (kb.digit1Key.wasPressedThisFrame) w.Equip(0);
            if (kb.digit2Key.wasPressedThisFrame) w.Equip(1);
            if (Mathf.Abs(ms.scroll.ReadValue().y) > .01f) w.Equip(w.slot == 0 ? 1 : 0);
            if (kb.rKey.wasPressedThisFrame) w.StartReload();
            if (kb.eKey.wasPressedThisFrame) { if (Projectiles.Aimed.Contains(w.lethal) && w.ConsReady()) w.aimThrow = true; else w.UseCons(); }
            if (kb.eKey.wasReleasedThisFrame && w.aimThrow) { w.aimThrow = false; w.UseCons(); }
            if (kb.tKey.wasPressedThisFrame) w.aimNade = true;
            if (kb.tKey.wasReleasedThisFrame && w.aimNade) { w.aimNade = false; w.ThrowNade(); }
            if (kb.vKey.wasPressedThisFrame && w.turretKit > 0 && !f.Frozen) { if (Turrets.Deploy(f)) w.turretKit--; }
        }
    }
}
