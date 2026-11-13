// Ability runtime (match chat): Blaze's fireball and Lil Pink's broken heart in flight, the shield / thorn bubble,
// Ghost's vanish, the grapple rope and the player's Ability button on phones. The rules live in FighterAbility.cs.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StreetLife
{
    [DefaultExecutionOrder(60)]
    public class Abilities : MonoBehaviour
    {
        public static Abilities I;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; Ensure(); }
        static void OnScene(Scene s, LoadSceneMode m) => Ensure();
        public static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("Abilities").AddComponent<Abilities>();
        }

        void OnEnable() { RoundManager.RoundStarted += OnRound; }
        void OnDisable() { RoundManager.RoundStarted -= OnRound; if (I == this) I = null; }
        void OnRound(int r) { ClearProjs(); }

        // ------------------------------------------------------------------ shared helpers
        /// where the player is aiming (crosshair target from the camera), or the given direction
        public static Vector3 AimFrom(Fighter f, Vector3 from, Vector3? aimDir)
        {
            if (aimDir.HasValue) return aimDir.Value.normalized;
            var cam = Camera.main;
            if (f != Combat.Me || !cam) return f.Aim;
            var c = cam.transform.position; var d = cam.transform.forward; float t = 150;
            if (Combat.RayBoxes(c, d, 200, out var hb)) t = Mathf.Min(t, hb.t);
            if (Combat.RayFoes(c, d, f, out var hf)) t = Mathf.Min(t, hf.t);
            var dir = c + d * t - from; return dir.sqrMagnitude > 1e-6f ? dir.normalized : f.Aim;
        }

        /// an ability hit: hit marker for the player, stats, and the kill feed names the ability
        public static void Hit(Fighter src, Fighter v, float dmg, string name, bool head = false, bool count = true)
        {
            if (!v || !v.alive) return;
            var prev = RoundManager.WeaponNameOf;
            RoundManager.WeaponNameOf = f => f == src ? name : prev != null ? prev(f) : f.primary;
            try
            {
                bool downs = v.downed ? dmg >= v.dhp : dmg >= v.hp + v.armor;
                if (src && src == Combat.Me && v != src) Combat.Feedback(v, dmg, head, downs);
                if (src && v != src && count) src.NoteHit(head);
                Combat.Damage(v, dmg, src, head, "ability");
            }
            finally { RoundManager.WeaponNameOf = prev; }
        }

        /// a short bright flash of light (lightning strike, blink)
        public static void Flash(Vector3 at, Color c, float range, float intensity)
        {
            if (!I) return;
            I.flashLight.transform.position = at; I.flashLight.color = c; I.flashLight.range = range; I.flashI = intensity;
        }

        // ------------------------------------------------------------------ fireball and broken heart
        class Proj { public Fighter f; public string id; public Vector3 pos, vel; public float life; public Transform mesh; public Light light; }
        readonly List<Proj> projs = new List<Proj>();

        // the web game's GUNS entries (damage after its +20%)
        static float ProjDmg(string id) => id == "fireball" ? 54 : 36;
        static float ProjRadius(string id) => id == "fireball" ? 2.7f : 2.4f;
        static float ProjSpeed(string id) => id == "fireball" ? 30 : 28;
        const float ProjGrav = 2, ProjLife = 4;

        public static void Spawn(Fighter f, string id, Vector3 from, Vector3 dir)
        {
            Ensure(); if (!I) return;
            var p = new Proj { f = f, id = id, pos = from, vel = dir.normalized * ProjSpeed(id), life = ProjLife };
            p.mesh = id == "fireball" ? I.FireballMesh() : I.HeartMesh();
            p.mesh.position = from; p.mesh.rotation = Quaternion.LookRotation(dir);
            if (!Application.isMobilePlatform)
            {
                p.light = new GameObject("glow").AddComponent<Light>(); p.light.transform.SetParent(p.mesh, false);
                p.light.type = LightType.Point; p.light.range = id == "fireball" ? 7 : 5; p.light.intensity = id == "fireball" ? 4 : 2.5f;
                p.light.color = id == "fireball" ? new Color(1, .55f, .2f) : new Color(1, .45f, .75f); p.light.shadows = LightShadows.None;
            }
            I.projs.Add(p);
        }

        void ClearProjs() { foreach (var p in projs) if (p.mesh) Destroy(p.mesh.gameObject); projs.Clear(); }

        void StepProjs(float dt)
        {
            for (int i = projs.Count - 1; i >= 0; i--)
            {
                var p = projs[i]; p.life -= dt;
                p.vel.y -= ProjGrav * dt;
                var st = p.vel * dt; float L = st.magnitude; var d = L > 1e-6f ? st / L : Vector3.forward;
                float t = L; bool hitBox = false, hitFoe = false; Vector3? n = null;
                if (Combat.RayBoxes(p.pos, d, L, out var hb) && hb.t <= t) { t = hb.t; hitBox = true; n = hb.n; }
                if (Combat.RayFoes(p.pos, d, p.f, out var hf) && hf.t <= t) { t = hf.t; hitFoe = true; hitBox = false; n = null; }
                if (hitBox || hitFoe || p.life <= 0 || p.pos.y < -20)
                {
                    var at = p.pos + d * t;
                    Burst(p, at, n);
                    if (p.mesh) Destroy(p.mesh.gameObject);
                    projs.RemoveAt(i); continue;
                }
                p.pos += st;
                if (p.mesh) { p.mesh.position = p.pos; p.mesh.rotation = Quaternion.LookRotation(d) * Quaternion.Euler(0, 0, Time.time * 400); }
                if (p.id == "heart") { if (Random.value < .8f) Trail(p.pos, 2, .5f, .22f, .4f, -1, "#ff6fae", "#ff9ccc", "#ffffff"); }
                else
                {
                    if (Random.value < .9f) Trail(p.pos, 2, .6f, .3f, .4f, -2, "#ffd23f", "#ff7a1f", "#ff3b1f");
                    FlameFx.Lick(p.pos - d * .2f, .9f);
                }
            }
        }

        static void Trail(Vector3 at, int n, float speed, float size, float life, float grav, params string[] cols) => GunFx.Emit(at, GunFx.E(n, speed, size, life, grav, cols));

        static readonly string[] FireCols = { "#ffd23f", "#ff7a1f", "#ff3b1f", "#ffb347" };
        static readonly string[] HeartCols = { "#ff6fae", "#ff9ccc", "#ffffff", "#16161a" };

        /// the burst: the weapons chat's explode() with the ability's numbers, then burning (fireball) or slowed (heart)
        void Burst(Proj p, Vector3 at, Vector3? n)
        {
            string name = p.id == "fireball" ? "Fireball" : "Heartbreak";
            float R = ProjRadius(p.id);
            var cfg = new Combat.Blast { dmg = ProjDmg(p.id), radius = R, gun = p.id, colors = p.id == "fireball" ? FireCols : HeartCols };
            var prev = RoundManager.WeaponNameOf;
            RoundManager.WeaponNameOf = f => f == p.f ? name : prev != null ? prev(f) : f.primary;
            try { Combat.Explode(p.f, at, n, cfg); }
            finally { RoundManager.WeaponNameOf = prev; }
            var c0 = n.HasValue ? at + n.Value * .2f : at;
            foreach (var o in Combat.Fighters)
            {
                if (!o || !o.alive || (o != p.f && !Combat.IsFoe(p.f, o))) continue;
                var c = new Vector3(o.pos.x, o.pos.y + 1.1f * Fighter.SC, o.pos.z); float dd = Vector3.Distance(c, c0);
                if (dd > R + .6f || (!Combat.Los(c0, c) && dd > 1.2f)) continue;
                if (p.id == "fireball") { o.burnT = Mathf.Max(o.burnT, 2.5f); o.burnBy = p.f; }
                else if (o != p.f) o.slowT = Mathf.Max(o.slowT, 1.5f);
            }
            if (p.id == "heart") GunFx.Emit(at, GunFx.E(26, 7, .22f, .7f, 6, "#ff6fae", "#ff9ccc", "#ffffff", "#e8325f"));
        }

        // ------------------------------------------------------------------ meshes and materials
        Material fireMat, heartMat, coreMat;
        static Mesh sphere;

        /// a low-poly sphere with white vertex colours (the Fx shader multiplies by them)
        public static Mesh Sphere
        {
            get
            {
                if (sphere) return sphere;
                var v = new List<Vector3>(); var t = new List<int>(); const int S = 14, R = 10;
                for (int y = 0; y <= R; y++) for (int x = 0; x <= S; x++)
                    {
                        float a = x / (float)S * Mathf.PI * 2, b = y / (float)R * Mathf.PI;
                        v.Add(new Vector3(Mathf.Sin(b) * Mathf.Cos(a), Mathf.Cos(b), Mathf.Sin(b) * Mathf.Sin(a)) * .5f);
                    }
                for (int y = 0; y < R; y++) for (int x = 0; x < S; x++)
                    {
                        int i = y * (S + 1) + x;
                        t.Add(i); t.Add(i + 1); t.Add(i + S + 1); t.Add(i + 1); t.Add(i + S + 2); t.Add(i + S + 1);
                    }
                sphere = new Mesh { name = "ability sphere" };
                sphere.SetVertices(v); sphere.SetTriangles(t, 0);
                var cols = new Color[v.Count]; for (int i = 0; i < cols.Length; i++) cols[i] = Color.white; sphere.colors = cols;
                sphere.RecalculateNormals(); sphere.RecalculateBounds();
                return sphere;
            }
        }

        Transform Part(Transform parent, Vector3 lp, Vector3 scale, Material m, Quaternion? rot = null)
        {
            var go = new GameObject("part"); go.transform.SetParent(parent, false);
            go.transform.localPosition = lp; go.transform.localScale = scale; go.transform.localRotation = rot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = Sphere;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        Transform FireballMesh()
        {
            if (!fireMat) { fireMat = GunFx.MakeMat(false, null, true); fireMat.SetColor("_Color", new Color(4.2f, 1.7f, .45f)); }
            if (!coreMat) { coreMat = GunFx.MakeMat(false, null, true); coreMat.SetColor("_Color", new Color(5f, 4.2f, 2.6f)); }
            var root = new GameObject("fireball").transform;
            Part(root, Vector3.zero, Vector3.one * .62f, fireMat);
            Part(root, Vector3.zero, Vector3.one * .36f, coreMat);
            return root;
        }

        Transform HeartMesh()
        {
            if (!heartMat) { heartMat = GunFx.MakeMat(false, null, true); heartMat.SetColor("_Color", new Color(3.2f, .9f, 1.7f)); }
            var root = new GameObject("broken heart").transform;
            var h = new GameObject("heart").transform; h.SetParent(root, false); h.localRotation = Quaternion.Euler(0, 90, 0);
            Part(h, new Vector3(-.12f, .08f, 0), new Vector3(.3f, .3f, .2f), heartMat);
            Part(h, new Vector3(.12f, .08f, 0), new Vector3(.3f, .3f, .2f), heartMat);
            Part(h, new Vector3(0, -.08f, 0), new Vector3(.3f, .3f, .2f), heartMat, Quaternion.Euler(0, 0, 45));
            return root;
        }

        static Material fresnel;
        /// a new instance of the see-through rim material (StreetLife/Fresnel), or a plain see-through one
        public static Material NewFresnel(Color c, float rim)
        {
            if (!fresnel) fresnel = Resources.Load<Material>("StreetLife/Fresnel");
            Material m;
            if (fresnel) { m = new Material(fresnel); m.SetColor("_Color", c); m.SetFloat("_Rim", rim); }
            else { m = GunFx.MakeMat(false, null); m.SetColor("_Color", new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a + rim * .4f))); }
            return m;
        }

        // ------------------------------------------------------------------ per-fighter looks
        class Look
        {
            public Fighter f; public Transform bubble; public Material bubbleMat;
            public Renderer[] rends; public Material[][] orig; public UnityEngine.Rendering.ShadowCastingMode[] shadow; public Material ghostMat; public bool ghosted; public float ghostA = 1;
        }
        readonly Dictionary<Fighter, Look> looks = new Dictionary<Fighter, Look>();
        Light flashLight; float flashI;
        static readonly Color ShieldC = Web.Hex("#7fd3ff"), ThornC = Web.Hex("#7bd13a");

        void Awake()
        {
            flashLight = new GameObject("ability flash").AddComponent<Light>(); flashLight.transform.SetParent(transform);
            flashLight.type = LightType.Point; flashLight.intensity = 0; flashLight.shadows = LightShadows.None;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            StepProjs(dt);
            flashI = Mathf.Max(0, flashI - dt * 20); flashLight.intensity = flashI;
            // the player's Ability button on phones: at the web game's E place, labelled with the cooldown
            var me = Combat.Me;
            if (me && TouchControls.I)
            {
                bool on = Fighter.AbilitiesOn && me.alive && !MainMenu.Open;
                string label = me.abilT > 0 ? "Ability\n" + Mathf.CeilToInt(me.abilT) + "s" : "Ability";
                TouchControls.AddButton("ability", 234, 90, on ? 52 : 0, label, d => { var p = Combat.Me; if (!p) return; p.abilityHeld = d; if (d) p.UseAbility(); });
            }
        }

        void LateUpdate()
        {
            foreach (var f in Combat.Fighters)
            {
                if (!f) continue;
                if (!looks.TryGetValue(f, out var L)) { L = new Look { f = f }; looks[f] = L; }
                UpdLook(L);
            }
        }

        void UpdLook(Look L)
        {
            var f = L.f; var p = f.VisualPos;
            // shield / thorn bubble
            bool bub = f.alive && (f.shieldT > 0 || f.thornT > 0);
            if (bub && !L.bubble)
            {
                var go = new GameObject("ability bubble"); L.bubble = go.transform;
                go.AddComponent<MeshFilter>().sharedMesh = Sphere;
                var mr = go.AddComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                L.bubbleMat = NewFresnel(new Color(1, 1, 1, .1f), .75f); mr.sharedMaterial = L.bubbleMat;
            }
            if (L.bubble)
            {
                L.bubble.gameObject.SetActive(bub);
                if (bub)
                {
                    var c = f.shieldT > 0 ? ShieldC : ThornC; float left = f.shieldT > 0 ? f.shieldT : f.thornT;
                    float pulse = 1 + Mathf.Sin(Time.time * 6) * .015f, blink = left < 1 ? (Mathf.Sin(Time.time * 30) > 0 ? 1 : .35f) : 1;
                    L.bubble.position = p + Vector3.up * (1.15f * Fighter.SC);
                    L.bubble.localScale = new Vector3(2.3f, 2.9f, 2.3f) * Fighter.SC * pulse;
                    L.bubbleMat.SetColor("_Color", new Color(c.r * 1.6f, c.g * 1.6f, c.b * 1.6f, .1f * blink));
                    L.bubbleMat.SetFloat("_Rim", .8f * blink);
                }
            }
            // vanish: nearly invisible to the other side, see-through to yourself
            float want = f.alive && f.ghostT > 0 ? (Combat.IsFoe(Combat.Me, f) ? .08f : .35f) : 1;
            L.ghostA = Mathf.MoveTowards(L.ghostA, want, Time.deltaTime * 4);
            bool ghost = L.ghostA < .99f;
            if (ghost && !L.ghosted) GhostOn(L);
            else if (!ghost && L.ghosted) GhostOff(L);
            if (L.ghosted && L.ghostMat)
            {
                float a = L.ghostA;
                L.ghostMat.SetColor("_Color", new Color(.85f, .93f, 1f, a * .45f));
                L.ghostMat.SetFloat("_Rim", a * 1.4f);
            }
            // grapple rope from the right hand to the hook
            if (f.hookT > 0 && f.alive && !f.downed)
            {
                var hand = p + Vector3.up * (1.55f * Fighter.SC) + f.Right * .35f;
                GunFx.Tracer(hand, f.hookP, Web.Hex("#e6c25a") * 1.4f, .035f);
            }
            // jetpack: a hot glow at the pack while it burns
            if (f.jetOn > 0 && Random.value < .6f)
            {
                var back = p - f.Forward * .4f + Vector3.up * .6f;
                var o = GunFx.E(3, 2, .32f, .4f, 2, "#ffb347", "#ff7a1f", "#ffffff"); o.up = -10; GunFx.Emit(back, o);
            }
        }

        void GhostOn(Look L)
        {
            L.rends = L.f.GetComponentsInChildren<Renderer>(false);
            var list = new List<Renderer>();
            foreach (var r in L.rends) if (r && !(r is ParticleSystemRenderer) && r.gameObject.layer != ViewModel.Layer && (!L.bubble || r.transform != L.bubble)) list.Add(r);
            L.rends = list.ToArray(); L.orig = new Material[L.rends.Length][]; L.shadow = new UnityEngine.Rendering.ShadowCastingMode[L.rends.Length];
            if (!L.ghostMat) L.ghostMat = NewFresnel(new Color(.85f, .93f, 1f, .2f), 1);
            for (int i = 0; i < L.rends.Length; i++)
            {
                var r = L.rends[i]; L.orig[i] = r.sharedMaterials; L.shadow[i] = r.shadowCastingMode;
                var g = new Material[Mathf.Max(1, L.orig[i].Length)]; for (int k = 0; k < g.Length; k++) g[k] = L.ghostMat; r.sharedMaterials = g;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            L.ghosted = true;
        }

        void GhostOff(Look L)
        {
            if (L.rends != null)
                for (int i = 0; i < L.rends.Length; i++)
                {
                    var r = L.rends[i]; if (!r) continue;
                    r.sharedMaterials = L.orig[i];
                    r.shadowCastingMode = L.shadow[i];
                }
            L.rends = null; L.orig = null; L.ghosted = false;
        }
    }
}
