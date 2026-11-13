// A fighter's weapons: loadout (primary, secondary, lethal), ammo, reloads, swaps, bursts, spread and bloom, recoil,
// and every way of attacking from the web game - hitscan guns (pellets, falloff, headshots, wood penetration,
// the shotgun one-shot rule), rockets / missiles / bolts, melee with lunge, the flamethrower, thrown lethals and
// grenades (with the dotted throw preview), and the shop kit (gold grenade, armour, grenades, Sentry, flamethrower, speed).
// Weapons never read the mouse or keyboard: they follow the fighter's intent (inFire, ads, yaw, pitch) set by the
// player's controls or a bot, so bots use the same weapons. The player's weapon keys are in WeaponInput.
// The loadout is the fighter's primary / secondary / lethal (see claude/street-life-interfaces.md).
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [RequireComponent(typeof(Fighter)), DefaultExecutionOrder(-5)]
    public class Weapons : MonoBehaviour
    {
        public const float AUTO_RELOAD_DELAY = .35f, SG_MAX_FRAC = .75f;

        public bool isPlayer;
        // the loadout lives on the fighter (the match code sets it; the weapon picker changes the player's)
        public string primary { get => F.primary; set => F.primary = value; }
        public string secondary { get => F.secondary; set => F.secondary = value; }
        public string lethal { get => F.lethal; set => F.lethal = value; }

        [Header("State (read only)")]
        public int slot;
        public float fireT, reloadT, reloadMax = 1, switchT, burstT, bloom, kick, flashT, swingT, consT, nadeT, flameT, autoRT = -1;
        public int burstN, consN, nades, goldNade, turretKit;
        public bool dualL, swL, aimThrow, aimNade;
        public float vmRaise, shotAt = -9;
        public string flamerPrev;
        public readonly Dictionary<string, int> ammo = new Dictionary<string, int>();

        [System.NonSerialized] public WeaponPose pose;
        public Fighter F { get; private set; }
        bool triggerWas; int swI; Fighter swLock; Vector3 swAim; float flHit, flHitT = -9;
        AudioSource relSnd; static float whizT = -9;
        readonly List<BoxHit> woods = new List<BoxHit>();
        readonly Dictionary<Fighter, HitRec> hits = new Dictionary<Fighter, HitRec>();
        class HitRec { public float t, d = 1e9f; public int n; public bool h; }

        public string Gun => slot == 0 ? primary : (string.IsNullOrEmpty(secondary) ? "smg" : secondary);
        public GunStat G => GunsDb.Gun(Gun);
        public int Ammo(string id) => ammo.TryGetValue(id, out var a) ? a : 0;

        void Awake() { F = GetComponent<Fighter>(); }
        void OnEnable() { if (!F) F = GetComponent<Fighter>(); if (!Combat.Fighters.Contains(F)) Combat.Fighters.Add(F); }
        void OnDisable() { Combat.Fighters.Remove(F); }

        // ------------------------------------------------------------------ loadout
        public void ResetKit()
        {
            ammo.Clear();
            if (secondary == "flamer") { secondary = flamerPrev ?? "smg"; flamerPrev = null; }
            Fill(primary); Fill(secondary);
            fireT = reloadT = burstT = bloom = kick = swingT = consT = nadeT = flameT = 0; burstN = 0; autoRT = -1;
            var c = GunsDb.Cons(lethal); consN = c != null ? c.uses : 0;
            nades = 0; goldNade = 0; turretKit = 0; aimThrow = aimNade = false;
            F.dazeT = F.blindT = F.speedT = F.burnT = 0;
            slot = -1; Equip(0); switchT = 0;
        }

        void Fill(string id) { var g = GunsDb.Gun(id); if (g != null) ammo[id] = g.InfiniteMag ? int.MaxValue : Mathf.RoundToInt(g.mag); }

        public void SetLoadout(string pri, string sec, string leth)
        {
            if (GunsDb.Gun(pri) != null) primary = pri;
            if (GunsDb.Gun(sec) != null) { secondary = sec; flamerPrev = null; }
            if (GunsDb.Cons(leth) != null) lethal = leth;
            ResetKit();
        }

        /// down, in a finishing move, or out: hands off the weapons (no firing, reloading, swapping or throwing)
        public bool Busy => !F.alive || F.downed || F.InFinisher;

        public void Equip(int s)
        {
            if (slot == s) return;
            if (slot >= 0 && Busy) return;
            if (relSnd) { relSnd.Stop(); relSnd = null; }
            if (slot >= 0 && !F.Frozen) Sfx.Play("swap", F.pos, 1, 6);
            slot = s; reloadT = 0; burstN = 0; burstT = 0;
            var g = G; switchT = g != null ? g.SwapTime : .35f;
        }

        public void StartReload()
        {
            var id = Gun; var g = GunsDb.Gun(id); if (g == null || Busy) return;
            if (reloadT > 0 || g.InfiniteMag || g.noReload || Ammo(id) >= g.mag) return;
            reloadT = g.reload > 0 ? g.reload : 1; reloadMax = reloadT;
            // the reload sound is 3.4 s long: sped up a little and started part way in so it ends with the reload
            const float L = 3.4f; float r = Mathf.Clamp(L / reloadT, .95f, 1.2f);
            relSnd = Sfx.Play("reload", F.pos, isPlayer ? 1 : .8f, 8, r);
            if (relSnd && relSnd.clip) relSnd.time = Mathf.Clamp(L - reloadT * r, 0, relSnd.clip.length - .05f);
        }

        bool CanFire()
        {
            var g = G; if (g == null) return false;
            return F.onLadder == null && F.alive && !F.downed && !F.InFinisher && !F.Frozen && fireT <= 0 && reloadT <= 0 && switchT <= 0 && Ammo(Gun) > 0;
        }

        // ------------------------------------------------------------------ spread (curSpread)
        public float CurSpread()
        {
            var id = Gun; var g = G; if (g == null) return 0;
            float s = F.ads ? g.adsSpread : g.spread;
            if (isPlayer && F.ads && g.vm) s = Mathf.Lerp(g.spread, g.adsSpread, vmRaise);
            float sp = F.HorizontalSpeed; bool sgn = g.Pellets > 1;
            if (sp > 2) s *= sgn ? 1.15f : 1.45f;
            if (!F.grounded) s *= sgn ? 1.35f : 2.2f;
            if (F.ads && id == "sniper" && sp > 2) s += 1.2f;
            if (F.crouching) s *= .65f;
            bool aimed = F.ads && (!isPlayer || !g.vm || vmRaise > .9f);
            return s + bloom * (aimed ? .3f : 1);
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (!GunsDb.Loaded) return;
            if (ammo.Count == 0) ResetKit();
            float dt = Time.deltaTime;
            var g = G;
            // aiming pace depends on the gun (moveSpeed)
            F.adsMoveSpeed = Gun == "sniper" ? 3.8f : Gun == "sword" ? 8f : 5.6f;   // the match code's movement reads these two
            F.moveMul = F.WeaponMoveMul;
            TickStatus(dt);
            bool inFire = F.inFire;

            // first-person aim raise (VM.raise)
            {
                bool want = isPlayer && !Busy && F.ads && g != null && g.vm;
                float at = g != null ? g.AdsTime : .2f;
                vmRaise = Mathf.Clamp01(vmRaise + (want ? dt / at : -dt / Mathf.Min(.15f, at * .6f)));
            }

            // the trigger
            if (!Busy && !F.Frozen && g != null)
            {
                if (inFire && CanFire() && (g.auto || !triggerWas)) FireFromAim(false);
                else if (inFire && Ammo(Gun) <= 0 && reloadT <= 0) { if (!triggerWas) Sfx.Play("dry", F.pos, 1, 8); StartReload(); }
            }
            triggerWas = inFire;

            // timers (the web game's per-frame fighter loop)
            fireT -= dt; switchT -= dt; bloom = Mathf.Max(0, bloom - dt * 3);
            if (reloadT > 0) { reloadT -= dt; if (reloadT <= 0) { reloadT = 0; Fill(Gun); } }
            if (burstN > 0)
            {
                burstT -= dt;
                if (burstT <= 0)
                {
                    var bg = G;
                    if (Busy || F.Frozen || reloadT > 0 || switchT > 0 || Ammo(Gun) <= 0 || bg == null || bg.burst <= 0) burstN = 0;
                    else { burstN--; burstT = bg.burstGap; FireFromAim(true); if (Ammo(Gun) == 0 && reloadT <= 0) { burstN = 0; StartReload(); } }
                }
            }
            consT -= dt; nadeT -= dt;
            kick = Mathf.Max(0, kick - dt * 9); flashT -= dt; swingT = Mathf.Max(0, swingT - dt);
            if (kick > .6f) shotAt = Combat.Time;
            if (flameT > 0) flameT -= dt;
            // auto reload a moment after the mag runs dry
            if (!Busy && g != null && !g.InfiniteMag && !g.noReload && Ammo(Gun) <= 0 && reloadT <= 0 && switchT <= 0)
            { if (autoRT < 0) autoRT = AUTO_RELOAD_DELAY; autoRT -= dt; if (autoRT <= 0) { autoRT = -1; StartReload(); } }
            else autoRT = -1;

            if (isPlayer) PreviewThrow();
        }

        // dazed, whited out, Speed from the shop, burning (4 damage every half second)
        void TickStatus(float dt)
        {
            if (F.dazeT > 0) F.dazeT -= dt; if (F.blindT > 0) F.blindT -= dt; if (F.speedT > 0) F.speedT = Mathf.Max(0, F.speedT - dt);
            if (F.burnT > 0 && F.alive && !F.InFinisher)
            {
                F.burnT -= dt; F.burnTick -= dt;
                if (Random.value < .7f) GunFx.Lick(F.pos + new Vector3(Random.Range(-.35f, .35f), Random.Range(.3f, 1.9f), Random.Range(-.35f, .35f)), 1.2f);
                if (F.burnTick <= 0) { F.burnTick = .5f; Combat.Damage(F, 4, F.burnBy ? F.burnBy : F, false, "flamer"); }
            }
            else if (!F.alive) F.burnT = 0;
        }

        // ------------------------------------------------------------------ firing
        /// The player fires from the camera, starting level with the eyes so nothing between the camera and the head is hit
        void FireFromAim(bool fromBurst)
        {
            Vector3 o, d;
            var cam = Camera.main;
            if (isPlayer && cam)
            {
                d = cam.transform.forward; var c = cam.transform.position;
                float t0 = Mathf.Max(0, Vector3.Dot(F.Eye - c, d) - .2f); o = c + d * t0;
            }
            else { o = F.Eye; d = F.Aim; }
            Fire(o, d, fromBurst);
        }

        Vector3 Hand => pose ? pose.HandWorld : F.Eye + F.Forward * .5f - Vector3.up * .5f;

        public void Fire(Vector3 origin, Vector3 dir, bool fromBurst = false)
        {
            var id = Gun; var g = GunsDb.Gun(id); if (g == null) return;
            bool me = isPlayer;
            F.NoteShot();

            if (g.IsMelee) { fireT = g.rate; kick = 1; Sfx.Play("swing", F.pos, 1, 14, id == "fists" ? 1.3f : 1); Melee(g); return; }
            if (g.IsFlame) { FireFlame(g, origin, dir); return; }
            if (g.IsProj)
            {
                fireT = g.rate; if (!fromBurst && g.burst > 0) { burstN = g.burst - 1; burstT = g.burstGap; }
                Use(id); kick = 1; flashT = id == "rocket" ? .08f : 0;
                var mz = Hand;
                if (me && vmRaise > .5f && (id == "rocket" || id == "crossbow" || id == "swarm")) mz = origin + dir * 1.05f + new Vector3(0, -.05f, 0);
                bool hb = Combat.RayBoxes(origin, dir, 250, out var bh), hf = Combat.RayFoes(origin, dir, F, out var fh);
                float t = 200; if (hb) t = Mathf.Min(t, bh.t); if (hf) t = Mathf.Min(t, fh.t);
                var aim = origin + dir * t; var d2 = aim - mz; if (d2.magnitude < .5f) d2 = dir; d2.Normalize();
                d2 = Combat.SpreadDir(d2, CurSpread());
                if (id == "swarm")
                {
                    swI = (swI + 1) % 4; var sd = new Vector3(d2.z, 0, -d2.x).normalized; int k = swI;
                    mz += sd * (k % 2 == 1 ? .06f : -.06f) + new Vector3(0, k < 2 ? .06f : -.06f, 0);
                    d2 = (d2 + sd * ((k % 2 == 1 ? 1 : -1) * .32f) + new Vector3(0, k < 2 ? .22f : .02f, 0)).normalized;
                    swAim = origin + dir * t;
                    swLock = hf && fh.t <= t + .01f ? fh.who : (fromBurst && swLock && swLock.alive ? swLock : null);
                }
                if (GSnd.TryGetValue(id, out var gs)) Sfx.Play(gs.n, F.pos, 1, 16, gs.r); else Sfx.Play(id == "rocket" ? "rocket" : "throw", F.pos, 1, 16);
                if (id == "rocket" || id == "swarm")
                {
                    if (me && vmRaise > .5f) GunFx.Emit(mz + d2 * 1.2f, GunFx.E(5, 1.5f, .08f, .18f, 0, "#ffd23f", "#ffb347"));
                    else GunFx.Emit(mz, GunFx.E(10, 2.5f, .18f, .4f, -1, "#f4f4f4", "#ffb347"));
                }
                var P = Projectiles.Spawn(F, id, mz, d2);
                if (id == "swarm" && P != null) { P.tgt = swAim; P.lockOn = swLock; P.life = 3; }
                if (me) F.pitch = Mathf.Clamp(F.pitch + g.kick, -1.3f, 1.3f);
                bloom = Mathf.Min(bloom + g.bloom, 3);
                return;
            }

            // ---- hitscan
            if (!fromBurst) { fireT = g.rate; if (g.burst > 0) { burstN = g.burst - 1; burstT = g.burstGap; } }
            Use(id); flashT = .05f; kick = 1;
            if (GSnd.TryGetValue(id, out var snd)) Sfx.Play(snd.n, F.pos, 1, g.Sniper ? 30 : g.Shotgun ? 24 : 16, snd.r);
            if (id == "duals") dualL = !dualL;
            Vector3 tr0 = pose ? pose.MuzzleWorld(dualL) : Hand;
            if (me && vmRaise > .5f) tr0 = origin + dir * .9f + new Vector3(0, -.09f, 0);
            float spr = CurSpread(); bool anyHit = false, head = false; hits.Clear();
            Color trc = me ? Web.Hex("#fff3b0") : Web.Hex("#ffd0c0");
            for (int p = 0; p < g.Pellets; p++)
            {
                var d = Combat.SpreadDir(dir, spr);
                var hbN = Combat.RayPen(origin, d, 400, woods); bool hb = hbN.HasValue; var bh = hb ? hbN.Value : default;
                bool hf = Combat.RayFoes(origin, d, F, out var fh);
                if (Mines.List.Count > 0 && Mines.Ray(origin, d, Mathf.Min(hb ? bh.t : 400, hf ? fh.t : 400), out var mt, out var mi))
                { Mines.Pop(mi, F); if (p < 3) GunFx.Tracer(tr0, origin + d * mt, trc, .045f); continue; }
                Vector3 end;
                if (hf && (!hb || fh.t < bh.t))
                {
                    end = origin + d * fh.t; float dist = fh.t; int wc = 0; foreach (var w in woods) if (w.t < fh.t) wc++;
                    float dm = g.dmg * (fh.head ? g.head : 1) * Mathf.Pow(.7f, wc);
                    if (g.HasFall) dm *= dist <= g.fall[0] ? 1 : dist >= g.fall[1] ? .45f : Mathf.Lerp(1, .45f, (dist - g.fall[0]) / (g.fall[1] - g.fall[0]));
                    if (!hits.TryGetValue(fh.who, out var hr)) hits[fh.who] = hr = new HitRec();
                    hr.t += dm; hr.n++; hr.d = Mathf.Min(hr.d, dist); if (fh.head) { hr.h = true; head = true; }
                    anyHit = true;
                    GunFx.HitSpray(end, d, g.Pellets > 1 ? 2 : 6, 1, null, hr.n == 1);
                }
                else if (hb)
                {
                    end = origin + d * bh.t;
                    if (p == 0) Sfx.Play("impact", end, 1, 9);
                    // breakables (glass, furniture, drums, cars, trees): the match chat's destruction takes the hit; the bullet still stops here
                    Destruction.Hit(bh.box, g.dmg, end, d, F);
                    GunFx.Emit(end + bh.n * .05f, new EmitOpts { n = 3, speed = 3, up = 1, size = .1f, life = .4f, vx = bh.n.x * 2, vz = bh.n.z * 2, colors = new[] { Web.Hex("#d8d4cc"), Web.Hex("#8a8f99"), Color.white } });
                    Turrets.HitBox(bh.box, g.dmg * (g.Pellets > 1 ? 1 : 1.2f), F);
                }
                else end = origin + d * 180;
                float et = Vector3.Distance(origin, end);
                Destruction.KnockRay(origin, d, et, g.dmg);
                // a bot's bullet whizzing past the player's head
                var me0 = Combat.Me;
                if (p == 0 && !me && me0 && me0.alive && !hits.ContainsKey(me0) && Combat.Time - whizT > .6f)
                {
                    var hp = me0.pos + Vector3.up * (2 * Fighter.SC); float tt = Mathf.Clamp(Vector3.Dot(hp - origin, d), 0, et); var q0 = origin + d * tt; float mm = Vector3.Distance(q0, hp);
                    if (mm < .9f && tt > 4 && tt < et - .5f) { whizT = Combat.Time; Sfx.Play("nearmiss", q0, 1 - mm / .9f * .4f, 14); }
                }
                foreach (var w in woods)
                {
                    if (w.t > et) break; var q = origin + d * w.t;
                    Destruction.Hit(w.box, g.dmg * .7f, q, d, F);   // wood the bullet passes through
                    GunFx.Emit(q + new Vector3(w.n.x, 0, w.n.z) * .05f, new EmitOpts { n = 3, speed = 2.5f, up = 1, size = .09f, life = .35f, colors = new[] { Web.Hex("#c99a5b"), Web.Hex("#8a5a34"), Web.Hex("#e9d3a8") } });
                }
                if (p < 3 || Random.value < .4f) GunFx.Tracer(tr0, end, trc, id == "sniper" ? .08f : .045f);
            }
            if (anyHit)
            {
                F.NoteHit(head);
                bool killed = false;
                foreach (var kv in hits)
                {
                    var foe = kv.Key; var hr = kv.Value; float total = Mathf.Round(hr.t), shown = total;
                    // shotguns: one blast downs only point-blank (most pellets landing from right next to them); otherwise it tops out at 75% of their health
                    if (g.Shotgun)
                    {
                        if (hr.d <= g.PointBlank && hr.n >= Mathf.CeilToInt(g.Pellets / 2f)) { shown = Mathf.Max(1, Mathf.Round(foe.hp + foe.armor)); total = Mathf.Ceil(shown) + 1; }
                        else total = shown = Mathf.Min(total, Mathf.Round(foe.hpMax * SG_MAX_FRAC));
                    }
                    if (Combat.Damage(foe, total, F, hr.h, id)) killed = true;
                    if (me) Combat.Feedback(foe, shown, hr.h, head || killed);
                }
            }
            if (me) { F.pitch = Mathf.Clamp(F.pitch + g.kick * (F.ads ? .6f : 1), -1.3f, 1.3f); F.yaw += (GR.Next() - .5f) * g.kick * .4f; }
            bloom = Mathf.Min(bloom + g.bloom, 3);
        }

        // gun sounds: the web game's GSND table (sound name, playback rate)
        static readonly Dictionary<string, (string n, float r)> GSnd = new Dictionary<string, (string, float)>
        {
            {"ump",("smgnova",.9f)}, {"sting",("smgnova",1.22f)}, {"swarm",("rocket",1.45f)}, {"sniper",("hdr",1)}, {"scout",("kar",1)}, {"ar",("akm",1)}, {"gar",("m4",1)},
            {"burst",("famas",1.18f)}, {"smg",("smgnova",1)}, {"hornet",("smgnova",.9f)}, {"vector",("smgnova",1.12f)}, {"p90",("smgnova",1.04f)}, {"shotgun",("shotboom",1)},
            {"autoshot",("shotboom",.94f)}, {"mg",("lmg",1)}, {"m60",("lmg",.86f)}, {"duals",("pistol",1.08f)}, {"crossbow",("crossbow",1)}
        };

        void Use(string id) { if (ammo.TryGetValue(id, out var a) && a != int.MaxValue) ammo[id] = a - 1; }

        // ------------------------------------------------------------------ melee: swing, lunge, first foe in the arc
        void Melee(GunStat g)
        {
            swingT = .32f; if (Gun == "sword") swL = !swL;
            F.vel.x += Mathf.Sin(F.yaw) * g.Lunge; F.vel.z += Mathf.Cos(F.yaw) * g.Lunge;
            var e = F.Eye;
            for (int k = 0; k < 10; k++) { float a = F.yaw + (k / 9f - .5f) * 1.6f; GunFx.Emit(new Vector3(F.pos.x + Mathf.Sin(a) * 1.7f, e.y - .6f, F.pos.z + Mathf.Cos(a) * 1.7f), GunFx.E(1, .3f, .16f, .25f, 0, "#ffffff", "#ffe9a8")); }
            var foes = new List<Fighter>(Combat.FoesOf(F));
            foes.Sort((a, b) => Vector3.SqrMagnitude(a.pos - F.pos).CompareTo(Vector3.SqrMagnitude(b.pos - F.pos)));
            foreach (var foe in foes)
            {
                float dx = foe.pos.x - F.pos.x, dz = foe.pos.z - F.pos.z, dy = foe.pos.y - F.pos.y, dist = Mathf.Sqrt(dx * dx + dz * dz);
                float ang = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, F.yaw * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                if (dist < g.reach && Mathf.Abs(dy) < 1.9f && ang < g.arc && Combat.Los(e, foe.pos + Vector3.up * (1.2f * Fighter.SC)))
                {
                    float dm = g.dmg;
                    GunFx.HitSpray(foe.pos + Vector3.up * 1.4f, null, 10, 1.3f);
                    Sfx.Play("punch", foe.pos, 1, 14, Gun == "sword" ? .9f : 1.05f); F.NoteHit(false);
                    if (isPlayer) Combat.Feedback(foe, dm, false, dm >= foe.hp);
                    foe.vel.x += dx / (dist > 0 ? dist : 1) * 5; foe.vel.z += dz / (dist > 0 ? dist : 1) * 5;
                    Combat.Damage(foe, dm, F, false, Gun);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ flamethrower: 20 bursts a second, burns everyone in the cone
        void FireFlame(GunStat g, Vector3 origin, Vector3 dir)
        {
            const string id = "flamer";
            if (!(flameT > 0)) Sfx.Play("fireball", F.pos, .5f, 14, .8f);
            fireT = g.rate; Use(id); kick = .25f; flameT = .12f;
            var mz = pose && pose.HeldVisible ? pose.MuzzleWorld(false) : F.Eye + F.Aim * .9f - Vector3.up * .32f;
            float R = g.range; bool hb = Combat.RayBoxes(origin, dir, R + 2, out var bh);
            var d2 = origin + dir * (hb ? bh.t : R) - mz; float stop = d2.magnitude;
            if (stop < .3f) { d2 = dir; stop = .3f; } else d2 /= stop;
            stop = Mathf.Min(stop, R);
            GunFx.FlameJet(mz, d2, stop);
            foreach (var v in new List<Fighter>(Combat.FoesOf(F)))
            {
                if (!v.alive) continue;
                var rel = v.pos + Vector3.up * 1.1f - mz; float along = Vector3.Dot(rel, d2);
                if (along < -.3f || along > stop + .6f) continue;
                if ((rel - d2 * along).magnitude > .7f + Mathf.Max(0, along) * .12f) continue;
                float L = rel.magnitude; if (L > .6f && Combat.RayBoxes(mz, rel / L, L - .5f, out _)) continue;
                float dm = g.dmg * (along < 5 ? 1 : Mathf.Lerp(1, .45f, Mathf.Clamp01((along - 5) / (R - 5))));
                Combat.Damage(v, dm, F, false, id); v.burnT = Mathf.Max(v.burnT, 1.5f); v.burnBy = F;
                if (isPlayer) { flHit += dm; if (Combat.Time - flHitT > .25f) { flHitT = Combat.Time; Combat.Feedback(v, Mathf.Round(flHit), false, false); flHit = 0; } }
            }
            // out of fuel: the old secondary comes back
            if (secondary == "flamer" && Ammo(id) <= 0) { secondary = flamerPrev ?? "smg"; flamerPrev = null; flameT = 0; int s = slot; slot = -1; Equip(s); }
        }

        // ------------------------------------------------------------------ lethals and grenades
        public bool ConsReady()
        {
            var c = GunsDb.Cons(lethal); if (c == null || c.passive) return false;
            return consT <= 0 && (c.uses == 0 || consN > 0);
        }

        Vector3 ThrowFrom() => F.Eye + F.Forward * .7f + new Vector3(0, -.2f, 0);

        /// heading toward whatever the crosshair is on, elevation from where you're looking, plus a lob
        /// set for one call by bots (BotExtras): throw along this direction instead of the fixed bot lob
        [System.NonSerialized] public Vector3? throwDirOverride;
        Vector3 ThrowDir()
        {
            if (throwDirOverride.HasValue) return throwDirOverride.Value;
            var cam = Camera.main;
            if (isPlayer && cam)
            {
                var c = cam.transform.position; var cd = cam.transform.forward;
                var aim = c + cd * (Combat.RayBoxes(c, cd, 120, out var hb) ? Mathf.Max(4, hb.t) : 120);
                var from = ThrowFrom(); float yaw = Mathf.Atan2(aim.x - from.x, aim.z - from.z); if (float.IsNaN(yaw)) yaw = F.yaw;
                var d = Dir(yaw, F.pitch); d.y += .22f; return d.normalized;
            }
            return Dir(F.yaw, .35f);
        }
        static Vector3 Dir(float yaw, float pitch) { float cp = Mathf.Cos(pitch); return new Vector3(Mathf.Sin(yaw) * cp, Mathf.Sin(pitch), Mathf.Cos(yaw) * cp); }

        public bool UseCons()
        {
            if (Busy || F.Frozen || !ConsReady()) return false;
            if (lethal == "mine") { if (!Mines.Lay(F)) return false; consN--; consT = .9f; swingT = .32f; }
            else if (Projectiles.Aimed.Contains(lethal)) { Sfx.Play("throw", F.pos, 1, 14); Projectiles.Spawn(F, lethal, ThrowFrom(), ThrowDir()); consN--; consT = .9f; swingT = .32f; }
            return true;
        }

        public bool ThrowNade()
        {
            if (!(nades > 0 || goldNade > 0) || nadeT > 0 || Busy || F.Frozen) return false;
            Sfx.Play("throw", F.pos, 1, 14);
            var p = Projectiles.Spawn(F, "grenade", ThrowFrom(), ThrowDir());
            if (goldNade > 0) { goldNade--; if (p != null) Projectiles.MakeGold(p); } else nades--;
            nadeT = .9f; swingT = .32f; return true;
        }

        // ------------------------------------------------------------------ the dotted throw arc while E or T is held
        readonly List<Vector3> arc = new List<Vector3>(); readonly List<Transform> dots = new List<Transform>(); Transform landDot; Material dotMat;
        void PreviewThrow()
        {
            string id = null;
            if (!Busy && !F.Frozen)
            {
                if (aimThrow && Projectiles.Aimed.Contains(lethal) && ConsReady()) id = lethal;
                else if (aimNade && (nades > 0 || goldNade > 0)) id = "grenade";
            }
            if (id == null) { foreach (var d in dots) d.gameObject.SetActive(false); if (landDot) landDot.gameObject.SetActive(false); return; }
            if (!dotMat) { dotMat = GunFx.MakeMat(false, null); dotMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always); dotMat.renderQueue = 4000; }
            var col = Web.Hex(id == "stun" ? "#4fb0ff" : id == "flash" ? "#ffffff" : id == "cluster" ? "#ff8a1f" : id == "grenade" ? "#ffcc1f" : "#39c6ff");
            dotMat.SetColor("_Color", col);
            var end = Projectiles.SimThrow(id, ThrowFrom(), ThrowDir(), arc, out var n);
            // resample the flight path every 0.55 m
            var pts = new List<Vector3> { arc[0] }; float acc = 0;
            for (int i = 1; i < arc.Count; i++)
            {
                var a = arc[i - 1]; var b = arc[i]; float L = Vector3.Distance(a, b);
                while (acc + L >= .55f) { float t = (.55f - acc) / L; a = Vector3.Lerp(a, b, t); pts.Add(a); L = Vector3.Distance(a, b); acc = 0; }
                acc += L;
            }
            int nn = Mathf.Min(110, pts.Count);
            while (dots.Count < nn) { var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(s.GetComponent<Collider>()); s.GetComponent<MeshRenderer>().sharedMaterial = dotMat; s.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; s.name = "throw dot"; dots.Add(s.transform); }
            for (int i = 0; i < dots.Count; i++)
            {
                bool on = i < nn && i >= 2; dots[i].gameObject.SetActive(on); if (!on) continue;
                float k = i / (float)Mathf.Max(1, nn - 1); dots[i].position = pts[i]; dots[i].localScale = Vector3.one * (.22f * (.85f - .3f * k));
            }
            if (!landDot) { var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(s.GetComponent<Collider>()); s.GetComponent<MeshRenderer>().sharedMaterial = dotMat; landDot = s.transform; landDot.name = "throw landing"; }
            landDot.gameObject.SetActive(true); landDot.position = end + n * .12f;
            var cam = Camera.main; landDot.localScale = Vector3.one * (cam ? Vector3.Distance(cam.transform.position, landDot.position) * .035f : .3f);
        }

        // ------------------------------------------------------------------ the shop kit (bought from the loadout menu while testing)
        public void Buy(string item)
        {
            switch (item)
            {
                case "gold": goldNade = 1; break;
                case "armor": F.armor = Mathf.Min(100, F.armor + 50); break;
                case "nades": nades = Mathf.Min(6, nades + 3); break;
                case "turret": turretKit = 1; break;
                case "flamer": if (secondary != "flamer") { flamerPrev = secondary; secondary = "flamer"; Fill("flamer"); slot = -1; Equip(1); } break;
                case "speed": F.speedT += 30; break;
            }
        }
    }
}
