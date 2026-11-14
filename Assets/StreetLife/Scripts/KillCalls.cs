// Kill call-outs and grudges (match chat), from the web game's killNotes / killCall:
//  - Your kills: FIRST BLOOD, REVENGE (got back the one who last got you, 3+ fighters), SHUTDOWN (ended a streak of 3+),
//    DOUBLE / TRIPLE / QUAD / MULTI KILL (kills within 4 s of each other), or else KILLING SPREE (3), RAMPAGE (5),
//    UNSTOPPABLE (7), GODLIKE (10).
//  - Kills on you, or by whoever got you: the same in red ("Blaze drew first blood", PAYBACK, ...).
//  - Two lines at most, the second a beat later; gone after 2.6 s.
//  - Grudges: a bot that gets killed hunts its killer until it next gets a kill (RoundManager.PickTargets reads
//    Fighter.grudge), and bots lean slightly towards whoever is closest to winning.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter
    {
        [System.NonSerialized] public int streak, multi;
        [System.NonSerialized] public float multiT = -99;
        [System.NonSerialized] public Fighter grudge;
    }

    [DefaultExecutionOrder(85)]
    public class KillCalls : MonoBehaviour
    {
        static KillCalls I;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();   // RoundManager.Reboot runs this again after every scene reload
        static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("KillCalls").AddComponent<KillCalls>();
        }

        static readonly string[] MULTI = { "", "", "DOUBLE KILL", "TRIPLE KILL", "QUAD KILL", "MULTI KILL" };
        static readonly Dictionary<int, string> SPREE = new Dictionary<int, string> { { 3, "KILLING SPREE" }, { 5, "RAMPAGE" }, { 7, "UNSTOPPABLE" }, { 10, "GODLIKE" } };

        bool first; readonly Dictionary<Fighter, Fighter> last = new Dictionary<Fighter, Fighter>();
        struct Line { public string big, sub; public bool bad; }
        readonly List<Line> lines = new List<Line>(); float shownAt = -99;

        void OnEnable() { Fighter.Killed += OnKilled; RoundManager.RoundStarted += OnRound; }
        void OnDisable() { Fighter.Killed -= OnKilled; RoundManager.RoundStarted -= OnRound; if (I == this) I = null; }

        void OnRound(int r)
        {
            if (r > 1) return;   // per match
            first = false; last.Clear(); lines.Clear();
            foreach (var f in Combat.Fighters) if (f) { f.streak = 0; f.multi = 0; f.multiT = -99; f.grudge = null; }
        }

        static string Nm(Fighter f) => f == Combat.Me ? "You" : f.displayName;

        void OnKilled(Fighter v, Fighter cr, bool head)
        {
            if (!v || !cr || cr == v || !Combat.IsFoe(cr, v)) return;
            var rm = RoundManager.Instance; var me = Combat.Me;
            bool firstOfGame = !first; first = true;
            bool many = rm && rm.fighters.Count > 2;
            last.TryGetValue(cr, out var crLast);
            bool revenge = many && crLast == v; if (crLast == v) last.Remove(cr);
            last[v] = cr;
            int vStreak = v.streak; v.streak = 0; cr.streak++;
            cr.multi = Time.time - cr.multiT < 4 ? Mathf.Max(1, cr.multi) + 1 : 1; cr.multiT = Time.time;
            cr.grudge = null;
            if (v != me) v.grudge = cr;
            if (cr != me && v != me) return;

            var L = new List<Line>();
            if (cr == me)
            {
                if (firstOfGame) L.Add(new Line { big = "FIRST BLOOD" });
                if (revenge) L.Add(new Line { big = "REVENGE", sub = "You got " + Nm(v) + " back" });
                if (vStreak >= 3) L.Add(new Line { big = "SHUTDOWN", sub = "Ended " + Nm(v) + "'s " + vStreak + " kill streak" });
                if (cr.multi >= 2) L.Add(new Line { big = MULTI[Mathf.Min(cr.multi, 5)] });
                else if (SPREE.TryGetValue(cr.streak, out var sp)) L.Add(new Line { big = sp, sub = cr.streak + " kills in a row" });
            }
            else
            {
                string who = Nm(cr);
                if (firstOfGame) L.Add(new Line { big = "FIRST BLOOD", sub = who + " drew first blood", bad = true });
                if (revenge) L.Add(new Line { big = "PAYBACK", sub = who + " got their revenge", bad = true });
                if (cr.multi >= 2) L.Add(new Line { big = MULTI[Mathf.Min(cr.multi, 5)], sub = who, bad = true });
                else if (SPREE.TryGetValue(cr.streak, out var sp)) L.Add(new Line { big = sp, sub = who + "  ·  " + cr.streak + " in a row", bad = true });
            }
            if (L.Count == 0) return;
            lines.Clear(); for (int i = 0; i < L.Count && i < 2; i++) lines.Add(L[i]);
            shownAt = Time.unscaledTime;
        }

        // ------------------------------------------------------------------ drawing (the web game's pop: grows in, settles, fades)
        GUIStyle big, sub; float s;

        void OnGUI()
        {
            if (lines.Count == 0 || MainMenu.Open || MainMenu.Paused) return;
            float age = Time.unscaledTime - shownAt;
            if (age > 2.6f) { lines.Clear(); return; }
            if (Event.current.type != EventType.Repaint) return;
            GUI.depth = -6;
            s = Screen.height / 1080f * 1.4f;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow, wordWrap = false };
                sub = new GUIStyle(big);
            }
            big.fontSize = Mathf.RoundToInt(34 * s); sub.fontSize = Mathf.RoundToInt(12 * s);
            float W = Screen.width / s, H = Screen.height / s;
            float y = HudLayout.CalloutY(H * .27f);
            for (int i = 0; i < lines.Count; i++)
            {
                float t = age - i * .35f; if (t < 0) break;
                float a = Pop(t, out float sc);
                var L = lines[i];
                Color c1 = L.bad ? Web.Hex("#ff3b3b") : Web.Hex("#ff4fa3"), c2 = L.bad ? Web.Hex("#ff9a3b") : Web.Hex("#a05ad6");
                var m = GUI.matrix;
                GUIUtility.ScaleAroundPivot(Vector2.one * sc, new Vector2(W / 2 * s, (y + 18) * s));
                var r = new Rect(0, y * s, Screen.width, 40 * s);
                // a dark drop, the second colour as an edge, then the face (the web game's gradient, in two layers)
                Label(new Rect(r.x, r.y + 3 * s, r.width, r.height), L.big, big, new Color(.06f, .07f, .08f, a));
                Label(new Rect(r.x - 1 * s, r.y, r.width, r.height), L.big, big, new Color(c2.r, c2.g, c2.b, a * .55f));
                Label(r, L.big, big, new Color(c1.r, c1.g, c1.b, a));
                GUI.matrix = m;
                y += 44;
                if (!string.IsNullOrEmpty(L.sub))
                {
                    var sz = sub.CalcSize(new GUIContent(L.sub.ToUpperInvariant()));
                    var rr = new Rect(Screen.width / 2f - sz.x / 2 - 9 * s, y * s, sz.x + 18 * s, 22 * s);
                    var o = GUI.color; GUI.color = new Color(.06f, .07f, .08f, a * .92f); GUI.DrawTexture(rr, Texture2D.whiteTexture); GUI.color = o;
                    Label(rr, L.sub.ToUpperInvariant(), sub, new Color(1, 1, 1, a));
                    y += 28;
                }
                y += 4;
            }
        }

        static void Label(Rect r, string t, GUIStyle st, Color c) { st.normal.textColor = c; GUI.Label(r, t, st); }

        // 1.9 s: 0-.15 grow from 60% to 112%, settle to 100% by .3, hold, fade over the last .5
        static float Pop(float t, out float scale)
        {
            if (t < .15f) scale = Mathf.Lerp(.6f, 1.12f, t / .15f);
            else if (t < .3f) scale = Mathf.Lerp(1.12f, 1f, (t - .15f) / .15f);
            else scale = 1;
            float a = t < .1f ? t / .1f : t > 1.4f ? Mathf.Clamp01(1 - (t - 1.4f) / .5f) : 1;
            return a;
        }

        /// remote test: call KillCalls.Test "DOUBLE KILL|sub|bad"
        public static string Test(string a)
        {
            if (!I) return "no KillCalls";
            var p = (a ?? "FIRST BLOOD").Split('|');
            I.lines.Clear(); I.lines.Add(new Line { big = p[0], sub = p.Length > 1 ? p[1] : null, bad = p.Length > 2 });
            I.lines.Add(new Line { big = "TRIPLE KILL" });
            I.shownAt = Time.unscaledTime; return "showing " + p[0];
        }

        // ------------------------------------------------------------------ for RoundManager.PickTargets (the web game's botTarget)
        public static float TargetBias(Fighter f, Fighter o, RoundManager rm)
        {
            float b = 0;
            if (f.grudge == o) b -= 60;
            // leadBias: up to 9 m of "closeness" for being ahead of the other foes
            int n = 0; float sum = 0;
            foreach (var x in rm.fighters) if (x && x != f && x.team != f.team) { n++; sum += ScoreOf(x, rm); }
            if (n >= 2) b -= Mathf.Clamp((ScoreOf(o, rm) - sum / n) * 3, 0, 9);
            return b;
        }
        static float ScoreOf(Fighter o, RoundManager rm) => rm.Deathmatch ? o.kills : (o.team >= 0 && o.team < rm.score.Length ? rm.score[o.team] : 0);
    }
}
