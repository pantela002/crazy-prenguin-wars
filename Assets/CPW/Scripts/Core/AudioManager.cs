using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Plays the original game's sounds by their Sound section id (e.g. "BasicNukeExplosion", "ThemeMusic").
    /// Files live in Resources/Audio/music/... (copied from cpw-server/assets/music by Tools/build_data.py).
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }

        AudioSource music;
        readonly List<AudioSource> sfxPool = new List<AudioSource>();
        readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, AudioSource> loops = new Dictionary<string, AudioSource>();
        string currentMusic;

        public static void Create(Transform parent)
        {
            if (I != null) return;
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            I = go.AddComponent<AudioManager>();
            I.music = go.AddComponent<AudioSource>();
            I.music.loop = true;
            for (int i = 0; i < 12; i++) I.sfxPool.Add(go.AddComponent<AudioSource>());
        }

        AudioClip Clip(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (cache.TryGetValue(path, out var c)) return c;
            var p = path;
            int dot = p.LastIndexOf('.');
            if (dot > 0) p = p.Substring(0, dot);
            c = Resources.Load<AudioClip>("Audio/" + p);
            cache[path] = c;
            return c;
        }

        static string Pick(List<string> l) => l.Count == 0 ? null : l[Random.Range(0, l.Count)];

        /// <summary>Play a sound effect by Sound id (uses the "Start" list).</summary>
        public static void Sfx(string soundId, float volume = 1f)
        {
            if (I == null || string.IsNullOrEmpty(soundId) || !ProfileService.P.sfxOn) return;
            var r = GameData.Get("Sound", soundId);
            if (r == null) return;
            I.PlayClip(I.Clip(Pick(r.List("Start"))), volume);
        }

        /// <summary>Play a sound by direct path relative to the original assets folder, e.g. "music/menu/click.mp3".</summary>
        public static void SfxPath(string path, float volume = 1f)
        {
            if (I == null || !ProfileService.P.sfxOn) return;
            I.PlayClip(I.Clip(path), volume);
        }

        void PlayClip(AudioClip clip, float volume)
        {
            if (clip == null) return;
            foreach (var s in sfxPool)
            {
                if (!s.isPlaying)
                {
                    s.pitch = 1f;
                    s.PlayOneShot(clip, volume * ProfileService.P.sfxVolume);
                    return;
                }
            }
            sfxPool[0].PlayOneShot(clip, volume * ProfileService.P.sfxVolume);
        }

        /// <summary>Start a looping sound (the Sound record's "Loop" list) under a handle; stop it with StopLoop.</summary>
        public static void Loop(string handle, string soundId, float volume = 1f)
        {
            if (I == null || !ProfileService.P.sfxOn) return;
            var r = GameData.Get("Sound", soundId);
            if (r == null) return;
            var clip = I.Clip(Pick(r.List("Loop"))) ?? I.Clip(Pick(r.List("Start")));
            if (clip == null) return;
            StopLoop(handle);
            var src = I.gameObject.AddComponent<AudioSource>();
            src.clip = clip; src.loop = true; src.volume = volume * ProfileService.P.sfxVolume; src.Play();
            I.loops[handle] = src;
        }

        public static void StopLoop(string handle)
        {
            if (I == null || handle == null) return;
            if (I.loops.TryGetValue(handle, out var s)) { if (s) Destroy(s); I.loops.Remove(handle); }
        }

        /// <summary>Play music by Sound id (uses the "Loop" list, falling back to "Start").</summary>
        public static void Music(string soundId)
        {
            if (I == null || soundId == I.currentMusic) return;
            I.currentMusic = soundId;
            var r = GameData.Get("Sound", soundId);
            AudioClip clip = null;
            if (r != null) clip = I.Clip(Pick(r.List("Loop"))) ?? I.Clip(Pick(r.List("Start")));
            I.music.clip = clip;
            I.ApplySettings();
            if (clip != null && ProfileService.P.musicOn) I.music.Play();
        }

        public void ApplySettings()
        {
            music.volume = ProfileService.P.musicVolume;
            if (!ProfileService.P.musicOn) music.Stop();
            else if (music.clip != null && !music.isPlaying) music.Play();
        }
    }
}
