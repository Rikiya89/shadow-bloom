using System;
using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// The signature bloom. One clock drives three phases:
    ///   Lock (lead-in while the puzzle snaps and brightens) -> Bloom (petals unfold from the solved shadow into 3D)
    ///   -> Resolve (a single soft swell, then a calm resting sculpture that stays until the next action).
    /// Colour follows the same clock: it starts in the solved target's vermilion and settles to paper petals, a vermilion heart and ivory tips.
    /// The mesh is built once per Play (cached across retries) and unfolded by the BloomPetal shader, so the
    /// whole effect is one draw call with no per-frame mesh work. Phase events let audio follow the same clock.
    /// </summary>
    public sealed class BloomEffectController : MonoBehaviour
    {
        /// <summary>Look of the sequence at one moment (shared by Update and the Editor preview).</summary>
        public struct Frame
        {
            public float Clock;   // 0..1 unfold clock fed to the shader
            public float Level;   // brightness multiplier
            public float Warmth;  // solved-target colour -> warm palette
            public float Spin;    // yaw, degrees
        }

        [SerializeField] MeshFilter _filter;
        [SerializeField] MeshRenderer _renderer;

        [Header("Timing (seconds)")]
        [Tooltip("Lock lead-in before the unfold; matches the puzzle's solved snap.")]
        [SerializeField, Range(0f, 1f)] float _lockLead = PuzzleController.SnapDuration;
        [SerializeField, Range(0.8f, 4f)] float _duration = 2f;
        [SerializeField, Range(1f, 5f)] float _finaleDuration = 3.2f;
        [SerializeField, Range(0.2f, 2f)] float _settle = 0.7f;

        [Header("Shape")]
        [Tooltip("Outer extent as a multiple of the puzzle radius (0 = layout default).")]
        [SerializeField, Range(0f, 3f)] float _expansion;
        [SerializeField, Range(0f, 90f)] float _tiltInner = 60f;
        [SerializeField, Range(0f, 90f)] float _tiltOuter = 16f;
        [SerializeField, Range(0.1f, 1f)] float _startScale = 0.25f;
        [SerializeField] float _ribbonWidth = 0.0036f;

        [Header("Motion")]
        [Tooltip("Unwinding twist during the unfold, degrees.")]
        [SerializeField, Range(0f, 180f)] float _twistDegrees = 34f;
        [SerializeField, Range(0f, 20f)] float _spinDegreesPerSecond = 4f;
        [Tooltip("easeOutBack overshoot; 0 = easeOutCubic.")]
        [SerializeField, Range(0f, 2f)] float _overshoot = 0.55f;

        [Header("Light")]
        [Tooltip("Heart of the opened bloom (vermilion). Also the colour an opening petal's leading edge burns toward.")]
        [SerializeField] Color _innerColor = GeometryPalette.Vermilion;
        [Tooltip("Petals (paper).")]
        [SerializeField] Color _outerColor = GeometryPalette.Paper;
        [Tooltip("Tips of the outer petals (ivory).")]
        [SerializeField] Color _tipColor = GeometryPalette.Ivory;
        [Tooltip("Start of the seed-to-flower shift when Play/PreviewAt get no seed colour (e.g. a preview without a " +
                 "puzzle): the solved target's on-screen colour. The game passes PuzzleController.SolvedTargetDisplayColor.")]
        [SerializeField] Color _seedColor = GeometryPalette.Vermilion;
        [Tooltip("Brightness multiplier, capped so the strongest moment (the success swell) lands at full brightness: " +
                 "the display is LDR, so anything above would flatten the swell, rest level and breath. " +
                 "The garden scales its flowers from this value uncapped.")]
        [SerializeField, Range(0.5f, 3f)] float _glow = 1.4f;
        [Tooltip("Resting brightness after the bloom (normal stages / finale).")]
        [SerializeField, Range(0.2f, 1f)] float _restLevel = 0.9f;
        [Tooltip("Single success swell at the end of the unfold. Kept low: no flashes.")]
        [SerializeField, Range(0f, 0.6f)] float _swell = 0.25f;
        [Tooltip("Resting breath amplitude at 0.2 Hz (disabled with reduced motion).")]
        [SerializeField, Range(0f, 0.2f)] float _breath = 0.06f;

        /// <summary>The unfold begins (end of the lock lead-in).</summary>
        public event Action BloomStarted;
        /// <summary>The structure has fully opened; the success swell peaks shortly after.</summary>
        public event Action Settled;
        /// <summary>Resolve done. The bloom keeps resting on screen until Stop / FadeOut.</summary>
        public event Action Finished;

        public bool IsPlaying { get; private set; }
        public bool IsFinished { get; private set; }
        public bool Finale { get; private set; }
        /// <summary>Freezes the clock (game paused) so the sequence never completes unseen.</summary>
        public bool Paused { get; set; }
        public bool ReducedMotion { get; set; }
        public float MainDuration => Finale ? _finaleDuration : _duration;
        public float LockLead => _lockLead;
        public float SettleDuration => _settle;
        public float TotalDuration => _lockLead + MainDuration + _settle;
        /// <summary>Outer extent of the opened bloom, in metres (for the contrast plate).</summary>
        public float Extent => _hasLayout ? _layout.Radius * _layout.Expansion * 1.05f : 0f;

        internal static readonly int BloomId = Shader.PropertyToID("_Bloom");
        internal static readonly int StaggerId = Shader.PropertyToID("_Stagger");
        internal static readonly int TwistId = Shader.PropertyToID("_Twist");
        internal static readonly int OvershootId = Shader.PropertyToID("_Overshoot");
        internal static readonly int WidthId = Shader.PropertyToID("_Width");
        internal static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        internal static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        internal static readonly int WarmthId = Shader.PropertyToID("_Warmth");
        internal static readonly int ColorAId = Shader.PropertyToID("_ColorA");
        internal static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        internal static readonly int ColorCId = Shader.PropertyToID("_ColorC");
        internal static readonly int SeedColorId = Shader.PropertyToID("_SeedColor");
        static readonly int SeedYawId = Shader.PropertyToID("_SeedYaw");

        readonly BloomGeometry _geometry = new BloomGeometry();
        Mesh _mesh;
        MaterialPropertyBlock _mpb;
        BloomLayout _layout;
        bool _hasLayout;
        PuzzleStage _seedStage;
        float _time;
        float _fadeTime, _fadeDuration;
        bool _started, _settled;
        float _spin;
        Color _activeSeed;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _activeSeed = _seedColor;
            _renderer.enabled = false;
        }

        /// <param name="seedStage">Stage whose solved outline seeds the bloom (null = circular seed).</param>
        /// <param name="seedShape">That outline already projected at <paramref name="radius"/> (e.g. the puzzle's
        /// target shape); computed from <paramref name="seedStage"/> when null.</param>
        /// <param name="seedColor">On-screen colour of that solved outline, where the colour shift starts
        /// (<see cref="PuzzleController.SolvedTargetDisplayColor"/>); the serialized fallback when null.</param>
        public void Play(int petals, int layers, float radius, bool finale, PuzzleStage seedStage = null, ProjectedShape seedShape = null,
            Color? seedColor = null)
        {
            Finale = finale;
            _activeSeed = seedColor ?? _seedColor;
            EnsureLayout(petals, layers, radius, finale, seedStage, seedShape);

            _time = 0f;
            _fadeDuration = 0f;
            _started = _settled = false;
            IsFinished = false;
            Paused = false;
            _spin = 0f;
            transform.localRotation = Quaternion.identity;
            IsPlaying = true;
            _renderer.enabled = true;
            // The seed outline is visible from the first frame, so it must start at the lead-in brightness, not black.
            var f = Evaluate(0f, finale, ReducedMotion);
            Apply(f.Clock, f.Level, 1f, f.Warmth);
        }

        /// <summary>Builds (or keeps) the mesh ahead of <see cref="Play"/> so the solve frame does no mesh work.</summary>
        public void Prepare(int petals, int layers, float radius, bool finale, PuzzleStage seedStage = null, ProjectedShape seedShape = null) =>
            EnsureLayout(petals, layers, radius, finale, seedStage, seedShape);

        /// <summary>Gentle exit for stage transitions; Stop() for an immediate cut.</summary>
        public void FadeOut(float seconds)
        {
            if (!IsPlaying) return;
            _fadeDuration = Mathf.Max(0.01f, seconds);
            _fadeTime = 0f;
        }

        public void Stop()
        {
            IsPlaying = false;
            IsFinished = false;
            _fadeDuration = 0f;
            _renderer.enabled = false;
        }

