// Kill clips (match chat), from the web game's kill replays (RP / clipKill / openReplay / saveReplay):
//  - A rolling few seconds of exactly what you see (3D view, scope, first-person gun; no HUD) is kept as JPEG frames:
//    1280x720 at 30 fps on PC, 960x540 at 30 on phones, 640x360 at 15 in the browser. Frames are grabbed at the end of
//    URP's camera stack (CameraCaptureBridge), read back asynchronously and compressed on worker threads, so the game
//    thread never waits on the GPU or the encoder (the browser build reads back and encodes on the main thread).
//  - When a fighter goes down (or is killed outright) the frames from 3.2 s before to 1.2 s after are held back. When
//    someone is finally out, and you were in it (you got them, they got you, or you'd hit them), that moment plus the
//    last 4.5 s and ~0.6 s after becomes a clip.
//  - Only the best 4 clips of the match are kept: the winning shot first, then finishers, headshots and quick doubles.
//  - At match end a "Clips" button on the results screen lists them. The viewer plays one (Space / Pause, Esc / Close)
//    and "Save video" writes it: an .mp4 in the phone's gallery (Movies/imok), a Motion-JPEG .avi in Videos\imok on
//    a computer, a .webm download in the browser.
//  - While the viewer is open the rematch timer stops and Enter / the Rematch button do nothing.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace StreetLife
{
    [DefaultExecutionOrder(80)]
    public class KillClips : MonoBehaviour
    {
        public static KillClips I;
        /// the clip viewer is open (RoundManager holds the rematch, MainMenu doesn't pause, PlayerController doesn't grab the mouse)
        public static bool Viewing => I && (I.view != null || I.listOpen);
        /// the results screen owns the mouse (so clicks on the clip buttons don't lock it)
        public static bool WantsCursor => I && RoundManager.Instance && RoundManager.Instance.state == RoundManager.State.MatchEnd;
        public static bool Enabled = true;

        const float PRE = 3.2f, POST = 1.2f, TAIL = 4.5f, AFTER = .6f, KEEP = 6.5f;
        const int MAX_CLIPS = 4;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();   // RoundManager.Reboot runs this again after every scene reload
        static void Ensure()
        {
            if (I || !Application.isPlaying || !FindAnyObjectByType<MapBuilder>()) return;
            I = new GameObject("KillClips").AddComponent<KillClips>();
        }

        // ------------------------------------------------------------------ data
        public struct Frame { public float t, u; public byte[] jpg; }   // game time (for the windows), real time (for the playback rate)
        public struct Mark { public Fighter v; public float t; }
        public class Clip
        {
            public string label, sub; public List<byte[]> jpgs = new List<byte[]>(); public int fps, w, h, id;
            public float score, at, readyAt; public bool mine, ready; public Fighter v; public Mark? mk; public int round;
        }

        readonly List<Frame> ring = new List<Frame>(), hold = new List<Frame>();
        readonly List<Mark> marks = new List<Mark>();
        public readonly List<Clip> clips = new List<Clip>();
        readonly List<Clip> roundClips = new List<Clip>(), pending = new List<Clip>();
        readonly HashSet<Fighter> hitByMe = new HashSet<Fighter>();
        readonly Dictionary<Fighter, bool> wasDown = new Dictionary<Fighter, bool>();
        readonly Dictionary<Fighter, string> lastFin = new Dictionary<Fighter, string>();
        readonly Dictionary<Fighter, bool> lastHead = new Dictionary<Fighter, bool>();
        int seq;

        // capture
        int W, H, FPS, Q; bool async;
        RenderTexture rt; Camera capCam; bool registered, grabNow; float capAcc;
        readonly Queue<Vector2> inFlight = new Queue<Vector2>();
        readonly ConcurrentQueue<Frame> encoded = new ConcurrentQueue<Frame>();
        int busy;   // frames read back or encoding
        Texture2D readTex;   // main-thread readback (no async GPU readback, e.g. the browser)
        bool syncDue; float syncT, syncU;

        void Awake()
        {
            bool web = Application.platform == RuntimePlatform.WebGLPlayer;
            bool phone = Application.isMobilePlatform;
            if (web) { W = 640; H = 360; FPS = 15; Q = 75; }
            else if (phone) { W = 960; H = 540; FPS = 30; Q = 80; }
            else { W = 1280; H = 720; FPS = 30; Q = 85; }
            async = SystemInfo.supportsAsyncGPUReadback && !web;
        }

        void OnEnable()
        {
            Fighter.Killed += OnKilled;
            Combat.OnDamage += OnDamage;
            Finishers.Started += OnFinStart;
            RoundManager.RoundStarted += OnRound;
            RoundManager.RoundEnded += OnRoundEnded;
        }
        void OnDisable()
        {
            Fighter.Killed -= OnKilled;
            Combat.OnDamage -= OnDamage;
            Finishers.Started -= OnFinStart;
            RoundManager.RoundStarted -= OnRound;
            RoundManager.RoundEnded -= OnRoundEnded;
            Unregister();
            if (I == this) I = null;
        }
        void OnDestroy() { if (rt) { rt.Release(); Destroy(rt); } if (readTex) Destroy(readTex); if (viewTex) Destroy(viewTex); }

        // ------------------------------------------------------------------ events
        void OnRound(int r)
        {
            if (r <= 1) { clips.Clear(); pending.Clear(); seq = 0; CloseViewer(); listOpen = false; }
            roundClips.Clear(); marks.Clear(); hold.Clear(); hitByMe.Clear(); lastFin.Clear(); lastHead.Clear(); wasDown.Clear();
        }
        void OnDamage(Fighter v, float amt, Fighter src, bool head, string gun) { if (src && src == Combat.Me && v && v != src) hitByMe.Add(v); }
        void OnFinStart(Finisher f) { if (f != null && f.v) lastFin[f.v] = f.fx; }

        bool Involves(Fighter v, Fighter by) { var me = Combat.Me; return me && v && (by == me || v == me || hitByMe.Contains(v)); }

        void MarkDown(Fighter v, Fighter by)
        {
            if (!Involves(v, by)) return;
            marks.RemoveAll(m => m.v == v);
            marks.Add(new Mark { v = v, t = Time.time });
        }

        void OnKilled(Fighter v, Fighter by, bool head)
        {
            if (!v) return;
            lastHead[v] = head;
            if (!marks.Exists(m => m.v == v)) MarkDown(v, by);
            if (!Enabled || !Involves(v, by)) return;
            var me = Combat.Me; float now = Time.time;
            int mi = marks.FindIndex(m => m.v == v); Mark? mk = mi >= 0 ? marks[mi] : (Mark?)null;
            var frames = Pick(mk, now);
            if (frames.Count < 6) return;
            lastFin.TryGetValue(v, out var how);
            string nm(Fighter f) => f == me ? "You" : f.displayName;
            string label = by && by != v ? $"{nm(by)}  ›  {nm(v)}" : $"{nm(v)} out";
            bool mine = by == me && v != me;
            float score = mine ? 10 + (how != null ? 8 : 0) + (head ? 5 : 0) : v == me ? 1 : 3;
            if (mine) { var prev = clips.Find(c => c.mine && now - c.at < 5); if (prev != null) { score += 6; prev.score += 3; } }
            var rm = RoundManager.Instance;
            var clip = new Clip
            {
                label = label, sub = $"Round {(rm ? rm.round : 1)}{(how != null ? "  ·  " + how : "")}{(head ? "  ·  headshot" : "")}",
                fps = FPS, w = W, h = H, id = ++seq, score = score, mine = mine, at = now, v = v, mk = mk, round = rm ? rm.round : 1,
                readyAt = Time.unscaledTime + AFTER + .35f,
            };
            Keep(clip, frames);
            clips.Add(clip); roundClips.Add(clip); pending.Add(clip);
            KeepBest();
        }

        void OnRoundEnded(int round, int winner)
        {
            var me = Combat.Me; var rm = RoundManager.Instance;
            if (!me || winner < 0 || winner != me.team) return;
            bool final = rm && rm.matchOver;
            Clip c = null; for (int i = roundClips.Count - 1; i >= 0; i--) if (roundClips[i].mine) { c = roundClips[i]; break; }
            if (c == null) return;
            if (!clips.Contains(c)) clips.Add(c);
            c.score += final ? 40 : 12;
            if (final && !c.label.StartsWith("Winning")) c.label = "Winning shot  ·  " + c.label;
            KeepBest();
        }

        List<Frame> Pick(Mark? mk, float now)
        {
            var map = new SortedDictionary<float, Frame>();
            if (mk.HasValue)
            {
                float a = mk.Value.t - PRE, b = mk.Value.t + POST;
                foreach (var o in hold) if (o.t >= a && o.t <= b) map[o.t] = o;
                foreach (var o in ring) if (o.t >= a && o.t <= b) map[o.t] = o;
            }
            foreach (var o in ring) if (o.t >= now - TAIL && o.t <= now + AFTER) map[o.t] = o;
            return new List<Frame>(map.Values);
        }

        static void Keep(Clip c, List<Frame> fr)
        {
            if (fr.Count == 0) return;
            c.jpgs.Clear(); foreach (var f in fr) c.jpgs.Add(f.jpg);
            // play back at the rate the frames were really taken (a slow PC grabs fewer than 30 a second); slow motion stays slow
            float span = 0; for (int i = 1; i < fr.Count; i++) { float d = fr[i].u - fr[i - 1].u; if (d > 0 && d < .5f) span += d; }
            if (fr.Count > 6 && span > .1f) c.fps = Mathf.Clamp(Mathf.RoundToInt((fr.Count - 1) / span), 10, 60);
        }

        void KeepBest()
        {
            clips.Sort((a, b) => b.score != a.score ? b.score.CompareTo(a.score) : b.id.CompareTo(a.id));
            if (clips.Count > MAX_CLIPS) clips.RemoveRange(MAX_CLIPS, clips.Count - MAX_CLIPS);
            clips.Sort((a, b) => a.id.CompareTo(b.id));
        }

        bool Needed(float t) { foreach (var m in marks) if (t >= m.t - PRE && t <= m.t + POST) return true; return false; }

        void Trim()
        {
            float now = Time.time;
            marks.RemoveAll(m => now - m.t > 40);
            while (ring.Count > 1 && ring[ring.Count - 1].t - ring[0].t > KEEP)
            {
                var o = ring[0]; ring.RemoveAt(0);
                if (Needed(o.t)) hold.Add(o);
            }
            if (hold.Count > 0) hold.RemoveAll(o => !Needed(o.t));
        }

        // ------------------------------------------------------------------ capture
        bool Capturing
        {
            get
            {
                var rm = RoundManager.Instance;
                return Enabled && rm && Combat.Me && !MainMenu.Open && !MainMenu.Paused && view == null && !Crowning.Active
                    && (rm.state == RoundManager.State.Fight || rm.state == RoundManager.State.RoundEnd);
            }
        }

        void Register(Camera cam)
        {
            if (registered && capCam == cam) return;
            Unregister();
            if (!cam) return;
            if (!rt)
            {
                rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "KillClip", useMipMap = false, autoGenerateMips = false };
                rt.Create();
            }
            CameraCaptureBridge.AddCaptureAction(cam, OnCapture);
            capCam = cam; registered = true;
        }
        void Unregister()
        {
            if (registered && capCam) CameraCaptureBridge.RemoveCaptureAction(capCam, OnCapture);
            registered = false; capCam = null;
        }

        // runs inside URP at the end of the camera stack: the final image with post-processing, before the IMGUI HUD
        void OnCapture(RenderTargetIdentifier src, CommandBuffer cmd)
        {
            if (!grabNow || !rt) return;
            grabNow = false;
            float aspect = capCam ? capCam.aspect : (float)Screen.width / Screen.height, tw = 16f / 9f;
            Vector2 sc = Vector2.one, off = Vector2.zero;
            if (aspect > tw) { sc.x = tw / aspect; off.x = (1 - sc.x) / 2; } else { sc.y = aspect / tw; off.y = (1 - sc.y) / 2; }
            cmd.Blit(src, rt, sc, off);
            if (async)
            {
                inFlight.Enqueue(new Vector2(Time.time, Time.unscaledTime)); busy++;
                cmd.RequestAsyncReadback(rt, 0, TextureFormat.RGBA32, OnReadback);
            }
            else { syncDue = true; syncT = Time.time; syncU = Time.unscaledTime; }
        }

        void OnReadback(AsyncGPUReadbackRequest req)
        {
            var tu = inFlight.Count > 0 ? inFlight.Dequeue() : new Vector2(Time.time, Time.unscaledTime); float t = tu.x, u = tu.y;
            if (req.hasError) { busy--; return; }
            var data = req.GetData<byte>();
            var px = new byte[data.Length]; data.CopyTo(px);
            int w = W, h = H, q = Q;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                byte[] jpg = null;
                try { jpg = ImageConversion.EncodeArrayToJPG(px, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, q); } catch { }
                encoded.Enqueue(new Frame { t = t, u = u, jpg = jpg });
            });
        }

        void Update()
        {
            // finished encodes into the ring, in time order
            while (encoded.TryDequeue(out var f))
            {
                busy = Mathf.Max(0, busy - 1);
                if (f.jpg == null) continue;
                int i = ring.Count; while (i > 0 && ring[i - 1].t > f.t) i--;
                ring.Insert(i, f);
            }
            if (ring.Count > 0) Trim();

            // clips waiting for their last frames
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var c = pending[i];
                if (Time.unscaledTime < c.readyAt) continue;
                var fr = Pick(c.mk, c.at); if (fr.Count >= c.jpgs.Count) Keep(c, fr);
                c.ready = true; pending.RemoveAt(i);
                if (c.mk.HasValue) { var m = c.mk.Value; marks.RemoveAll(x => x.v == m.v && x.t == m.t); }
                Trim();
            }

            // the down moment of anyone (polling: Finishers has no "went down" event)
            foreach (var f in Combat.Fighters)
            {
                if (!f) continue;
                wasDown.TryGetValue(f, out bool was);
                if (f.downed && !was) MarkDown(f, f.downedBy);
                wasDown[f] = f.downed;
            }

            var rm = RoundManager.Instance;
            if (Capturing)
            {
                var cam = Camera.main;
                Register(cam);
                capAcc += Time.unscaledDeltaTime;
                if (capAcc >= 1f / FPS) { capAcc = 0; if (busy <= 3) grabNow = true; }
            }
            else { if (registered) Unregister(); grabNow = false; }

            // results screen: the mouse is free for the clip buttons
            if (rm && rm.state == RoundManager.State.MatchEnd && !Application.isMobilePlatform && Cursor.lockState != CursorLockMode.None)
            { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (!rm || rm.state != RoundManager.State.MatchEnd) { listOpen = false; if (view != null) CloseViewer(); }

            TapUpdate();
            KeysUpdate();
            ViewUpdate();
            SaveUpdate();
        }

        void LateUpdate()
        {
            // main-thread readback (no async GPU readback): the frame grabbed during the last render
            if (!syncDue || !rt) return;
            syncDue = false;
            if (!readTex) readTex = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            readTex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
            RenderTexture.active = prev;
            var jpg = readTex.EncodeToJPG(Q);
            int i = ring.Count; while (i > 0 && ring[i - 1].t > syncT) i--;
            ring.Insert(i, new Frame { t = syncT, u = syncU, jpg = jpg });
            Trim();
        }

        // ------------------------------------------------------------------ results screen and viewer
        bool listOpen;
        Clip view; int viewI; float viewAcc; bool playing = true; Texture2D viewTex; int shownI = -1;
        string saveMsg; float saveMsgT;

        public void OpenViewer(Clip c)
        {
            if (c == null) return;
            view = c; viewI = 0; viewAcc = 0; playing = true; shownI = -1;
            if (!viewTex) viewTex = new Texture2D(2, 2, TextureFormat.RGB24, false, false) { name = "KillClipView" };
        }
        public void CloseViewer() { view = null; shownI = -1; }

        void KeysUpdate()
        {
            var kb = Keyboard.current; if (kb == null) return;
            var rm = RoundManager.Instance; if (!rm || rm.state != RoundManager.State.MatchEnd) return;
            if (view == null && listOpen && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) { listOpen = false; return; }
            if (view != null)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) CloseViewer();
                else if (kb.spaceKey.wasPressedThisFrame) TogglePlay();
                else if (kb.sKey.wasPressedThisFrame) Save();
                return;
            }
            for (int i = 0; i < clips.Count && i < 9; i++)
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) { OpenViewer(clips[i]); return; }
            if (kb.cKey.wasPressedThisFrame && clips.Count > 0) listOpen = !listOpen;
        }

        void TogglePlay() { playing = !playing; if (playing && view != null && viewI >= view.jpgs.Count - 1) viewI = 0; }

        void ViewUpdate()
        {
            if (view == null) return;
            if (!view.ready) return;
            if (playing && view.jpgs.Count > 0)
            {
                viewAcc += Mathf.Min(.25f, Time.unscaledDeltaTime);
                float step = 1f / Mathf.Max(1, view.fps);
                while (viewAcc >= step) { viewAcc -= step; viewI++; if (viewI >= view.jpgs.Count) viewI = 0; }
            }
            if (viewI != shownI && viewI < view.jpgs.Count) { viewTex.LoadImage(view.jpgs[viewI], false); shownI = viewI; }
        }

        float s;
        GUIStyle lbl, small, btn, btnHi; Texture2D white;
        Rect R(float x, float y, float w, float h) => new Rect(x * s, y * s, w * s, h * s);

        void Styles()
        {
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            int fs(int n) => Mathf.RoundToInt(n * s);
            lbl = new GUIStyle(GUI.skin.label) { fontSize = fs(18), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            small = new GUIStyle(GUI.skin.label) { fontSize = fs(13), alignment = TextAnchor.MiddleLeft };
            btn = new GUIStyle(GUI.skin.label) { fontSize = fs(14), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            btnHi = new GUIStyle(btn);
            lbl.normal.textColor = Color.white; small.normal.textColor = new Color(1, 1, 1, .75f);
            btn.normal.textColor = Color.white; btnHi.normal.textColor = Color.black;
        }

        // taps are read straight from the touch screen as well (IMGUI's own touch handling varies by device); a press counts once
        Vector2? tap; float lastFire;
        void TapUpdate()
        {
            var ts = UnityEngine.InputSystem.Touchscreen.current;
            tap = null;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame) { var p = ts.primaryTouch.position.ReadValue(); tap = new Vector2(p.x, Screen.height - p.y); }
        }

        bool Hit(Rect r)
        {
            bool hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (!hit && tap.HasValue && r.Contains(tap.Value)) { tap = null; hit = true; }
            if (!hit || Time.unscaledTime - lastFire < .3f) return false;
            lastFire = Time.unscaledTime; return true;
        }

        bool Button(Rect r, string text, bool primary = false)
        {
            bool over = r.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                HudRound.Fill(r, primary ? new Color(1f, .85f, .3f, over ? 1 : .92f) : new Color(1, 1, 1, over ? .26f : .16f), white);
                GUI.Label(r, text, primary ? btnHi : btn);
            }
            return Hit(r);
        }

        void OnGUI()
        {
            var rm = RoundManager.Instance;
            if (!rm || rm.state != RoundManager.State.MatchEnd || MainMenu.Open || MainMenu.Paused) return;
            GUI.depth = -20;   // over the HUD, the XP panel and the finisher HUD
            s = Screen.height / 1080f * 1.4f;
            Styles();
            float Wd = Screen.width / s, Hd = Screen.height / s;
            if (view != null) { Viewer(Wd, Hd); return; }
            if (clips.Count == 0) return;

            bool touch = HudLayout.Touch;
            if (!listOpen)
            {
                Rect b;
                if (touch)
                {   // beside the Rematch pill (TouchControls: 190 x 50 CSS px, centre 72 px up from the bottom of the safe area)
                    var sa = Screen.safeArea; float S = Mathf.Max(1f, Mathf.Min(sa.height / 360f, sa.width / 640f));
                    b = new Rect(sa.center.x - 95 * S - 16 * S - 150 * S, Screen.height - sa.yMin - 97 * S, 150 * S, 50 * S);
                }
                else b = R(Wd / 2 - 100, Crowning.Active ? Hd - 60 : Hd / 2 + 262, 200, 38);   // under the XP panel (crowning: at the bottom)
                if (Button(b, $"Clips ({clips.Count})" + (touch ? "" : "   ·   C"))) listOpen = true;
                return;
            }
            // the list: a small panel in the middle, over the results
            var o2 = GUI.color; GUI.color = new Color(0, 0, 0, .6f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white); GUI.color = o2;
            float pw = Mathf.Min(560, Wd - 40), rowH = 44, ph = 70 + clips.Count * (rowH + 8) + 54;
            float x = Wd / 2 - pw / 2, y = Hd / 2 - ph / 2;
            HudRound.Fill(R(x, y, pw, ph), new Color(.08f, .09f, .12f, .96f), white);
            GUI.Label(R(x + 20, y + 14, pw - 40, 30), "Clips from this match", lbl);
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i]; float ry = y + 56 + i * (rowH + 8);
                if (Button(R(x + 20, ry, pw - 40, rowH), (touch ? "" : (i + 1) + "    ") + c.label)) { OpenViewer(c); return; }
            }
            if (Button(R(x + pw / 2 - 70, y + ph - 50, 140, 38), "Back")) listOpen = false;
        }

        void Viewer(float Wd, float Hd)
        {
            var old = GUI.color; GUI.color = new Color(0, 0, 0, .82f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white); GUI.color = old;
            float fw = Mathf.Min(Wd - 80, (Hd - 190) * 16f / 9f), fh = fw * 9f / 16f;
            float fx = Wd / 2 - fw / 2, fy = Mathf.Max(70, Hd / 2 - fh / 2 - 10);
            // title and close
            GUI.Label(R(fx, fy - 58, fw - 140, 30), view.label, lbl);
            string sub = view.ready ? view.sub : view.sub + "  ·  preparing";
            GUI.Label(R(fx, fy - 30, fw - 140, 24), sub, small);
            if (Button(R(fx + fw - 120, fy - 52, 120, 38), "Close")) { CloseViewer(); return; }
            // the frame
            HudRound.Fill(R(fx - 4, fy - 4, fw + 8, fh + 8), new Color(1, 1, 1, .1f), white);
            if (view.ready && shownI >= 0) GUI.DrawTexture(R(fx, fy, fw, fh), viewTex, ScaleMode.StretchToFill, false);
            // tap the picture to pause
            if (Hit(R(fx, fy, fw, fh))) TogglePlay();
            // play, progress, save
            float by = fy + fh + 14;
            if (Button(R(fx, by, 110, 38), playing ? "Pause" : "Play")) TogglePlay();
            float px = fx + 124, pw = fw - 124 - 190;
            HudRound.Fill(R(px, by + 16, pw, 6), new Color(1, 1, 1, .15f), white);
            float k = view.jpgs.Count > 0 ? (viewI + 1f) / view.jpgs.Count : 0;
            HudRound.Fill(R(px, by + 16, Mathf.Max(6, pw * k), 6), new Color(1f, .85f, .3f), white);
            string sl = saving ? $"Saving {Mathf.RoundToInt(saveProg * 100)}%" : "Save video";
            if (Button(R(fx + fw - 176, by, 176, 38), sl, true) && !saving) Save();
            if (!string.IsNullOrEmpty(saveMsg) && Time.unscaledTime - saveMsgT < 6)
                GUI.Label(R(fx, by + 44, fw, 24), saveMsg, new GUIStyle(small) { alignment = TextAnchor.MiddleRight });
            if (!HudLayout.Touch) GUI.Label(R(fx, by + 44, fw, 24), "Space pause   ·   S save   ·   Esc close", small);
        }

        // ------------------------------------------------------------------ saving
        bool saving; float saveProg;
        ClipSaver.Job job;

        void Save()
        {
            if (view == null || !view.ready || saving || view.jpgs.Count == 0) return;
            string name = "imok-replay-" + System.Text.RegularExpressions.Regex.Replace(view.label.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-') + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            job = ClipSaver.Start(view.jpgs, view.w, view.h, view.fps, name);
            saving = job != null; saveProg = 0;
            if (!saving) { saveMsg = "Saving video isn't supported here"; saveMsgT = Time.unscaledTime; }
        }

        void SaveUpdate()
        {
            if (!saving || job == null) return;
            job.Poll();
            saveProg = job.progress;
            if (!job.done) return;
            saving = false;
            saveMsg = job.error != null ? "Couldn't save: " + job.error : "Saved: " + job.where;
            saveMsgT = Time.unscaledTime;
            job = null;
        }

        // ------------------------------------------------------------------ remote test
        /// call KillClips.Test [state|list|open N|save N|fake]
        public static string Test(string a)
        {
            if (!I) return "no KillClips";
            var p = (a ?? "").Trim().Split(' ');
            switch (p[0])
            {
                case "list":
                    var sb = new System.Text.StringBuilder($"{I.clips.Count} clips: ");
                    foreach (var c in I.clips) sb.Append($"[{c.id} '{c.label}' {c.sub} n{c.jpgs.Count} score {c.score} ready {c.ready}] ");
                    return sb.ToString();
                case "open": { int i = p.Length > 1 ? int.Parse(p[1]) : 0; if (i >= I.clips.Count) return "no clip " + i; I.OpenViewer(I.clips[i]); return "viewing " + I.clips[i].label; }
                case "save": { int i = p.Length > 1 ? int.Parse(p[1]) : 0; if (i >= I.clips.Count) return "no clip " + i; I.OpenViewer(I.clips[i]); I.clips[i].ready = true; I.Save(); return "saving " + I.clips[i].label; }
                case "avi":
                    {   // write clip N as Logs/clip.avi (checks the writer without touching the Videos folder)
                        int i = p.Length > 1 ? int.Parse(p[1]) : 0; if (i >= I.clips.Count) return "no clip " + i;
                        var path = Path.Combine(Application.dataPath, "../Logs/clip.avi"); var c = I.clips[i];
                        ClipSaver.Avi.Write(path, c.jpgs, c.w, c.h, c.fps, null); return "wrote " + path;
                    }
                case "savestate": return I.saving ? $"saving {I.saveProg:P0}" : "idle; " + I.saveMsg;
                case "frame":
                    {   // write the newest ring frame to Logs/clipframe.jpg
                        if (I.ring.Count == 0) return "ring empty";
                        var path = Path.Combine(Application.dataPath, "../Logs/clipframe.jpg");
                        File.WriteAllBytes(path, I.ring[I.ring.Count - 1].jpg); return "wrote " + path + " " + I.ring[I.ring.Count - 1].jpg.Length + " bytes";
                    }
                default:
                    long bytes = 0; foreach (var f in I.ring) bytes += f.jpg.Length;
                    return $"ring {I.ring.Count} frames ({bytes / 1024} KB), hold {I.hold.Count}, marks {I.marks.Count}, clips {I.clips.Count}, capturing {I.Capturing} async {I.async} {I.W}x{I.H}@{I.FPS} busy {I.busy}";
            }
        }
    }

    // ====================================================================== writing a clip to a video file
    public static class ClipSaver
    {
        public abstract class Job
        {
            public float progress; public bool done; public string error, where;
            public virtual void Poll() { }
        }

        public static Job Start(List<byte[]> jpgs, int w, int h, int fps, string name)
        {
            var frames = new List<byte[]>(jpgs);
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidJob(frames, w, h, fps, name);
#elif UNITY_WEBGL && !UNITY_EDITOR
            return new WebJob(frames, w, h, fps, name);
#else
            return new AviJob(frames, w, h, fps, name);
#endif
        }

        // ---------------------------------------------------------------- computers: Motion-JPEG in an AVI (plays in Windows' Media Player, VLC, editors)
        class AviJob : Job
        {
            public AviJob(List<byte[]> frames, int w, int h, int fps, string name)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "imok");
                if (string.IsNullOrEmpty(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos))) dir = Path.Combine(Application.persistentDataPath, "Videos");
                string path = Path.Combine(dir, name + ".avi");
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                        Avi.Write(path, frames, w, h, fps, p => progress = p);
                        where = path;
                    }
                    catch (Exception e) { error = e.Message; }
                    done = true;
                });
            }
        }

        public static class Avi
        {
            static void FourCC(BinaryWriter b, string s) { foreach (char c in s) b.Write((byte)c); }
            public static void Write(string path, List<byte[]> frames, int w, int h, int fps, Action<float> prog)
            {
                int n = frames.Count, maxF = 0; foreach (var f in frames) maxF = Mathf.Max(maxF, f.Length);
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var b = new BinaryWriter(fs))
                {
                    FourCC(b, "RIFF"); long riffSize = fs.Position; b.Write(0); FourCC(b, "AVI ");
                    // header list
                    FourCC(b, "LIST"); b.Write(4 + 8 + 56 + 8 + 4 + 8 + 56 + 8 + 40); FourCC(b, "hdrl");
                    FourCC(b, "avih"); b.Write(56);
                    b.Write(1000000 / fps); b.Write(maxF * fps); b.Write(0); b.Write(0x10); b.Write(n); b.Write(0); b.Write(1); b.Write(maxF); b.Write(w); b.Write(h);
                    b.Write(0); b.Write(0); b.Write(0); b.Write(0);
                    FourCC(b, "LIST"); b.Write(4 + 8 + 56 + 8 + 40); FourCC(b, "strl");
                    FourCC(b, "strh"); b.Write(56);
                    FourCC(b, "vids"); FourCC(b, "MJPG"); b.Write(0); b.Write((short)0); b.Write((short)0); b.Write(0);
                    b.Write(1); b.Write(fps); b.Write(0); b.Write(n); b.Write(maxF); b.Write(-1); b.Write(0);
                    b.Write((short)0); b.Write((short)0); b.Write((short)w); b.Write((short)h);
                    FourCC(b, "strf"); b.Write(40);
                    b.Write(40); b.Write(w); b.Write(h); b.Write((short)1); b.Write((short)24); FourCC(b, "MJPG"); b.Write(w * h * 3); b.Write(0); b.Write(0); b.Write(0); b.Write(0);
                    // frames
                    FourCC(b, "LIST"); long moviSize = fs.Position; b.Write(0); long moviStart = fs.Position; FourCC(b, "movi");
                    var offs = new int[n];
                    for (int i = 0; i < n; i++)
                    {
                        offs[i] = (int)(fs.Position - moviStart);
                        FourCC(b, "00dc"); b.Write(frames[i].Length); b.Write(frames[i]);
                        if ((frames[i].Length & 1) == 1) b.Write((byte)0);
                        prog?.Invoke((i + 1f) / n);
                    }
                    long moviEnd = fs.Position;
                    // index
                    FourCC(b, "idx1"); b.Write(16 * n);
                    for (int i = 0; i < n; i++) { FourCC(b, "00dc"); b.Write(0x10); b.Write(offs[i]); b.Write(frames[i].Length); }
                    long end = fs.Position;
                    fs.Position = moviSize; b.Write((int)(moviEnd - moviStart));
                    fs.Position = riffSize; b.Write((int)(end - 8));
                }
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // ---------------------------------------------------------------- phones: H.264 .mp4 into the gallery (Plugins/Android/ClipEncoder.java)
        class AndroidJob : Job
        {
            AndroidJavaObject enc;
            public AndroidJob(List<byte[]> frames, int w, int h, int fps, string name)
            {
                try
                {
                    var act = new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity");
                    enc = new AndroidJavaObject("com.postworkculture.streetlife.ClipEncoder", act, w, h, fps, name);
                    foreach (var f in frames) enc.Call("add", f);
                    enc.Call("finish");
                }
                catch (Exception e) { error = e.Message; done = true; }
            }
            public override void Poll()
            {
                if (done || enc == null) return;
                string st = enc.Call<string>("state");
                if (st.StartsWith("working:")) { float.TryParse(st.Substring(8), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out progress); return; }
                if (st.StartsWith("done:")) { where = st.Substring(5); done = true; }
                else if (st.StartsWith("error:")) { error = st.Substring(6); done = true; }
            }
        }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        // ---------------------------------------------------------------- browser: .webm download through MediaRecorder (Plugins/WebGL/StreetLifeClips.jslib)
        [DllImport("__Internal")] static extern void SLClipBegin(int fps);
        [DllImport("__Internal")] static extern void SLClipFrame(byte[] data, int len);
        [DllImport("__Internal")] static extern void SLClipEnd(string name);
        [DllImport("__Internal")] static extern string SLClipState();
        class WebJob : Job
        {
            public WebJob(List<byte[]> frames, int w, int h, int fps, string name)
            {
                SLClipBegin(fps);
                foreach (var f in frames) SLClipFrame(f, f.Length);
                SLClipEnd(name);
            }
            public override void Poll()
            {
                string st = SLClipState() ?? "";
                if (st.StartsWith("working:")) { float.TryParse(st.Substring(8), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out progress); return; }
                if (st.StartsWith("done")) { where = "your downloads"; done = true; }
                else if (st.StartsWith("error")) { error = st.Length > 6 ? st.Substring(6) : "browser can't record video"; done = true; }
            }
        }
#endif
    }
}
