using System.Collections.Generic;
using UnityEngine;

namespace LaneBattle.Game
{
    /// <summary>효과음·음악. Resources/Audio 의 클립을 이름으로 튼다. 같은 소리가 겹치면 짧은 간격 안에서는 한 번만.</summary>
    public static class Sfx
    {
        static GameObject _go;
        static AudioSource[] _pool;
        static AudioSource _music, _musicNext;
        static int _next;
        static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        static readonly Dictionary<string, float> _lastPlay = new Dictionary<string, float>();
        static readonly HashSet<string> _warned = new HashSet<string>();
        static string _musicName = "";
        static float _fade;
        public static bool Muted;

        static float _sfxVolume = -1, _musicVolume = -1;
        public static float SfxVolume
        {
            get { if (_sfxVolume < 0) _sfxVolume = PlayerPrefs.GetFloat("sfx", 0.8f); return _sfxVolume; }
            set { _sfxVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat("sfx", _sfxVolume); }
        }
        public static float MusicVolume
        {
            get { if (_musicVolume < 0) _musicVolume = PlayerPrefs.GetFloat("music", 0.5f); return _musicVolume; }
            set { _musicVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat("music", _musicVolume); if (_music != null) _music.volume = _musicVolume; }
        }

        static void Ensure()
        {
            if (_go != null) return;
            _go = new GameObject("Sfx");
            Object.DontDestroyOnLoad(_go);
            _pool = new AudioSource[14];
            for (int i = 0; i < _pool.Length; i++) { _pool[i] = _go.AddComponent<AudioSource>(); _pool[i].playOnAwake = false; }
            _music = _go.AddComponent<AudioSource>(); _music.loop = true; _music.playOnAwake = false;
            _musicNext = _go.AddComponent<AudioSource>(); _musicNext.loop = true; _musicNext.playOnAwake = false;
        }

        static AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out var c)) return c;
            c = Resources.Load<AudioClip>("Audio/" + name);
            if (c == null && _warned.Add(name)) Debug.LogWarning($"소리 없음: {name}");
            _clips[name] = c;
            return c;
        }

        /// <summary>효과음. minGap 초 안에 같은 소리는 다시 안 튼다 (타워 12개가 동시에 쏠 때 귀 보호).</summary>
        public static void Play(string name, float volume = 1f, float pitch = 1f, float jitter = 0.06f, float minGap = 0.05f)
        {
            if (Muted || Application.isBatchMode && !Application.isPlaying) return;
            Ensure();
            var clip = Clip(name);
            if (clip == null) return;
            float now = Time.unscaledTime;
            if (_lastPlay.TryGetValue(name, out float last) && now - last < minGap) return;
            _lastPlay[name] = now;
            var src = _pool[_next]; _next = (_next + 1) % _pool.Length;
            src.Stop();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume * SfxVolume);
            src.pitch = pitch + (jitter > 0 ? Random.Range(-jitter, jitter) : 0f);
            src.Play();
        }

        public static void Music(string name)
        {
            if (Muted) return;
            Ensure();
            if (_musicName == name && _music.isPlaying) return;
            var clip = Clip(name);
            _musicName = name;
            if (clip == null) { _music.Stop(); return; }
            _music.Stop();
            _music.clip = clip; _music.volume = MusicVolume; _music.loop = true;
            _music.Play();
        }

        public static void StopMusic() { if (_music != null) _music.Stop(); _musicName = ""; }
        public static string CurrentMusic => _musicName;
    }
}