#if UNITY_EDITOR
        /// <summary>Editor preview: shows the sequence frozen at <paramref name="time"/> seconds after Play
        /// (lock lead-in included). Raises no events and leaves the play state untouched.</summary>
        public void PreviewAt(int petals, int layers, float radius, bool finale, float time, PuzzleStage seedStage = null,
            Color? seedColor = null)
        {
            _mpb ??= new MaterialPropertyBlock();
            Finale = finale;
            _activeSeed = seedColor ?? _seedColor;
            EnsureLayout(petals, layers, radius, finale, seedStage, null);
            _renderer.enabled = true;
            var f = Evaluate(time, finale, ReducedMotion);
            transform.localRotation = Quaternion.Euler(0f, f.Spin, 0f);
            Apply(f.Clock, f.Level, 1f, f.Warmth);
        }
#endif

        /// <summary>Time (seconds since Play, pauses excluded) -> clock, brightness, colour shift and spin.</summary>
        public Frame Evaluate(float time, bool finale, bool reducedMotion)
        {
            float main = finale ? _finaleDuration : _duration;
            float t = time - _lockLead;
            var f = new Frame
            {
                Clock = Mathf.Clamp01(t / main),
                Warmth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / (main + _settle))),
                Level = 1f
            };

            // Brightness: full while opening, one smooth swell as it completes, then down to the resting level.
            if (t > main)
            {
                float rest = finale ? 1f : _restLevel;
                float k = Mathf.Clamp01((t - main) / _settle);
                f.Level = Mathf.Lerp(1f, rest, Mathf.SmoothStep(0f, 1f, k)) + _swell * Mathf.Sin(Mathf.PI * k);
            }
            if (!reducedMotion)
            {
                if (t >= main + _settle) f.Level *= 1f + _breath * Mathf.Sin((t - main - _settle) * Mathf.PI * 2f * 0.2f);
                f.Spin = _spinDegreesPerSecond * (finale ? 1f : 0.75f) * time;
            }
            return f;
        }

        void EnsureLayout(int petals, int layers, float radius, bool finale, PuzzleStage seedStage, ProjectedShape seedShape)
        {
            var layout = BloomLayout.Default(petals, layers, radius, finale);
            if (_expansion > 0f) layout.Expansion = _expansion;
            layout.TiltInner = _tiltInner;
            layout.TiltOuter = _tiltOuter;
            layout.StartScale = _startScale;
            if (seedStage != null) layout.Petal = PetalShapes.For(seedStage.curve.kind);
            if (!_hasLayout || !SameLayout(layout, _layout) || _seedStage != seedStage)
            {
                _seedStage = seedStage;
                BuildMesh(layout, seedStage != null ? seedShape : null);
            }
        }

        void BuildMesh(in BloomLayout layout, ProjectedShape seed)
        {
            _layout = layout;
            _hasLayout = true;
            if (seed == null && _seedStage != null)
            {
                var curve = CurveGenerator.Generate(_seedStage.curve);
                seed = new ProjectedShape(curve);
                GeometryProjection.ProjectPlaneLocal(curve, 0f, _seedStage.SolutionRotation, layout.Radius,
                    new PlaneBasis(Vector3.zero, Quaternion.identity), seed);
            }
            _geometry.Build(layout, _overshoot, seed);
            _mesh ??= new Mesh { name = "BloomFlower" };
            BloomMesh.Fill(_mesh, _geometry);
            _filter.sharedMesh = _mesh;
        }

        static bool SameLayout(in BloomLayout a, in BloomLayout b) =>
            a.Petals == b.Petals && a.Layers == b.Layers && Mathf.Approximately(a.Radius, b.Radius) && a.Finale == b.Finale &&
            Mathf.Approximately(a.Expansion, b.Expansion) && Mathf.Approximately(a.TiltInner, b.TiltInner) &&
            Mathf.Approximately(a.TiltOuter, b.TiltOuter) && Mathf.Approximately(a.StartScale, b.StartScale) &&
            a.Petal == b.Petal;

        public Material SharedMaterial => _renderer != null ? _renderer.sharedMaterial : null;
        public Color InnerColor => _innerColor;
        public Color OuterColor => _outerColor;
        public Color TipColor => _tipColor;
        public float Glow => _glow;
        public float Overshoot => _overshoot;

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        void Update()
        {
            if (!IsPlaying || Paused) return;
            float dt = Time.deltaTime;
            _time += dt;

            float main = MainDuration;
            float t = _time - _lockLead;
            if (!_started && t >= 0f) { _started = true; BloomStarted?.Invoke(); }
            if (!_settled && t >= main) { _settled = true; Settled?.Invoke(); }
            if (!IsFinished && t >= main + _settle) { IsFinished = true; Finished?.Invoke(); }

            var f = Evaluate(_time, Finale, ReducedMotion);

            float alpha = 1f;
            if (_fadeDuration > 0f)
            {
                _fadeTime += dt;
                alpha = 1f - Mathf.SmoothStep(0f, 1f, _fadeTime / _fadeDuration);
                if (_fadeTime >= _fadeDuration) { Stop(); return; }
            }

            if (!ReducedMotion)
            {
                _spin += _spinDegreesPerSecond * (Finale ? 1f : 0.75f) * dt;
                transform.localRotation = Quaternion.Euler(0f, _spin, 0f);
            }
            Apply(f.Clock, f.Level, alpha, f.Warmth);
        }

        /// <summary>Highest brightness level <see cref="Evaluate"/> can return (rest levels are at most 1).</summary>
        float PeakLevel => 1f + Mathf.Max(_swell, _breath);

        /// <summary>Shader intensity for a brightness level. The palette's brightest channel is about 1, so the peak
        /// level is scaled to land at 1: the LDR target can then show the swell, rest level and breath.</summary>
        public float ShaderIntensity(float level) => level * Mathf.Min(_glow, 1f / PeakLevel);

        void Apply(float clock, float level, float alpha, float warmth)
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(BloomId, clock);
            _mpb.SetFloat(StaggerId, _layout.Stagger);
            _mpb.SetFloat(TwistId, ReducedMotion ? 0f : _twistDegrees * Mathf.Deg2Rad);
            _mpb.SetFloat(OvershootId, ReducedMotion ? 0f : _overshoot);
            _mpb.SetFloat(WidthId, _ribbonWidth * (Finale ? 1.15f : 1f));
            _mpb.SetFloat(IntensityId, ShaderIntensity(level));
            _mpb.SetFloat(AlphaId, alpha);
            _mpb.SetFloat(WarmthId, warmth);
            _mpb.SetColor(ColorAId, _innerColor);
            _mpb.SetColor(ColorBId, _outerColor);
            _mpb.SetColor(ColorCId, _tipColor);
            // The seed is an on-screen colour: pre-divide by the opening intensity so the shader's seed * _Intensity
            // reproduces it on the solve frame. SetVector skips the colour-space conversion, so convert here.
            var seed = QualitySettings.activeColorSpace == ColorSpace.Linear ? _activeSeed.linear : _activeSeed;
            _mpb.SetVector(SeedColorId, (Vector4)seed / ShaderIntensity(1f));
            _mpb.SetFloat(SeedYawId, transform.localEulerAngles.y * Mathf.Deg2Rad);
            _renderer.SetPropertyBlock(_mpb);
        }
    }

    /// <summary>Packs <see cref="BloomGeometry"/> into the vertex layout the BloomPetal shader expects.</summary>
    public static class BloomMesh
    {
        public static void Fill(Mesh mesh, BloomGeometry g)
        {
            mesh.Clear();
            mesh.SetVertices(g.Positions);
            mesh.SetColors(g.Colors);
            mesh.SetUVs(0, g.Keys);
            mesh.SetUVs(1, g.Neighbours);
            mesh.SetUVs(2, g.Elements);
            mesh.SetUVs(3, g.Styles);
            mesh.SetTriangles(g.Indices, 0, false);
            // Geometry is displaced on the GPU: bounds must cover the fully opened shape or it would be culled.
            mesh.bounds = g.Bounds;
        }
    }
}
