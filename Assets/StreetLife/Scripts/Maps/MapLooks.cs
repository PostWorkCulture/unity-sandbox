// Look passes for the small maps (Sky High, Lagoon, The House, Backyard), the same recipe as Railed and Beach Please had
// by hand (RailLook.cs, BeachLook.cs), driven by one description per map (MapLook):
//  - surfaces: the map's flat-colour materials are swapped for proper surfaces picked by exported colour, mapped in world space;
//  - glow cards and the shop's yellow floor disc are hidden (each has a real light next to it);
//  - its own dressing (dress): fittings, lights and things to smash, usually built with the Tiki Bar's kit (TikiBar.Kit);
//  - baked bounce light in a scene of its own (Editor/RailBake.cs: call RailBake.Run <id>) and room reflections.
// Hooks: MapWorld.Start calls MapLooks.Env before the sky and lights are applied and MapLooks.Apply after the map is built.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class MapLook
    {
        public string id, set, stem;                              // map id, Resources name of its RailLookSet, name of its light scene
        public Dictionary<string, string> rules;                  // exported colour (+ had a texture, - had none) -> surface
        public string fromAbove = "grass";                        // the surface that is always mapped top-down
        public HashSet<string> hide;                              // exported colours whose meshes go (plain blocks the dressing replaces)
        public System.Action<EnvJson> env;                        // changes to the sky and lights (null: the map's own)
        public System.Action<MapWorld, RailLookSet> dress;        // the map's own dressing, after the surfaces
        public System.Action<Transform, RailLookSet> dressBake;   // what of that the bake should see
        public float strength = 1f;                               // how much of the baked light is used
        public Vector3 probeCentre, probeSize, farCentre, farSize;   // where the bake puts probes (far: a coarse outer volume, or zero)
        public float cloudCover = .5f; public Color cloudLit = Color.white, cloudShade = new Color(.62f, .68f, .78f);
        public (string name, Vector3 min, Vector3 max)[] rooms;   // box-projected reflections indoors
        public string LightScene => "Assets/StreetLife/Maps/" + id + "/Light/" + stem + ".unity";
    }

    public static class MapLooks
    {
        static Dictionary<string, MapLook> all;
        static Dictionary<string, MapLook> All
        {
            get
            {
                if (all != null) return all;
                all = new Dictionary<string, MapLook>();
                foreach (var l in new[] { SkyLook.Def, LagoonLook.Def }) all[l.id] = l;
                return all;
            }
        }
        public static MapLook Get(string id) => id != null && All.TryGetValue(id, out var l) ? l : null;
        public static IEnumerable<MapLook> Each => All.Values;

        /// every baked light scene there can be (builds and the editor's scene list)
        public static List<string> LightScenes()
        {
            var list = new List<string> { RailLook.LightScene, BeachLook.LightScene };
            foreach (var l in Each) list.Add(l.LightScene);
            return list;
        }

        public static void Env(string id, EnvJson e)
        {
            var l = Get(id);
            if (l != null && l.env != null && e != null) l.env(e);
        }

        public static void Apply(MapWorld w)
        {
            if (!w || !Application.isPlaying || w.entry == null) return;
            var l = Get(w.entry.id); if (l == null) return;
            var set = Resources.Load<RailLookSet>(l.set);
            if (set) { try { Surfaces(w, set, l); } catch (System.Exception ex) { Debug.LogException(ex); } }
            try { l.dress?.Invoke(w, set); } catch (System.Exception ex) { Debug.LogException(ex); }
            var light = MapBakedLight.Add(w.gameObject, l.LightScene);
            light.strength = l.strength;
            if (l.rooms != null) foreach (var r in l.rooms) light.Room(r.name, r.min, r.max);
            NavGrid.Ready = false;   // a dressing may have added or moved boxes
        }

        internal static string RuleFor(MapLook l, Material src)
        {
            if (src && src.IsKeywordEnabled("_EMISSION")) return null;   // lit signs and lamps keep their own material
            return RailLook.RuleFor(src, l.rules);
        }

        static void Surfaces(MapWorld w, RailLookSet set, MapLook l)
        {
            var mb = w.builder; var e = w.entry;
            if (!mb || !e.mapJson || !e.mapBin) return;
            var gen = mb.transform.Find("__Generated"); var geo = gen ? gen.Find("Geometry") : null;
            if (!geo) return;
            var wj = JsonUtility.FromJson<WorldJson>(e.mapJson.text);
            if (wj == null || wj.meshes == null) return;
            var bin = e.mapBin.bytes;
            var made = new Dictionary<Material, Material>();
            int n = 0, hidden = 0;
            foreach (Transform t in geo)
            {
                string nm = t.name; int us = nm.IndexOf("_m", System.StringComparison.Ordinal);
                if (!nm.StartsWith("mesh") || us < 5) continue;
                if (!int.TryParse(nm.Substring(4, us - 4), out int i) || i < 0 || i >= wj.meshes.Length) continue;
                var mr = t.GetComponent<MeshRenderer>(); var mf = t.GetComponent<MeshFilter>();
                var src = mr ? mr.sharedMaterial : null;
                if (!src || !mf) continue;
                var rec = wj.meshes[i];
                // glow cards (unlit, see-through, textured, not the far-off light shafts) and the shop's floor disc
                if (src.shader && src.shader.name.Contains("Unlit") && src.renderQueue >= 3000 && rec.bsize != null)
                {
                    float big = Mathf.Max(rec.bsize[0], Mathf.Max(rec.bsize[1], rec.bsize[2]));
                    bool card = src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap") && big < 14f;
                    bool disc = rec.bsize[1] < .06f && big > 3f && big < 8f && NearShop(rec);
                    if (card || disc) { t.gameObject.SetActive(false); hidden++; continue; }
                }
                if (l.hide != null && src.HasProperty("_BaseColor") && l.hide.Contains(ColorUtility.ToHtmlStringRGB(src.GetColor("_BaseColor")).ToLowerInvariant())) { t.gameObject.SetActive(false); hidden++; continue; }
                string rule = RuleFor(l, src); if (rule == null) continue;
                var s = set.Get(rule); if (s == null || !s.material) continue;
                if (!made.TryGetValue(src, out var m)) { m = RailLook.Make(s, src); made[src] = m; }
                var mesh = MeshIO.Build(bin, rec, nm);
                RailLook.WorldUV(mesh, Mathf.Max(.1f, s.metres), rule == l.fromAbove, s.upright);
                var old = mf.sharedMesh; mf.sharedMesh = mesh; if (old) Object.Destroy(old);
                mesh.UploadMeshData(true);
                mr.sharedMaterial = m;
                n++;
            }
            Debug.Log("Map look (" + l.id + "): " + n + " meshes on " + made.Count + " new surfaces, " + hidden + " hidden");
        }

        static bool NearShop(WMesh rec)
        {
            float cx = rec.bmin[0] + rec.bsize[0] / 2, cz = rec.bmin[2] + rec.bsize[2] / 2;
            foreach (var k in Kiosk.Here) if (Mathf.Abs(k.x - cx) < 3f && Mathf.Abs(k.z - cz) < 3f) return true;
            return false;
        }

        /// For the lighting bake (Editor/RailBake.cs): the map's solid geometry with the surfaces the game shows
        public static void BuildForBake(Transform root, MapSet.Entry e, MapLook l)
        {
            var set = Resources.Load<RailLookSet>(l.set);
            RailLook.BuildForBake(root, e, set, src => RuleFor(l, src), l.fromAbove, "tree");
            l.dressBake?.Invoke(root, set);
        }
    }

    /// firelight: each lamp wavers on its own
    public class MapFlicker : MonoBehaviour
    {
        readonly List<(Light light, float power, float seed)> lamps = new List<(Light, float, float)>();
        public void Add(Light l) { if (l) lamps.Add((l, l.intensity, lamps.Count * 7.31f)); }
        void Update()
        {
            float t = Time.time;
            foreach (var (light, power, seed) in lamps)
                if (light) light.intensity = power * (.78f + .3f * Mathf.PerlinNoise(t * 5.5f, seed) + .1f * Mathf.PerlinNoise(t * 17f, seed + 3f));
        }
    }

    /// a slow blink for warning lights: every lamp and the one glowing material together
    public class MapBlink : MonoBehaviour
    {
        public Material glow; public Color on; public readonly List<Light> lamps = new List<Light>(); public float power = 1, every = 1.6f;
        void Update()
        {
            float t = Time.time % every / every, k = t < .35f ? Mathf.SmoothStep(0, 1, Mathf.Min(1, t * 8)) * Mathf.SmoothStep(1, 0, Mathf.Max(0, (t - .2f) / .15f)) : 0;
            if (glow) glow.SetColor("_EmissionColor", on * (.08f + k));
            foreach (var l in lamps) if (l) l.intensity = power * k;
        }
    }
}
