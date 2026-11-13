// Mode rules that touch fighters (match chat): Rumble's air strike, and 2v2 team-mates reviving each other.
using UnityEngine;

namespace StreetLife
{
    public static class Rumble
    {
        /// a punch landed while falling: 20 damage (Weapons.Melee), both fly apart, the target is stunned for 1 s
        public static void AirStrike(Fighter f, Fighter foe, float dx, float dz, float dist)
        {
            float l = dist > 0 ? dist : 1, nx = dx / l, nz = dz / l;
            foe.vel.x += nx * 11; foe.vel.z += nz * 11; foe.vel.y = Mathf.Max(foe.vel.y, 5); foe.grounded = false;
            f.vel.x = -nx * 7; f.vel.z = -nz * 7; f.vel.y = Mathf.Max(f.vel.y, 6);
            foe.stunT = Mathf.Max(foe.stunT, 1);
            if (f == Combat.Me || foe == Combat.Me) GunFx.Shake = Mathf.Max(GunFx.Shake, .5f);
            var o = GunFx.E(30, 8, .22f, .5f, 12, "#ffd23f", "#ffffff", "#ff4a3d"); GunFx.Emit(foe.pos + Vector3.up * 1.6f, o);
            if (f == Combat.Me) Hud.Flash("Air strike!", "20 damage  ·  stunned");
        }
    }

    public partial class Fighter
    {
        /// a team-mate standing over a downed fighter revives them twice as fast (the web game's helped)
        float ReviveHelp()
        {
            foreach (var m in Combat.Fighters)
            {
                if (!m || m == this || m.team != team || !m.alive || m.downed || m.InFinisher) continue;
                if (Mathf.Sqrt((m.pos.x - pos.x) * (m.pos.x - pos.x) + (m.pos.z - pos.z) * (m.pos.z - pos.z)) < 1.7f && Mathf.Abs(m.pos.y - pos.y) < 1.5f) return 2;
            }
            return 1;
        }
    }
}
