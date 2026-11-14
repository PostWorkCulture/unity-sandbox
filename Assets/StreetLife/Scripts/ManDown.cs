// Man Down (match chat), the parts of the web game's startEdgeFall / edgeFallStep / waterFallStep the maps chat's
// FighterEdge leaves to the presentation: slow motion (1.7 s, 2.4 s into the water), the camera cut out over the drop
// (looking back at the edge, then down at the body tumbling away; it stays on you if it was you), the HUD fading out
// meanwhile, a dark red vignette under the maps chat's "MAN DOWN" banner, and the tumble itself: spinning head over
// heels with arms and legs flailing, or a slow topple into the water, a drift down and a fade into the blue.
// The camera cut happens for your own fall, for someone you hit, and for anyone when there are four fighters or fewer.
using UnityEngine;

namespace StreetLife
{
    /// HUD switches for the cinematic moments (the crowning, Man Down)
    public static class Cine { public static bool HideHud => Crowning.Active || ManDown.Active; }

    [DefaultExecutionOrder(1150)]
    public class ManDown : MonoBehaviour
    {
        static ManDown I;
        public static bool Active => I && I.cam && I.D != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();   // RoundManager.Reboot runs this again after every scene reload
        static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("ManDown").AddComponent<ManDown>();
        }

        class Shot { public Fighter f; public Vector3 n, e, side, look; public float rt; public bool cut = true, water; }
        Shot D; Camera cam; float camFov; Texture2D vig; float vigT = -9;

        // the tumble of every faller
        class Fall { public float rx, spin; }
        readonly System.Collections.Generic.Dictionary<Fighter, Fall> falls = new System.Collections.Generic.Dictionary<Fighter, Fall>();

        void OnEnable() { Fighter.WentOverEdge += OnEdge; RoundManager.RoundStarted += OnRound; }
        void OnDisable() { Fighter.WentOverEdge -= OnEdge; RoundManager.RoundStarted -= OnRound; Release(); if (I == this) I = null; }
        void OnRound(int r) { Release(); D = null; foreach (var kv in falls) if (kv.Key) { var rig = kv.Key.GetComponent<CharacterRig>(); if (rig) rig.SetVisible(true); } falls.Clear(); }

