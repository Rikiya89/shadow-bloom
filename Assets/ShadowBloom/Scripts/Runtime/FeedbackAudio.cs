using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// Original procedurally synthesised sounds (no third-party assets), generated once at startup.
    /// Tonal sounds are FM bells (an inharmonic modulator whose depth fades, so each note starts bright and rings
    /// out pure) with a soft room tail baked into the clip, so there is no runtime effect cost.
    /// Puzzle events (hover, grab, release, alignment ticks, lock, bloom) come from a spatial emitter that
    /// follows the puzzle, so they sound from the object in your hands; flow events (stage transition,
    /// completion) play non-spatially. All harmonic material is one C-major pentatonic family so layers agree.
    /// Every sound is optional feedback: the same information is always visible.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class FeedbackAudio : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float _volume = 0.6f;
        [Tooltip("Minimum time between hover sounds, so brushing past the puzzle stays quiet.")]
        [SerializeField] float _hoverCooldown = 0.8f;
        [Tooltip("Alignment (0..1) at which each rising tick plays.")]
        [SerializeField] float[] _tickThresholds = { 0.3f, 0.55f, 0.75f, 0.9f };
        [Tooltip("Tick pitch per threshold, Hz (C-major pentatonic). The last note repeats if this is shorter.")]
        [SerializeField] float[] _tickNotes = { 523.25f, 587.33f, 659.25f, 783.99f, 880f };

        const int Rate = 44100;

        [Header("Sources (created at runtime if empty; assigned sources keep their Inspector settings)")]
        [Tooltip("2D source for stage events.")]
        [SerializeField] AudioSource _flatSource;
        [Tooltip("2D source for UI taps.")]
        [SerializeField] AudioSource _uiSource;
        [Tooltip("Positional source that follows the puzzle pivot.")]
        [SerializeField] AudioSource _spatialSource;

        Transform _anchor;
        AudioClip _hover, _grab, _release, _lock, _bloom, _finale, _transition, _complete, _uiTap;
        AudioClip[] _ticks;
        int _tickLevel;
        float _lastTick;
        float _lastHover = -10f;

        bool _muted, _paused;
        public bool Muted
        {
            get => _muted;
            set
            {
                _muted = value;
                if (!value) return;
                // Muting also silences tails already playing, including sources paused mid-bloom.
                if (_flatSource != null) _flatSource.Stop();
                if (_spatialSource != null) _spatialSource.Stop();
                if (_uiSource != null) _uiSource.Stop();
            }
        }

        public bool Paused
        {
            get => _paused;
            set
            {
                if (_paused == value) return;
                _paused = value;
                foreach (var source in new[] { _flatSource, _spatialSource })
                {
                    if (source == null) continue;
                    if (value) source.Pause(); else source.UnPause();
                }
            }
        }

        void Awake()
        {
            if (_flatSource == null)
            {
                _flatSource = GetComponent<AudioSource>();
                if (_flatSource == null) _flatSource = gameObject.AddComponent<AudioSource>();
                ConfigureFlat(_flatSource);
            }
            if (_uiSource == null)
            {
                _uiSource = gameObject.AddComponent<AudioSource>();
                ConfigureFlat(_uiSource);
            }
            if (_spatialSource == null)
            {
                var emitter = new GameObject("PuzzleAudioEmitter");
                emitter.transform.SetParent(transform, false);
                _spatialSource = emitter.AddComponent<AudioSource>();
                ConfigureSpatial(_spatialSource);
            }

            _hover = Tone("sb_hover", 1046.5f, 0.09f, 0.05f);
            _grab = Click("sb_grab", 880f, 0.05f);
            _release = Click("sb_release", 520f, 0.06f);
            _tickThresholds ??= new float[0];
            _ticks = new AudioClip[_tickThresholds.Length];
            for (int i = 0; i < _ticks.Length; i++) _ticks[i] = Tone("sb_tick" + i, TickNote(i), 0.18f, 0.25f);
            _lock = Chord("sb_lock", new[] { 523.25f, 783.99f }, 0.9f);
            _bloom = Arpeggio("sb_bloom", new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f }, 0.16f, 2.2f);
            _finale = Arpeggio("sb_finale", new[] { 261.63f, 392f, 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f, 1568f }, 0.24f, 4f);
            _transition = Sweep("sb_transition", 392f, 784f, 0.55f);
            _complete = Chord("sb_complete", new[] { 261.63f, 392f, 523.25f, 659.25f, 783.99f }, 2.6f);
            _uiTap = Click("sb_ui_tap", 1318.5f, 0.04f);
        }

        /// <summary>Default settings of a non-positional source (also used by the Editor bake).</summary>
        public static void ConfigureFlat(AudioSource source)
        {
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        /// <summary>Default settings of the puzzle emitter (also used by the Editor bake).</summary>
        public static void ConfigureSpatial(AudioSource source)
        {
            source.playOnAwake = false;
            source.spatialBlend = 0.8f;      // mostly positional, never fully dry so it stays audible when turned away
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 0.4f;
            source.maxDistance = 4f;
            source.dopplerLevel = 0f;
        }

        /// <summary>Puzzle-event sounds are emitted from this transform (the puzzle pivot).</summary>
        public void SetAnchor(Transform anchor) => _anchor = anchor;

        void LateUpdate()
        {
            if (_anchor != null) _spatialSource.transform.position = _anchor.position;
        }

        public void Hover()
        {
            if (Time.time - _lastHover < _hoverCooldown) return;
            _lastHover = Time.time;
            Play(_spatialSource, _hover, 0.12f);
        }

        public void Grab() => Play(_spatialSource, _grab, 0.5f);
        public void Release() => Play(_spatialSource, _release, 0.4f);
        /// <summary>Alignment lock: the shape has been held in place.</summary>
        public void Lock() => Play(_spatialSource, _lock, 0.7f);
        public void Bloom(bool finale) => Play(_spatialSource, finale ? _finale : _bloom, 0.6f);
        public void Transition() => Play(_flatSource, _transition, 0.25f);
        public void Complete() => Play(_flatSource, _complete, 0.45f);
        /// <summary>Panel button accepted (quiet, non-spatial: the panel is not the puzzle).</summary>
        public void UiTap() => Play(_uiSource, _uiTap, 0.22f);

        public void ResetAlignment() => _tickLevel = 0;

        /// <summary>Plays a tick only when alignment climbs past the next threshold; drops reset silently.</summary>
        public void Alignment(float alignment01)
        {
            // Both lengths: the thresholds can be edited live in the Inspector after the clips were built.
            int count = Mathf.Min(_ticks?.Length ?? 0, _tickThresholds.Length);
            _tickLevel = Mathf.Min(_tickLevel, count);
            while (_tickLevel > 0 && alignment01 < _tickThresholds[_tickLevel - 1] - 0.12f) _tickLevel--;
            if (_tickLevel < count && alignment01 >= _tickThresholds[_tickLevel] && Time.time - _lastTick > 0.18f)
            {
                Play(_spatialSource, _ticks[_tickLevel], 0.35f);
                _tickLevel++;
                _lastTick = Time.time;
            }
        }

        float TickNote(int i) => _tickNotes == null || _tickNotes.Length == 0 ? 523.25f : _tickNotes[Mathf.Min(i, _tickNotes.Length - 1)];

        void Play(AudioSource source, AudioClip clip, float gain)
        {
            if (Muted || clip == null || source == null || (Paused && source != _uiSource)) return;
            source.PlayOneShot(clip, gain * _volume);
        }

        void OnDestroy()
        {
            foreach (var clip in new[] { _hover, _grab, _release, _lock, _bloom, _finale, _transition, _complete, _uiTap })
                if (clip != null) Destroy(clip);
            if (_ticks != null) foreach (var clip in _ticks) if (clip != null) Destroy(clip);
        }

        static AudioClip Click(string name, float freq, float length)
        {
            int n = (int)(Rate * length);
            var d = new float[n];
            uint seed = 1234567u;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                seed = seed * 1664525u + 1013904223u;
                float noise = ((seed >> 9) / 8388608f - 1f) * Mathf.Exp(-t * 400f) * 0.3f;
                // The last few milliseconds fade to zero, so a very short click never ends on a step.
                float fade = Mathf.Clamp01((n - 1 - i) / (0.004f * Rate));
                d[i] = (Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 70f) + noise) * 0.6f * fade;
            }
            return Make(name, d);
        }

        /// <summary>
        /// Adds one FM bell note. The modulator runs at <paramref name="ratio"/> times the note (3.5 = bell, 4 =
        /// wooden bar); its depth falls faster than the volume, so the note opens bright and settles to a sine.
        /// </summary>
        /// <param name="ring">Seconds for the note to fade to silence (-60 dB).</param>
        static void AddBell(float[] d, int start, float freq, float gain, float ring, float ratio, float index)
        {
            float ampRate = 6.9f / Mathf.Max(0.02f, ring);
            for (int i = start; i < d.Length; i++)
            {
                float t = (float)(i - start) / Rate;
                float env = Mathf.Min(1f, t * 500f) * Mathf.Exp(-t * ampRate);
                if (t > 0.05f && env < 1e-4f) break;
                float depth = index * Mathf.Exp(-t * ampRate * 3f);
                float phase = 2f * Mathf.PI * freq * t;
                // A quiet, slightly sharp upper partial adds the shimmer of a struck bar.
                float partial = 0.12f * Mathf.Sin(phase * 2.756f) * Mathf.Exp(-t * ampRate * 2f);
                d[i] += (Mathf.Sin(phase + depth * Mathf.Sin(phase * ratio)) + partial) * env * gain;
            }
        }

        /// <summary>Short wooden-bar note: hover and the rising alignment ticks.</summary>
        static AudioClip Tone(string name, float freq, float length, float decay)
        {
            // Long enough for the note to ring out, so the dry sound never stops mid-note.
            float ring = Mathf.Max(length, decay * 1.6f);
            var d = new float[(int)(Rate * ring)];
            AddBell(d, 0, freq, 0.42f, ring, 4f, 1.1f);
            return Make(name, Room(d, 0.35f, 0.16f));
        }

        static AudioClip Chord(string name, float[] freqs, float length)
        {
            var d = new float[(int)(Rate * length)];
            // Struck a few milliseconds apart, low to high, like one mallet stroke across the bars.
            for (int k = 0; k < freqs.Length; k++)
                AddBell(d, (int)(k * 0.012f * Rate), freqs[k], 0.5f / freqs.Length, length, 3.5f, 1.8f);
            return Make(name, Room(d, 0.9f, 0.22f));
        }

        static AudioClip Arpeggio(string name, float[] freqs, float step, float length)
        {
            var d = new float[(int)(Rate * length)];
            for (int k = 0; k < freqs.Length; k++)
                AddBell(d, (int)(k * step * Rate), freqs[k], 0.2f, length - k * step, 3.5f, 1.6f);
            return Make(name, Room(d, 1.1f, 0.24f));
        }

        /// <summary>
        /// Bakes a small room into a clip: four damped comb filters and two all-pass filters (a Schroeder reverb),
        /// plus one quiet echo. Returns a longer buffer holding the dry sound and its tail.
        /// </summary>
        /// <param name="tail">Seconds of tail after the dry sound.</param>
        /// <param name="mix">Level of the room relative to the dry sound.</param>
        static float[] Room(float[] dry, float tail, float mix)
        {
            int n = dry.Length + (int)(tail * Rate);
            var wet = new float[n];
            float[] combSeconds = { 0.0297f, 0.0371f, 0.0411f, 0.0437f };
            foreach (float seconds in combSeconds)
            {
                int len = (int)(seconds * Rate);
                // Feedback for a -60 dB decay over the tail; the one-pole low-pass darkens each repeat.
                float feedback = Mathf.Pow(10f, -3f * seconds / Mathf.Max(0.1f, tail));
                var line = new float[len];
                float damp = 0f;
                for (int i = 0, w = 0; i < n; i++, w = (w + 1) % len)
                {
                    float y = line[w];
                    damp = y * 0.6f + damp * 0.4f;
                    line[w] = (i < dry.Length ? dry[i] : 0f) + damp * feedback;
                    wet[i] += y * 0.25f;
                }
            }
            float[] allPassSeconds = { 0.005f, 0.0017f };
            foreach (float seconds in allPassSeconds)
            {
                int len = (int)(seconds * Rate);
                var line = new float[len];
                for (int i = 0, w = 0; i < n; i++, w = (w + 1) % len)
                {
                    float delayed = line[w];
                    float x = wet[i];
                    wet[i] = delayed - 0.7f * x;
                    line[w] = x + 0.7f * wet[i];
                }
            }
            int echo = (int)(0.23f * Rate);
            var result = new float[n];
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float x = i < dry.Length ? dry[i] : 0f;
                float e = i >= echo && i - echo < dry.Length ? dry[i - echo] * 0.18f : 0f;
                // Fade the very end so a clipped tail never clicks.
                float fade = Mathf.Clamp01((n - i) / (0.05f * Rate));
                result[i] = (x + (wet[i] + e) * mix) * fade;
                peak = Mathf.Max(peak, Mathf.Abs(result[i]));
            }
            if (peak > 0.95f)
                for (int i = 0; i < n; i++) result[i] *= 0.95f / peak;
            return result;
        }

        /// <summary>Soft sine glide with a raised-cosine envelope (stage transition).</summary>
        static AudioClip Sweep(string name, float from, float to, float length)
        {
            int n = (int)(Rate * length);
            var d = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                float f = from * Mathf.Pow(to / from, k * k);
                phase += 2.0 * Mathf.PI * f / Rate;
                float env = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * k);
                d[i] = ((float)System.Math.Sin(phase) + 0.2f * (float)System.Math.Sin(phase * 2.0)) * env * 0.3f;
            }
            return Make(name, Room(d, 0.6f, 0.2f));
        }

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
