// Every character and weapon exported from the imok 1v1 web game, plus their stats (Data/catalog.json).
// The Street Life setup fills this in from Data/Characters and Data/Weapons; meshes are built once and shared.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    [CreateAssetMenu(menuName = "Street Life/Armory")]
    public class Armory : ScriptableObject
    {
        [Serializable] public class Entry { public string id; public TextAsset json, bin; }

        public MaterialLibrary library;
        public TextAsset catalog;
        public Entry[] characters;
        public Entry[] weapons;

        [NonSerialized] CatalogJson cat;
        [NonSerialized] Dictionary<string, CharInfo> charInfo;
        [NonSerialized] Dictionary<string, WeaponInfo> weaponInfo;
        [NonSerialized] Dictionary<string, WeaponJson> weaponData;
        [NonSerialized] Dictionary<string, Mesh[]> weaponMeshes;

        void OnEnable() { cat = null; }

        void Load()
        {
            if (cat != null) return;
            cat = catalog ? JsonUtility.FromJson<CatalogJson>(catalog.text) : new CatalogJson();
            charInfo = new Dictionary<string, CharInfo>(); weaponInfo = new Dictionary<string, WeaponInfo>();
            weaponData = new Dictionary<string, WeaponJson>(); weaponMeshes = new Dictionary<string, Mesh[]>();
            if (cat.characters != null) foreach (var c in cat.characters) charInfo[c.id] = c;
            if (cat.weapons != null) foreach (var w in cat.weapons) weaponInfo[w.id] = w;
        }

        public CatalogJson Catalog { get { Load(); return cat; } }
        public CharInfo Char(string id) { Load(); return id != null && charInfo.TryGetValue(id, out var c) ? c : null; }
        public WeaponInfo Weapon(string id) { Load(); return id != null && weaponInfo.TryGetValue(id, out var w) ? w : null; }

        Entry Find(Entry[] list, string id) { if (list != null) foreach (var e in list) if (e != null && e.id == id) return e; return null; }
        public Entry CharEntry(string id) => Find(characters, id);

        public WeaponJson WeaponModel(string id)
        {
            Load();
            if (weaponData.TryGetValue(id, out var wj)) return wj;
            var e = Find(weapons, id);
            wj = e != null && e.json ? JsonUtility.FromJson<WeaponJson>(e.json.text) : null;
            weaponData[id] = wj;
            if (wj != null)
            {
                var bin = e.bin ? e.bin.bytes : null;
                var ms = new Mesh[wj.meshes != null ? wj.meshes.Length : 0];
                for (int i = 0; i < ms.Length; i++) ms[i] = bin != null ? MeshIO.Build(bin, wj.meshes[i], id + i) : null;
                weaponMeshes[id] = ms;
            }
            return wj;
        }

        public Material Mat(int i) => library && library.character != null && i >= 0 && i < library.character.Length ? library.character[i] : library ? library.unlitBase : null;

        /// A new object showing weapon <id>'s model (in the weapon's own space), with its renderers.
        public GameObject MakeWeapon(string id, Transform parent, List<Renderer> renderers = null)
        {
            var wj = WeaponModel(id);
            var go = new GameObject("weapon " + id);
            go.transform.SetParent(parent, false);
            if (wj == null) return go;
            var ms = weaponMeshes[id];
            for (int i = 0; i < ms.Length; i++)
            {
                if (!ms[i]) continue;
                var m = new GameObject("part" + i);
                m.transform.SetParent(go.transform, false);
                m.AddComponent<MeshFilter>().sharedMesh = ms[i];
                var mr = m.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Mat(wj.meshes[i].mat);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderers?.Add(mr);
            }
            return go;
        }

        /// Ids in menu order for one loadout slot ("primary", "secondary", "lethal", or "other" for shop and extras).
        public List<string> Slot(string slot)
        {
            Load(); var o = new List<string>();
            string[] order = slot == "primary" ? cat.order?.primaries : slot == "secondary" ? cat.order?.secondaries : slot == "lethal" ? cat.order?.lethals : null;
            if (order != null) foreach (var id in order) if (weaponInfo.ContainsKey(id)) o.Add(id);
            if (order == null && cat.weapons != null) foreach (var w in cat.weapons) if (w.slot == slot) o.Add(w.id);
            return o;
        }
    }
}
