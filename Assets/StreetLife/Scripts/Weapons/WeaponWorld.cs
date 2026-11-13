// Starts the weapons up when the Street Life scene plays: loads the exported weapon data, gives every fighter
// (you and the match chat's bot) the Weapons and the holding pose, gives you the weapon keys, the first-person
// view and the weapon HUD, resets ammo and grenades at the start of each round, and runs projectiles, mines,
// the Sentry and effects every frame. Nothing has to be added to the scene by hand.
using UnityEngine;

namespace StreetLife
{
    [DefaultExecutionOrder(-20)]
    public class WeaponWorld : MonoBehaviour
    {
        public static WeaponWorld I;
        float scanT;
        Weapons player;
        TouchControls.Button tbReload, tbSwap, tbLethal, tbNade, tbSentry;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I || !Object.FindAnyObjectByType<PlayerController>()) return;   // not the Street Life scene
            var assets = Resources.Load<WeaponAssets>("StreetLifeWeapons");
            if (!assets || !assets.gunsJson || !assets.gunsBin) { Debug.LogWarning("Street Life: weapon data not found (menu Street Life > Rebuild weapon materials)."); return; }
            var go = new GameObject("Weapons (runtime)"); var ww = go.AddComponent<WeaponWorld>(); ww.Load(assets);
        }

        void Load(WeaponAssets a)
        {
            I = this;
            GunsDb.Load(a.gunsJson.text, a.gunsBin.bytes);
            WeaponModels.Mats = a.materials;
            GunFx.FxShader = a.fxShader;
            GR.Seed(Random.Range(1, int.MaxValue));
            new GameObject("Weapon FX").AddComponent<GunFx>().transform.SetParent(transform);
            // hooks the match code offers (claude/street-life-interfaces.md)
            RoundManager.WeaponNameOf = Combat.GunName;
            BotBrain.RangeForWeapon = BotRange;
            RoundManager.RoundStarted += OnRound;
            Equip();
        }

        void OnDestroy()
        {
            RoundManager.RoundStarted -= OnRound;
            if (I == this) I = null;
            Projectiles.Clear(); Combat.Fighters.Clear(); Combat.Me = null;
        }

        /// a new round: everything thrown or placed goes, and everyone's ammo, lethals and shop kit reset
        void OnRound(int round)
        {
            Equip();
            Projectiles.Clear();
            foreach (var w in Object.FindObjectsByType<Weapons>(FindObjectsSortMode.None)) w.ResetKit();
        }

        /// give weapons to any fighter that hasn't got them yet (the bot is made after the scene loads)
        void Equip()
        {
            foreach (var f in Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
            {
                if (f.GetComponent<Weapons>()) continue;
                var pc = f.GetComponent<PlayerController>();
                var w = f.gameObject.AddComponent<Weapons>(); w.isPlayer = pc;
                var rig = f.GetComponent<CharacterRig>();
                var pose = f.gameObject.AddComponent<WeaponPose>(); pose.fighter = f; pose.weapons = w; pose.rig = rig; w.pose = pose;
                if (!pc) continue;
                Combat.Me = f; player = w;
                f.gameObject.AddComponent<WeaponInput>();
                gameObject.AddComponent<WeaponHud>().weapons = w;
                var cam = Camera.main;
                if (cam)
                {
                    var sc = cam.GetComponent<ShoulderCamera>(); if (sc) sc.weapons = w;
                    var vm = cam.GetComponent<ViewModel>(); if (!vm) vm = cam.gameObject.AddComponent<ViewModel>(); vm.weapons = w;
                }
            }
        }

        /// the range the bot likes to fight at with each gun (metres)
        static float BotRange(string id)
        {
            var g = GunsDb.Gun(id); if (g == null) return 20;
            if (g.IsMelee) return 2.5f;
            switch (g.cat)
            {
                case "Shotgun": return 7;
                case "SMG": case "Pistols": return 12;
                case "Sniper": return id == "sniper" ? 40 : 30;
                case "Launcher": return 18;
                case "Crossbow": return 25;
                case "LMG": return 22;
                default: return id == "flamer" ? 8 : 20;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Combat.Time += dt;
            scanT -= dt; if (scanT <= 0) { scanT = .5f; Equip(); }
            Projectiles.Update(dt);
            Mines.Update(dt);
            Turrets.Update(dt);
            Touch();
        }

        // ------------------------------------------------------------------ phone buttons (the web game's places, CSS px from the bottom right)
        void Touch()
        {
            if (!player || !TouchControls.I) return;
            var w = player;
            if (tbReload == null)
            {
                tbReload = TouchControls.AddButton("reload", 162, 136, 50, "Reload", d => { if (d) w.StartReload(); });
                tbSwap = TouchControls.AddButton("swap", 146, 212, 46, "Swap", d => { if (d) w.Equip(w.slot == 0 ? 1 : 0); });
                // lethal: hold to see the arc, let go to throw (a mine goes down at once), as the web game's Item button
                tbLethal = TouchControls.AddButton("lethal", 222, 236, 48, "Item", d =>
                {
                    if (w.Busy || WeaponHud.MenuOpen) { w.aimThrow = false; return; }
                    if (d) { if (Projectiles.Aimed.Contains(w.lethal) && w.ConsReady()) w.aimThrow = true; else w.UseCons(); }
                    else if (w.aimThrow) { w.aimThrow = false; w.UseCons(); }
                });
                tbNade = TouchControls.AddButton("nade", 306, 150, 46, "Nade", d =>
                {
                    if (w.Busy || WeaponHud.MenuOpen) { w.aimNade = false; return; }
                    if (d) w.aimNade = true; else if (w.aimNade) { w.aimNade = false; w.ThrowNade(); }
                });
                tbSentry = TouchControls.AddButton("sentry", 306, 226, 46, "Sentry", d => { if (d && w.turretKit > 0 && !w.Busy && !w.F.Frozen && Turrets.Deploy(w.F)) w.turretKit--; });
                if (tbReload == null) return;
            }
            // as in the web game, Nade and Sentry only show when you have one; the Item button names your lethal
            var c = GunsDb.Cons(w.lethal);
            if (tbLethal != null) tbLethal.label = c != null ? c.@short : "Item";
            if (tbNade != null) tbNade.size = w.nades > 0 || w.goldNade > 0 ? 46 : 0;
            if (tbSentry != null) tbSentry.size = w.turretKit > 0 ? 46 : 0;
        }
    }
}
