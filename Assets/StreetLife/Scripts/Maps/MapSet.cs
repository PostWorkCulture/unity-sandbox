// Maps chat: every map from the imok 1v1 web game, exported per map into Assets/StreetLife/Maps/<id> (see claude/street-life-interfaces.md).
// Street Life keeps its own data (Data/map.*, owned by the match chat); the others are swapped into the scene's MapBuilder
// when play starts, through one hook line at the top of MapBuilder.OnEnable: MapSet.Apply(this).
// The pick is saved in PlayerPrefs ("StreetLife.map"); the F4 picker (MapPicker) changes it and reloads the scene.
using System;
using UnityEngine;

namespace StreetLife
{
    [CreateAssetMenu(menuName = "Street Life/Map Set")]
    public class MapSet : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string id, title, desc, mode;   // mode: Duels, 1v1 or Rumble (which web game menu it comes from)
            public Color[] swatch;
            public TextAsset mapJson, mapBin, dynJson, dynBin, extrasJson, extrasBin, envJson;
            public MaterialLibrary library;
            public bool IsStreet => id == "street";
        }

        public Entry[] maps;
        [Header("Shaders (referenced here so builds include them)")]
        public Shader sky, cloud, ground, water;

        public const string Pref = "StreetLife.map", Default = "street";
        static MapSet cached;
        public static MapSet Load() { if (!cached) cached = Resources.Load<MapSet>("StreetLifeMaps"); return cached; }

        /// The map picked for play (saved between sessions).
        public static string Choice
        {
            get { var v = PlayerPrefs.GetString(Pref, Default); return string.IsNullOrEmpty(v) ? Default : v; }
            set { PlayerPrefs.SetString(Pref, value ?? Default); PlayerPrefs.Save(); }
        }

        /// The map actually built this play session (set by Apply).
        public static string Current { get; private set; } = Default;
        public static Entry CurrentEntry { get; private set; }

        public Entry Get(string id) { if (maps != null) foreach (var e in maps) if (e != null && e.id == id) return e; return null; }

        /// Hook (first line of MapBuilder.OnEnable): in play mode, hand the builder the chosen map's data before it builds.
        /// Street Life (or a map whose data is missing) leaves the builder exactly as the scene has it.
        public static void Apply(MapBuilder mb)
        {
            if (!Application.isPlaying || !mb) return;
            NavGrid.Ready = false;   // the scene may have been reloaded on a different map: the bots' grid is rebuilt for it
            var set = Load(); var id = Choice;
            var e = set ? set.Get(id) : null;
            if (e == null || e.IsStreet || !e.mapJson || !e.mapBin || !e.library)
            {
                if (e == null || !e.IsStreet) { if (id != Default) Debug.LogWarning("Street Life maps: no data for '" + id + "', playing Street Life"); }
                Current = Default; CurrentEntry = set ? set.Get(Default) : null;
                MapRules.Set(Default);
                if (CurrentEntry != null && CurrentEntry.envJson) MapWorld.Create(mb, CurrentEntry, set);   // Street Life's sky and clouds
                return;
            }
            Current = e.id; CurrentEntry = e;
            mb.mapJson = e.mapJson; mb.mapBin = e.mapBin; mb.dynJson = e.dynJson; mb.dynBin = e.dynBin;
            var lib = Instantiate(e.library); lib.name = e.library.name;
            if (mb.library) { if (!lib.unlitBase) lib.unlitBase = mb.library.unlitBase; if (!lib.lineBase) lib.lineBase = mb.library.lineBase; }
            mb.library = lib;
            MapRules.Set(e.id);
            MapWorld.Create(mb, e, set);
        }
    }
}
