// The weapon showroom on the main menu's Loadout step: the weapon you pick turns slowly under studio lights, and you can
// drag it round (and scroll or pinch to zoom). It is rendered by its own camera into a texture shown in the menu, in a
// "studio" far above the map: a warm key light, a cool fill, two hard white rim lights that pick out the edges, and a
// glint light that sweeps across the top so highlights run along the metal. A studio reflection map (dark room with
// bright softbox strips, HDR) gives the gunmetal and chrome something to mirror, and the game's bloom makes the
// brightest glints glow. Monochrome panel: name, class, four stat bars (the web game's loadout stats), a hint.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace StreetLife
{
    public class WeaponShowcase : MonoBehaviour
    {
        public const int Layer = 29;                                   // the weapon on show: only the showroom camera draws it
        static readonly Vector3 Studio = new Vector3(0, 4000, 0);      // far above every map, out of reach of its lights and probes
        public static WeaponShowcase I;

        // menu look (monochrome, as the rest of the menu)
        static readonly Color PanelBg = new Color32(10, 10, 10, 238), Line = new Color32(70, 70, 70, 255), Text = new Color32(245, 245, 245, 255),
            Dim = new Color32(138, 138, 138, 255), Track = new Color32(40, 40, 40, 255), Bar = new Color32(245, 245, 245, 255);

        Camera cam; RenderTexture rt; VolumeProfile look; Transform pivot, model; ReflectionProbe probe; Cubemap studioMap;
        Light key, fill, rimA, rimB, glint, front, top;
        Transform backdrop;   // a soft grey glow behind the weapon, so dark guns stand out from the black
        Image view; Label title, sub, note; readonly Dictionary<string, VisualElement> bars = new Dictionary<string, VisualElement>();
        string shown; bool active;
        float yaw = -25, pitch = 8, zoom = 1, spinV, idleT = 99, radius = .6f, halfW = .5f, halfH = .2f, glintT;
        bool dragging; Vector2 lastPos; int pointer = -1;
        readonly Dictionary<int, Vector2> touches = new Dictionary<int, Vector2>(); float pinchD;

        static WeaponShowcase Get()
        {
            if (!I) { var go = new GameObject("Weapon showroom"); I = go.AddComponent<WeaponShowcase>(); }
            return I;
        }

        // ================================================================== for the menu
        /// The preview card (460 x 440, top right of the Loadout step, leaving the middle to the character). Call once while building the menu.
        public static VisualElement Panel()
        {
            var s = Get();
            var card = new VisualElement(); card.style.width = 460; card.style.height = 440; card.style.flexShrink = 0; card.style.alignSelf = Align.FlexStart; card.style.marginLeft = 16;
            card.style.backgroundColor = PanelBg; R(card, 16); B(card, 1, Line); card.style.overflow = Overflow.Hidden;
            s.view = new Image { scaleMode = ScaleMode.ScaleAndCrop }; s.view.style.position = Position.Absolute;
            s.view.style.left = s.view.style.top = s.view.style.right = s.view.style.bottom = 0;
            card.Add(s.view);
            // name and class, top left
            var head = new VisualElement(); head.style.position = Position.Absolute; head.style.left = 26; head.style.top = 20; head.pickingMode = PickingMode.Ignore;
            s.title = L("", 30, Text, true); s.sub = L("", 12, Dim, true); s.sub.style.letterSpacing = 1.5f; s.sub.style.marginTop = 2;
            head.Add(s.title); head.Add(s.sub); card.Add(head);
            // stats, bottom left
            var st = new VisualElement(); st.style.position = Position.Absolute; st.style.left = 26; st.style.bottom = 22; st.style.right = 26; st.pickingMode = PickingMode.Ignore;
            foreach (var k in new[] { "Damage", "Fire rate", "Range", "Magazine" })
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.marginTop = 6; row.pickingMode = PickingMode.Ignore;
                var n = L(k.ToUpperInvariant(), 10, Dim, true); n.style.width = 84; n.style.letterSpacing = 1.2f; row.Add(n);
                var track = new VisualElement(); track.style.flexGrow = 1; track.style.height = 5; track.style.backgroundColor = Track; R(track, 3); track.pickingMode = PickingMode.Ignore;
                var fillBar = new VisualElement(); fillBar.style.height = 5; fillBar.style.backgroundColor = Bar; R(fillBar, 3); fillBar.style.width = new Length(0, LengthUnit.Percent); fillBar.pickingMode = PickingMode.Ignore;
                track.Add(fillBar); row.Add(track); st.Add(row); s.bars[k] = fillBar;
            }
            s.note = L("", 11, Dim); s.note.style.marginTop = 10; s.note.style.whiteSpace = WhiteSpace.Normal; st.Add(s.note);
            card.Add(st);
            var hint = L("DRAG TO TURN", 10, Dim, true); hint.style.letterSpacing = 1.5f; hint.style.position = Position.Absolute; hint.style.right = 22; hint.style.top = 24; hint.pickingMode = PickingMode.Ignore;
            card.Add(hint);

            // turning and zooming
            var v = s.view;
            v.RegisterCallback<PointerDownEvent>(e =>
            {
                s.touches[e.pointerId] = e.position;
                if (s.touches.Count == 2) { var p = new List<Vector2>(s.touches.Values); s.pinchD = Vector2.Distance(p[0], p[1]); s.dragging = false; }
                else { s.dragging = true; s.pointer = e.pointerId; s.lastPos = e.position; v.CapturePointer(e.pointerId); }
                if (e.clickCount == 2) { s.yaw = -25; s.pitch = 8; s.zoom = 1; s.spinV = 0; }
                s.idleT = 0;
            });
            v.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (s.touches.ContainsKey(e.pointerId)) s.touches[e.pointerId] = e.position;
                if (s.touches.Count >= 2)
                {
                    var p = new List<Vector2>(s.touches.Values); float d = Vector2.Distance(p[0], p[1]);
                    if (s.pinchD > 1) s.zoom = Mathf.Clamp(s.zoom * s.pinchD / Mathf.Max(1, d), .55f, 1.8f);
                    s.pinchD = d; s.idleT = 0; return;
                }
                if (!s.dragging || e.pointerId != s.pointer) return;
                Vector2 dp = (Vector2)e.position - s.lastPos; s.lastPos = e.position;
                s.yaw -= dp.x * .45f; s.pitch = Mathf.Clamp(s.pitch + dp.y * .3f, -60, 60);
                s.spinV = -dp.x * .45f / Mathf.Max(.001f, Time.unscaledDeltaTime);   // keeps turning a little when let go
                s.idleT = 0;
            });
            EventCallback<PointerUpEvent> up = e => { s.touches.Remove(e.pointerId); if (e.pointerId == s.pointer) { s.dragging = false; s.pointer = -1; if (v.HasPointerCapture(e.pointerId)) v.ReleasePointer(e.pointerId); } };
            v.RegisterCallback(up);
            v.RegisterCallback<PointerCancelEvent>(e => { s.touches.Remove(e.pointerId); s.dragging = false; });
            v.RegisterCallback<WheelEvent>(e => { s.zoom = Mathf.Clamp(s.zoom * (1 + e.delta.y * .04f), .55f, 1.8f); s.idleT = 0; e.StopPropagation(); });
            v.RegisterCallback<GeometryChangedEvent>(_ => s.Resize());
            return card;
        }

        /// Put a weapon on show: any gun or the sword (web ids, e.g. "gar"), or a lethal ("grenade", "cluster", "mine", "stun", "flash").
        public static void Show(string id) { var s = Get(); s.SetWeapon(id); }

        /// The showroom renders only while the Loadout step is on screen.
        public static void SetActive(bool on, string first = null)
        {
            if (!on) { if (I) { I.active = false; if (I.cam) I.cam.enabled = false; } return; }   // never makes a showroom just to switch it off
            var s = Get(); s.active = true; if (s.cam) s.cam.enabled = true;
            if (s.shown == null || first != null) s.SetWeapon(first ?? Settings.Primary);
            // keep the showroom to itself: the map's cameras don't draw it and the sun doesn't light it
            var mc = Camera.main; if (mc) mc.cullingMask &= ~(1 << Layer);
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) l.cullingMask &= ~(1 << Layer);
        }

        static Label L(string t, int size, Color c, bool bold = false)
        {
            var l = new Label(t); l.style.fontSize = size; l.style.color = c; l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            l.style.marginTop = l.style.marginBottom = l.style.marginLeft = l.style.marginRight = 0; l.style.paddingTop = l.style.paddingBottom = l.style.paddingLeft = l.style.paddingRight = 0;
            l.pickingMode = PickingMode.Ignore; return l;
        }
        static void R(VisualElement e, float r) { e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r; }
        static void B(VisualElement e, float w, Color c)
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = w;
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c;
        }

        // ================================================================== the studio
        void Awake() { I = this; transform.position = Studio; BuildStudio(); }
        void OnDestroy() { if (I == this) I = null; if (rt) { rt.Release(); Destroy(rt); } if (studioMap) Destroy(studioMap); if (look) Destroy(look); foreach (var p in polished.Values) if (p) Destroy(p); }

        void BuildStudio()
        {
            pivot = new GameObject("pivot").transform; pivot.SetParent(transform, false);
            var cg = new GameObject("Showroom camera"); cg.transform.SetParent(transform, false);
            cam = cg.AddComponent<Camera>(); cam.cullingMask = 1 << Layer; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.05f, .05f, .056f, 1); cam.fieldOfView = 26; cam.nearClipPlane = .02f; cam.farClipPlane = 40; cam.depth = -50;
            cam.allowHDR = true; cam.enabled = false;
            var d = cam.GetUniversalAdditionalCameraData();
            d.renderPostProcessing = true; d.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; d.antialiasingQuality = AntialiasingQuality.High;
            d.renderShadows = false; d.requiresDepthTexture = false;
            Resize();
            // its own look (ACES and bloom on layer 29), so a map's mood volume (fog, lens flare, grading) never reaches the showroom
            d.volumeLayerMask = 1 << Layer;
            var vol = new GameObject("showroom look").AddComponent<Volume>(); vol.transform.SetParent(transform, false); vol.gameObject.layer = Layer;
            vol.isGlobal = true; vol.priority = 50; look = ScriptableObject.CreateInstance<VolumeProfile>(); vol.sharedProfile = look;
            var tm = look.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var bl = look.Add<Bloom>(true); bl.intensity.Override(.55f); bl.threshold.Override(1f); bl.scatter.Override(.6f);
            var ca = look.Add<ColorAdjustments>(true); ca.contrast.Override(8f); ca.postExposure.Override(.3f);   // brighter overall: dark guns read (Pete, 15 Nov)

            // lights: warm key from above front-left, cool fill low right, two hard rims behind, a glint that sweeps over the top
            key = MakeLight("key", LightType.Spot, new Color(1f, .95f, .88f), 40, 14);
            fill = MakeLight("fill", LightType.Point, new Color(.75f, .84f, 1f), 20, 10);
            // more light so dark guns are easy to see: a soft front light by the camera and a softbox overhead
            front = MakeLight("front", LightType.Point, new Color(1f, .98f, .95f), 16, 10);
            top = MakeLight("top", LightType.Point, Color.white, 12, 10);
            rimA = MakeLight("rim A", LightType.Spot, Color.white, 55, 14);
            rimB = MakeLight("rim B", LightType.Spot, new Color(.9f, .94f, 1f), 40, 14);
            glint = MakeLight("glint", LightType.Point, Color.white, 14, 4);
            key.spotAngle = 55; key.innerSpotAngle = 25; rimA.spotAngle = rimB.spotAngle = 45; rimA.innerSpotAngle = rimB.innerSpotAngle = 20;

            // backdrop: a quad behind the weapon with a radial glow (grey in the middle, black at the edges), unlit
            var bq = GameObject.CreatePrimitive(PrimitiveType.Quad); var bcol = bq.GetComponent<Collider>(); if (bcol) Destroy(bcol);
            bq.name = "backdrop"; bq.layer = Layer; bq.transform.SetParent(transform, false); backdrop = bq.transform;
            var glow = new Texture2D(128, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "backdrop glow" };
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                { float dx = (x - 63.5f) / 64f, dy = (y - 63.5f) / 64f * 1.15f, r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy)); float v = Mathf.Lerp(.26f, .05f, Mathf.SmoothStep(0, 1, r)); glow.SetPixel(x, y, new Color(v, v, v * 1.04f, 1)); }
            glow.Apply();
            var bm = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "backdrop" }; bm.SetTexture("_BaseMap", glow); bm.SetColor("_BaseColor", Color.white);
            var br = bq.GetComponent<MeshRenderer>(); br.sharedMaterial = bm; br.shadowCastingMode = ShadowCastingMode.Off; br.receiveShadows = false;

            // studio reflections: an HDR cube of a dark room with softbox strips, so metal has bright edges to mirror
            studioMap = MakeStudioMap(128);
            probe = new GameObject("studio reflections").AddComponent<ReflectionProbe>(); probe.transform.SetParent(transform, false);
            probe.mode = ReflectionProbeMode.Custom; probe.customBakedTexture = studioMap; probe.size = Vector3.one * 12; probe.importance = 100; probe.intensity = 1.3f;
            probe.boxProjection = false;
        }

        Light MakeLight(string n, LightType t, Color c, float intensity, float range)
        {
            var go = new GameObject(n); go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>(); l.type = t; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            l.cullingMask = 1 << Layer; l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        static Cubemap MakeStudioMap(int n)
        {
            var cube = new Cubemap(n, TextureFormat.RGBAHalf, true) { name = "studio", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[n * n];
            for (int f = 0; f < 6; f++)
            {
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1;
                        var dir = FaceDir((CubemapFace)f, u, v).normalized;
                        px[y * n + x] = StudioColor(dir);
                    }
                cube.SetPixels(px, (CubemapFace)f, 0);
            }
            cube.Apply(true, false);
            return cube;
        }

        static Vector3 FaceDir(CubemapFace f, float u, float v)
        {
            switch (f)
            {
                case CubemapFace.PositiveX: return new Vector3(1, -v, -u);
                case CubemapFace.NegativeX: return new Vector3(-1, -v, u);
                case CubemapFace.PositiveY: return new Vector3(u, 1, v);
                case CubemapFace.NegativeY: return new Vector3(u, -1, -v);
                case CubemapFace.PositiveZ: return new Vector3(u, -v, 1);
                default: return new Vector3(-u, -v, -1);
            }
        }

        /// the room: near-black walls, a soft grey floor glow, one long softbox overhead, two tall strips at the sides and a small kicker behind
        static Color StudioColor(Vector3 d)
        {
            float b = Mathf.Lerp(.02f, .06f, d.y * .5f + .5f);
            Color c = new Color(b, b, b * 1.08f);
            if (d.y < -.2f) c += new Color(.05f, .05f, .05f) * Mathf.InverseLerp(-.2f, -1, d.y);
            // overhead softbox: a long bar across x, slightly toward the camera
            if (d.y > .55f) { float k = Soft(Mathf.Abs(d.z - .15f), .22f) * Soft(Mathf.Abs(d.x), .75f); c += new Color(6.5f, 6.3f, 6f) * k; }
            // tall strips left and right, a little behind the weapon
            float sideX = Mathf.Abs(d.x);
            if (sideX > .6f) { float k = Soft(Mathf.Abs(d.z + .25f), .14f) * Soft(Mathf.Abs(d.y - .05f), .55f); c += (d.x > 0 ? new Color(5f, 5.2f, 5.6f) : new Color(4.2f, 4f, 3.8f)) * k; }
            // a small warm kicker straight behind
            if (d.z < -.7f) { float k = Soft(Mathf.Abs(d.x), .18f) * Soft(Mathf.Abs(d.y - .25f), .12f); c += new Color(3f, 2.4f, 1.8f) * k; }
            c.a = 1; return c;
        }
        static float Soft(float x, float w) => Mathf.Clamp01((w - x) / (w * .35f));

        void Resize()
        {
            int w = 1024, h = 640;
            if (view != null && view.panel != null)
            {
                var r = view.worldBound; var root = view.panel.visualTree.worldBound;
                if (r.width > 4 && r.height > 4 && root.width > 1) { float k = Screen.width / root.width; w = Mathf.RoundToInt(r.width * k); h = Mathf.RoundToInt(r.height * k); }
            }
            float lim = 2048f / Mathf.Max(w, h); if (lim < 1) { w = Mathf.RoundToInt(w * lim); h = Mathf.RoundToInt(h * lim); }
            w = Mathf.Max(64, w); h = Mathf.Max(64, h);
            if (rt && rt.width == w && rt.height == h) return;
            if (rt) { if (cam) cam.targetTexture = null; rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "weapon showroom", antiAliasing = 1 };
            rt.Create();
            if (cam) { cam.targetTexture = rt; cam.aspect = (float)w / h; }
            if (view != null) view.image = rt;
        }

        // ================================================================== the weapon on show
        void SetWeapon(string id)
        {
            if (!GunsDb.Loaded || string.IsNullOrEmpty(id) || id == shown) return;
            WNode[] nodes = null;
            if (id == "sword") nodes = GunsDb.Data.sword != null ? GunsDb.Data.sword.nodes : null;
            else if (GunsDb.Held(id) != null) nodes = GunsDb.Held(id).nodes;
            else if (GunsDb.Proj(id) != null) nodes = GunsDb.Proj(id).nodes;
            if (nodes == null) return;
            ShowNodes(id, nodes);
        }

        void ShowNodes(string id, WNode[] nodes)
        {
            shown = id;
            if (model) { model.gameObject.SetActive(false); Destroy(model.gameObject); }
            var rends = new List<Renderer>();
            var holder = new GameObject("model").transform; holder.SetParent(pivot, false);
            if (id == "duals" && WeaponFixes.Pistol != null)
            {
                // a matched pair, staggered one above the other like a display case
                foreach (var (x, y, z) in new[] { (.07f, .17f, .02f), (-.07f, -.17f, -.27f) })
                {
                    var one = new GameObject("pistol").transform; one.SetParent(holder, false); one.localPosition = new Vector3(x, y, z);
                    WeaponModels.Build(WeaponFixes.Pistol, one, "pistol", Layer, false, rends);
                }
            }
            else WeaponModels.Build(nodes, holder, id, Layer, false, rends);
            model = holder;
            // the flame on rocket / missile models stays hidden; everything is lit by the studio only
            var fl = WeaponModels.Find(holder, "flame"); if (fl) fl.gameObject.SetActive(false);
            foreach (var r in rends)
            {
                if (!r) continue;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes; r.lightProbeUsage = LightProbeUsage.Off;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = Polished(ms[i]); r.sharedMaterials = ms;
            }
            // lay it side-on, muzzle to the right, and centre it on the turntable
            holder.localRotation = Quaternion.Euler(0, -90, 0); pivot.localRotation = Quaternion.identity;
            var bb = new Bounds(); bool any = false;
            foreach (var r in rends) if (r && r.enabled && r.gameObject.activeInHierarchy) { if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
            if (any) { holder.position -= bb.center - pivot.position; radius = Mathf.Max(.08f, bb.extents.magnitude);
                var e = bb.extents; halfW = Mathf.Max(.05f, new Vector2(e.x, e.z).magnitude * .95f); halfH = Mathf.Max(.05f, e.y + e.z * .3f); }
            // small things (grenades, mines) are shown bigger so they fill the frame the same way
            yaw = -25; pitch = id == "mine" ? 28 : 8; zoom = 1; spinV = 0; idleT = 99;
            UpdateInfo(id);
        }

        // showroom copies of the weapon materials, polished up so the studio lights catch them (the game's own stay as they are)
        readonly Dictionary<Material, Material> polished = new Dictionary<Material, Material>();
        Material Polished(Material m)
        {
            if (!m || m.renderQueue >= 3000 || !m.HasProperty("_Smoothness")) return m;
            if (polished.TryGetValue(m, out var p)) return p;
            p = new Material(m) { name = m.name + " (showroom)" };
            if (!m.IsKeywordEnabled("_METALLICSPECGLOSSMAP"))
            {
                float sm = m.GetFloat("_Smoothness"), mt = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0;
                // metal gets a mirror polish; paint and plastic only a soft sheen, so dark parts stay dark under the studio lights
                p.SetFloat("_Smoothness", mt > .3f ? Mathf.Lerp(sm, .93f, .6f) : Mathf.Clamp(sm, .3f, .45f));
            }
            // near-black paint and plastic lifted a little in the showroom only, so their shape shows (the game's materials are untouched)
            if (p.HasProperty("_BaseColor"))
            {
                var bc = p.GetColor("_BaseColor"); float lum = bc.r * .3f + bc.g * .59f + bc.b * .11f;
                if (lum < .14f) { float k = (.14f - lum) / .14f; p.SetColor("_BaseColor", Color.Lerp(bc, new Color(.16f, .16f, .17f, bc.a), k * .8f)); }
            }
            if (p.HasProperty("_SpecularHighlights")) p.SetFloat("_SpecularHighlights", 1);
            if (p.HasProperty("_EnvironmentReflections")) p.SetFloat("_EnvironmentReflections", 1);
            p.DisableKeyword("_SPECULARHIGHLIGHTS_OFF"); p.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            polished[m] = p; return p;
        }

        void UpdateInfo(string id)
        {
            if (title == null) return;
            var g = GunsDb.Gun(id); var c = GunsDb.Cons(id);
            title.text = c != null && (g == null || g.IsProj && !g.primary && !g.secondary) ? c.name : g != null ? g.name : id;
            sub.text = (g != null && !string.IsNullOrEmpty(g.cat) ? g.cat : c != null ? "Lethal" : "").ToUpperInvariant();
            float dmg = 0, rps = 0, rng = 0, mag = 0;
            if (g != null)
            {
                dmg = Mathf.Max(g.direct, g.dmg) * g.Pellets * Mathf.Max(1, g.burst) / Mx("dmg");
                rps = g.rate > 0 ? 1f / g.rate / Mx("rps") : 0;
                rng = (g.IsMelee ? g.reach : g.HasFall ? g.fall[1] : g.reach) / Mx("rng");
                mag = g.InfiniteMag ? 1 : g.mag / Mx("mag");
                if (c != null) { rps = 0; mag = Mathf.Max(1, c.uses) / 3f; rng = g.radius / 13f; }
            }
            SetBar("Damage", dmg); SetBar("Fire rate", rps); SetBar("Range", rng); SetBar("Magazine", mag);
            note.text = c != null ? c.desc + "  ·  " + c.meta : g != null ? g.note : "";
        }

        void SetBar(string k, float v) { if (bars.TryGetValue(k, out var b)) b.style.width = new Length(Mathf.Clamp01(v) * 100, LengthUnit.Percent); }

        // best value of each stat across every primary and secondary, so the top gun fills the bar (the web game's statMax)
        static Dictionary<string, float> mx;
        static float Mx(string k)
        {
            if (mx == null)
            {
                mx = new Dictionary<string, float> { { "dmg", 1 }, { "rps", 1 }, { "rng", 1 }, { "mag", 1 } };
                var all = new List<string>(GunsDb.Primaries); all.AddRange(GunsDb.Secondaries);
                foreach (var id in all)
                {
                    var g = GunsDb.Gun(id); if (g == null) continue;
                    mx["dmg"] = Mathf.Max(mx["dmg"], Mathf.Max(g.direct, g.dmg) * g.Pellets * Mathf.Max(1, g.burst));
                    mx["rps"] = Mathf.Max(mx["rps"], g.rate > 0 ? 1 / g.rate : 0);
                    mx["rng"] = Mathf.Max(mx["rng"], g.IsMelee ? g.reach : g.HasFall ? g.fall[1] : g.reach);
                    if (!g.InfiniteMag) mx["mag"] = Mathf.Max(mx["mag"], g.mag);
                }
                mx["rng"] = Mathf.Min(mx["rng"], 160);
            }
            return mx[k];
        }

        // ================================================================== every frame
        void LateUpdate()
        {
            if (!active || !cam) return;
            float dt = Time.unscaledDeltaTime;
            idleT += dt;
            if (!dragging)
            {
                spinV = Mathf.Lerp(spinV, 0, 1 - Mathf.Exp(-3 * dt));
                yaw += spinV * dt;
                if (idleT > 2.5f) yaw += 14 * dt * Mathf.Clamp01((idleT - 2.5f) / 1.5f);   // a slow turntable when left alone
            }
            pivot.localRotation = Quaternion.Euler(pitch, yaw, 0);
            glintT += dt;
            Frame(false);
        }

        /// camera and lights round the weapon on show (sheet: centred, for the contact sheet)
        void Frame(bool sheet)
        {
            // camera: framed on the weapon's size, a touch above
            // fit the length to 78% of the width and the height to the open middle (between the name and the stats)
            float t = Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad);
            float dist = sheet ? Mathf.Max(halfW / (t * cam.aspect * .86f), halfH / (t * .8f)) : Mathf.Max(halfW / (t * cam.aspect * .78f), halfH / (t * .48f), radius * 1.2f) / zoom;
            var c = pivot.position;
            cam.transform.position = c + new Vector3(0, dist * .06f, -dist);
            cam.transform.LookAt(sheet ? c : c - Vector3.up * dist * t * .1f);

            // lights placed round the weapon in camera space, scaled to its size
            float s = Mathf.Max(.5f, radius * 2.4f);
            Place(key, c + new Vector3(-1.1f, 1.3f, -1.4f) * s, c);
            fill.transform.position = c + new Vector3(1.3f, -.35f, -1.1f) * s;
            Place(rimA, c + new Vector3(1.2f, .9f, 1.5f) * s, c);
            Place(rimB, c + new Vector3(-1.4f, .5f, 1.3f) * s, c);
            // the glint sweeps back and forth over the top, so a highlight runs along the barrel and receiver
            float sweep = Mathf.Sin(glintT * .9f);
            glint.transform.position = c + new Vector3(sweep * 1.2f, .75f + .15f * Mathf.Cos(glintT * .5f), -.55f) * s;
            glint.range = 3.2f * s;
            front.transform.position = c + new Vector3(.25f, .45f, -1.7f) * s;
            top.transform.position = c + new Vector3(0, 1.7f, -.2f) * s;
            front.range = top.range = 5 * s;
            if (backdrop)
            {   // far enough behind that the weapon never touches it, big enough to fill the view
                float back = radius * 3f + .6f, size = 2.4f * (dist + back) * t * Mathf.Max(1f, cam.aspect);
                backdrop.position = c + Vector3.forward * back; backdrop.rotation = Quaternion.identity; backdrop.localScale = new Vector3(size, size, 1);
            }
            foreach (var l in new[] { key, rimA, rimB }) l.range = 5 * s;
            fill.range = 5 * s;
        }

        static void Place(Light l, Vector3 at, Vector3 look) { l.transform.position = at; l.transform.rotation = Quaternion.LookRotation(look - at); }

        // ================================================================== contact sheet (call WeaponShowcase.Sheet)
        /// Renders every weapon (primaries, secondaries, lethals, grenade, Sentry) side-on to Logs/weapons/<n>-<id>.png
        /// (800 x 500) and lists them in Logs/weapons/index.txt (file, id, name, class). Needs play mode (weapon data loaded).
        public static string Sheet(string arg)
        {
            if (!Application.isPlaying) return "play first";
            if (!GunsDb.Loaded)
            {
                // any scene will do: load the weapon data the way WeaponWorld does
                var a = Resources.Load<WeaponAssets>("StreetLifeWeapons");
                if (!a || !a.gunsJson || !a.gunsBin) return "weapon data not found";
                GunsDb.Load(a.gunsJson.text, a.gunsBin.bytes); WeaponModels.Mats = a.materials;
            }
            var s = Get();
            var ids = new List<string>(GunsDb.Primaries);
            foreach (var id in GunsDb.Secondaries) if (!ids.Contains(id)) ids.Add(id);
            if (!ids.Contains("sword")) ids.Add("sword");
            foreach (var id in new[] { "grenade" }) if (!ids.Contains(id)) ids.Add(id);
            foreach (var id in GunsDb.Lethals) if (!ids.Contains(id)) ids.Add(id);
            string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs", "weapons");
            System.IO.Directory.CreateDirectory(dir);
            foreach (var f in System.IO.Directory.GetFiles(dir, "*.png")) System.IO.File.Delete(f);
            var rtS = new RenderTexture(800, 500, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 }; rtS.Create();
            var oldRt = s.cam.targetTexture; float oldAspect = s.cam.aspect; bool wasOn = s.cam.enabled;
            s.cam.targetTexture = rtS; s.cam.aspect = 1.6f;
            var tex = new Texture2D(800, 500, TextureFormat.RGB24, false);
            var lines = new List<string>(); int n = 0;
            // the Sentry: the shop's turret, built straight from its nodes
            ids.Add("sentry");
            foreach (var id in ids)
            {
                if (id == "sentry")
                {
                    if (GunsDb.Data.turret == null || GunsDb.Data.turret.nodes == null) continue;
                    s.shown = null; s.ShowNodes("sentry", GunsDb.Data.turret.nodes);
                }
                else { s.shown = null; s.SetWeapon(id); if (s.shown != id) continue; }
                s.pivot.localRotation = Quaternion.Euler(10, id == "mine" ? -25 : -20, 0);
                s.glintT = 1.2f; s.Frame(true);
                s.cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rtS;
                tex.ReadPixels(new Rect(0, 0, 800, 500), 0, 0); tex.Apply(); RenderTexture.active = prev;
                n++; string file = n.ToString("00") + "-" + id + ".png";
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, file), tex.EncodeToPNG());
                var g = GunsDb.Gun(id); var c = GunsDb.Cons(id);
                string name = id == "sentry" ? "Sentry" : c != null ? c.name : g != null ? g.name : id;
                string cat = id == "sentry" ? "Shop" : g != null && !string.IsNullOrEmpty(g.cat) && c == null ? g.cat : c != null ? "Lethal" : "";
                string slot = GunsDb.Primaries.Contains(id) ? "Primary" : GunsDb.Secondaries.Contains(id) ? "Secondary" : id == "sentry" ? "Shop" : "Lethal";
                lines.Add(file + "\t" + id + "\t" + name + "\t" + cat + "\t" + slot);
            }
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "index.txt"), lines);
            Destroy(tex); s.cam.targetTexture = oldRt; s.cam.aspect = oldAspect; s.cam.enabled = wasOn; rtS.Release(); Destroy(rtS);
            s.shown = null;
            return n + " weapons to " + dir;
        }

        /// remote test: put a gun in the player's hands as their primary (call WeaponShowcase.Hold gar)
        public static string Hold(string id)
        {
            var f = Combat.Me; if (!f) return "no player";
            var w = f.GetComponent<Weapons>(); if (!w) return "no weapons";
            f.primary = id.Trim(); w.ResetKit(); return "holding " + w.Gun;
        }

        // ================================================================== remote test (Logs/streetlife-cmd.txt: call WeaponShowcase.Test gar)
        /// Opens the menu's Loadout step and puts the weapon on show. Optional "id yaw" turns it.
        public static string Test(string arg)
        {
            var parts = (arg ?? "").Trim().Split(' ');
            var mm = FindAnyObjectByType<MainMenu>(); if (!mm) return "no main menu";
            var m = typeof(MainMenu).GetMethod("SetStep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (m == null) return "MainMenu.SetStep not found";
            m.Invoke(mm, new object[] { 2 });
            if (parts.Length > 0 && parts[0] != "") Show(parts[0]);
            if (parts.Length > 1 && float.TryParse(parts[1], out var y)) { Get().yaw = y; Get().idleT = 0; }
            var s = Get();
            return $"showing {s.shown}, rt {(s.rt ? s.rt.width + "x" + s.rt.height : "none")}, camera {(s.cam && s.cam.enabled ? "on" : "off")}, radius {s.radius:0.00}";
        }
    }
}
