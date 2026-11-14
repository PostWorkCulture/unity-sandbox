// The crowning (match chat), from the web game's startCrown / poseCrown: winning the match cuts away from the map to
// a black throne room. The winners (you first, two at most) sit down on gold-and-velvet thrones with their kills and
// damage on the backs, two ring girls walk in carrying crowns overhead and lower them onto the winners' heads, then the
// camera cuts to a close-up of each winner turning to the lens, and back to the wide shot behind the results screen.
// Angel doesn't sit: she floats where her throne would be, in soft shafts of white light.
// 7 s with one winner, 8.8 s with two; Space or Enter skips to the results. It runs on its own layer far below the
// map; while it plays the main camera only draws that layer (so the map and its lights drop out) and the ambient goes dark
// (all put back afterwards). Left out from the web version: the winner's smirk (it repaints the face texture).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StreetLife
{
    [DefaultExecutionOrder(1200)]
    public class Crowning : MonoBehaviour
    {
        public static Crowning I;
        public const int Layer = 27;
        public const float DELAY = .8f;   // let the winning moment land before the cut
        static readonly Vector3 O = new Vector3(0, -8000, 0);
        public static bool Active => I && I.run;
        public static float Duration(int n) => n > 1 ? 8.8f : 7f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();   // RoundManager.Reboot runs this again after every scene reload
        static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("Crowning").AddComponent<Crowning>();
        }

        // ------------------------------------------------------------------ the set
        Transform stage; Transform[] thrones = new Transform[2], crowns = new Transform[2];
        Light[] spots = new Light[2]; Light keyL, rimL; Halo[] halos = new Halo[2];
        Girl[] girls = new Girl[2];
        class Girl { public Transform root, head; public Transform[] legs = new Transform[2], arms = new Transform[2]; public float hip; }
        class Halo { public Transform root; public List<Material> beams = new List<Material>(); public Material ring; public Light glow; }

        class Seat
        {
            public Fighter f; public float x, k = 1, seatY, side; public bool fly;
            public GameObject model; public Transform aR, aL, eR, eL, lR, lL, kR, kL, head, up;
            public float headTop, headW; public int kills, dmg;
        }
        class GirlRun { public Girl g; public int w; public float side; public bool helper; }

        bool run, pending; float t, startAt; int n; float half;
        readonly List<Seat> seats = new List<Seat>();
        readonly List<GirlRun> grs = new List<GirlRun>();
        MaterialLibrary lib;

        void OnEnable() { RoundManager.RoundEnded += OnRoundEnded; RoundManager.RoundStarted += OnRound; }
        void OnDisable() { RoundManager.RoundEnded -= OnRoundEnded; RoundManager.RoundStarted -= OnRound; Stop(); if (I == this) I = null; }
        void OnRound(int r) { pending = false; Stop(); }

        void OnRoundEnded(int round, int winner)
        {
            var rm = RoundManager.Instance;
            if (!rm || !rm.matchOver || winner < 0) return;
            pending = true; startAt = Time.time + DELAY;
            int cnt = 0; foreach (var f in rm.fighters) if (f && f.team == winner) cnt++;
            rm.stateT = Mathf.Max(rm.stateT, DELAY + Duration(Mathf.Min(2, cnt)));
        }

        // ------------------------------------------------------------------ start / stop
        void Begin()
        {
            var rm = RoundManager.Instance; if (!rm) return;
            int win = rm.lastWinner; if (win < 0) return;
            var who = new List<Fighter>();
            foreach (var f in rm.fighters) if (f && f.team == win) who.Add(f);
            who.Sort((a, b) => (b == rm.player ? 1 : 0) - (a == rm.player ? 1 : 0));
            if (who.Count > 2) who.RemoveRange(2, who.Count - 2);
            if (who.Count == 0) return;
            if (!BuildSet()) return;
            Stop();
            n = who.Count; seats.Clear(); grs.Clear();
            float[] xs = n > 1 ? new[] { -(1.15f + .15f), 1.15f + .15f } : new[] { 0f };
            { float L = xs[0] - 1.1f - 1.0f, Rt = xs[n - 1] + 1.1f + 1.0f, sh = -(L + Rt) / 2; for (int i = 0; i < n; i++) xs[i] += sh; half = (Rt - L) / 2 + .2f; }
            for (int i = 0; i < n; i++)
            {
                var f = who[i];
                var st = new Seat { f = f, x = xs[i], seatY = .3f + .775f, side = n > 1 ? (i == 1 ? 1 : -1) : (i == 1 ? 1 : -1), kills = f.kills, dmg = Mathf.RoundToInt(f.damageDealt) };
                var rig = f.GetComponent<CharacterRig>(); string id = CharacterSkin.Of(rig) ?? f.characterName;
                st.fly = id == "angel";
                if (!MakeModel(st, id)) continue;
                var th = thrones[i]; th.gameObject.SetActive(true); th.localPosition = new Vector3(st.x, .3f, -.3f);
                foreach (Transform c in th) c.gameObject.SetActive(!st.fly);
                SetHalo(i, st.fly ? st.x : (float?)null);
                var sp = spots[i]; sp.enabled = true; sp.transform.localPosition = new Vector3(st.x, 9, 3);
                sp.transform.rotation = Quaternion.LookRotation((stage.TransformPoint(new Vector3(st.x, .8f, -.3f)) - sp.transform.position).normalized);
                seats.Add(st);
            }
            n = seats.Count; if (n == 0) return;
            for (int i = n; i < 2; i++) { thrones[i].gameObject.SetActive(false); spots[i].enabled = false; SetHalo(i, null); }
            // one winner: the girls come in from both sides; two: one girl each
            if (n > 1) { grs.Add(new GirlRun { g = girls[0], w = 0, side = -1 }); grs.Add(new GirlRun { g = girls[1], w = 1, side = 1 }); }
            else { grs.Add(new GirlRun { g = girls[0], w = 0, side = -1 }); grs.Add(new GirlRun { g = girls[1], w = 0, side = 1, helper = true }); }
            for (int i = 0; i < 2; i++) { crowns[i].gameObject.SetActive(i < n); girls[i].root.gameObject.SetActive(true); }
            stage.gameObject.SetActive(true);
            t = 0; run = true; TakeOver(true);
        }

        void Stop()
        {
            if (run) TakeOver(false);
            run = false;
            foreach (var s in seats) if (s.model) { foreach (var mf in s.model.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) Destroy(mf.sharedMesh); Destroy(s.model); }
            seats.Clear();
            if (stage) stage.gameObject.SetActive(false);
        }

        bool MakeModel(Seat st, string id)
        {
            var set = CharacterSet.Load(); var e = set ? set.EntryOf(id) : null;
            if (e == null || !e.json || !e.bin) return false;
            if (!lib)
            {
                var src = st.f.GetComponent<CharacterRig>(); if (!src || !src.library) return false;
                lib = Instantiate(src.library); lib.name = "crowning materials";
                if (set.materials != null && set.materials.Length > 0) lib.character = set.materials;
            }
            var go = new GameObject("winner " + id); go.transform.SetParent(stage, false);
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);   // the model faces -z; the lens is at +z
            var rig = go.AddComponent<CharacterRig>(); rig.library = lib; rig.charJson = e.json; rig.charBin = e.bin;
            rig.Build();
            foreach (var x in go.GetComponentsInChildren<Transform>(true))
            {
                x.gameObject.layer = Layer;
                switch (x.name)
                {
                    case "aR.p": st.aR = x; break; case "aL.p": st.aL = x; break; case "aR.e": st.eR = x; break; case "aL.e": st.eL = x; break;
                    case "lR.p": st.lR = x; break; case "lL.p": st.lL = x; break; case "lR.k": st.kR = x; break; case "lL.k": st.kL = x; break;
                    case "headG": st.head = x; break; case "up": st.up = x; break;
                }
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.On;
            // the head's top and width at rest, for the crown
            go.transform.localPosition = new Vector3(0, 100, 0);
            if (st.head)
            {
                var b = new Bounds(); bool any = false;
                foreach (var r in st.head.GetComponentsInChildren<Renderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                if (any)
                {
                    st.headTop = st.head.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z)).y;
                    st.headW = Mathf.Min(b.size.x, b.size.z);
                }
                else { st.headTop = .63f; st.headW = .45f; }
            }
            st.model = go;
            return true;
        }

        // ------------------------------------------------------------------ taking the camera and the lighting over (and giving them back)
        Camera cam; CameraClearFlags sFlags; Color sBg; int sMask; float sFov, sNear, sFar;
        readonly List<(Camera c, bool on)> sStack = new List<(Camera, bool)>();
        AmbientMode sAmbMode; Color sAmb; bool sFog; float sAmbI, sRefl; SphericalHarmonicsL2 sProbe;

        void TakeOver(bool on)
        {
            if (on)
            {
                cam = Camera.main; if (!cam) return;
                sFlags = cam.clearFlags; sBg = cam.backgroundColor; sMask = cam.cullingMask; sFov = cam.fieldOfView; sNear = cam.nearClipPlane; sFar = cam.farClipPlane;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.cullingMask = 1 << Layer; cam.nearClipPlane = .05f; cam.farClipPlane = 200;
                sStack.Clear();
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null && data.cameraStack != null) foreach (var c in data.cameraStack) if (c) { sStack.Add((c, c.enabled)); c.enabled = false; }
                // the map's lights (sun included) sit on layers this camera no longer draws, so they drop out by themselves;
                // the throne room has its own warm rim light from behind, a soft key from the front and the two spots
                keyL.enabled = true; rimL.enabled = true;
                sRefl = RenderSettings.reflectionIntensity; RenderSettings.reflectionIntensity = .3f;
                sAmbMode = RenderSettings.ambientMode; sAmb = RenderSettings.ambientLight; sAmbI = RenderSettings.ambientIntensity; sFog = RenderSettings.fog; sProbe = RenderSettings.ambientProbe;
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.1f, .1f, .1f); RenderSettings.fog = false;
                var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(new Color(.1f, .1f, .1f)); RenderSettings.ambientProbe = sh;
            }
            else
            {
                if (cam)
                {
                    cam.clearFlags = sFlags; cam.backgroundColor = sBg; cam.cullingMask = sMask; cam.fieldOfView = sFov; cam.nearClipPlane = sNear; cam.farClipPlane = sFar;
                }
                foreach (var s in sStack) if (s.c) s.c.enabled = s.on;
                sStack.Clear();
                if (keyL) keyL.enabled = false; if (rimL) rimL.enabled = false;
                RenderSettings.reflectionIntensity = sRefl;
                RenderSettings.ambientMode = sAmbMode; RenderSettings.ambientLight = sAmb; RenderSettings.ambientIntensity = sAmbI; RenderSettings.fog = sFog; RenderSettings.ambientProbe = sProbe;
                cam = null;
            }
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            var rm = RoundManager.Instance;
            if (pending && Time.time >= startAt) { pending = false; if (rm && (rm.state == RoundManager.State.RoundEnd || rm.state == RoundManager.State.MatchEnd)) Begin(); }
            if (!run) return;
            if (!rm || MainMenu.Open || rm.state == RoundManager.State.Fight || rm.state == RoundManager.State.Countdown || rm.state == RoundManager.State.Intro || rm.state == RoundManager.State.Boot) { Stop(); return; }
            t += Time.deltaTime;
            // Space / Enter: on to the results (the throne room stays behind them)
            var kb = Keyboard.current;
            if (rm.state == RoundManager.State.RoundEnd && kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) rm.stateT = Mathf.Min(rm.stateT, .05f);
        }

        static float Ease(float u) => u * u * (3 - 2 * u);
        float Seg(float a, float b) => Mathf.Clamp01((t - a) / (b - a));
        static Quaternion WebRot(float x, float y, float z) => Quaternion.Euler(-x * Mathf.Rad2Deg, -y * Mathf.Rad2Deg, z * Mathf.Rad2Deg);

        void LateUpdate()
        {
            if (!run || !cam) return;
            float[] cuts = n > 1 ? new[] { 3.7f, 5.5f, 7.3f } : new[] { 3.7f, 5.6f };
            int shot = -1; if (t >= cuts[0] && t < cuts[cuts.Length - 1]) shot = t < cuts[1] ? 0 : 1;

            for (int i = 0; i < n; i++)
            {
                var st = seats[i]; var m = st.model.transform; float k = st.k, sit = Ease(Seg(.35f, 1.15f));
                if (st.fly)
                {
                    float upk = Ease(Seg(.2f, 1.6f)), bob = Mathf.Sin(t * 1.7f) * .09f * k;
                    m.localPosition = new Vector3(st.x, .3f + 1.25f * upk * k + bob * upk, Mathf.Lerp(.75f * k, -.1f * k, upk));
                    Pose(st.lL, WebRot(.12f * upk, 0, .05f)); Pose(st.lR, WebRot(-.08f * upk, 0, -.05f));
                    Pose(st.aL, WebRot(-.25f * upk, 0, -.35f * upk)); Pose(st.aR, WebRot(-.25f * upk, 0, .35f * upk));
                    var H = halos[i];
                    if (H != null)
                    {
                        float p = .8f + Mathf.Sin(t * 2.3f) * .2f;
                        for (int j = 0; j < H.beams.Count; j++) { var c = Color.white; c.a = (.10f + .05f * Mathf.Sin(t * 1.3f + j)) * upk; H.beams[j].SetColor("_Color", c); }
                        H.glow.intensity = 6 * upk * p; var rc = Web.Hex("#fff6dc"); rc.a = .35f * upk; H.ring.SetColor("_Color", rc);
                        H.root.localRotation = Quaternion.Euler(0, t * .25f * Mathf.Rad2Deg, 0);
                    }
                }
                else
                {
                    m.localPosition = new Vector3(st.x, Mathf.Lerp(.3f, st.seatY - 1 * Fighter.SC, sit), Mathf.Lerp(.75f * k, -.3f + .1f * k, sit));
                    Pose(st.lL, WebRot(-1.35f * sit, 0, .08f * sit)); Pose(st.lR, WebRot(-1.35f * sit, 0, -.08f * sit));
                    Pose(st.aL, WebRot(-.55f * sit, 0, -.12f)); Pose(st.aR, WebRot(-.55f * sit, 0, .12f));
                }
                Pose(st.kL, Quaternion.identity); Pose(st.kR, Quaternion.identity); Pose(st.eL, Quaternion.identity); Pose(st.eR, Quaternion.identity);
                if (st.up) st.up.localRotation = Quaternion.identity;
                // head: glances at the girl crowning them, then turns to the lens in the close-up
                float myCut = cuts[i], turn = Ease(Seg(myCut + .35f, myCut + .75f));
                float hy = t > 2.2f && t < myCut ? -st.side * .45f * Ease(Seg(2.2f, 2.6f)) : 0;
                float tc = Mathf.Max(0, t - myCut), look = Mathf.Atan2(.75f - .1f * tc, 2.4f - .12f * tc);
                if (t >= myCut) hy = Mathf.Lerp(-st.side * .45f, shot == i ? look * .9f : 0, turn);
                if (t >= myCut && shot != i) hy = 0;
                Pose(st.head, WebRot(t >= myCut + .4f ? .06f : 0, hy, t >= myCut + .4f ? -.06f : 0));
            }

            // girls walk in with the crowns overhead, lower them onto the heads, then step back and present
            foreach (var G in grs)
            {
                var g = G.g; var st = seats[G.w]; float k = st.k; var cr = crowns[G.w];
                var end = new Vector3(st.x + G.side * (1.0f * k + .55f), .3f, .45f); var start = new Vector3(st.x + G.side * 5.5f, .3f, .7f);
                float w = Ease(Seg(.7f, 2.3f)); var gp = Vector3.Lerp(start, end, w);
                if (t > 3.6f) gp.x += G.side * .25f * Ease(Seg(3.6f, 4.1f));
                g.root.localPosition = gp;
                bool walking = t > .7f && t < 2.3f;
                float ry = walking ? -G.side * Mathf.PI / 2 : Mathf.Lerp(-G.side * Mathf.PI / 2, -G.side * (Mathf.PI / 2 - .75f), Ease(Seg(2.3f, 2.6f)));
                if (t > 3.6f) ry = -G.side * (Mathf.PI / 2 - .75f) * (1 - Ease(Seg(3.6f, 4.1f))) + (-G.side * .35f) * Ease(Seg(3.6f, 4.1f));
                g.root.localRotation = Quaternion.Euler(0, ry * Mathf.Rad2Deg, 0);
                float ph = t * 8;
                g.legs[0].localRotation = Quaternion.Euler((walking ? Mathf.Sin(ph) * .45f : 0) * Mathf.Rad2Deg, 0, 0);
                g.legs[1].localRotation = Quaternion.Euler((walking ? -Mathf.Sin(ph) * .45f : 0) * Mathf.Rad2Deg, 0, 0);
                g.head.localRotation = Quaternion.Euler(-.1f * Mathf.Rad2Deg, 0, 0);

                // where the crown sits: on top of the winner's head, riding with it once it's on
                Vector3 on; Quaternion q;
                if (st.head) { on = st.head.TransformPoint(new Vector3(0, st.headTop, 0)) + Vector3.up * .02f; q = st.head.rotation * Quaternion.Euler(0, 180, 0); }
                else { on = stage.TransformPoint(new Vector3(st.x, 2.6f, 0)); q = stage.rotation; }
                float cs = Mathf.Max(.6f, st.headW / .8f * 1.08f);
                var over = stage.TransformPoint(gp + new Vector3(0, (g.hip + 1.25f) * .9f, 0) + new Vector3(-G.side, 0, 0) * .35f);
                Vector3 hand;
                if (!G.helper)
                {
                    float lower = Ease(Seg(2.5f, 3.4f));
                    cr.position = t < 2.5f ? over : Vector3.Lerp(over, on, lower);
                    cr.rotation = t >= 3.4f ? q : stage.rotation;
                    if (t >= 3.4f) cr.position = on;
                    cr.localScale = Vector3.one * cs;
                    hand = cr.position;
                }
                else hand = on + stage.rotation * new Vector3(G.side * .35f, .2f + .3f * (1 - Ease(Seg(2.5f, 3.4f))), 0);
                // arms reach for the crown until it's placed, then one hand on the hip, the other presenting the winner
                var inv = Quaternion.Inverse(g.root.rotation);
                for (int j = 0; j < 2; j++)
                {
                    var p = g.arms[j]; var sw = p.position;
                    if (t < 3.5f)
                    {
                        var tw = hand + g.root.rotation * new Vector3(j == 1 ? .14f : -.14f, 0, 0);
                        var dir = inv * (tw - sw).normalized;
                        p.localRotation = Quaternion.FromToRotation(Vector3.down, dir);
                    }
                    else
                    {
                        float pres = Ease(Seg(3.6f, 4.1f)); float sj = j == 1 ? 1 : -1;
                        p.localRotation = sj == -G.side ? Quaternion.Euler(-1.2f * pres * Mathf.Rad2Deg, 0, sj * .5f * pres * Mathf.Rad2Deg) : Quaternion.Euler(0, 0, sj * .45f * Mathf.Rad2Deg);
                    }
                }
            }

            // camera: a wide shot of the stage, then a close-up of each winner in turn, then wide again
            float asp = cam.aspect;
            if (shot < 0)
            {
                float fov = 38, tn = Mathf.Tan(fov * Mathf.Deg2Rad / 2), top = .3f + 4.7f;
                float rf = HudLayout.Touch ? .5f : .45f;   // the results sit over the top: frame the thrones in the screen below them
                float d = Mathf.Max(7.5f, half / tn / asp + 1.2f, (top / (1 - rf) / 2 + .15f) / tn + .4f);
                float push = Mathf.Min(.45f, t >= cuts[cuts.Length - 1] ? (t - cuts[cuts.Length - 1]) * .15f : t * .12f);
                float lift = rf * (d - push) * tn;
                cam.fieldOfView = fov;
                cam.transform.position = stage.TransformPoint(new Vector3(0, top * .5f + .4f + d * .03f + lift, d - push));
                cam.transform.LookAt(stage.TransformPoint(new Vector3(0, top * .5f - .1f + lift, 0)));
            }
            else
            {
                var st = seats[Mathf.Min(shot, seats.Count - 1)]; float tt = t - cuts[shot], k = st.k;
                var h = st.head ? st.head.TransformPoint(new Vector3(0, st.headTop * .55f, 0)) : stage.TransformPoint(new Vector3(st.x, 2, 0));
                cam.fieldOfView = 30;
                cam.transform.position = h + stage.rotation * new Vector3((.95f - .1f * tt) * k, .15f * k, (3.1f - .12f * tt) * k);
                cam.transform.LookAt(h + stage.rotation * new Vector3(.05f * k, -.02f * k, 0));
            }
        }

        static void Pose(Transform j, Quaternion q) { if (j) j.localRotation = q; }

        // ------------------------------------------------------------------ the plates' numbers (drawn over the throne backs, under the results screen)
        GUIStyle small, big;
        void OnGUI()
        {
            if (!run || !cam || Event.current.type != EventType.Repaint) return;
            GUI.depth = 5;   // behind the HUD (its results dimmer goes over this too)
            float a = Ease(Seg(1.15f, 1.6f)); if (a <= 0) return;
            if (small == null) { small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow }; big = new GUIStyle(small); }
            for (int i = 0; i < n; i++)
            {
                var st = seats[i]; if (st.fly) continue;
                var th = thrones[i];
                Vector3 W(float x, float y) => th.TransformPoint(new Vector3(x, y, -.465f + .16f));
                var c = cam.WorldToScreenPoint(W(0, 3.62f)); if (c.z <= 0) continue;
                var c2 = cam.WorldToScreenPoint(W(0, 3.62f + .375f));
                float px = Mathf.Abs(c2.y - c.y) / .375f;   // screen pixels per metre at the plate
                var gold = Web.Hex("#f3cf63"); gold.a = a;
                small.fontSize = Mathf.Max(6, Mathf.RoundToInt(.1f * px)); big.fontSize = Mathf.Max(8, Mathf.RoundToInt(.27f * px));
                void Txt(float x, float y, string s, GUIStyle stl)
                {
                    var p = cam.WorldToScreenPoint(W(x, y)); var r = new Rect(p.x - 200, Screen.height - p.y - 60, 400, 120);
                    stl.normal.textColor = new Color(0, 0, 0, a * .6f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, stl);
                    stl.normal.textColor = gold; GUI.Label(r, s, stl);
                }
                // the lens looks along -z from +z, which mirrors x on screen: KILLS goes on the left from the viewer's side
                Txt(.375f, 3.62f + .375f - .27f * .75f, "KILLS", small); Txt(-.375f, 3.62f + .375f - .27f * .75f, "DAMAGE", small);
                Txt(.375f, 3.62f + .375f - .66f * .75f, st.kills.ToString(), big); Txt(-.375f, 3.62f + .375f - .66f * .75f, st.dmg.ToString(), big);
                var top = cam.WorldToScreenPoint(W(0, 3.62f + .375f - .16f * .75f)); var bot = cam.WorldToScreenPoint(W(0, 3.62f + .375f - .74f * .75f));
                var o = GUI.color; GUI.color = gold; GUI.DrawTexture(new Rect(top.x - 2, Screen.height - top.y, 4, Mathf.Abs(top.y - bot.y)), Texture2D.whiteTexture); GUI.color = o;
            }
        }

        // ================================================================== building the set (once)
        static Mesh cube, cone4, sphere, octa, bandMesh, capMesh;
        Material M(string hex, float smooth, float metal = 0, string emit = null)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "crown " + hex };
            m.SetColor("_BaseColor", Web.Hex(hex)); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            if (emit != null) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Web.Hex(emit)); }
            return m;
        }
        Transform Box(Transform p, float w, float h, float d, Material m, float x, float y, float z, Vector3? rotRad = null)
        {
            var go = new GameObject("b"); go.layer = Layer; var t = go.transform; t.SetParent(p, false);
            t.localPosition = new Vector3(x, y, z); t.localScale = new Vector3(w, h, d);
            if (rotRad.HasValue) t.localRotation = Quaternion.Euler(rotRad.Value * Mathf.Rad2Deg);
            go.AddComponent<MeshFilter>().sharedMesh = cube; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            return t;
        }
        Transform Part(Transform p, Mesh mesh, Material m, Vector3 at, Vector3 scale, bool shadow = true)
        {
            var go = new GameObject("p"); go.layer = Layer; var t = go.transform; t.SetParent(p, false);
            t.localPosition = at; t.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return t;
        }

        bool BuildSet()
        {
            if (stage) return true;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
            tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
            cone4 = Cone(4); octa = Octahedron(); bandMesh = Band(.38f, .4f, .24f, 8); capMesh = Hemisphere(12, 8);

            stage = new GameObject("Throne room").transform; stage.SetParent(transform, false); stage.position = O;
            var gold = M("#e2b33c", .72f, .85f); var velvet = M("#7a0f1c", .15f);
            // floor, platform, gold trims
            Box(stage, 80, .02f, 80, M("#000000", .05f), 0, -.01f, 0);
            Box(stage, 9.4f, .3f, 3.6f, M("#0b0b0c", .6f), 0, .15f, -.2f);
            Box(stage, 9.44f, .05f, .05f, gold, 0, .29f, 1.6f); Box(stage, 9.44f, .05f, .05f, gold, 0, .02f, 1.6f);
            // lights (on the throne layer): a soft key from the front, a warm rim from behind, two spots over the thrones
            keyL = new GameObject("key").AddComponent<Light>(); keyL.transform.SetParent(stage, false); keyL.gameObject.layer = Layer;
            rimL = new GameObject("rim").AddComponent<Light>(); rimL.transform.SetParent(stage, false); rimL.gameObject.layer = Layer;
            rimL.type = LightType.Directional; rimL.color = Web.Hex("#ffd9a0"); rimL.intensity = .9f; rimL.shadows = LightShadows.Soft; rimL.transform.rotation = Quaternion.LookRotation(new Vector3(0, -6, 8).normalized); rimL.enabled = false;
            keyL.type = LightType.Directional; keyL.color = Color.white; keyL.intensity = .4f; keyL.shadows = LightShadows.None;
            keyL.transform.rotation = Quaternion.LookRotation(-new Vector3(2, 4, 9).normalized); keyL.cullingMask = 1 << Layer; keyL.enabled = false;
            for (int i = 0; i < 2; i++)
            {
                var s = new GameObject("spot").AddComponent<Light>(); s.transform.SetParent(stage, false); s.gameObject.layer = Layer;
                s.type = LightType.Spot; s.color = Web.Hex("#fff1d6"); s.intensity = 60; s.range = 20; s.spotAngle = 48; s.innerSpotAngle = 22;
                s.shadows = LightShadows.Soft; s.cullingMask = 1 << Layer; s.enabled = false; spots[i] = s;
            }
            var gemR = M("#c8102e", .8f, .3f, "#3a0008"); var gemB = M("#1f6fe0", .85f, .2f, "#04102a");
            for (int i = 0; i < 2; i++)
            {
                var t = new GameObject("throne").transform; t.SetParent(stage, false); thrones[i] = t;
                Box(t, 1.7f, .45f, 1.3f, velvet, 0, .55f, 0); Box(t, 1.8f, .12f, 1.4f, gold, 0, .3f, 0);
                foreach (var sx in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 }) Box(t, .16f, .3f, .16f, gold, sx * .78f, .15f, sz * .55f);
                Box(t, 1.7f, 3.7f, .3f, velvet, 0, 2.3f, -.62f); Box(t, 1.86f, .14f, .36f, gold, 0, 4.18f, -.62f);
                Box(t, .14f, 3.7f, .36f, gold, -.88f, 2.3f, -.62f); Box(t, .14f, 3.7f, .36f, gold, .88f, 2.3f, -.62f);
                foreach (var sx in new[] { -1, 1 }) { Box(t, .26f, .5f, 1.25f, velvet, sx * .98f, 1.0f, 0); Box(t, .3f, .1f, 1.3f, gold, sx * .98f, 1.28f, 0); }
                foreach (var sx in new[] { -1, 0, 1 })
                {
                    Part(t, cone4, gold, new Vector3(sx * .6f, sx != 0 ? 4.4f : 4.6f, -.62f), new Vector3(.28f, .5f, .28f));
                    Part(t, octa, gemR, new Vector3(sx * .6f, 4.35f, -.45f), Vector3.one * .16f);
                }
                t.gameObject.SetActive(false);
            }
            // crowns: an open gold band, eight points with balls, gems round it, a velvet cap
            var bandM = M("#e8b93a", .75f, .9f); bandM.SetFloat("_Cull", 0);
            for (int i = 0; i < 2; i++)
            {
                var c = new GameObject("crown").transform; c.SetParent(stage, false); crowns[i] = c;
                Part(c, bandMesh, bandM, Vector3.zero, Vector3.one);
                for (int j = 0; j < 8; j++)
                {
                    float a = j / 8f * Mathf.PI * 2 + Mathf.PI / 8, x = Mathf.Cos(a) * .4f * .97f, z = Mathf.Sin(a) * .4f * .97f;
                    Part(c, cone4, gold, new Vector3(x, .25f, z), new Vector3(.16f, .3f, .16f));
                    Part(c, sphere, gold, new Vector3(x, .42f, z), Vector3.one * .07f);
                    var gm = Part(c, cube, j % 2 == 1 ? gemB : gemR, new Vector3(Mathf.Cos(a) * .405f, 0, Mathf.Sin(a) * .405f), new Vector3(.07f, .07f, .03f));
                    gm.localRotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)));
                }
                Part(c, capMesh, velvet, new Vector3(0, .02f, 0), new Vector3(.4f * .92f, .4f * .92f * .5f, .4f * .92f));
                c.gameObject.SetActive(false);
            }
            for (int i = 0; i < 2; i++) girls[i] = MakeGirl();
            stage.gameObject.SetActive(false);
            return true;
        }

        void SetHalo(int i, float? x)
        {
            var H = halos[i];
            if (x == null) { if (H != null) H.root.gameObject.SetActive(false); return; }
            if (H == null)
            {
                H = halos[i] = new Halo();
                H.root = new GameObject("halo").transform; H.root.SetParent(stage, false);
                for (int j = 0; j < 5; j++)
                {
                    var m = GunFx.MakeMat(true, null); H.beams.Add(m);
                    Part(H.root, Band(1.1f + j * .35f, .35f + j * .18f, 9, 24, true), m, new Vector3(0, 4.5f - .2f, 0), Vector3.one, false);
                }
                H.ring = GunFx.MakeMat(true, null);
                Part(H.root, Ring(1.0f, 1.5f, 48), H.ring, new Vector3(0, .02f, 0), Vector3.one, false);
                H.glow = new GameObject("glow").AddComponent<Light>(); H.glow.gameObject.layer = Layer; H.glow.transform.SetParent(H.root, false); H.glow.transform.localPosition = new Vector3(0, 3, 1.2f);
                H.glow.type = LightType.Point; H.glow.range = 9; H.glow.intensity = 0; H.glow.cullingMask = 1 << Layer;
                foreach (var tr in H.root.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = Layer;
            }
            H.root.gameObject.SetActive(true);
            H.root.localPosition = new Vector3(x.Value, .3f, -.2f);
        }

        // ring girl: long legs, heels, black satin one-piece with a red belt, long blonde hair, a painted face
        Girl MakeGirl()
        {
            var g = new Girl(); var r = new GameObject("ring girl").transform; r.SetParent(stage, false); g.root = r;
            var skin = M("#f2c9a8", .45f); var suit = M("#0e0e10", .75f, .4f); var hair = M("#f3d36b", .55f); var heel = M("#c9ced8", .82f, .9f, "#22262c");
            float legH = 1.55f, hip = .12f + legH; g.hip = hip;
            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? -1 : 1; var p = new GameObject("leg").transform; p.SetParent(r, false); p.localPosition = new Vector3(sx * .13f, hip, 0);
                Box(p, .21f, legH, .24f, skin, 0, -legH / 2 + .06f, 0); Box(p, .23f, .12f, .36f, heel, 0, -legH, .05f); Box(p, .06f, .14f, .06f, heel, 0, -legH + .01f, -.1f);
                g.legs[s] = p;
            }
            Box(r, .52f, .3f, .3f, suit, 0, hip + .05f, 0); Box(r, .5f, .6f, .28f, suit, 0, hip + .45f, 0);
            Box(r, .54f, .07f, .31f, M("#d0101e", .7f, .2f), 0, hip + .2f, 0); Box(r, .18f, .16f, .18f, skin, 0, hip + .82f, 0);
            var head = new GameObject("head").transform; head.SetParent(r, false); head.localPosition = new Vector3(0, hip + .9f, 0); g.head = head;
            Box(head, .44f, .46f, .4f, skin, 0, .23f, 0);
            var face = new GameObject("face"); face.layer = Layer; face.transform.SetParent(head, false); face.transform.localPosition = new Vector3(0, .23f, .201f);
            face.transform.localScale = new Vector3(.44f, .46f, 1); face.transform.localRotation = Quaternion.Euler(0, 180, 0);
            face.AddComponent<MeshFilter>().sharedMesh = Quad();
            var fm = new Material(Shader.Find("Universal Render Pipeline/Lit")); fm.SetTexture("_BaseMap", FaceTex()); fm.SetFloat("_Smoothness", .35f);
            face.AddComponent<MeshRenderer>().sharedMaterial = fm;
            Box(head, .5f, .14f, .46f, hair, 0, .49f, -.01f); Box(head, .5f, .85f, .12f, hair, 0, .12f, -.24f);
            Box(head, .08f, .7f, .3f, hair, -.25f, .15f, -.06f); Box(head, .08f, .7f, .3f, hair, .25f, .15f, -.06f);
            Box(head, .46f, .12f, .06f, hair, .04f, .39f, .2f, new Vector3(0, 0, -.15f));
            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? -1 : 1; var p = new GameObject("arm").transform; p.SetParent(r, false); p.localPosition = new Vector3(sx * .33f, hip + .68f, 0);
                Box(p, .14f, .85f, .14f, skin, 0, -.42f, 0); Box(p, .13f, .12f, .14f, skin, 0, -.88f, 0);
                g.arms[s] = p;
            }
            r.localScale = Vector3.one * .9f;
            r.gameObject.SetActive(false);
            return g;
        }

        // her face, painted like the web game's canvas: cheeks, blue eyes with lashes and brows, red lips
        static Texture2D FaceTex()
        {
            const int S = 128; var px = new Color32[S * S]; var skin = (Color32)Web.Hex("#f2c9a8");
            for (int i = 0; i < px.Length; i++) px[i] = skin;
            void Put(int x, int y, Color32 c, float a = 1)
            {
                if (x < 0 || y < 0 || x >= S || y >= S) return; int i = (S - 1 - y) * S + x; var o = px[i];
                px[i] = new Color32((byte)(o.r + (c.r - o.r) * a), (byte)(o.g + (c.g - o.g) * a), (byte)(o.b + (c.b - o.b) * a), 255);
            }
            void Ell(float cx, float cy, float rx, float ry, Color32 c, float a = 1)
            { for (int y = (int)(cy - ry - 1); y <= cy + ry + 1; y++) for (int x = (int)(cx - rx - 1); x <= cx + rx + 1; x++) { float d = (x - cx) * (x - cx) / (rx * rx) + (y - cy) * (y - cy) / (ry * ry); if (d <= 1) Put(x, y, c, a * Mathf.Clamp01((1 - d) * rx)); } }
            void Line(float x0, float y0, float x1, float y1, float w, Color32 c)
            { int n = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) * 2) + 1; for (int i = 0; i <= n; i++) { float u = i / (float)n; Ell(Mathf.Lerp(x0, x1, u), Mathf.Lerp(y0, y1, u), w / 2, w / 2, c); } }
            var cheek = new Color32(239, 159, 149, 255); Ell(30, 82, 10, 10, cheek, .5f); Ell(98, 82, 10, 10, cheek, .5f);
            var ink = new Color32(17, 17, 17, 255);
            foreach (var ex in new[] { 42f, 86f })
            {
                Ell(ex, 62, 11, 8, new Color32(255, 255, 255, 255)); Ell(ex, 63, 6, 6, (Color32)Web.Hex("#2f7fe0")); Ell(ex, 63, 3, 3, ink);
                for (int k = 0; k <= 12; k++) { float a0 = Mathf.PI + k / 12f * Mathf.PI; Ell(ex + Mathf.Cos(a0) * 11, 62 + Mathf.Sin(a0) * 8, 1.5f, 1.5f, ink); }
                foreach (var kk in new[] { -8f, 0f, 8f }) Line(ex + kk, 55, ex + kk * 1.3f, 49, 2.5f, ink);
                var brow = new Color32(160, 122, 58, 255); for (int k = 0; k <= 10; k++) { float u = k / 10f; Ell(Mathf.Lerp(ex - 11, ex + 11, u), 44 - Mathf.Sin(u * Mathf.PI) * 3, 1.5f, 1.5f, brow); }
            }
            var lip = new Color32(224, 0, 27, 255);
            for (int y = 90; y <= 108; y++) for (int x = 47; x <= 81; x++)
            {
                float u = (x - 64) / 17f; float topY = 95 - 4 * Mathf.Abs(Mathf.Sin(u * Mathf.PI)) * (1 - Mathf.Abs(u)) - 1, botY = 98 + 12 * (1 - u * u);
                if (Mathf.Abs(u) <= 1 && y >= topY && y <= botY) Put(x, y, lip);
            }
            for (int x = 58; x < 66; x++) for (int y = 100; y < 103; y++) Put(x, y, new Color32(255, 255, 255, 255), .55f);
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "ring girl face", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px); tex.Apply(true, true); return tex;
        }

        // ------------------------------------------------------------------ meshes
        static Mesh Quad()
        {
            var m = new Mesh { name = "quad" };
            m.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 }; m.RecalculateNormals(); m.RecalculateTangents(); return m;
        }
        static Mesh Cone(int seg)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2, a1 = (i + 1) / (float)seg * Mathf.PI * 2;
                var p0 = new Vector3(Mathf.Cos(a0) * .5f, -.5f, Mathf.Sin(a0) * .5f); var p1 = new Vector3(Mathf.Cos(a1) * .5f, -.5f, Mathf.Sin(a1) * .5f);
                int b = v.Count; v.Add(p0); v.Add(new Vector3(0, .5f, 0)); v.Add(p1); t.Add(b); t.Add(b + 1); t.Add(b + 2);
                b = v.Count; v.Add(p0); v.Add(p1); v.Add(new Vector3(0, -.5f, 0)); t.Add(b); t.Add(b + 1); t.Add(b + 2);
            }
            var m = new Mesh { name = "cone" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return Colourise(m);
        }
        static Mesh Octahedron()
        {
            var P = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            int[] f = { 2, 4, 0, 2, 0, 5, 2, 5, 1, 2, 1, 4, 3, 0, 4, 3, 5, 0, 3, 1, 5, 3, 4, 1 };
            var v = new List<Vector3>(); var t = new List<int>(); foreach (var i in f) { t.Add(v.Count); v.Add(P[i] * .5f); }
            var m = new Mesh { name = "octa" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return Colourise(m);
        }
        // an open tube (crown band, light shafts): radius at the bottom r0, top r1, centred, seg sides
        static Mesh Band(float r0, float r1, float h, int seg, bool upFromZero = false)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            float y0 = upFromZero ? -h / 2 : -h / 2, y1 = h / 2;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * r0 + Vector3.up * y0); v.Add(d * r1 + Vector3.up * y1); n.Add(d); n.Add(d);
            }
            for (int i = 0; i < seg; i++) { int a = i * 2; t.Add(a); t.Add(a + 1); t.Add(a + 2); t.Add(a + 2); t.Add(a + 1); t.Add(a + 3); }
            var m = new Mesh { name = "band" }; m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds(); return Colourise(m);
        }
        static Mesh Ring(float r0, float r1, int seg)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++) { float a = i / (float)seg * Mathf.PI * 2; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); v.Add(d * r0); v.Add(d * r1); }
            for (int i = 0; i < seg; i++) { int a = i * 2; t.Add(a); t.Add(a + 2); t.Add(a + 1); t.Add(a + 1); t.Add(a + 2); t.Add(a + 3); }
            var m = new Mesh { name = "ring" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return Colourise(m);
        }
        static Mesh Hemisphere(int seg, int rings)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int j = 0; j <= rings; j++)
                for (int i = 0; i <= seg; i++)
                {
                    float ph = j / (float)rings * Mathf.PI / 2, a = i / (float)seg * Mathf.PI * 2;
                    v.Add(new Vector3(Mathf.Cos(a) * Mathf.Cos(ph), Mathf.Sin(ph), Mathf.Sin(a) * Mathf.Cos(ph)));
                }
            for (int j = 0; j < rings; j++) for (int i = 0; i < seg; i++) { int a = j * (seg + 1) + i, b = a + seg + 1; t.Add(a); t.Add(b); t.Add(a + 1); t.Add(a + 1); t.Add(b); t.Add(b + 1); }
            var m = new Mesh { name = "cap" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return Colourise(m);
        }
        static Mesh Colourise(Mesh m) { var c = new Color[m.vertexCount]; for (int i = 0; i < c.Length; i++) c[i] = Color.white; m.colors = c; return m; }

        /// remote test: call Crowning.Test [start|state]
        public static string Test(string a)
        {
            if (!I) return "no Crowning";
            if (a == "start") { I.Begin(); return I.run ? $"crowning {I.n}" : "couldn't start"; }
            return $"run {I.run} t {I.t:F1} n {I.n} pending {I.pending}";
        }
    }
}
