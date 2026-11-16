// The shop kiosk (match chat), as in the web game: each Duels map has a kiosk on open ground near the middle. Walk up to
// it (within 3.1 m) and the shop opens (Tab); pick one item and the shop is on a 30 s cooldown for you. Bots pop over to
// it now and then for what they need most (botShopWant). In Rumble only the non-gun items are on offer.
// The weapons chat's shop panel calls Weapons.Buy, which asks Kiosk.TryBuy first.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter { [System.NonSerialized] public float shopCD; }

    public static class Kiosk
    {
        public const float SHOP_CD = 30, REACH = 3.1f;
        // kiosk spots from the web game's placeShop, per map id (Unity coordinates)
        static readonly Dictionary<string, Vector3[]> Spots = new Dictionary<string, Vector3[]>
        {
            ["street"] = new[] { new Vector3(-10.5f, 0, 10.5f) },   // 16 Nov: the machine moved +4 m with its building (Tools/streetshift.py)
            ["rail"] = new[] { new Vector3(-4.6f, 1.25f, 0) },
            ["wreck"] = new[] { new Vector3(-.5f, .4f, .5f) },
            ["arena"] = new[] { new Vector3(0, 4, 0) },
            ["skyhigh"] = new[] { new Vector3(5.97f, 0, 3.75f) },
            ["lagoon"] = new[] { new Vector3(12.85f, 0, 0) },
        };
        static readonly HashSet<string> GunItems = new HashSet<string> { "gold", "nades", "turret", "flamer" };

        public static Vector3[] Here
        {
            get
            {
                string id = MapSet.Current; if (string.IsNullOrEmpty(id)) id = "street";
                return Spots.TryGetValue(id, out var s) ? s : new Vector3[0];
            }
        }

        public static bool Near(Fighter f, out Vector3 at)
        {
            at = default;
            foreach (var s in Here)
                if (Mathf.Sqrt((f.pos.x - s.x) * (f.pos.x - s.x) + (f.pos.z - s.z) * (f.pos.z - s.z)) < REACH && Mathf.Abs(f.pos.y - s.y) < 1.6f) { at = s; return true; }
            return false;
        }
        public static bool Near(Fighter f) => Near(f, out _);

        public static Vector3? Nearest(Fighter f)
        {
            Vector3? best = null; float bd = 1e9f;
            foreach (var s in Here) { float d = Vector3.Distance(s, f.pos); if (d < bd) { bd = d; best = s; } }
            return best;
        }

        /// the web game's can() for each item
        public static bool Can(Fighter f, string item)
        {
            var w = f.GetComponent<Weapons>(); if (!w) return false;
            if (Rules.Rumble && GunItems.Contains(item)) return false;
            switch (item)
            {
                case "gold": return w.goldNade <= 0;
                case "armor": return f.armor < 100;
                case "nades": return w.nades < 6;
                case "turret": return w.turretKit <= 0;
                case "flamer": return f.secondary != "flamer";
                case "speed": return f.speedT < 45;
            }
            return true;
        }

        public static bool Open(Fighter f)
        {
            var rm = RoundManager.Instance;
            bool live = !rm || rm.state == RoundManager.State.Fight || rm.state == RoundManager.State.Countdown;
            return f && f.alive && !f.downed && !f.InFinisher && live && f.shopCD <= 0 && Near(f);
        }

        /// called by Weapons.Buy before it hands the item over
        public static bool TryBuy(Fighter f, string item)
        {
            if (!RoundManager.Instance) return true;   // free roam: the old test shop, anywhere
            if (!Open(f) || !Can(f, item)) return false;
            f.shopCD = SHOP_CD;
            Sfx.Play("cash", f.pos, 1, 12);
            var o = GunFx.E(20, 4, .2f, .5f, -1, "#ffc61a", "#ffffff"); GunFx.Emit(f.pos + Vector3.up * 1.4f, o);
            return true;
        }

        /// what a bot wants most right now (botShopWant), or null
        public static string Want(Fighter f)
        {
            if (f.armor < 50 && Can(f, "armor")) return "armor";
            if (Random.value < .3f && Can(f, "gold")) return "gold";
            if (Random.value < .25f && Can(f, "turret")) return "turret";
            if (Random.value < .2f && Can(f, "flamer")) return "flamer";
            var w = f.GetComponent<Weapons>();
            if (w && w.nades < 3 && Can(f, "nades")) return "nades";
            if (f.speedT <= 0 && Can(f, "speed")) return "speed";
            return null;
        }

        public static bool BotBuy(Fighter f)
        {
            var it = Want(f); var w = f.GetComponent<Weapons>();
            if (it == null || !w || f.shopCD > 0) return false;
            w.Buy(it); return true;
        }

        public static void Tick(float dt) { foreach (var f in Combat.Fighters) if (f && f.shopCD > 0) f.shopCD -= dt; }

        public static void ResetRound() { foreach (var f in Combat.Fighters) if (f) f.shopCD = 0; }
    }
}
