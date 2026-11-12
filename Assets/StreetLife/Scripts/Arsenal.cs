// A fighter's weapons: primary and secondary (1 / 2 or the wheel), a lethal on G, ammo, reloads, swaps and firing,
// ported from the web game's fire(), curSpread(), melee(), fireFlame() and the per-tick weapon timers.
// Stats come straight from the web game's GUNS table (Data/catalog.json), with its reload and damage multipliers applied.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [RequireComponent(typeof(Fighter)), DefaultExecutionOrder(-5)]
    public class Arsenal : MonoBehaviour
    {
        public Armory armory;
        public CharacterRig rig;
        [Tooltip("Where shots come from for the player (the shoulder camera); bots and dummies leave it empty")]
        public Camera aimCamera;

        [Header("Loadout (web game ids)")]
        public string primary = "gar";
        public string secondary = "sword";
        public string lethal = "cluster";

        [Header("State (read only)")]
        public int slot;
        public float fireT, reloadT, reloadMax = 1, switchT, bloom, kick, swingT, throwT, adsRaise;
        public bool swingLeft, dualLeft;
        public int lethalLeft = 2;

        // input, set each frame by the controller
        [System.NonSerialized] public bool trigger, reloadPress, throwPress, cancelSnipe;
        [System.NonSerialized] public int switchTo = -1;

        Fighter f;
        readonly Dictionary<string, int> ammo = new Dictionary<string, int>();
        readonly List<float> woods = new List<float>();
        bool triggerWas, snipeHeld, snipeQueued; float snipeAt, shotAt = -99, autoRT, burstT, throwCd; int burstN, swarmI;
        float flameSince;

        public string Current => slot == 0 ? primary : secondary;
        public WeaponInfo Info => armory ? armory.Weapon(Current) : null;
        public string ShownWeapon => throwT > 0 ? lethal : Current == "fists" ? null : Current;
        public float SinceShot => Time.time - shotAt;
        public float ThrowPhase => 1 - throwT / .35f;
        public int Ammo(string id) { var g = armory ? armory.Weapon(id) : null; if (g == null) return 0; if (g.Infinite) return -1; return ammo.TryGetValue(id, out var n) ? n : Mathf.RoundToInt(g.mag); }
        public bool Scoped => f && f.ads && Info != null && Info.Sniper;

        void Awake() { f = GetComponent<Fighter>(); }
        void Start() { if (CombatWorld.armory == null) CombatWorld.armory = armory; Refill(); }

        /// Full mags, two lethals, back on the primary (a new round or respawn).
        public void Refill()
        {
            ammo.Clear(); lethalLeft = 2; reloadT = 0; fireT = 0; bloom = 0; burstN = 0; slot = 0; switchT = .35f;
            var lg = armory ? armory.Weapon(lethal) : null; if (lg != null && lg.mag > 0) lethalLeft = Mathf.RoundToInt(Mathf.Min(lg.mag, 2));
        }

        public void SetLoadout(string prim, string sec, string leth)
        {
            if (!string.IsNullOrEmpty(prim)) primary = prim;
            if (!string.IsNullOrEmpty(sec)) secondary = sec;
            if (!string.IsNullOrEmpty(leth)) lethal = leth;
            Refill();
        }

        void Equip(int s)
        {
            if (s == slot) return;
            slot = s; reloadT = 0; burstN = 0; snipeQueued = false; adsRaise = 0;
            var g = Info; switchT = g != null && g.swap > 0 ? g.swap : .35f;
        }

        void StartReload()
        {
            var g = Info; if (g == null || reloadT > 0 || g.Infinite || g.noReload || Ammo(Current) >= g.mag) return;
            reloadT = reloadMax = g.reload > 0 ? g.reload : 1;
        }

        float CurSpread()
        {
            var g = Info; if (g == null) return 0;
            float s = f.ads ? Mathf.Lerp(g.spread, g.adsSpread, adsRaise) : g.spread;
            float sp = f.HorizontalSpeed; bool sgn = g.pellets > 1;
            if (sp > 2) s *= sgn ? 1.15f : 1.45f;
            if (!f.grounded) s *= sgn ? 1.35f : 2.2f;
            if (f.ads && Current == "sniper" && sp > 2) s += 1.2f;
            if (f.crouching) s *= .65f;
            bool aimed = f.ads && adsRaise > .9f;
            return s + bloom * (aimed ? .3f : 1);
        }
        public float SpreadNow => CurSpread();

        bool CanFire() => f.alive && f.onLadder == null && fireT <= 0 && reloadT <= 0 && switchT <= 0 && f.stunT <= 0 && (Info != null && (Info.Infinite || Ammo(Current) > 0));

        void Update()
        {
            if (!f || armory == null) return;
            float dt = Time.deltaTime;
            var g = Info;

            // timers (the web game's per-tick weapon bookkeeping)
            fireT -= dt; switchT -= dt; throwT = Mathf.Max(0, throwT - dt); throwCd -= dt;
            bloom = Mathf.Max(0, bloom - dt * (trigger ? 1 : 3));
            kick = Mathf.Max(0, kick - dt * 9); swingT = Mathf.Max(0, swingT - dt);
            adsRaise = f.ads && g != null ? Mathf.Min(1, adsRaise + dt / Mathf.Max(.05f, g.ads)) : 0;
            if (reloadT > 0) { reloadT -= dt; if (reloadT <= 0) { reloadT = 0; if (g != null) ammo[Current] = Mathf.RoundToInt(g.mag); } }
            if (burstN > 0) { burstT -= dt; if (burstT <= 0) { if (!CanFireBurst()) burstN = 0; else { burstN--; burstT = g.burstGap; Fire(true); } } }
            if (!f.alive) { triggerWas = false; return; }

            if (switchTo == 0 || switchTo == 1) Equip(switchTo);
            switchTo = -1;
            if (reloadPress) StartReload();
            reloadPress = false;
            if (throwPress) ThrowLethal();
            throwPress = false;
            g = Info; if (g == null) return;

            // an empty gun in your hands reloads by itself after a short beat
            if (!g.Infinite && !g.noReload && Ammo(Current) <= 0 && reloadT <= 0) { autoRT += dt; if (autoRT > .25f) { StartReload(); autoRT = 0; } } else autoRT = 0;

            if (g.Sniper)
            {
                // snipers scope while the trigger is held and fire on release (right mouse cancels)
                if (trigger) { f.ads = true; snipeHeld = true; if (cancelSnipe) { snipeHeld = false; } }
                else if (snipeHeld) { snipeHeld = false; snipeQueued = true; snipeAt = Time.time; }
                if (snipeQueued) { if (Time.time - snipeAt > 2.5f) snipeQueued = false; else if (CanFire()) { snipeQueued = false; Fire(false); } }
            }
            else if (trigger && CanFire() && (g.auto || !triggerWas)) Fire(false);
            if (g.type == "flame" && !trigger) flameSince = 0;
            triggerWas = trigger; cancelSnipe = false;

            // the flamethrower is a shop item: once its fuel is gone the old secondary comes back
            if (Current == "flamer" && Ammo("flamer") <= 0 && secondary == "flamer") { secondary = prevSecondary ?? "smg"; prevSecondary = null; }
        }
        [System.NonSerialized] public string prevSecondary;

        bool CanFireBurst() => f.alive && reloadT <= 0 && switchT <= 0 && Info != null && Info.burst > 0 && Ammo(Current) > 0;

        // where the shot starts: from the camera, moved up to the fighter's eye along the view (as the web game does)
        void AimRay(out Vector3 o, out Vector3 d)
        {
            var eye = Combat.Eye(f);
            if (aimCamera)
            {
                var c = aimCamera.transform.position; d = aimCamera.transform.forward;
                float t0 = Mathf.Max(0, Vector3.Dot(eye - c, d) - .2f); o = c + d * t0;
            }
            else { o = eye; d = f.Aim; }
        }

        Vector3 Muzzle(bool second = false) => rig ? rig.Muzzle(second) : Combat.Eye(f) + f.Aim * .9f + Vector3.down * .3f;

        void Fire(bool fromBurst)
        {
            var g = Info; string id = Current;
            AimRay(out var origin, out var dir);
            shotAt = Time.time;

            if (g.type == "melee") { fireT = g.rate; kick = 1; Melee(g, id); return; }
            if (g.type == "flame") { FireFlame(g, origin, dir); return; }

            if (!g.Infinite) ammo[id] = Ammo(id) - 1;
            kick = 1;
            if (!fromBurst) { fireT = g.rate; if (g.burst > 0) { burstN = g.burst - 1; burstT = g.burstGap; } }

            if (g.type == "proj")
            {
                var mz = Muzzle();
                float hb = BoxWorld.Ray(origin, dir, 250); float t = 200; if (hb >= 0) t = Mathf.Min(t, hb);
                bool hf = Combat.RayFoes(origin, dir, f, out var fh); if (hf) t = Mathf.Min(t, fh.t);
                var aim = origin + dir * t; var d2 = aim - mz; d2 = d2.magnitude < .5f ? dir : d2.normalized;
                d2 = Combat.SpreadDir(d2, CurSpread());
                Fighter lockOn = null;
                if (id == "grenade") { d2.y += .22f; d2.Normalize(); }
                else if (id == "swarm")
                {
                    swarmI = (swarmI + 1) % 4; int k = swarmI;
                    var sd = new Vector3(d2.z, 0, -d2.x).normalized;
                    mz += sd * (k % 2 == 1 ? .06f : -.06f) + Vector3.up * (k < 2 ? .06f : -.06f);
                    d2 = (d2 + sd * ((k % 2 == 1 ? 1 : -1) * .32f) + Vector3.up * (k < 2 ? .22f : .02f)).normalized;
                    lockOn = hf && fh.t <= t + .01f ? fh.who : null;
                }
                CombatWorld.I.Spawn(f, id, mz, d2, origin + dir * t, lockOn);
                if (f.team == 0) f.pitch = Mathf.Clamp(f.pitch + g.kick, -1.3f, 1.3f);
                bloom = Mathf.Min(bloom + g.bloom, 3);
                return;
            }

            // hitscan: each pellet goes through wood (30% off per layer) and stops at anything solid
            if (id == "duals") dualLeft = !dualLeft;
            var muz = Muzzle(id == "duals" && dualLeft);
            float spr = CurSpread();
            var hits = new Dictionary<Fighter, (float dmg, int n, float dist, bool head)>();
            int pellets = Mathf.Max(1, g.pellets);
            for (int p = 0; p < pellets; p++)
            {
                var d = Combat.SpreadDir(dir, spr);
                float hb = BoxWorld.RayHit(origin, d, 400, out var n, woods);
                bool hf = Combat.RayFoes(origin, d, f, out var fh);
                Vector3 end;
                if (hf && (hb < 0 || fh.t < hb))
                {
                    end = origin + d * fh.t; float dist = fh.t;
                    int layers = 0; foreach (var w in woods) if (w < fh.t) layers++;
                    float dm = g.dmg * (fh.head ? g.head : 1) * Mathf.Pow(.7f, layers);
                    if (g.HasFall) dm *= dist <= g.fall[0] ? 1 : dist >= g.fall[1] ? .45f : Mathf.Lerp(1, .45f, (dist - g.fall[0]) / (g.fall[1] - g.fall[0]));
                    hits.TryGetValue(fh.who, out var hr);
                    hr.dmg += dm; hr.n++; hr.dist = hr.n == 1 ? dist : Mathf.Min(hr.dist, dist); hr.head |= fh.head; hits[fh.who] = hr;
                    Fx.Impact(end, -d, new Color(.85f, .1f, .12f));
                }
                else if (hb >= 0) { end = origin + d * hb; if (p < 3) Fx.Impact(end, n, new Color(.85f, .83f, .8f)); }
                else end = origin + d * 180;
                if (p < 3 || Random.value < .4f) Fx.Tracer(muz, end, f.team == 0 ? new Color(1, .95f, .69f) : new Color(1, .82f, .75f), id == "sniper" ? .08f : .045f);
            }
            foreach (var kv in hits)
            {
                var foe = kv.Key; var hr = kv.Value; float total = Mathf.Round(hr.dmg);
                // shotguns: a blast only downs in one shot point-blank (most pellets landing from right next to them); otherwise it tops out at 75% of their health
                if (g.Shotgun)
                {
                    float pb = g.pb > 0 ? g.pb : 3;
                    if (hr.dist <= pb && hr.n >= Mathf.CeilToInt(pellets / 2f)) total = foe.hp + foe.armor + 1;
                    else total = Mathf.Min(total, Mathf.Round(foe.hpMax * .75f));
                }
                Combat.Hurt(foe, total, f, hr.head);
            }
            if (f.team == 0) { f.pitch = Mathf.Clamp(f.pitch + g.kick * (f.ads ? .6f : 1), -1.3f, 1.3f); f.yaw += (Random.value - .5f) * g.kick * .4f; }
            bloom = Mathf.Min(bloom + g.bloom, 3);
        }

        // sword and fists: lunge forward and hit everyone in front within reach (web melee())
        void Melee(WeaponInfo g, string id)
        {
            swingT = .32f; if (id == "sword") swingLeft = !swingLeft;
            float lg = g.lunge > 0 ? g.lunge : 6;
            f.vel += f.Forward * lg;
            var e = Combat.Eye(f);
            foreach (var foe in Fighter.All.ToArray())
            {
                if (!Combat.IsFoe(f, foe) || !foe.alive) continue;
                float dx = foe.pos.x - f.pos.x, dz = foe.pos.z - f.pos.z, dy = foe.pos.y - f.pos.y, dist = Mathf.Sqrt(dx * dx + dz * dz);
                float ang = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, f.yaw * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                if (dist < g.reach && Mathf.Abs(dy) < 1.9f && ang < g.arc && BoxWorld.Los(e, Combat.Chest(foe)))
                    Combat.Hurt(foe, g.dmg, f, false);
            }
        }

        // flamethrower: a 13 m jet; everyone in the cone that no wall shields burns (2 a tick close up, 45% at full range)
        void FireFlame(WeaponInfo g, Vector3 origin, Vector3 dir)
        {
            fireT = g.rate; ammo["flamer"] = Ammo("flamer") - 1; kick = .25f;
            float R = g.range > 0 ? g.range : 13;
            var mz = Muzzle();
            float hb = BoxWorld.Ray(origin, dir, R + 2); var tgt = origin + dir * (hb >= 0 ? hb : R);
            var d2 = tgt - mz; float stop = d2.magnitude; if (stop < .3f) { d2 = dir; stop = .3f; } else d2 /= stop; stop = Mathf.Min(stop, R);
            Fx.FlamePuff(mz, d2, stop);
            foreach (var v in Fighter.All.ToArray())
            {
                if (!Combat.IsFoe(f, v) || !v.alive) continue;
                var rel = Combat.Chest(v) - mz; float along = Vector3.Dot(rel, d2);
                if (along < -.3f || along > stop + .6f) continue;
                if ((rel - d2 * along).magnitude > .7f + Mathf.Max(0, along) * .12f) continue;
                float L = rel.magnitude; if (L > .6f && BoxWorld.Ray(mz, rel / L, L - .5f) >= 0) continue;
                float dm = g.dmg * (along < 5 ? 1 : Mathf.Lerp(1, .45f, Mathf.Clamp01((along - 5) / (R - 5))));
                Combat.Hurt(v, dm, f, false, false);
                flameSince += dm; if (flameSince >= 8 && f.team == 0) { Hud.DamageNumber(Combat.Chest(v) + Vector3.up * .9f, Mathf.RoundToInt(flameSince), false); Hud.HitMarker(false); flameSince = 0; }
            }
        }

        // G: throw the lethal toward the crosshair (or lay the mine at your feet)
        void ThrowLethal()
        {
            if (lethalLeft <= 0 || throwCd > 0 || string.IsNullOrEmpty(lethal) || f.onLadder != null) return;
            var g = armory.Weapon(lethal); if (g == null) return;
            lethalLeft--; throwT = .35f; throwCd = .6f;
            if (lethal == "mine") { CombatWorld.I.Spawn(f, "mine", f.pos + f.Forward * 1.1f, Vector3.zero); return; }
            AimRay(out _, out var d);
            var from = Combat.Eye(f) + f.Forward * .7f + Vector3.down * .2f;
            d.y += .22f; d.Normalize();
            CombatWorld.I.Spawn(f, lethal, from, d);
        }
    }
}
