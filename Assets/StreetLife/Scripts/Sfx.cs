// Sound, using the web game's own files (Resources/StreetLife/sfx and /music) and its mixing rule:
// volume / (1 + max(0, distance - 2) / range), panned by where the sound is relative to the camera.
// Music: "menu", "rounds" (countdowns and between rounds) and "matchend", cross-faded, at the web game's default 50%.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class Sfx : MonoBehaviour
    {
        public static float SfxVolume = 1f, MusicVolume = .5f;
        static Sfx inst;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        // every effect is loaded while the menu is up, one a frame, instead of on its first play in the middle of a fight
        System.Collections.IEnumerator Start()
        {
            yield return null; yield return null;
            var all = Resources.LoadAll<AudioClip>("StreetLife/sfx");
            foreach (var c in all)
            {
                if (!c) continue;
                clips["sfx/" + c.name] = c;
                if (c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
                yield return null;
            }
        }
        AudioSource musicA, musicB; string musicName; float musicFade = 1;

        static Sfx I
        {
            get
            {
                if (!inst)
                {
                    var go = new GameObject("Sfx");
                    DontDestroyOnLoad(go);
                    inst = go.AddComponent<Sfx>();
                }
                return inst;
            }
        }

        AudioClip Clip(string folder, string name)
        {
            var key = folder + "/" + name;
            if (!clips.TryGetValue(key, out var c)) { c = Resources.Load<AudioClip>("StreetLife/" + key); clips[key] = c; }
            return c;
        }

        AudioSource Free()
        {
            foreach (var s in pool) if (!s.isPlaying) return s;
            if (pool.Count >= 32) return pool[0];
            var a = gameObject.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 0; a.mute = menuMute; pool.Add(a); return a;
        }

        /// A positional one-shot by the web game's sfx name (e.g. "m4", "step2", "explode").
        public static AudioSource Play(string name, Vector3 at, float vol = 1, float range = 14, float pitch = 1)
        {
            var cam = Camera.main;
            float pan = 0;
            if (cam)
            {
                var d = at - cam.transform.position;
                float dist = d.magnitude;
                vol /= 1 + Mathf.Max(0, dist - 2) / Mathf.Max(.01f, range);
                var flat = new Vector3(d.x, 0, d.z);
                if (flat.sqrMagnitude > 1e-4f) pan = Vector3.Dot(flat.normalized, cam.transform.right) * .7f;
            }
            if (vol < .01f) return null;
            return I.Emit(name, vol, pan, pitch);
        }

        public static AudioSource Play2D(string name, float vol = 1, float pitch = 1) => I.Emit(name, vol, 0, pitch);

        AudioSource Emit(string name, float vol, float pan, float pitch)
        {
            var c = Clip("sfx", name); if (!c) return null;
            var s = Free(); s.clip = c; s.volume = Mathf.Clamp01(vol * SfxVolume); s.panStereo = pan; s.pitch = pitch; s.loop = false; s.Play();
            return s;
        }

        /// Cross-fade to a music track (null for silence).
        public static void Music(string name) => I.SetMusic(name);

        void SetMusic(string name)
        {
            if (name == musicName) return;
            musicName = name;
            if (!musicA) { musicA = gameObject.AddComponent<AudioSource>(); musicB = gameObject.AddComponent<AudioSource>(); foreach (var m in new[] { musicA, musicB }) { m.loop = true; m.playOnAwake = false; m.volume = 0; } }
            (musicA, musicB) = (musicB, musicA);       // A is the new one, B fades out
            if (!string.IsNullOrEmpty(name))
            {
                var c = Clip("music", name);
                if (c) { musicA.clip = c; musicA.volume = 0; musicA.Play(); }
            }
            musicFade = 0;
        }

        // the main menu plays music only: every other source (effects, map ambience, car engines, surf) is muted while it is
        // up, and comes back when the match starts. UI clicks have their own source.
        bool menuMute; float menuMuteT; AudioSource uiSrc;
        void MenuMute()
        {
            bool m = MainMenu.Open; menuMuteT -= Time.unscaledDeltaTime;
            if (m == menuMute && !(m && menuMuteT <= 0)) return;
            menuMute = m; menuMuteT = .5f;
            foreach (var a in FindObjectsByType<AudioSource>(FindObjectsInactive.Include)) if (a && a != musicA && a != musicB && a != uiSrc) a.mute = m;
        }
        /// a menu or HUD click: heard in the menu too
        public static void Ui(string name, float vol = 1, float pitch = 1)
        {
            var c = I.Clip("sfx", name); if (!c) return;
            if (!I.uiSrc) { I.uiSrc = I.gameObject.AddComponent<AudioSource>(); I.uiSrc.playOnAwake = false; I.uiSrc.spatialBlend = 0; }
            I.uiSrc.pitch = pitch; I.uiSrc.PlayOneShot(c, Mathf.Clamp01(vol * SfxVolume));
        }

        void Update()
        {
            MenuMute();
            if (!musicA) return;
            musicFade = Mathf.MoveTowards(musicFade, 1, Time.unscaledDeltaTime / 1.2f);
            float target = MusicVolume * MusicVolume;   // slider curve: quiet at the low end like the web game
            if (!string.IsNullOrEmpty(musicName)) musicA.volume = target * musicFade; else musicA.volume = Mathf.MoveTowards(musicA.volume, 0, Time.unscaledDeltaTime);
            musicB.volume = target * (1 - musicFade);
            if (musicB.volume <= .001f && musicB.isPlaying) musicB.Stop();
        }
    }
}
