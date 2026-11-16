// The throwables wheel (menu chat, 15 Nov). Hold E (phones: hold the Item button) and a wheel pops up with everything
// you can throw or place: your lethal, the shop grenades (gold one included) and the Sentry, each with its picture and
// how many you have; empty ones are greyed. Move the mouse (or slide your thumb) towards one and let go: it's selected
// and ready in your hand, with the throw arc showing. Then click / tap Fire, or tap E / Item, to throw it.
// A quick tap of E or Item (no wheel) throws whatever is selected straight away. T and V still work as before.
// Swapping weapons (1, 2, wheel) or reloading puts it away. Hooks: WeaponInput (E), WeaponWorld (the Item button), WeaponHud (items list).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace StreetLife
{
    [DefaultExecutionOrder(-8)]   // after PlayerController (-10) and TouchControls (-9) set the trigger, before Weapons (-5) fires
    public class ThrowWheel : MonoBehaviour
    {
        public static ThrowWheel I;
        public string sel = "lethal";       // "lethal", "nade" or "sentry"
        public bool open, ready;
        Weapons w; PlayerController pc; bool pcWas;
        GUIStyle bigSt;
        bool holding, touchHold; float holdT; int touchFinger = -1; Vector2 touchStart, pick; int hover = -1;
        bool swallowFire, fireWas;
        const float HoldToOpen = .22f;

        public class Item { public string id, name, iconId; public int count; public bool avail; public Color tint = Color.white; }
        readonly List<Item> items = new List<Item> { new Item { id = "lethal" }, new Item { id = "nade" }, new Item { id = "sentry" } };

        void Awake() { I = this; w = GetComponent<Weapons>(); pc = GetComponent<PlayerController>(); }
        void OnDestroy() { if (I == this) I = null; if (open) Close(false); }

        // ------------------------------------------------------------------ what's in the wheel
        void Refresh()
        {
            var c = GunsDb.Cons(w.lethal);
            var L = items[0]; L.name = c != null ? c.name : "Lethal"; L.count = w.consN; L.avail = !Rules.Rumble && w.consN > 0; L.iconId = w.lethal; L.tint = Color.white;
            var N = items[1]; bool gold = w.goldNade > 0; N.name = gold ? "Gold grenade" : "Shop grenades"; N.count = w.nades + w.goldNade; N.avail = !Rules.Rumble && N.count > 0;
            N.iconId = "grenade"; N.tint = gold ? new Color(1f, .82f, .25f) : Color.white;
            var S = items[2]; S.name = "Sentry"; S.count = w.turretKit; S.avail = w.turretKit > 0; S.iconId = "sentry"; S.tint = Color.white;
        }
        Item Of(string id) { foreach (var it in items) if (it.id == id) return it; return items[0]; }
        public static string Label { get { if (!I) return null; I.Refresh(); var it = I.Of(I.sel); return it.id == "lethal" ? ShortOf(I.w.lethal) : it.id == "nade" ? "Nade" : "Sentry"; } }
        static string ShortOf(string id) { var c = GunsDb.Cons(id); return c != null ? c.@short : "Item"; }

        // ------------------------------------------------------------------ input
        /// WeaponInput: the E key each frame
        public void KeyE(bool down, bool held, bool up)
        {
            if (down) { holding = true; touchHold = false; holdT = 0; pick = Vector2.zero; hover = -1; }
            if (up && holding) Release();
        }

        /// WeaponWorld: the Item button
        public void TouchItem(bool down, int finger)
        {
            if (down) { holding = true; touchHold = true; holdT = 0; pick = Vector2.zero; hover = -1; touchFinger = finger; touchStart = FingerPos(finger) ?? Vector2.zero; }
            else if (holding) Release();
        }

        static Vector2? FingerPos(int id)
        {
            try { foreach (var t in ETouch.activeTouches) if (t.touchId == id) return t.screenPosition; } catch { }
            return null;
        }

        void Update()
        {
            if (!w || !GunsDb.Loaded) return;
            var f = w.F;
            if (w.Busy || WeaponHud.MenuOpen || MainMenu.Open || MainMenu.Paused || f.Frozen && !open) { if (open) Close(false); holding = false; ready = false; return; }
            Refresh();
            if (holding && holdT > .1f) foreach (var it in items) ThrowIcons.Get(it.iconId);   // draw the pictures before the wheel opens (never inside OnGUI)
            if (holding)
            {
                holdT += Time.unscaledDeltaTime;
                if (!open && holdT >= HoldToOpen) OpenWheel();
                if (open)
                {
                    if (touchHold) { var p = FingerPos(touchFinger); if (p.HasValue) pick = (p.Value - touchStart) / (70f * Mathf.Max(1, Screen.height / 720f)); }
                    else { var ms = Mouse.current; if (ms != null) pick += ms.delta.ReadValue() * .012f; }
                    if (pick.magnitude > 1.4f) pick = pick.normalized * 1.4f;
                    hover = pick.magnitude > .35f ? Sector(pick) : -1;
                    f.inFire = false; f.ads = false; f.inFwd = f.inSide = 0;
                }
            }
            // put away on swap or reload
            var kb = Keyboard.current;
            if (ready && kb != null && (kb.digit1Key.wasPressedThisFrame || kb.digit2Key.wasPressedThisFrame || kb.rKey.wasPressedThisFrame)) ready = false;
            if (ready && !Of(sel).avail) ready = false;
            // ready in hand: the trigger throws instead of firing (mouse or the touch Fire button)
            bool fire = f.inFire;
            if (ready && fire && !fireWas) { Throw(); swallowFire = true; }
            if (swallowFire) { if (fire) f.inFire = false; else swallowFire = false; }
            fireWas = fire;
            // the throw arc while it's ready (Weapons.PreviewThrow)
            if (!open) { w.aimThrow = ready && sel == "lethal" && Projectiles.Aimed.Contains(w.lethal) && w.ConsReady(); w.aimNade = ready && sel == "nade"; }
        }

        /// the slot under a direction: 0 at the top, then clockwise (3 slots, 120 degrees each)
        static int Sector(Vector2 d)
        {
            float a = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; if (a < 0) a += 360;   // 0 = up, clockwise (screen y up)
            return Mathf.FloorToInt(((a + 60) % 360) / 120f);
        }

        void OpenWheel()
        {
            open = true; ready = false; w.aimThrow = w.aimNade = false;
            if (pc) { pcWas = pc.enabled; pc.enabled = false; }   // the mouse picks a slot instead of turning you
        }

        void Close(bool pickIt)
        {
            open = false;
            if (pc) pc.enabled = pcWas || pc.enabled;
            if (pickIt && hover >= 0 && hover < items.Count && items[hover].avail) { sel = items[hover].id; ready = true; }
        }

        void Release()
        {
            holding = false;
            if (open) { Close(true); return; }
            Throw();   // a quick tap: throw what's selected straight away
        }

        void Throw()
        {
            var f = w.F; bool ok = false;
            switch (sel)
            {
                case "lethal": ok = w.UseCons(); break;
                case "nade": ok = w.ThrowNade(); break;
                case "sentry": if (w.turretKit > 0 && !w.Busy && !f.Frozen && Turrets.Deploy(f)) { w.turretKit--; ok = true; } break;
            }
            ready = false; w.aimThrow = w.aimNade = false;
            if (!ok && sel != "lethal" && !Of(sel).avail) sel = "lethal";   // ran out: back to the lethal
        }

        // ------------------------------------------------------------------ drawing
        GUIStyle nameSt, countSt, hintSt;
        void OnGUI()
        {
            if (!w || (!open && !ready)) return;
            float s = Screen.height / 1080f * 1.4f;
            if (nameSt == null)
            {
                nameSt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
                countSt = new GUIStyle(nameSt); hintSt = new GUIStyle(nameSt);
            }
            nameSt.fontSize = Mathf.RoundToInt(13 * s); countSt.fontSize = Mathf.RoundToInt(15 * s); hintSt.fontSize = Mathf.RoundToInt(12 * s);
            nameSt.normal.textColor = countSt.normal.textColor = Color.white; hintSt.normal.textColor = new Color(1, 1, 1, .8f);
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            if (!open)
            {   // ready in hand: a small reminder under the crosshair
                var it = Of(sel);
                GUI.Label(new Rect(cx - 200 * s, cy + 46 * s, 400 * s, 20 * s), it.name + " ready  ·  " + (HudLayout.Touch ? "tap Fire to throw" : "click or E to throw"), hintSt);
                return;
            }
            GUI.depth = -15;
            float R = 118 * s, cw = 112 * s, ch = 104 * s;
            if (hover >= 0 && hover < items.Count)
            {
                if (bigSt == null || bigSt.fontSize != Mathf.RoundToInt(20 * s)) bigSt = new GUIStyle(nameSt) { fontSize = Mathf.RoundToInt(20 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                var hi = items[hover]; var oc = GUI.color; GUI.color = hi.avail ? Color.white : new Color(1, 1, 1, .5f);
                GUI.Label(new Rect(cx - 90 * s, cy - 16 * s, 180 * s, 32 * s), hi.avail ? hi.name : hi.name + " (none)", bigSt); GUI.color = oc;
            }
            // a soft dark disc behind
            GUI.DrawTexture(new Rect(cx - R - cw * .62f, cy - R - ch * .62f, 2 * (R + cw * .62f), 2 * (R + ch * .62f)), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0, 0, 0, .45f), 0, R + cw * .62f);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i]; float a = i * 120f * Mathf.Deg2Rad;
                var r = new Rect(cx + Mathf.Sin(a) * R - cw / 2, cy - Mathf.Cos(a) * R - ch / 2, cw, ch);
                bool on = i == hover, cur = it.id == sel;
                // 16 Nov: the slot you point at grows, gets a thick white frame and its name is written large in the middle; the others dim
                if (on) { float g = 10 * s; r = new Rect(r.x - g, r.y - g, r.width + 2 * g, r.height + 2 * g); GUI.DrawTexture(new Rect(r.x - 4 * s, r.y - 4 * s, r.width + 8 * s, r.height + 8 * s), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, it.avail ? Color.white : new Color(1, 1, 1, .4f), 0, 14 * s); }
                var old = GUI.color; GUI.color = new Color(1, 1, 1, !it.avail ? .35f : hover >= 0 && !on ? .5f : 1);
                HudDraw.Card(r, on && it.avail, false, s);
                var ic = ThrowIcons.Cached(it.iconId);
                var ir = new Rect(r.x + 8 * s, r.y + 6 * s, r.width - 16 * s, r.height - 46 * s);
                if (ic) { var o2 = GUI.color; GUI.color = new Color(it.tint.r, it.tint.g, it.tint.b, GUI.color.a); GUI.DrawTexture(ir, ic, ScaleMode.ScaleToFit, true); GUI.color = o2; }
                else GUI.Label(ir, it.id == "lethal" && w.lethal == "sense" ? "♥" : "?", countSt);
                GUI.Label(new Rect(r.x + 2 * s, r.yMax - 40 * s, r.width - 4 * s, 18 * s), it.name, nameSt);
                GUI.Label(new Rect(r.x + 2 * s, r.yMax - 22 * s, r.width - 4 * s, 18 * s), "x" + it.count, countSt);
                if (cur) GUI.DrawTexture(new Rect(r.center.x - 3 * s, r.yMax + 5 * s, 6 * s, 6 * s), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Color.white, 0, 3 * s);   // the one you have now
                GUI.color = old;
            }
            if (hover < 0) GUI.Label(new Rect(cx - 120 * s, cy - 10 * s, 240 * s, 20 * s), HudLayout.Touch ? "slide to pick, let go" : "move to pick, let go of E", hintSt);
        }
    }

    /// pictures of the throwables for the wheel: each model rendered once, side-on, into a small texture (layer 28, like MenuPortraits)
    public static class ThrowIcons
    {
        static readonly Dictionary<string, Texture2D> pics = new Dictionary<string, Texture2D>();
        static Camera cam; static RenderTexture rt; static Light key, fill; static Transform studio;
        const int W = 192, H = 160;

        public static Texture2D Cached(string id) => id != null && pics.TryGetValue(id, out var t) ? t : null;
        public static Texture2D Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (pics.TryGetValue(id, out var t)) return t;
            pics[id] = t = Make(id); return t;
        }

        static Texture2D Make(string id)
        {
            if (!GunsDb.Loaded) return null;
            WNode[] nodes = id == "sentry" ? (GunsDb.Data.turret != null ? GunsDb.Data.turret.nodes : null) : GunsDb.Proj(id) != null ? GunsDb.Proj(id).nodes : null;
            if (nodes == null) return null;
            try
            {
                if (!cam)
                {
                    studio = new GameObject("throw icons").transform; studio.position = new Vector3(600, -4000, 0); Object.DontDestroyOnLoad(studio.gameObject);
                    rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "throw icon" };
                    var cg = new GameObject("icon camera"); cg.transform.SetParent(studio, false);
                    cam = cg.AddComponent<Camera>(); cam.enabled = false; cam.cullingMask = 1 << MenuPortraits.Layer; cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color32(26, 26, 26, 255);   // the card colour (HudDraw.Tile) cam.orthographic = true; cam.nearClipPlane = .01f; cam.farClipPlane = 20; cam.targetTexture = rt; cam.aspect = (float)W / H;
                    var d = cg.AddComponent<UniversalAdditionalCameraData>(); d.renderPostProcessing = false; d.renderShadows = false;
                    key = Lamp("key", 6f, new Color(1, .97f, .93f)); fill = Lamp("fill", 3f, new Color(.85f, .9f, 1f));
                    var mc = Camera.main; if (mc) mc.cullingMask &= ~(1 << MenuPortraits.Layer);
                }
                var go = new GameObject("icon " + id); go.transform.SetParent(studio, false);
                var rends = new List<Renderer>();
                WeaponModels.Build(nodes, go.transform, id, MenuPortraits.Layer, false, rends);
                var fl = WeaponModels.Find(go.transform, "flame"); if (fl) fl.gameObject.SetActive(false);
                go.transform.localRotation = Quaternion.Euler(18, -35, 0);
                var bb = new Bounds(); bool any = false;
                // frame on the solid parts only (glows, trails and see-through bits can be much bigger than the thing itself)
                foreach (var r in rends) if (r && r.gameObject.activeInHierarchy && r.sharedMaterial && r.sharedMaterial.renderQueue < 2500) { if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
                if (!any) foreach (var r in rends) if (r && r.gameObject.activeInHierarchy) { if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
                foreach (var r in rends) if (r && r.sharedMaterial && r.sharedMaterial.renderQueue >= 2500) r.enabled = false;
                Texture2D tex = null;
                if (any)
                {
                    var c = bb.center; float half = Mathf.Max(bb.extents.y * .9f, bb.extents.x / cam.aspect * .9f, bb.extents.z / cam.aspect * .9f, .02f);
                    // perspective framing (the render request ignored an orthographic size): far enough back that the thing fills the picture
                    cam.orthographic = false; cam.fieldOfView = 30; float dd = half / Mathf.Tan(15 * Mathf.Deg2Rad) + bb.extents.z;
                    cam.nearClipPlane = Mathf.Max(.005f, dd * .05f); cam.farClipPlane = dd * 3 + 1;
                    cam.transform.position = c - Vector3.forward * dd; cam.transform.rotation = Quaternion.identity;
                    key.transform.position = c + new Vector3(-1, 1.2f, -1.5f) * Mathf.Max(.3f, half * 3); fill.transform.position = c + new Vector3(1.4f, -.2f, -1.4f) * Mathf.Max(.3f, half * 3);
                    key.range = fill.range = Mathf.Max(1, half * 10);
                    key.enabled = fill.enabled = true;
                    // a render request (Unity 6's way to draw a camera into a texture on demand); Camera.Render here clashed with the frame's own passes
                    var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                    if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req); else cam.Render();
                    key.enabled = fill.enabled = false;
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "icon " + id, wrapMode = TextureWrapMode.Clamp };
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(false, true);
                    RenderTexture.active = prev;
                }
                Object.Destroy(go);
                return tex;
            }
            catch (System.Exception e) { Debug.LogWarning("Street Life: couldn't draw the " + id + " icon: " + e.Message); return null; }
        }

        static Light Lamp(string n, float i, Color c)
        {
            var go = new GameObject(n); go.transform.SetParent(studio, false);
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.intensity = i; l.color = c; l.shadows = LightShadows.None; l.cullingMask = 1 << MenuPortraits.Layer; l.enabled = false;
            return l;
        }
    }
}
