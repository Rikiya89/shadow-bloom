using System;
using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// Runs one puzzle on the surface frame: renders the 3D curve, its live projection and the target,
    /// evaluates the match at a bounded rate and raises <see cref="Solved"/> once the match is held.
    /// Rotation input arrives through <see cref="SetPivotWorldRotation"/> / <see cref="ApplyWorldRotationDelta"/>
    /// from whichever adapter is active (hand grab transformer, desktop mouse).
    /// </summary>
    public sealed class PuzzleController : MonoBehaviour
    {
        [Header("Scene")]
        [SerializeField] Transform _surfaceFrame;
        [SerializeField] Transform _pivot;
        [SerializeField] MeshFilter _curveFilter;
        [SerializeField] MeshRenderer _curveRenderer;
        [SerializeField] MeshFilter _ghostFilter;
        [SerializeField] MeshRenderer _ghostRenderer;
        [SerializeField] MeshFilter _currentFilter;
        [SerializeField] MeshRenderer _currentRenderer;
        [SerializeField] MeshFilter _targetFilter;
        [SerializeField] MeshRenderer _targetRenderer;
        [SerializeField] Transform _plate;
        [SerializeField] SphereCollider _grabVolume;
        [Tooltip("The Interaction SDK grab object. Off while the puzzle is hidden: a live grab volume outranks the hand ray, " +
                 "which hid the laser and swallowed Confirm on the setup panel.")]
        [SerializeField] GameObject _grabInteraction;

        [Header("Look")]
        [SerializeField] float _tubeRadius = 0.0034f;
        [SerializeField] float _currentWidth = 0.0036f;
        [SerializeField] float _targetWidth = 0.007f;
        [SerializeField] Color _neutral = GeometryPalette.PaperMuted;
        [SerializeField] Color _highlight = GeometryPalette.Paper;
        [SerializeField] Color _targetColor = GeometryPalette.WithAlpha(GeometryPalette.Paper, GeometryPalette.TargetAlpha);
        [SerializeField] Color _shadowColor = GeometryPalette.WithAlpha(GeometryPalette.PaperMuted, GeometryPalette.ShadowAlpha);
        [Tooltip("Target colour at full hold. Same meaning as the panel's HOLD STEADY.")]
        [SerializeField] Color _holdColor = GeometryPalette.WithAlpha(GeometryPalette.Vermilion, GeometryPalette.HoldAlpha);

        [Header("Feedback")]
        [SerializeField, Range(0f, 0.5f)] float _hoverBoost = 0.18f;
        [SerializeField, Range(0f, 1f)] float _grabBoost = 0.35f;
        [Tooltip("Near-solution pulse rate. Kept well under 3 Hz.")]
        [SerializeField, Range(0.3f, 2f)] float _pulseHz = 1.1f;
        [SerializeField, Range(0f, 1f)] float _lockSwell = 0.6f;
        [SerializeField, Range(0.1f, 1.5f)] float _introSeconds = 0.6f;
        [Tooltip("Resting size of the soft contrast plate under the puzzle, in puzzle radii.")]
        [SerializeField] float _plateBaseScale = 1.45f;
        [Tooltip("Length of one tutorial ghost demonstration loop.")]
        [SerializeField, Range(1f, 8f)] float _ghostCycleSeconds = 3.2f;
        [Tooltip("How far idle hints turn the ghost toward the solution (tutorial always 1).")]
        [SerializeField, Range(0f, 1f)] float _idleHintAmount = 0.35f;

        [Header("Matching")]
        [Tooltip("Shape comparisons per second. The projection mesh still updates every frame.")]
        [SerializeField, Range(2f, 30f)] float _matchHz = 12f;

        public event Action Solved;
        public event Action<bool> GrabChanged;
        public event Action<bool> HoverChanged;
        /// <summary>The pose was reset (stage start / retry); rotation adapters must rebase their grab.</summary>
        public event Action PoseReset;

        public PuzzleStage Stage { get; private set; }
        public float Error { get; private set; } = float.PositiveInfinity;
        public float Alignment { get; private set; }
        public float HoldProgress => _tracker?.HoldProgress01 ?? 0f;
        public bool IsWithinTolerance => _tracker != null && _tracker.IsWithin;
        public bool IsSolved => _tracker != null && _tracker.Solved;
        bool _inputEnabled;
        public bool InputEnabled
        {
            get => _inputEnabled;
            set
            {
                if (_inputEnabled == value) return;
                _inputEnabled = value;
                // Interrupted tracking/pause must never contribute time to a held solution.
                if (!IsSolved) _tracker?.Reset();
                _matchTimer = 0f;
                _lastEvalTime = Time.time;
                PoseReset?.Invoke();
                UpdateGrabbable();
            }
        }
        public bool IsGrabbed { get; private set; }
        public bool IsHovered { get; private set; }
        public bool ReducedMotion { get; set; }
        /// <summary>Freeze snap, ghost, reveal, pulse and dissolve without changing the solve state.</summary>
        public bool PresentationPaused { get; set; }
        float _leniency = 1f, _holdScale = 1f;
        /// <summary>Tolerance multiplier (Assist). Applies immediately, including to the stage in progress.</summary>
        public float Leniency
        {
            get => _leniency;
            set { _leniency = Mathf.Max(0.1f, value); ApplyLeniency(); }
        }
        /// <summary>Hold-time multiplier (Assist).</summary>
        public float HoldScale
        {
            get => _holdScale;
            set { _holdScale = Mathf.Max(0f, value); ApplyLeniency(); }
        }
        public float Tolerance => Stage != null ? Stage.tolerance * _leniency : 0f;
        public float PuzzleRadius { get; private set; } = 0.12f;
        public float PivotHeight => _pivotHeight;
        public Transform Pivot => _pivot;
        /// <summary>The solved outline as drawn by the target ribbon (plane-local, current radius).</summary>
        public ProjectedShape TargetShape => _targetRenderShape;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int PulseId = Shader.PropertyToID("_Pulse");
        static readonly int RevealId = Shader.PropertyToID("_Reveal");
        static readonly int DashFillId = Shader.PropertyToID("_DashFill");
        static readonly int OutlineId = Shader.PropertyToID("_Outline");

        StagePuzzle _puzzle;
        SolveTracker _tracker;
        CurveSet _renderCurve;
        ProjectedShape _renderShape;
        ProjectedShape _targetRenderShape;
        RibbonMesh _currentRibbon;
        RibbonMesh _targetRibbon;
        Mesh _tubeMesh;
        MaterialPropertyBlock _mpb;
        float _pivotHeight = 0.2f;
        float _farError = 1f;
        float _matchTimer;
        float _lastEvalTime;
        float _displayAlignment;
        Quaternion _lastProjectedRotation;
        Vector3 _lastProjectedPosition;
        bool _projectionDirty = true;

        // Tutorial ghost: loops initial -> solution to demonstrate the gesture without text.
        bool _ghostActive;
        float _ghostTime;

        // Snap-to-solution animation after solving.
        bool _snapping;
        Quaternion _snapFrom;
        float _snapTime;
        /// <summary>Solved snap length; the bloom's lock lead-in matches it.</summary>
        public const float SnapDuration = 0.45f;

        // Feedback state, smoothed so every change reads as light easing in, never as a flash.
        float _hover01, _grab01;
        float _lockTime = -1f;
        float _reveal = 1f;
        float _revealFrom, _revealTo = 1f, _revealTime, _revealDuration;
        float _curveFade = 1f, _curveFadeRate;
        float _plateScale, _plateTarget;
        float _visualTime;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _plateScale = _plateTarget = _plateBaseScale;
        }

        void ApplyLeniency()
        {
            if (_tracker == null || Stage == null) return;
            _tracker.Tolerance = Tolerance;
            _tracker.HoldSeconds = Stage.holdSeconds * _holdScale;
        }

        public void LoadStage(PuzzleStage stage, float puzzleRadius, float pivotHeight)
        {
            ReleaseProjectionMeshes();
            Stage = stage;
            _puzzle = new StagePuzzle(stage, puzzleRadius);
            _tracker = new SolveTracker(Tolerance, stage.holdSeconds * _holdScale);
            _renderCurve = CurveGenerator.Generate(stage.curve);
            _renderShape = new ProjectedShape(_renderCurve);
            _targetRenderShape = new ProjectedShape(_renderCurve);
            _currentRibbon = new RibbonMesh(_renderCurve.StripStart, _renderCurve.StripLength, _renderCurve.StripClosed, "CurrentProjection");
            _targetRibbon = new RibbonMesh(_renderCurve.StripStart, _renderCurve.StripLength, _renderCurve.StripClosed, "TargetProjection");
            _currentFilter.sharedMesh = _currentRibbon.Mesh;
            _targetFilter.sharedMesh = _targetRibbon.Mesh;
            _farError = Mathf.Max(_puzzle.EvaluatePlaneLocal(stage.InitialRotation), stage.tolerance * 2f);
            ApplyPlacement(puzzleRadius, pivotHeight);
            ResetPose();
            AnimateReveal(0f, 1f, _introSeconds);   // each stage draws itself on
            _curveFade = 1f;
            _curveFadeRate = 0f;
            _plateTarget = _plateBaseScale;
        }

        /// <summary>Bloom hand-off: the solved 3D form and its projections draw themselves off.</summary>
        public void DissolveCurve(float seconds) => _curveFadeRate = 1f / Mathf.Max(0.05f, seconds);

        /// <summary>Undoes <see cref="DissolveCurve"/> (e.g. the table is set up again without reloading the stage).</summary>
        public void RestoreCurve()
        {
            _curveFade = 1f;
            _curveFadeRate = 0f;
        }

        /// <summary>Widens the soft contrast plate under the bloom (extent in metres; 0 restores the default).</summary>
        public void ExpandPlate(float extent) =>
            _plateTarget = extent > 0f ? Mathf.Max(_plateBaseScale, extent / PuzzleRadius * 1.25f) : _plateBaseScale;

        /// <summary>Draws the curve and both projections off (stage exit), the reverse of the stage intro.</summary>
        public void BeginOutro(float seconds) => AnimateReveal(_reveal, 0f, seconds);

        void AnimateReveal(float from, float to, float seconds)
        {
            _revealFrom = from;
            _revealTo = to;
            _revealTime = 0f;
            _revealDuration = Mathf.Max(0.01f, seconds);
            _reveal = from;
        }

        /// <summary>Scale/height changed (new surface). Keeps the current puzzle rotation.</summary>
        public void ApplyPlacement(float puzzleRadius, float pivotHeight)
        {
            PuzzleRadius = puzzleRadius;
            _pivotHeight = pivotHeight;
            _pivot.localPosition = new Vector3(0f, pivotHeight, 0f);
            _pivot.localScale = Vector3.one * puzzleRadius;
            if (_plate != null) _plate.localScale = Vector3.one * puzzleRadius * _plateScale;
            if (_grabVolume != null) _grabVolume.radius = 1.2f;
            if (Stage == null) return;

            _puzzle.SetScale(puzzleRadius);
            _tubeMesh = LineMeshBuilder.BuildTube(_renderCurve, _tubeRadius / puzzleRadius, 6, _tubeMesh);
            _curveFilter.sharedMesh = _tubeMesh;
            if (_ghostFilter != null) _ghostFilter.sharedMesh = _tubeMesh;

            var canonical = new PlaneBasis(Vector3.zero, Quaternion.identity);
            GeometryProjection.ProjectPlaneLocal(_renderCurve, pivotHeight, Stage.SolutionRotation, puzzleRadius, canonical, _targetRenderShape);
            _targetRibbon.Update(_targetRenderShape.Points, _targetWidth * Stage.lineWidthScale, 0.0015f);
            _projectionDirty = true;
        }

        public void ResetPose()
        {
            _snapping = false;
            _lockTime = -1f;
            _pivot.localRotation = Stage != null ? Stage.InitialRotation : Quaternion.identity;
            _tracker?.Reset();
            Error = float.PositiveInfinity;
            Alignment = 0f;
            _displayAlignment = 0f;
            _matchTimer = 0f;
            _lastEvalTime = Time.time;
            _projectionDirty = true;
            PoseReset?.Invoke();
        }

        public bool IsVisible => _curveRenderer.enabled;

        /// <summary>Target outline colour and GlowLine intensity at a hold of 0 (dashed) .. 1 (solved). The visual
        /// update and <see cref="SolvedTargetDisplayColor"/> both read it, so the bloom's start colour cannot drift.</summary>
        Color TargetColor(PuzzleStage stage, float hold, out float intensity)
        {
            intensity = 1f + 0.4f * hold;
            // stage is kept for API stability; the hold colour is global.
            var c = Color.Lerp(_targetColor, _holdColor, hold);
            c.a = Mathf.Lerp(_targetColor.a, 0.9f, hold);
            return c;
        }

        /// <summary>What the solved target outline of <paramref name="stage"/> shows on screen: colour times intensity,
        /// clipped per channel like the LDR target clips GlowLine (no tone mapping). The bloom starts in this colour,
        /// so the solve frame does not pop. Reads serialized values only, so it also works for Edit-mode previews.</summary>
        public Color SolvedTargetDisplayColor(PuzzleStage stage)
        {
            var c = TargetColor(stage, 1f, out float intensity);
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            if (linear) c = c.linear;
            c = new Color(Mathf.Clamp01(c.r * intensity), Mathf.Clamp01(c.g * intensity), Mathf.Clamp01(c.b * intensity), 1f);
            return linear ? c.gamma : c;
        }

        /// <summary>Whether hands/controllers can grab the puzzle at all (only while it is shown and takes input).</summary>
        public bool IsGrabbable => _grabInteraction == null || _grabInteraction.activeSelf;

        // A grab volume outranks the hand ray (the rig lets only one interactor hover), so it is live only while a grab
        // can do something. Otherwise a hand near the puzzle hides the laser and the panel button (Confirm, Next on the
        // how-to-play cards, Resume) cannot be pressed (device log 2026-11-07: 28 ignored grabs, Next never registered).
        void UpdateGrabbable()
        {
            if (_grabInteraction != null) _grabInteraction.SetActive(IsVisible && _inputEnabled);
        }

        public void SetVisible(bool visible)
        {
            _curveRenderer.enabled = visible;
            _currentRenderer.enabled = visible;
            _targetRenderer.enabled = visible;
            if (_plate != null) _plate.gameObject.SetActive(visible);
            UpdateGrabbable();
            if (!visible) SetTutorialGhost(false);
        }

        public void SetTutorialGhost(bool active)
        {
            _ghostActive = active && _ghostRenderer != null;
            _ghostTime = 0f;
            if (_ghostRenderer != null) _ghostRenderer.enabled = _ghostActive;
        }

        public void SetPivotWorldRotation(Quaternion worldRotation)
        {
            if (!InputEnabled || _snapping) return;
            _pivot.rotation = worldRotation;
        }

        public void ApplyWorldRotationDelta(Quaternion delta)
        {
            if (!InputEnabled || _snapping) return;
            _pivot.rotation = delta * _pivot.rotation;
        }

        /// <summary>Called by input adapters so audio, tutorial and UI can react to grab/release.</summary>
        public void NotifyGrab(bool grabbed)
        {
            if (IsGrabbed == grabbed) return;
            Debug.Log($"[ShadowBloom] puzzle grab {(grabbed ? "begin" : "end")} (input {InputEnabled})");
            IsGrabbed = grabbed;
            GrabChanged?.Invoke(grabbed);
        }

        /// <summary>Called by the hand interaction adapter when a hand can grab the puzzle (ISDK hover state).</summary>
        public void NotifyHover(bool hovered)
        {
            if (IsHovered == hovered) return;
            IsHovered = hovered;
            HoverChanged?.Invoke(hovered);
        }

        /// <summary>Debug path: jump to the stage's known solution.</summary>
        public void DebugSetSolution()
        {
            if (Stage != null) _pivot.localRotation = Stage.SolutionRotation;
        }

        void Update()
        {
            if (Stage == null || PresentationPaused) return;

            _visualTime += Time.deltaTime;
            if (_snapping) TickSnap();

            if (_projectionDirty || _pivot.rotation != _lastProjectedRotation || _pivot.position != _lastProjectedPosition)
            {
                UpdateProjectionMesh();
            }

            if (InputEnabled && !IsSolved)
            {
                _matchTimer += Time.deltaTime;
                if (_matchTimer >= 1f / _matchHz)
                {
                    _matchTimer = 0f;
                    Evaluate();
                }
            }

            if (_ghostActive) TickGhost();
            UpdateVisuals();
        }

        void Evaluate()
        {
            float now = Time.time;
            float dt = Mathf.Min(now - _lastEvalTime, 0.25f); // a hitch must not count as held time
            _lastEvalTime = now;

            var plane = new PlaneBasis(_surfaceFrame.position, _surfaceFrame.rotation);
            Error = _puzzle.Evaluate(_pivot.localToWorldMatrix, plane);
            Alignment = AlignmentFeedback.Evaluate(Error, Tolerance, _farError);

            if (_tracker.Tick(Error, dt))
            {
                Alignment = 1f;
                _lockTime = 0f;
                StartSnap();
                Solved?.Invoke();
            }
        }

        void UpdateProjectionMesh()
        {
            var plane = new PlaneBasis(_surfaceFrame.position, _surfaceFrame.rotation);
            GeometryProjection.Project(_renderCurve, _pivot.localToWorldMatrix, plane, -plane.Normal, _renderShape);
            _currentRibbon.Update(_renderShape.Points, _currentWidth * Stage.lineWidthScale, 0.003f);
            _lastProjectedRotation = _pivot.rotation;
            _lastProjectedPosition = _pivot.position;
            _projectionDirty = false;
        }

        void StartSnap()
        {
            _snapping = true;
            _snapFrom = _pivot.localRotation;
            _snapTime = 0f;
        }

        void TickSnap()
        {
            _snapTime += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, _snapTime / SnapDuration);
            _pivot.localRotation = Quaternion.Slerp(_snapFrom, NearestEquivalentSolution(_snapFrom), t);
            if (_snapTime >= SnapDuration) _snapping = false;
        }

        // Snap to the equivalent solution closest to where the player ended (symmetric yaw steps, and the
        // upside-down flip for mirror-symmetric families), so the shape settles instead of spinning.
        // If no listed equivalent is close, the player's own (already matching) pose is kept.
        Quaternion NearestEquivalentSolution(Quaternion from) => NearestEquivalent(from, 25f, from);

        // Equivalent solution closest to `from` within maxAngle degrees, else `fallback`.
        Quaternion NearestEquivalent(Quaternion from, float maxAngle, Quaternion fallback)
        {
            int folds = StageSymmetry.Folds(Stage.curve);
            bool mirror = StageSymmetry.Mirror(Stage.curve.kind);

            var best = fallback;
            float bestAngle = maxAngle;
            for (int flip = 0; flip < (mirror ? 2 : 1); flip++)
            {
                var flipRot = flip == 0 ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f);
                for (int i = 0; i < folds; i++)
                {
                    var candidate = Quaternion.Euler(0f, 360f * i / folds, 0f) * Stage.SolutionRotation * flipRot;
                    float a = Quaternion.Angle(from, candidate);
                    if (a < bestAngle) { bestAngle = a; best = candidate; }
                }
            }
            return best;
        }

        void TickGhost()
        {
            float cycle = _ghostCycleSeconds;
            _ghostTime = (_ghostTime + Time.deltaTime) % cycle;
            float phase = _ghostTime / cycle;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase * 1.6f - 0.15f));
            var ghost = _ghostRenderer.transform;
            // The ring teaches the complete gesture. Later idle hints suggest a direction without giving
            // away the exact solution, and never move the real puzzle or contribute to its hold timer.
            // Aim at the equivalent solution nearest the current pose, so a partial turn points the shortest way.
            float amount = Stage.isTutorial ? 1f : _idleHintAmount;
            var goal = NearestEquivalent(_pivot.localRotation, 181f, Stage.SolutionRotation);
            ghost.localRotation = Quaternion.Inverse(_pivot.localRotation) * Quaternion.Slerp(_pivot.localRotation, goal, t * amount);
            float fade = Mathf.Sin(phase * Mathf.PI);
            var color = _highlight;
            color.a = 0.3f * fade;
            SetBlock(_ghostRenderer, color, 0.7f, 0f);
        }

        void UpdateVisuals()
        {
            float dt = Time.deltaTime;
            float ease = 1f - Mathf.Exp(-14f * dt);
            _displayAlignment = Mathf.Lerp(_displayAlignment, IsSolved ? 1f : Alignment, 1f - Mathf.Exp(-8f * dt));
            _hover01 = Mathf.Lerp(_hover01, IsHovered && InputEnabled ? 1f : 0f, ease);
            _grab01 = Mathf.Lerp(_grab01, IsGrabbed && InputEnabled ? 1f : 0f, ease);

            if (_revealDuration > 0f)
            {
                _revealTime += dt;
                float k = Mathf.Clamp01(_revealTime / _revealDuration);
                _reveal = Mathf.Lerp(_revealFrom, _revealTo, 1f - (1f - k) * (1f - k));
                if (k >= 1f) _revealDuration = 0f;
            }

            if (_curveFadeRate > 0f) _curveFade = Mathf.Max(0f, _curveFade - _curveFadeRate * dt);
            if (_plate != null && Mathf.Abs(_plateScale - _plateTarget) > 1e-3f)
            {
                _plateScale = Mathf.Lerp(_plateScale, _plateTarget, 1f - Mathf.Exp(-3f * dt));
                _plate.localScale = Vector3.one * PuzzleRadius * _plateScale;
            }

            // Lock: one smooth swell (0.6 s half-sine) as the solved shape stabilises.
            float lockSwell = 0f;
            if (_lockTime >= 0f)
            {
                _lockTime += dt;
                if (_lockTime < 0.6f) lockSwell = _lockSwell * Mathf.Sin(Mathf.PI * _lockTime / 0.6f);
            }

            // Hover brightens slightly; grab adds a clear lift.
            var lineColor = Color.Lerp(Stage.lineColor * _neutral, Stage.highlightColor, Mathf.Clamp01(_displayAlignment + 0.15f * _grab01));
            float hold = IsSolved ? 1f : HoldProgress;
            float pulse = 0f;
            if (IsWithinTolerance && !IsSolved)
            {
                float wave = ReducedMotion ? 0.5f : 0.5f + 0.5f * Mathf.Sin(_visualTime * Mathf.PI * 2f * _pulseHz);
                pulse = wave * (0.15f + 0.25f * hold);
            }
            float intensity = 1f + 0.8f * _displayAlignment + _hoverBoost * _hover01 + _grabBoost * _grab01 + lockSwell;

            SetBlock(_curveRenderer, lineColor, intensity, pulse, _reveal * _curveFade * _curveFade);
            var shadow = Color.Lerp(_shadowColor, Stage.highlightColor, _displayAlignment * 0.65f);
            SetBlock(_currentRenderer, shadow, 0.95f + 0.35f * _displayAlignment, pulse, _reveal * _curveFade);

            // Target: the dashes fill in to a solid line as the match is held, so "keep holding" needs no text.
            var target = TargetColor(Stage, hold, out float targetIntensity);
            SetBlock(_targetRenderer, target, targetIntensity, 0f, _reveal * _curveFade);
            _targetRenderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(DashFillId, Mathf.Lerp(0.55f, 1f, hold));
            _targetRenderer.SetPropertyBlock(_mpb);
        }

        void ReleaseProjectionMeshes()
        {
            if (_currentRibbon != null) Destroy(_currentRibbon.Mesh);
            if (_targetRibbon != null) Destroy(_targetRibbon.Mesh);
            _currentRibbon = null;
            _targetRibbon = null;
        }

        void OnDestroy()
        {
            ReleaseProjectionMeshes();
            if (_tubeMesh != null) Destroy(_tubeMesh);
        }

        void SetBlock(Renderer r, Color c, float intensity, float pulse, float reveal = 1f)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, c);
            _mpb.SetFloat(IntensityId, intensity);
            _mpb.SetFloat(PulseId, pulse);
            _mpb.SetFloat(RevealId, reveal);
            _mpb.SetFloat(OutlineId, r == _currentRenderer || r == _targetRenderer ? 1f : 0f);
            r.SetPropertyBlock(_mpb);
        }
    }
}
