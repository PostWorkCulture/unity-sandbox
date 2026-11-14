// Rumble's weapon ladder (Pete, 14 Nov 2026).
//  - Everyone starts a Rumble match on fists. Each kill you get without dying moves you one step up:
//    Fists -> Knife -> Baseball Bat -> Sword. Dying, however it happens, puts you back on fists.
//  - The step carries over between rounds in 1v1 and 2v2 (win a round with a kill and you start the next one armed)
//    and through free-for-all respawns. A new match starts everyone on fists again.
//  - The Knife and the Baseball Bat aren't in the web game. Their stats are made here from the Fists entry (AddGuns,
//    called by GunsDb.Load) and their models are built here from simple turned shapes (BuildProp, used by WeaponPose,
//    which swings them with the Sword's slashes).
//  - Your HUD shows the ladder where the ammo panel is in other modes (DrawPanel, called by WeaponHud).
//  - Remote test: call RumbleLadder.Test "2" (your step), "bot 3" (the bots' step), "" (just report).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter
    {
        /// Rumble: this fighter's step on the weapon ladder (0 fists, 1 knife, 2 bat, 3 sword)
        [NonSerialized] public int rumbleTier;
    }

    [DefaultExecutionOrder(86)]
    public class RumbleLadder : MonoBehaviour
    {
        public static readonly string[] Steps = { "fists", "knife", "bat", "sword" };
        static readonly string[] Short = { "FISTS", "KNIFE", "BAT", "SWORD" };
        const int Top = 3;

        static RumbleLadder I;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()   // RoundManager.Reboot runs this again after every scene reload
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("RumbleLadder").AddComponent<RumbleLadder>();
        }

        /// held in the right hand and swung with the Sword's slashes
        public static bool Swung(string id) => id == "sword" || id == "knife" || id == "bat";
        /// the weapon for this fighter's step
        public static string WeaponOf(Fighter f) => Steps[Mathf.Clamp(f ? f.rumbleTier : 0, 0, Top)];
        static string NameOf(int tier) { var g = GunsDb.Gun(Steps[tier]); return g != null ? g.name : Steps[tier]; }

        // ------------------------------------------------------------------ the two new weapons' stats
        // Fists 18 damage (6 hits), Knife 22 (5 hits, quicker), Bat 26 (4 hits, longer reach, a harder shove),
        // Sword 30 (4 hits, the longest reach and lunge; the web game's numbers).
        public static void AddGuns()
        {
            var fists = GunsDb.Gun("fists"); if (fists == null) return;
            GunsDb.Add(Make(fists, "knife", "Knife", 22, .38f, 2.8f, 1.05f, 4, "#d5dae2", "Rumble: one kill without dying"));
            GunsDb.Add(Make(fists, "bat", "Baseball Bat", 26, .55f, 3.2f, 1.15f, 5, "#c8935a", "Rumble: two kills without dying"));
        }

        static GunStat Make(GunStat from, string id, string name, float dmg, float rate, float reach, float arc, float lunge, string color, string note)
        {
            var g = JsonUtility.FromJson<GunStat>(JsonUtility.ToJson(from));
            g.id = id; g.name = name; g.dmg = dmg; g.rate = rate; g.reach = reach; g.arc = arc; g.lunge = lunge; g.color = color; g.note = note; g.cat = "Melee";
            return g;
        }

        // ------------------------------------------------------------------ the ladder
        readonly List<(Fighter v, Fighter by)> pending = new List<(Fighter, Fighter)>();

        void OnEnable() { Fighter.Killed += OnKilled; RoundManager.RoundStarted += OnRound; }
        void OnDisable() { Fighter.Killed -= OnKilled; RoundManager.RoundStarted -= OnRound; if (I == this) I = null; }

        static IEnumerable<Fighter> Everyone()
        {
            var rm = RoundManager.Instance;
            if (rm && rm.fighters.Count > 0) return rm.fighters;
            return Combat.Fighters;
        }

        void OnRound(int r)
        {
            pending.Clear();
            bool on = Rules.Rumble;
            foreach (var f in Everyone())
            {
                if (!f) continue;
                if (!on || r <= 1) f.rumbleTier = 0;   // a new match (or another mode): everyone back to the start
                if (on) Give(f, false);
            }
        }

        // the kill feed names the weapon in hand, so the change waits for Update (after everyone has heard about the kill)
        void OnKilled(Fighter v, Fighter by, bool head) { if (Rules.Rumble && v) pending.Add((v, by)); }

        void Update()
        {
            if (pending.Count == 0) return;
            foreach (var (v, by) in pending)
            {
                if (!v) continue;
                bool had = v.rumbleTier > 0;
                v.rumbleTier = 0; Give(v, false);
                if (v == Combat.Me && had) Pop(true);
                // the killer steps up: an enemy kill, and only if they're still standing themselves
                if (!by || !by.alive || !Combat.IsFoe(by, v) || by.rumbleTier >= Top) continue;
                by.rumbleTier++; Give(by, true);
                GunFx.Emit(by.pos + Vector3.up * 1.5f, GunFx.E(16, 3.5f, .14f, .45f, 3, "#ffffff", "#ffd23f"));
                if (by == Combat.Me) Pop(false);
            }
            pending.Clear();
        }

        /// put the fighter's step's weapon in both slots (Rumble has no second weapon)
        static void Give(Fighter f, bool draw)
        {
            string id = WeaponOf(f);
            if (GunsDb.Loaded && GunsDb.Gun(id) == null) id = "fists";
            bool changed = f.primary != id || f.secondary != id;
            f.primary = f.secondary = id;
            var w = f.GetComponent<Weapons>(); if (!w) return;
            w.flamerPrev = null;
            if (w.ammo.Count > 0) w.ammo[id] = int.MaxValue;   // (an empty table is filled by Weapons.ResetKit on its first frame)
            if (draw && changed) { w.switchT = .3f; w.swingT = 0; Sfx.Play("swap", f.pos, 1, 8); }
        }

        // ------------------------------------------------------------------ the HUD panel (in WeaponHud's units, in place of the ammo panel)
        static GUIStyle stHead, stHeadR, stName, stSeg, stNote;
        static float popAt = -99; static bool popBad;
        static readonly Color Panel = new Color(.07f, .075f, .09f, .82f), Ink = new Color(.06f, .065f, .08f), Gold = Hex.ToColor("#ffc61a"), Bad = Hex.ToColor("#ff4a3d");

        static void Pop(bool bad) { popAt = Time.unscaledTime; popBad = bad; }

        public static void DrawPanel(float x, float y, float pw, float ph, Fighter f)
        {
            if (stHead == null)
            {
                stHead = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Overflow, wordWrap = false };
                stName = new GUIStyle(stHead) { fontSize = 26 };
                stSeg = new GUIStyle(stHead) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
                stNote = new GUIStyle(stHead) { fontSize = 12, fontStyle = FontStyle.Normal };
                stHeadR = new GUIStyle(stHead) { fontSize = 10, alignment = TextAnchor.MiddleRight };
            }
            int tier = Mathf.Clamp(f ? f.rumbleTier : 0, 0, Top);
            float age = Time.unscaledTime - popAt, pop = age >= 0 && age < .9f ? 1 - age / .9f : 0;
            var hi = popBad ? Bad : Gold;
            Box(x, y, pw, ph, Panel);
            // (the bottom right corner, right of 175 and from 72 down, is left clear: CharacterPicker's "F3: characters" hint sits there)
            Text(new Rect(x + 10, y + 6, pw - 20, 16), "WEAPON LADDER", stHead, new Color(1, 1, 1, .55f), false);
            if (tier > 0) Text(new Rect(x + 10, y + 6, pw - 20, 16), "DYING RESETS", stHeadR, new Color(1, 1, 1, .4f), false);
            Text(new Rect(x + 10, y + 18, pw - 20, 34), NameOf(tier).ToUpperInvariant(), stName, Color.Lerp(Color.white, hi, pop), true);
            const float gap = 4; float sw = (pw - 20 - gap * 3) / 4, sy = y + 52;
            for (int i = 0; i <= Top; i++)
            {
                float sx = x + 10 + i * (sw + gap); bool cur = i == tier, done = i < tier;
                var bg = cur ? Color.Lerp(popBad ? Color.Lerp(Gold, Bad, pop) : Gold, Color.white, popBad ? 0 : pop * .7f) : done ? new Color(1, 1, 1, .85f) : new Color(1, 1, 1, .1f);
                Box(sx, sy, sw, 18, bg);
                Text(new Rect(sx, sy, sw, 18), Short[i], stSeg, cur || done ? Ink : new Color(1, 1, 1, .5f), false);
            }
            Text(new Rect(x + 10, y + 77, 165, 16), tier < Top ? "Next kill: " + NameOf(tier + 1) : "Top weapon", stNote, new Color(1, 1, 1, .9f), true);
        }

        static void Box(float x, float y, float w, float h, Color c) => HudRound.Fill(new Rect(x, y, w, h), c, Texture2D.whiteTexture);
        static void Text(Rect r, string s, GUIStyle st, Color c, bool shadow)
        {
            if (shadow) { st.normal.textColor = new Color(.06f, .07f, .08f, c.a); GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), s, st); }
            st.normal.textColor = c; GUI.Label(r, s, st);
        }

        // ------------------------------------------------------------------ the Knife and Baseball Bat models
        // Built once and shared. Model space: the hand is at the origin and the blade / barrel runs along +z; y is the
        // blade's wide direction (spine at +y, edge at -y); the holder turns it to the Sword's direction (web +z).
        struct Ring { public float z, rx, ry, cy; public Ring(float z, float rx, float ry, float cy = 0) { this.z = z; this.rx = rx; this.ry = ry; this.cy = cy; } }
        static Ring R(float z, float r) => new Ring(z, r, r);
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        /// The held model for "knife" or "bat" under parent (the right arm). Returns the holder; place it at the hand.
        public static Transform BuildProp(string id, Transform parent, List<Renderer> rends)
        {
            var holder = new GameObject(id == "bat" ? "Bat" : "Knife").transform; holder.SetParent(parent, false);
            var model = new GameObject("model").transform; model.SetParent(holder, false); model.localRotation = Quaternion.Euler(0, 180, 0);
            const float Q = 1.41421356f;
            if (id == "bat")
            {
                Part(model, rends, "bat wood", "#c8935a", 0, .4f, Lathe("bat wood", 14, 0, false,
                    R(-.215f, .02f), R(-.21f, .05f), R(-.185f, .056f), R(-.165f, .05f), R(-.15f, .03f), R(0, .03f), R(.2f, .032f), R(.45f, .042f),
                    R(.7f, .062f), R(.9f, .08f), R(1.1f, .086f), R(1.2f, .084f), R(1.245f, .066f), R(1.26f, .03f)));
                Part(model, rends, "bat grip", "#1d1f24", 0, .2f, Lathe("bat grip", 14, 0, false, R(-.15f, .0335f), R(.19f, .0355f)));
                Part(model, rends, "bat band", "#1d1f24", 0, .35f, Lathe("bat band", 14, 0, false, R(.6f, .0555f), R(.66f, .0605f)));
            }
            else
            {
                model.localScale = Vector3.one * 1.3f; model.localPosition = new Vector3(0, 0, -.05f);   // a chunky game knife, the guard just clear of the fist
                Part(model, rends, "knife handle", "#1c1e23", 0, .3f, Lathe("knife handle", 10, 0, false,
                    new Ring(-.125f, .012f, .014f), new Ring(-.12f, .033f, .04f), new Ring(-.095f, .034f, .042f), new Ring(-.07f, .027f, .034f), new Ring(0, .029f, .037f), new Ring(.065f, .026f, .033f)));
                Part(model, rends, "knife guard", "#3a3d44", .8f, .55f, Lathe("knife guard", 4, Mathf.PI / 4, true,
                    new Ring(.065f, .02f * Q, .085f * Q, -.012f), new Ring(.09f, .02f * Q, .085f * Q, -.012f)));
                Part(model, rends, "knife blade", "#d5dae2", .95f, .8f, Lathe("knife blade", 4, 0, true,
                    new Ring(.09f, .012f, .046f, -.01f), new Ring(.36f, .011f, .046f, -.01f), new Ring(.47f, .008f, .034f, -.004f), new Ring(.54f, .004f, .016f, .006f), new Ring(.585f, .0012f, .003f, .012f)));
            }
            return holder;
        }

        /// where the swing's light trail runs along each weapon (web z, from the hand)
        public static void TrailSpan(string id, out float z0, out float z1)
        {
            if (id == "knife") { z0 = .2f; z1 = .81f; } else if (id == "bat") { z0 = .55f; z1 = 1.24f; } else { z0 = .45f; z1 = 1.5f; }
        }

        static void Part(Transform parent, List<Renderer> rends, string name, string hex, float metal, float smooth, Mesh mesh)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mat(hex, metal, smooth);
            rends?.Add(mr);
        }

        static Material Mat(string hex, float metal, float smooth)
        {
            string key = hex + "|" + metal + "|" + smooth;
            if (mats.TryGetValue(key, out var m) && m) return m;
            // copy one of the guns' plain painted materials, so the shader variant is one the build already has
            Material src = null;
            if (WeaponModels.Mats != null)
                foreach (var x in WeaponModels.Mats)
                {
                    if (!x || !x.shader || x.shader.name != "Universal Render Pipeline/Lit" || x.renderQueue >= 2400) continue;
                    if (x.GetTexture("_BaseMap") || x.IsKeywordEnabled("_NORMALMAP") || x.IsKeywordEnabled("_METALLICSPECGLOSSMAP") || x.IsKeywordEnabled("_EMISSION")) continue;
                    src = x; break;
                }
            m = src ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "rumble " + hex; m.SetColor("_BaseColor", Hex.ToColor(hex)); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            return mats[key] = m;
        }

        /// A closed shape made of rings along z (radius rx across x, ry across y, centre lifted by cy), `sides` round.
        /// flat: hard edges (blades, the guard); otherwise smooth round the axis. Faces always end up pointing outwards.
        static Mesh Lathe(string name, int sides, float a0, bool flat, params Ring[] rings)
        {
            if (meshes.TryGetValue(name, out var have) && have) return have;
            var v = new List<Vector3>(); var t = new List<int>(); int n = rings.Length;
            Vector3 P(int i, int j) { float a = a0 + j * Mathf.PI * 2 / sides; var r = rings[i]; return new Vector3(Mathf.Cos(a) * r.rx, r.cy + Mathf.Sin(a) * r.ry, r.z); }
            if (flat)
                for (int i = 0; i < n - 1; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        int b = v.Count; v.Add(P(i, j)); v.Add(P(i, j + 1)); v.Add(P(i + 1, j)); v.Add(P(i + 1, j + 1));
                        t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
                    }
            else
            {
                for (int i = 0; i < n; i++) for (int j = 0; j < sides; j++) v.Add(P(i, j));
                for (int i = 0; i < n - 1; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        int a = i * sides + j, b = i * sides + (j + 1) % sides, c = a + sides, d = b + sides;
                        t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                    }
            }
            // end caps (their own corners, so the rim stays a hard edge)
            for (int e = 0; e < 2; e++)
            {
                int i = e == 0 ? 0 : n - 1; int c = v.Count; v.Add(new Vector3(0, rings[i].cy, rings[i].z));
                for (int j = 0; j < sides; j++) v.Add(P(i, j));
                for (int j = 0; j < sides; j++)
                {
                    int p = c + 1 + j, q = c + 1 + (j + 1) % sides;
                    if (e == 0) { t.Add(c); t.Add(p); t.Add(q); } else { t.Add(c); t.Add(q); t.Add(p); }
                }
            }
            // outwards: a closed mesh wound the right way has a positive volume
            float vol = 0; for (int k = 0; k < t.Count; k += 3) vol += Vector3.Dot(v[t[k]], Vector3.Cross(v[t[k + 1]], v[t[k + 2]]));
            if (vol < 0) for (int k = 0; k < t.Count; k += 3) { int s = t[k + 1]; t[k + 1] = t[k + 2]; t[k + 2] = s; }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return meshes[name] = m;
        }

        // ------------------------------------------------------------------ remote test
        /// call RumbleLadder.Test "2" puts you on step 2 (bat); "bot 3" puts the bots on the sword; "" reports everyone's step
        public static string Test(string a)
        {
            a = (a ?? "").Trim(); var me = Combat.Me; if (!me) return "no player";
            if (a.Length > 0)
            {
                bool bots = a.StartsWith("bot"); if (bots) a = a.Substring(3).Trim();
                int.TryParse(a, out int n); n = Mathf.Clamp(n, 0, Top);
                foreach (var f in Everyone()) if (f && (f == me) != bots) { f.rumbleTier = n; Give(f, true); if (f == me) Pop(false); }
            }
            var sb = new System.Text.StringBuilder(Rules.Rumble ? "rumble" : "not rumble");
            foreach (var f in Everyone()) if (f) sb.Append($" | {f.displayName}: step {f.rumbleTier} {f.primary} kills {f.kills} deaths {f.deaths}");
            return sb.ToString();
        }
    }
}
