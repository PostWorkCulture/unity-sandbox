// Seeing through walls (match chat), from the web game:
//  - HeartBeat (the "sense" lethal): 6 s of seeing every enemy through walls, 25 s cooldown. Bots that use it know where
//    you are for those 6 s, and you're told when an enemy has used it on you.
//  - Tagging: while you're down, your trigger fires a marker shot instead; an enemy it hits is outlined through walls
//    for 3 s (for you and your team's bots).
//  - Golden Armour (passive lethal): 30 armour at the start of every round, rebuilding slowly to 30 when out of fire.
// The outline is the web game's x-ray: a pulsing red fill drawn over everything.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter
    {
        [System.NonSerialized] public float senseT, tagT = -9;
        public bool Tagged => Time.time < tagT;
        public const float GOLD_ARMOUR = 30;
    }

    [DefaultExecutionOrder(70)]
    public class Intel : MonoBehaviour
    {
        static Intel I;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Ensure(); Ensure(); }
        static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("Intel").AddComponent<Intel>();
        }
        void OnEnable() { RoundManager.RoundStarted += OnRound; }
        void OnDisable() { RoundManager.RoundStarted -= OnRound; if (I == this) I = null; }
        void OnRound(int r) { foreach (var f in Combat.Fighters) if (f) { f.senseT = 0; f.tagT = -9; if (f.lethal == "gold") f.armor = Mathf.Max(f.armor, Fighter.GOLD_ARMOUR); } }

        /// HeartBeat: Weapons.UseCons calls this for the "sense" lethal
        public static bool Sense(Fighter f)
        {
            if (!f || !f.alive || f.downed) return false;
            Sfx.Play("sense", f.pos, 1, 6);
            var c = GunsDb.Cons("sense"); f.senseT = c != null && c.dur > 0 ? c.dur : 6;
            for (int i = 0; i < 36; i++)
            {
                float a = i / 36f * Mathf.PI * 2;
                var o = GunFx.E(1, .4f, .16f, .5f, 0, "#36f3ff", "#ffffff"); o.vx = Mathf.Cos(a) * 9; o.vz = Mathf.Sin(a) * 9;
                GunFx.Emit(new Vector3(f.pos.x + Mathf.Cos(a) * 1.2f, f.pos.y + 1.2f, f.pos.z + Mathf.Sin(a) * 1.2f), o);
            }
            return true;
        }

        /// someone on the other side has HeartBeat running (for the warning)
        public static bool EnemySensing(Fighter me)
        {
            foreach (var f in Combat.Fighters) if (f && f.alive && Combat.IsFoe(me, f) && f.senseT > 0) return true;
            return false;
        }

        float tagCD; bool wasFire;
        Material xr; readonly List<Renderer> rs = new List<Renderer>(); readonly Dictionary<Fighter, Renderer[]> cache = new Dictionary<Fighter, Renderer[]>();

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var f in Combat.Fighters)
            {
                if (!f) continue;
                if (f.senseT > 0) f.senseT -= dt;
                // Golden Armour rebuilds to 30 when out of fire (1.5 a second below half health, 0.75 above)
                if (f.lethal == "gold" && f.alive && !f.downed && !f.Frozen && f.armor < Fighter.GOLD_ARMOUR && f.simTime - f.lastHurt > Fighter.REGEN_DELAY)
                    f.armor = Mathf.Min(Fighter.GOLD_ARMOUR, f.armor + (f.hp < f.hpMax * .5f ? 6 : 3) * .25f * dt);
            }
            // tagging while down
            var me = Combat.Me; tagCD -= dt;
            bool fire = me && me.inFire;
            if (me && me.alive && me.downed && !me.InFinisher && fire && !wasFire && tagCD <= 0) TagShot(me);
            wasFire = fire;
        }

        void TagShot(Fighter f)
        {
            tagCD = .5f;
            var cam = Camera.main; if (!cam) return;
            var o = cam.transform.position; var d = cam.transform.forward;
            bool hf = Combat.RayFoes(o, d, f, out var fh);
            bool hb = Combat.RayBoxes(o, d, 200, out var bh);
            bool hit = hf && (!hb || fh.t < bh.t);
            var end = o + d * (hit ? fh.t : hb ? bh.t : 120);
            GunFx.Tracer(f.Eye, end, Web.Hex("#ff3b5c") * 2, .06f);
            if (!hit) return;
            fh.who.tagT = Time.time + 3;
            Combat.Feedback(fh.who, 0, false, false);
            GunFx.Emit(end, GunFx.E(12, 3, .15f, .4f, 0, "#ff3b5c", "#ffffff"));
            var rm = RoundManager.Instance;
            if (rm) rm.feed.Add(new RoundManager.FeedItem { text = $"{f.displayName}  tagged  {fh.who.displayName}", t = Time.time, mine = true });
        }

        // ------------------------------------------------------------------ the x-ray outline
        void LateUpdate()
        {
            var me = Combat.Me; if (!me) return;
            if (!xr)
            {
                var src = Resources.Load<Material>("StreetLife/Fresnel");
                xr = src ? new Material(src) : GunFx.MakeMat(false, null);
                xr.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                xr.renderQueue = 3500;
            }
            float pulse = .75f + .25f * Mathf.Sin(Time.time * 9);
            xr.SetColor("_Color", new Color(1f, .23f, .36f, .26f * pulse));
            xr.SetFloat("_Rim", .95f * pulse);
            foreach (var f in Combat.Fighters)
            {
                if (!f || !f.alive || !Combat.IsFoe(me, f)) continue;
                if (!(me.senseT > 0 || f.Tagged)) continue;
                if (!cache.TryGetValue(f, out var list) || list == null || list.Length == 0 || list[0] == null)
                {
                    rs.Clear();
                    foreach (var r in f.GetComponentsInChildren<MeshRenderer>(false)) if (r.gameObject.layer != ViewModel.Layer) rs.Add(r);
                    cache[f] = list = rs.ToArray();
                }
                foreach (var r in list)
                {
                    if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                    var rp = new RenderParams(xr) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                    Graphics.RenderMesh(rp, mf.sharedMesh, 0, r.localToWorldMatrix);
                }
            }
            if (Time.frameCount % 60 == 0) cache.Clear();   // rigs rebuild when skins change
        }
    }
}