        void OnEdge(Fighter f)
        {
            if (!f) return;
            falls[f] = new Fall { rx = 0, spin = (Random.value < .5f ? -1 : 1) * Random.Range(4.5f, 6.5f) };
            var rm = RoundManager.Instance; var me = Combat.Me;
            bool mine = f == me || f.lastBy == me || (rm && rm.fighters.Count <= 4);
            bool water = MapRules.DeepWater;
            if (Finishers.I && (mine || f == me)) Finishers.I.SlowMo(water ? 2.4f : 1.7f);
            if (!mine) return;
            Vector3 n;
            var hv = new Vector3(f.vel.x, 0, f.vel.z);
            if (water && hv.sqrMagnitude > .01f) n = hv.normalized;
            else n = Mathf.Abs(f.pos.x) / 18.5f > Mathf.Abs(f.pos.z) / 22.5f ? new Vector3(Mathf.Sign(f.pos.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(f.pos.z));
            // the side with more room (the web game picks one at random; a wall right there would fill the shot)
            var side = new Vector3(-n.z, 0, n.x) * (Random.value < .5f ? -1 : 1);
            var eye = new Vector3(f.pos.x, 0, f.pos.z) + Vector3.up * 2.3f;
            float r1 = Room(eye, side, 3.6f), r2 = Room(eye, -side, 3.6f); if (r2 > r1 + .5f) side = -side;
            D = new Shot { f = f, n = n, e = new Vector3(f.pos.x, 0, f.pos.z), side = side, water = water };
            vigT = Time.unscaledTime;
        }

        void Release()
        {
            if (cam) cam.fieldOfView = camFov;
            cam = null;
        }

        void LateUpdate()
        {
            Tumbles();
            var rm = RoundManager.Instance; var me = Combat.Me;
            if (D == null) { if (cam) Release(); return; }
            bool live = rm && (rm.state == RoundManager.State.Fight || rm.state == RoundManager.State.RoundEnd || rm.state == RoundManager.State.MatchEnd);
            if (!live || Crowning.Active || !D.f || !(D.rt < 3.4f || (D.f == me && me && !me.alive))) { Release(); if (!live || !D.f || D.rt >= 3.4f) D = null; return; }
            float dt = Time.deltaTime; D.rt += dt; float u = Mathf.Min(1, D.rt / 3);
            var c = Camera.main; if (!c) return;
            if (cam != c) { cam = c; camFov = c.fieldOfView; }
            float y = 2.3f - u * 2.6f;
            if (D.water) y = Mathf.Max(MapRules.LagoonWaterY(D.e.x, D.e.z, Time.time) + 1.3f, y);
            var want = D.e + D.n * (4.6f + u * 1.6f) + D.side * 3.4f + Vector3.up * y;
            { var from = D.e + Vector3.up * 1.5f; var d = want - from; float L = d.magnitude, h = L > .01f ? BoxWorld.Ray(from, d / L, L) : -1; if (h >= 0) want = from + d / L * Mathf.Max(.5f, h - .4f); }   // never inside a wall
            var tg = D.f.pos + Vector3.up;
            if (D.cut)
            {
                D.cut = false; var from = D.e + Vector3.up * 1.5f; var p0 = D.e - D.n * 2.2f + D.side * 2.4f + Vector3.up * 2.6f;
                var d = p0 - from; float L = d.magnitude, h = BoxWorld.Ray(from, d / L, L); if (h >= 0) p0 = from + d / L * Mathf.Max(.5f, h - .4f);
                c.transform.position = p0; D.look = D.e + Vector3.up;
            }
            c.transform.position = Vector3.Lerp(c.transform.position, want, 1 - Mathf.Exp(-2.6f * dt));
            D.look = Vector3.Lerp(D.look, tg, 1 - Mathf.Exp(-7 * dt));
            c.transform.LookAt(D.look);
            c.fieldOfView = Mathf.Lerp(c.fieldOfView, 62, 1 - Mathf.Exp(-4 * dt));
        }

        // spinning head over heels, arms up and flailing; in the water a slow topple, a drift down and gone after 3.6 s
        void Tumbles()
        {
            if (falls.Count == 0) return;
            float dt = Time.deltaTime;
            foreach (var kv in falls)
            {
                var f = kv.Key; var F = kv.Value; if (!f || !f.EdgeFalling) continue;
                var rig = f.GetComponent<CharacterRig>(); if (!rig || !rig.Built) continue;
                bool wet = MapRules.DeepWater && f.edgeWet; float wt = f.EdgeWetTime;
                float k;
                if (!MapRules.DeepWater) { F.rx += dt * F.spin; k = Time.time * 9; }
                else if (!wet) { F.rx += dt * F.spin * .8f; k = Time.time * 8; }
                else { F.rx += dt * F.spin * .12f * Mathf.Exp(-wt * .4f); k = Time.time * 2.2f; }
                float rz = wet ? Mathf.Sin(Time.time * .9f) * .25f : Mathf.Sin(F.rx * .7f) * (MapRules.DeepWater ? .4f : .45f);
                // turn about the middle of the body, not the feet
                var t = rig.transform; var pivot = f.VisualPos + Vector3.up * Fighter.SC;
                var rot = Quaternion.Euler(-F.rx * Mathf.Rad2Deg, f.yaw * Mathf.Rad2Deg + 180, rz * Mathf.Rad2Deg);
                t.rotation = rot; t.position = pivot - rot * (Vector3.up * Fighter.SC);
                Quaternion ar, al; float ll, lr;
                if (!wet) { ar = Web(-2.8f, .6f + Mathf.Sin(k) * .45f); al = Web(-2.8f, -.6f - Mathf.Sin(k) * .45f); ll = .6f * Mathf.Sin(k * .8f); lr = -.6f * Mathf.Sin(k * .8f); }
                else { ar = Web(-2.6f + Mathf.Sin(k) * .3f, .9f + Mathf.Sin(k) * .25f); al = Web(-2.6f - Mathf.Sin(k) * .3f, -.9f - Mathf.Sin(k) * .25f); ll = .35f * Mathf.Sin(k * .7f); lr = -.35f * Mathf.Sin(k * .7f); }
                foreach (var x in rig.GetComponentsInChildren<Transform>(false))
                    switch (x.name)
                    {
                        case "aR.p": x.localRotation = ar; break; case "aL.p": x.localRotation = al; break;
                        case "lL.p": x.localRotation = Quaternion.Euler(-ll * Mathf.Rad2Deg, 0, 0); break;
                        case "lR.p": x.localRotation = Quaternion.Euler(-lr * Mathf.Rad2Deg, 0, 0); break;
                    }
                if (wet && wt > 3.6f) rig.SetVisible(false);
                if (!MapRules.DeepWater && f.pos.y < -90) rig.SetVisible(false);
            }
        }
        static float Room(Vector3 from, Vector3 dir, float max) { float h = BoxWorld.Ray(from, dir.normalized, max); return h < 0 ? max : h; }
        static Quaternion Web(float x, float z) => Quaternion.Euler(-x * Mathf.Rad2Deg, 0, z * Mathf.Rad2Deg);

        /// remote test: call ManDown.Test: walks the player off the nearest open edge (maps with edges only)
        public static string Test(string a)
        {
            var me = Combat.Me; if (!me) return "no player"; if (!MapRules.HasEdge) return "no edges on this map";
            if (me.downed || !me.alive) return "player is down or out; try again next round";
            foreach (var f in Combat.Fighters) if (f && f != me) f.Frozen = true;   // the bots hold still for the shot
            for (float x = 0; x < 80; x += .25f) if (MapRules.Drop(x, 0)) { me.Place(new Vector3(x + .3f, .05f, 0), Mathf.PI / 2); me.vel = new Vector3(4, 0, 0); return $"player put over the edge at x {x:F1}"; }
            return "no drop found along +x";
        }

        // the dark red vignette under the banner (3.6 s)
        void OnGUI()
        {
            float a = Time.unscaledTime - vigT; if (a < 0 || a > 3.6f || Event.current.type != EventType.Repaint) return;
            if (!vig)
            {
                const int S = 64; vig = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                    {
                        float dx = (x + .5f) / S * 2 - 1, dy = (y + .5f) / S * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                        vig.SetPixel(x, y, new Color(45 / 255f, 0, 0, Mathf.Clamp01((d - .35f) / .65f) * .7f));
                    }
                vig.Apply();
            }
            float k = a < .36f ? a / .36f : a > 2.8f ? Mathf.Clamp01((3.6f - a) / .8f) : 1;
            GUI.depth = 4;
            var o = GUI.color; GUI.color = new Color(1, 1, 1, k); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vig); GUI.color = o;
        }
    }
}
