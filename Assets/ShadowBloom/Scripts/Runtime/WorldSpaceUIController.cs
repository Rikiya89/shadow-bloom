using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowBloom
{
    public struct UiView
    {
        /// <summary>Small caps line above the title: stage number, setup status, "PAUSED".</summary>
        public string Eyebrow;
        public UiTone EyebrowTone;
        /// <summary>Show a status dot before the eyebrow (setup status).</summary>
        public bool EyebrowDot;
        public string Title;
        public UiTone TitleTone;
        public string Subtitle;
        public UiTone SubtitleTone;
        /// <summary>One short instruction (accent). Coaching and placement guidance.</summary>
        public string Hint;
        /// <summary>Null hides the primary button.</summary>
        public string Primary;
        public bool Retry;
        public bool Pause;
        public string PauseLabel;
        public bool Reposition;
        public bool Alignment;
        /// <summary>Show the comfort toggles (sound, motion). Used on the pause panel.</summary>
        public bool Settings;
        /// <summary>Offer "Restart from stage 1" (pause panel).</summary>
        public bool Restart;
        /// <summary>Offer the Daily Shadow (secondary row under the primary button), labelled <see cref="DailyLabel"/>.</summary>
        public bool Daily;
        public string DailyLabel;
        /// <summary>Offer the how-to-play cards again (pause panel).</summary>
        public bool HowToPlay;
        /// <summary>Stage progress pips: index of the current stage, or -1 to hide them.</summary>
        public int Progress;
        public int ProgressCount;
        /// <summary>All stages done: every pip shows as complete.</summary>
        public bool ProgressComplete;
        /// <summary>The current stage is solved: its pip shows as done (success), not as in progress.</summary>
        public bool ProgressCurrentDone;
        /// <summary>Colour of the hairline at the top of the panel (state signature).</summary>
        public UiTone Accent;
        /// <summary>Nothing but a status notice is shown (bloom, stage transition).</summary>
        public bool Hidden;

        public static UiView HiddenView => new UiView { Hidden = true, Progress = -1 };
    }

    /// <summary>
    /// The one world-space panel beside the puzzle ("Spatial Observatory" design system, tokens in
    /// <see cref="UiTheme"/>). Beside the puzzle, never above it, so it cannot cover the hands, the projected target
    /// or the bloom. Content is stacked top-down; the panel is anchored at its bottom edge, so it grows upward and
    /// never into the table, and lines added at the top (the hands-lost notice) never move the buttons.
    /// The canvas root is resized to the visible panel, which keeps the Interaction SDK poke/ray clip bounds
    /// (driven by the root rect) exactly on the drawn buttons. State changes fade out, re-lay-out and fade in;
    /// presses are ignored until the panel is fully revealed, and a cooldown guards against double pokes.
    /// </summary>
    public sealed class WorldSpaceUIController : MonoBehaviour
    {
        [SerializeField] UiTheme _theme;
        [SerializeField] RectTransform _panel;
        [SerializeField] CanvasGroup _group;
        [SerializeField] Image _panelImage;
        [SerializeField] Image _accentRule;
        [Tooltip("Large stage index numeral ('01') in the grid's left column.")]
        [SerializeField] TMP_Text _stageIndex;
        [Tooltip("Divider between the content and the buttons.")]
        [SerializeField] Image _divider;
        [SerializeField] RectTransform _notice;
        [SerializeField] Image _noticeBar;
        [SerializeField] TMP_Text _eyebrow;
        [SerializeField] Image _eyebrowDot;
        [SerializeField] RectTransform _pips;
        [SerializeField] TMP_Text _title;
        [SerializeField] TMP_Text _subtitle;
        [SerializeField] TMP_Text _hint;
        [SerializeField] TMP_Text _status;
        [SerializeField] GameObject _alignmentRoot;
        [SerializeField] Image _alignmentFill;
        [SerializeField] TMP_Text _alignmentLabel;
        [SerializeField] Button _primary;
        [SerializeField] TMP_Text _primaryLabel;
        [SerializeField] Button _retry;
        [SerializeField] Button _pause;
        [SerializeField] TMP_Text _pauseLabel;
        [SerializeField] Button _reposition;
        [SerializeField] Button _sound;
        [SerializeField] TMP_Text _soundLabel;
        [SerializeField] Button _motion;
        [SerializeField] TMP_Text _motionLabel;
        [SerializeField] Button _restart;
        [SerializeField] TMP_Text _restartLabel;
        [Tooltip("Optional; cloned from the Motion button at runtime when missing, so the scene needs no rebuild.")]
        [SerializeField] Button _daily;
        [SerializeField] TMP_Text _dailyLabel;
        [SerializeField] Button _assist;
        [SerializeField] TMP_Text _assistLabel;
        [SerializeField] Button _howToPlay;
        [SerializeField] TMP_Text _howToPlayLabel;
        [Tooltip("Max angle from the head-to-puzzle line to the panel's far edge (FoV-aware placement).")]
        [SerializeField, Range(20f, 50f)] float _edgeDegrees = PanelPlacement.DefaultEdgeDegrees;
        [SerializeField] float _cooldown = 0.4f;
        [SerializeField] Color _alignmentNeutral = new Color(0.85f, 0.88f, 0.95f, 1f);
        [SerializeField] Color _alignmentMatched = new Color(0.15f, 0.55f, 1f, 1f);

        [Header("Placement (metres)")]
        [Tooltip("Puzzle grab volume radius, in puzzle radii (PuzzleController sets the pivot collider to 1.2).")]
        [SerializeField] float _grabVolumeRadius = 1.2f;
        [Tooltip("Clear space between the grab volume and the panel's inner edge, so a grabbing hand never meets the panel.")]
        [SerializeField] float _handClearance = 0.05f;
        [Tooltip("Panel bottom edge below the puzzle pivot.")]
        [SerializeField] float _bottomBelowPivot = 0.08f;
        [Tooltip("The bottom edge never comes closer to the table than this.")]
        [SerializeField] float _minBottomAboveSurface = 0.1f;
        [SerializeField] float _towardPlayer = 0.04f;

        public event Action PrimaryPressed;
        public event Action RetryPressed;
        public event Action PausePressed;
        public event Action RepositionPressed;
        public event Action SoundPressed;
        public event Action MotionPressed;
        public event Action RestartPressed;
        public event Action DailyPressed;
        public event Action AssistPressed;
        public event Action HowToPlayPressed;
        /// <summary>Any accepted press (audio feedback).</summary>
        public event Action ButtonPressed;

        enum Phase { Idle, FadingOut, FadingIn }

        UiTheme Theme => _theme != null ? _theme : UiTheme.Default;
        RectTransform Root => (RectTransform)transform;

        float _lastPress = -10f;
        float _shownAlignment = -1f;
        bool _shownWithin;
        UiCopy.AlignmentLevel _shownLevel = (UiCopy.AlignmentLevel)(-1);
        TMP_Text _retryLabel, _repositionLabel;
        string _statusText;
        string _shownStatus = "\u0001";
        bool _hintDimmed;

        UiView _view = UiView.HiddenView;
        UiView _target = UiView.HiddenView;
        bool _hasView;
        Phase _phase = Phase.Idle;
        float _phaseTime;
        float _fadeFrom;
        float _contentBottom;
        readonly List<Image> _pipImages = new List<Image>();

        /// <summary>Fully revealed and accepting presses.</summary>
        public bool IsInteractive => _group == null || _group.interactable;
        public bool IsVisible => _group == null ? _panel != null && _panel.gameObject.activeSelf : _group.alpha > 0.01f;
        public bool IsTransitioning => _phase != Phase.Idle;
        public UiView CurrentView => _view;
        public RectTransform Panel => _panel;
        /// <summary>How far the last placement swung the panel around the puzzle for FoV comfort (0 = default spot).</summary>
        public float LastSwingDegrees { get; private set; }
        /// <summary>Typical panel centre above its bottom edge, metres (used for the FoV check).</summary>
        public const float PanelCentreAbove = 0.06f;
        public float EdgeDegrees => _edgeDegrees;

        void Awake()
        {
            Bind(_primary, () => PrimaryPressed?.Invoke());
            Bind(_retry, () => RetryPressed?.Invoke());
            Bind(_pause, () => PausePressed?.Invoke());
            Bind(_reposition, () => RepositionPressed?.Invoke());
            Bind(_sound, () => SoundPressed?.Invoke());
            Bind(_motion, () => MotionPressed?.Invoke());
            Bind(_restart, () => RestartPressed?.Invoke());
            EnsureClone(ref _daily, ref _dailyLabel, "Daily");
            EnsureClone(ref _assist, ref _assistLabel, "Assist");
            EnsureClone(ref _howToPlay, ref _howToPlayLabel, "HowToPlay");
            Bind(_daily, () => DailyPressed?.Invoke());
            Bind(_assist, () => AssistPressed?.Invoke());
            Bind(_howToPlay, () => HowToPlayPressed?.Invoke());
            CachePips();
            ApplyTheme();
            if (_group != null) { _group.alpha = 0f; SetInteractable(false); }
            SetStatus(null);
        }

        void CachePips()
        {
            if (_pipImages.Count > 0 || _pips == null) return;
            foreach (Transform c in _pips) if (c.TryGetComponent<Image>(out var img)) _pipImages.Add(img);
        }

        /// <summary>New buttons share the Motion button's look, poke surface and press feedback.</summary>
        void EnsureClone(ref Button button, ref TMP_Text label, string name)
        {
            if (button != null || _motion == null) return;
            var go = Instantiate(_motion.gameObject, _motion.transform.parent);
            go.name = name;
            button = go.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            // By name, not the first TMP child: a button may hold more than one text.
            var labelTransform = go.transform.Find("Label");
            label = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : go.GetComponentInChildren<TMP_Text>(true);
            go.SetActive(false);
        }

        public Button DailyButton => _daily;
        public Button AssistButton => _assist;
        public Button HowToPlayButton => _howToPlay;

        void Bind(Button b, Action a)
        {
            if (b == null) return;
            var style = b.GetComponent<UiButtonStyle>();
            b.onClick.AddListener(() =>
            {
                // Device logs: tells "the press never reached the button" apart from "the panel ignored it".
                if (!IsInteractive) { Debug.Log($"[ShadowBloom] button {b.name} ignored (panel not interactive)"); return; }
                if (Time.unscaledTime - _lastPress < _cooldown) { Debug.Log($"[ShadowBloom] button {b.name} ignored (cooldown)"); return; }
                Debug.Log($"[ShadowBloom] button {b.name} pressed");
                _lastPress = Time.unscaledTime;
                if (style != null) style.Confirm();
                ButtonPressed?.Invoke();
                a();
            });
        }

        // ---- Theme -------------------------------------------------------------------------------------

        /// <summary>Applies the static design tokens (fonts, sizes, colours). Also called by the Editor upgrade.</summary>
        public void ApplyTheme()
        {
            var t = Theme;
            if (_panelImage != null)
            {
                _panelImage.color = t.surface;
                _panelImage.pixelsPerUnitMultiplier = t.panelCornerMultiplier;
            }
            Style(_eyebrow, t.eyebrowSize, FontStyles.Bold, t.textMuted, t.eyebrowSpacing);
            Style(_title, t.titleSize, FontStyles.Bold, t.text, t.titleSpacing);
            Style(_subtitle, t.bodySize, FontStyles.Normal, BodyMuted(t), 0f);
            Style(_hint, t.hintSize, FontStyles.Normal, t.text, 0f);
            Style(_status, t.hintSize, FontStyles.Normal, t.text, 0f);
            Style(_alignmentLabel, t.captionSize, FontStyles.Bold, t.textMuted, t.eyebrowSpacing * 0.7f);
            Style(_stageIndex, t.indexSize, FontStyles.Bold, t.textMuted, 0f);
            if (_stageIndex != null) _stageIndex.textWrappingMode = TextWrappingModes.NoWrap;
            if (_divider != null)
            {
                // Faded with alpha rather than drawn thinner: sub-2 px lines crawl as the head moves.
                _divider.color = new Color(t.text.r, t.text.g, t.text.b, 0.25f);
                _divider.raycastTarget = false;
            }
            if (_noticeBar != null) _noticeBar.color = t.warning;
            if (_notice != null && _notice.TryGetComponent<Image>(out var noticeBg))
            {
                noticeBg.color = t.surfaceRaised;
                noticeBg.pixelsPerUnitMultiplier = t.buttonCornerMultiplier;
            }
            if (_alignmentRoot != null && _alignmentRoot.TryGetComponent<Image>(out var track))
                track.color = new Color(t.surfaceRaised.r, t.surfaceRaised.g, t.surfaceRaised.b, 1f);
            if (t.font != null)
                foreach (var text in GetComponentsInChildren<TMP_Text>(true))
                    if (text.font != t.font) text.font = t.font;
            foreach (var s in GetComponentsInChildren<UiButtonStyle>(true)) s.ApplyStatic();
        }

        /// <summary>Muted body colour: 60 % ink in English, darker in Japanese where thin kanji strokes wash out.</summary>
        static Color BodyMuted(UiTheme t) => UiLocale.Current == UiLanguage.Japanese ? t.textMutedJapanese : t.textMuted;

        static void Style(TMP_Text t, float size, FontStyles style, Color color, float spacing)
        {
            if (t == null) return;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.characterSpacing = spacing;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.margin = Vector4.zero;
        }

        // ---- Public API -------------------------------------------------------------------------------

        public void Show(in UiView v)
        {
            _target = v;
            if (!_hasView || SameLayout(v, _view))
            {
                // Same structure (e.g. setup status text, a new instruction): update in place, no fade.
                bool wasVisible = HasContent(_view);
                _view = v;
                _hasView = true;
                Commit();
                if (!wasVisible && HasContent(_view)) BeginFadeIn();
                return;
            }
            if (_group == null) { _view = v; Commit(); return; }
            if (_group.alpha > 0.001f) BeginFadeOut();
            else { _view = v; Commit(); if (HasContent(_view)) BeginFadeIn(); }
        }

        /// <summary>Editor preview: applies the theme and lays out <paramref name="view"/> at once (no fade). In Play mode only refreshes the theme.</summary>
        public void Preview(in UiView view)
        {
            if (Application.isPlaying)
            {
                RefreshTheme();
                return;
            }
            CachePips();
            ApplyTheme();
            _view = view;
            _target = view;
            _hasView = true;
            Commit();
            if (_group != null) _group.alpha = HasContent(_view) ? 1f : 0f;
        }

        /// <summary>Re-applies UiTheme tokens and the current layout (live theme tuning).</summary>
        public void RefreshTheme()
        {
            ApplyTheme();
            if (_hasView) Commit();
        }

        public void SetSettingsLabels(bool muted, bool reducedMotion, bool assist)
        {
            if (_assistLabel != null) _assistLabel.text = assist ? UiCopy.AssistOn : UiCopy.AssistOff;
            if (_soundLabel != null) _soundLabel.text = muted ? UiCopy.SoundOff : UiCopy.SoundOn;
            if (_motionLabel != null) _motionLabel.text = reducedMotion ? UiCopy.MotionCalm : UiCopy.MotionFull;
        }

        /// <summary>System state notice (hands lost). Shown above everything, even while the panel is otherwise hidden. Null clears it.</summary>
        public void SetStatus(string text)
        {
            if (text == _shownStatus) return;
            _shownStatus = text;
            _statusText = string.IsNullOrEmpty(text) ? null : text;
            if (!_hasView) return;
            bool visibleBefore = _group == null || _group.alpha > 0.001f || _phase == Phase.FadingIn;
            Commit();
            if (_statusText != null && _notice != null)
            {
                // The notice eases in on its own; nothing below it moves (the panel grows upward).
                foreach (var g in _notice.GetComponentsInChildren<Graphic>()) { g.canvasRenderer.SetAlpha(0f); g.CrossFadeAlpha(1f, Theme.revealSeconds, true); }
            }
            if (!visibleBefore && HasContent(_view)) BeginFadeIn();
            else if (visibleBefore && !HasContent(_view) && _phase != Phase.FadingOut) BeginFadeOut();
        }

        /// <summary>One short instruction; a new one eases in.</summary>
        public void SetHint(string text)
        {
            if (_view.Hint == text) return;
            _view.Hint = text;
            _target.Hint = text;
            if (_phase == Phase.FadingOut) return;   // applied when the new view commits
            Commit();
            if (_hint != null && !string.IsNullOrEmpty(text))
            {
                _hint.canvasRenderer.SetAlpha(0f);
                _hint.CrossFadeAlpha(_hintDimmed ? 0.45f : 1f, Theme.revealSeconds, true);
            }
        }

        /// <summary>Quiets the instruction once the player is clearly using it (it returns with the idle hint).</summary>
        public void SetHintDimmed(bool dimmed)
        {
            if (_hintDimmed == dimmed) return;
            _hintDimmed = dimmed;
            if (_hint != null) _hint.CrossFadeAlpha(dimmed ? 0.45f : 1f, 0.6f, true);
        }

        public void SetAlignment(float alignment01, bool within) => SetAlignment(alignment01, within, 0f);

        /// <summary>
        /// Qualitative closeness (never a percentage). In tolerance the bar turns success-coloured and fills with
        /// the hold progress, so "keep holding" reads from the label, colour and motion together.
        /// </summary>
        public void SetAlignment(float alignment01, bool within, float hold01)
        {
            var t = Theme;
            var level = UiCopy.Level(alignment01, within);
            if (level != _shownLevel && _alignmentLabel != null)
            {
                _shownLevel = level;
                _alignmentLabel.text = UiCopy.AlignmentLabel(level);
                // Words carry the level; the single accent marks only the moment to hold still.
                _alignmentLabel.color = level == UiCopy.AlignmentLevel.Hold ? t.accent : level == UiCopy.AlignmentLevel.Almost ? t.text : t.textMuted;
            }
            if (_alignmentFill == null) return;
            float value = within ? Mathf.Lerp(0.08f, 1f, Mathf.Clamp01(hold01)) : Mathf.Clamp01(alignment01);
            // Only touch the canvas when the bar visibly changes; every write forces a canvas rebuild.
            if (within == _shownWithin && Mathf.Abs(value - _shownAlignment) < 0.004f) return;
            _shownAlignment = value;
            _shownWithin = within;
            _alignmentFill.fillAmount = value;
            _alignmentFill.color = within ? t.accent : Color.Lerp(t.textMuted, t.text, Mathf.Clamp01(alignment01 * 1.4f));
        }

        /// <summary>
        /// Beside the puzzle on the player's left, turned to face the head. The inner edge clears the puzzle's grab
        /// volume plus a hand's width; the bottom edge sits below pivot height but never near the table. When that
        /// spot would put the far edge outside the comfortable FoV, the panel swings around the puzzle toward the
        /// back (<see cref="PanelPlacement"/>), keeping the same clearance.
        /// </summary>
        public void PlaceBeside(Transform surfaceFrame, float puzzleRadius, float pivotHeight, Vector3 headPosition)
        {
            float halfWidth = Theme.panelWidth * 0.001f * 0.5f;
            float radial = puzzleRadius * _grabVolumeRadius + _handClearance + halfWidth;
            float bottom = Mathf.Max(_minBottomAboveSurface, pivotHeight - _bottomBelowPivot);
            var local = PanelPlacement.Solve(surfaceFrame.InverseTransformPoint(headPosition), pivotHeight, radial, halfWidth,
                bottom, PanelCentreAbove, _towardPlayer, _edgeDegrees, out float swing);
            LastSwingDegrees = swing;
            var pos = surfaceFrame.TransformPoint(local);
            var look = pos - headPosition;
            if (look.sqrMagnitude < 1e-4f) look = surfaceFrame.forward;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look.normalized, Vector3.up));
        }

        // ---- Transitions ------------------------------------------------------------------------------

        void BeginFadeOut()
        {
            _phase = Phase.FadingOut;
            _phaseTime = 0f;
            _fadeFrom = _group.alpha;
            SetInteractable(false);
        }

        void BeginFadeIn()
        {
            if (_group == null) return;
            _phase = Phase.FadingIn;
            _phaseTime = 0f;
            _fadeFrom = _group.alpha;
            SetInteractable(false);
        }

        void Update()
        {
            if (_phase == Phase.Idle || _group == null) return;
            var t = Theme;
            _phaseTime += Time.unscaledDeltaTime;
            if (_phase == Phase.FadingOut)
            {
                float k = Mathf.Clamp01(_phaseTime / t.hideSeconds);
                _group.alpha = Mathf.Lerp(_fadeFrom, 0f, k);
                if (k < 1f) return;
                _view = _target;
                Commit();
                if (HasContent(_view)) BeginFadeIn();
                else _phase = Phase.Idle;
                return;
            }

            float r = Mathf.Clamp01(_phaseTime / t.revealSeconds);
            float e = 1f - (1f - r) * (1f - r) * (1f - r);   // ease-out cubic
            _group.alpha = Mathf.Lerp(_fadeFrom, 1f, e);
            float rise = GameSettings.ReducedMotion ? 0f : (1f - e) * t.revealRise;
            _panel.anchoredPosition = new Vector2(0f, -rise);
            if (r < 1f) return;
            _panel.anchoredPosition = Vector2.zero;
            _phase = Phase.Idle;
            SetInteractable(true);
        }

        void SetInteractable(bool on)
        {
            if (_group == null) return;
            _group.interactable = on;
            _group.blocksRaycasts = on;
        }

        static bool HasContent(in UiView v, string status) => !v.Hidden || !string.IsNullOrEmpty(status);
        bool HasContent(in UiView v) => HasContent(v, _statusText);

        /// <summary>Same buttons, rows and title: update in place instead of fading.</summary>
        static bool SameLayout(in UiView a, in UiView b) =>
            a.Hidden == b.Hidden && a.Title == b.Title && (a.Primary == null) == (b.Primary == null) &&
            a.Retry == b.Retry && a.Pause == b.Pause && a.Reposition == b.Reposition && a.Alignment == b.Alignment &&
            a.Settings == b.Settings && a.Restart == b.Restart && (a.Progress < 0) == (b.Progress < 0) &&
            a.Daily == b.Daily && a.DailyLabel == b.DailyLabel && a.HowToPlay == b.HowToPlay &&
            a.Accent == b.Accent && ShowsIndex(a) == ShowsIndex(b);

        /// <summary>Whether the stage index numeral shows. It takes the grid's left column, so toggling it moves the
        /// text column and must fade like any other layout change.</summary>
        static bool ShowsIndex(in UiView v) => !v.Hidden && v.Progress >= 0 && v.ProgressCount > 0 && !v.ProgressComplete;

        // ---- Content and layout -----------------------------------------------------------------------

        void Commit()
        {
            Apply(_view);
            Layout();
        }

        void Apply(in UiView v)
        {
            var t = Theme;
            bool content = !v.Hidden;
            SetText(_eyebrow, content ? v.Eyebrow : null, t.Tone(v.EyebrowTone) * (v.EyebrowTone == UiTone.Neutral ? new Color(1, 1, 1, 0.65f) : Color.white));
            if (_eyebrowDot != null)
            {
                _eyebrowDot.gameObject.SetActive(content && v.EyebrowDot && !string.IsNullOrEmpty(v.Eyebrow));
                _eyebrowDot.color = t.Tone(v.EyebrowTone);
            }
            SetText(_title, content ? v.Title : null, t.Tone(v.TitleTone));
            // Swiss grid index: the stage (or how-to-play card) number, muted so it never competes with the primary action.
            SetText(_stageIndex, ShowsIndex(v) ? (v.Progress + 1).ToString("00") : null, t.textMuted);
            SetText(_subtitle, content ? v.Subtitle : null, v.SubtitleTone == UiTone.Neutral ? BodyMuted(t) : t.Tone(v.SubtitleTone));
            // The one instruction: full ink against the 60 % subtitle. Opacity carries the hierarchy, not a hue.
            SetText(_hint, content ? v.Hint : null, t.text);
            if (_hint != null && content && !string.IsNullOrEmpty(v.Hint) && _phase != Phase.Idle)
                _hint.canvasRenderer.SetAlpha(_hintDimmed ? 0.45f : 1f);

            if (_status != null) _status.text = _statusText ?? "";
            if (_notice != null) _notice.gameObject.SetActive(_statusText != null);
            if (_accentRule != null)
            {
                _accentRule.gameObject.SetActive(content);
                // Solid ink: the header rule frames the grid; the red stays small (current tick, HOLD STEADY).
                var c = v.Accent == UiTone.Neutral ? t.text : t.Tone(v.Accent);
                _accentRule.color = new Color(c.r, c.g, c.b, 1f);
            }

            ApplyPips(content ? v : UiView.HiddenView);

            SetButton(_primary, content && !string.IsNullOrEmpty(v.Primary));
            if (_primaryLabel != null) _primaryLabel.text = v.Primary ?? "";
            SetButton(_retry, content && v.Retry);
            // Retry and Move have no serialized label reference: their text is baked in the prefab (English), so the
            // label under each button is found once and follows the language like the others.
            if (_retryLabel == null && _retry != null) _retryLabel = _retry.GetComponentInChildren<TMP_Text>(true);
            if (_repositionLabel == null && _reposition != null) _repositionLabel = _reposition.GetComponentInChildren<TMP_Text>(true);
            if (_retryLabel != null) _retryLabel.text = UiCopy.Retry;
            if (_repositionLabel != null) _repositionLabel.text = UiCopy.Move;
            SetButton(_pause, content && v.Pause);
            if (_pauseLabel != null) _pauseLabel.text = string.IsNullOrEmpty(v.PauseLabel) ? UiCopy.Pause : v.PauseLabel;
            SetButton(_reposition, content && v.Reposition);
            SetButton(_sound, content && v.Settings);
            SetButton(_motion, content && v.Settings);
            SetButton(_assist, content && v.Settings);
            SetButton(_howToPlay, content && v.HowToPlay);
            if (_howToPlayLabel != null) _howToPlayLabel.text = UiCopy.HowToPlay;
            SetButton(_daily, content && v.Daily);
            if (_dailyLabel != null) _dailyLabel.text = v.DailyLabel ?? UiCopy.DailyShadow;
            SetButton(_restart, content && v.Restart);
            if (_restartLabel != null) _restartLabel.text = UiCopy.RestartAll;
            if (_alignmentRoot != null) _alignmentRoot.SetActive(content && v.Alignment);
            if (_alignmentLabel != null) _alignmentLabel.gameObject.SetActive(content && v.Alignment);
            // Above a primary block only: outlined buttons are already separated from the text by their own border.
            if (_divider != null) _divider.gameObject.SetActive(content && Active(_primary));
        }

        void ApplyPips(in UiView v)
        {
            if (_pips == null) return;
            bool show = v.Progress >= 0 && v.ProgressCount > 0;
            _pips.gameObject.SetActive(show);
            if (!show) return;
            var t = Theme;
            float done = t.pipDoneSize, current = t.pipCurrentSize, upcoming = t.pipUpcomingSize, spacing = t.pipSpacing;
            float x = 0f;
            for (int i = 0; i < _pipImages.Count; i++)
            {
                var img = _pipImages[i];
                bool on = i < v.ProgressCount;
                img.gameObject.SetActive(on);
                if (!on) continue;
                // Size encodes position (done / current / upcoming), so progress never relies on colour alone.
                float size; Color c;
                if (v.ProgressComplete) { size = done + 0.5f; c = t.success; }
                else if (i < v.Progress) { size = done; c = t.text; }
                else if (i == v.Progress) { size = current; c = v.ProgressCurrentDone ? t.success : t.accent; }
                else { size = upcoming; c = new Color(t.text.r, t.text.g, t.text.b, 0.28f); }
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(x, 0f);
                img.color = c;
                x += size + spacing;
            }
            _pips.sizeDelta = new Vector2(Mathf.Max(0f, x - spacing), 8f);
        }

        void Layout()
        {
            if (_panel == null) return;
            var t = Theme;
            float w = t.panelWidth, pad = t.padding, cw = w - 2f * pad;
            float y = pad;   // distance from the top edge
            _contentBottom = 0f;

            if (_notice != null && _notice.gameObject.activeSelf)
            {
                float textW = cw - 14f;
                float th = Preferred(_status, textW);
                float h = Mathf.Max(t.noticeHeightMin, th + 12f);
                Place(_notice, pad, y, cw, h);
                if (_noticeBar != null) PlaceIn(_noticeBar.rectTransform, 0f, 0f, 3f, h);
                if (_status != null) PlaceIn(_status.rectTransform, 10f, (h - th) * 0.5f, textW, th);
                y += h + (_view.Hidden ? 0f : t.gap * 1.5f);
            }

            bool eyebrow = _eyebrow != null && _eyebrow.gameObject.activeSelf;
            bool pips = _pips != null && _pips.gameObject.activeSelf;
            if (eyebrow || pips)
            {
                // At least 9 mm; taller when the typeface's line box needs it (the serif caps run ~9.6 mm).
                float h = Mathf.Max(9f, eyebrow ? Preferred(_eyebrow, 1000f) : 0f);
                float dotW = 0f;
                if (_eyebrowDot != null && _eyebrowDot.gameObject.activeSelf)
                {
                    Place(_eyebrowDot.rectTransform, pad, y + 2f, 5f, 5f);
                    dotW = 9f;
                }
                // Pips share the eyebrow's row when it is short ("STAGE 2 OF 8"), else take their own line.
                float pipsW = pips ? _pips.sizeDelta.x + 8f : 0f;
                float eyebrowW = eyebrow ? _eyebrow.GetPreferredValues(_eyebrow.text, 1000f, 0f).x : 0f;
                bool sameRow = !pips || !eyebrow || dotW + eyebrowW + pipsW <= cw;
                if (eyebrow) Place(_eyebrow.rectTransform, pad + dotW, y, cw - dotW - (sameRow ? pipsW : 0f), h);
                if (pips && sameRow) Place(_pips, pad + cw - _pips.sizeDelta.x, y + 1f, _pips.sizeDelta.x, 8f);
                y += h + t.gap * 0.5f;
                if (pips && !sameRow)
                {
                    Place(_pips, pad, y, _pips.sizeDelta.x, 8f);
                    y += 8f + t.gap * 0.5f;
                }
            }
            // Header rule: full width, in the state's signature colour, closing the eyebrow row.
            if (_accentRule != null && _accentRule.gameObject.activeSelf)
            {
                Place(_accentRule.rectTransform, pad, y, cw, t.ruleThickness);
                y += t.ruleThickness + t.gap;
            }

            // Grid: the index numeral takes the left column; title, text and alignment share the right one.
            float x = pad, colW = cw, indexBottom = 0f;
            if (_stageIndex != null && _stageIndex.gameObject.activeSelf)
            {
                float ih = Preferred(_stageIndex, t.indexColumn);
                // Top of the numeral's digits on the title's cap line (TMP boxes start at the ascender).
                float iy = y + CapInset(_title) - CapInset(_stageIndex);
                PlaceIn(_stageIndex.rectTransform, pad, iy, t.indexColumn, ih);
                // Digits sit on the baseline, so the box's descender space must not push the content down.
                indexBottom = iy + Baseline(_stageIndex);
                _contentBottom = Mathf.Max(_contentBottom, indexBottom);
                x = pad + t.indexColumn;
                colW = cw - t.indexColumn;
            }
            y = Stack(_title, x, y, colW, t.gap * 0.6f);
            y = Stack(_subtitle, x, y, colW, t.gap);
            y = Stack(_hint, x, y, colW, t.gap);

            if (_alignmentRoot != null && _alignmentRoot.activeSelf)
            {
                if (_alignmentLabel != null)
                {
                    float lh = Mathf.Max(8f, Preferred(_alignmentLabel, colW));
                    Place(_alignmentLabel.rectTransform, x, y, colW, lh);
                    y += lh + 3f;
                }
                var bar = (RectTransform)_alignmentRoot.transform;
                Place(bar, x, y, colW, t.alignmentBarHeight);
                if (_alignmentFill != null) PlaceIn(_alignmentFill.rectTransform, 0f, 0f, colW, t.alignmentBarHeight);
                y += t.alignmentBarHeight + t.gap;
            }
            if (indexBottom > 0f) y = Mathf.Max(y, indexBottom + t.gap);

            // Divider between what the panel says and what it lets you do (only under real content), with even space
            // on both sides: the trailing text gap above, the same plus a little below for the touch targets.
            if (_divider != null && _divider.gameObject.activeSelf)
            {
                Place(_divider.rectTransform, pad, y, cw, t.dividerThickness);
                y += t.dividerThickness + t.gap * 1.2f;
            }
            else y += t.gap * 0.5f;
            if (Active(_primary))
            {
                Place((RectTransform)_primary.transform, pad, y, cw, t.primaryHeight);
                y += t.primaryHeight + t.targetGap;
            }
            foreach (var row in SecondaryRows()) y = Row(y, t.secondaryHeight, row);

            float height = _contentBottom > 0f ? _contentBottom + pad : 0f;

            var root = Root;
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(w, Mathf.Max(1f, height));
            _panel.anchorMin = Vector2.zero;
            _panel.anchorMax = Vector2.one;
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = Vector2.zero;
            if (_phase != Phase.FadingIn) _panel.anchoredPosition = Vector2.zero;
        }

        /// <summary>Secondary button rows below the primary action, top to bottom (also their index order).</summary>
        Button[][] SecondaryRows() => new[]
        {
            new[] { _daily },
            new[] { _retry, _pause, _reposition },
            new[] { _sound, _motion },
            new[] { _assist, _howToPlay },
            new[] { _restart },
        };

        float Row(float y, float h, params Button[] buttons)
        {
            int n = 0;
            foreach (var b in buttons) if (Active(b)) n++;
            if (n == 0) return y;
            var t = Theme;
            float cw = t.panelWidth - 2f * t.padding;
            float bw = (cw - (n - 1) * t.targetGap) / n;
            float x = t.padding;
            foreach (var b in buttons)
            {
                if (!Active(b)) continue;
                Place((RectTransform)b.transform, x, y, bw, h);
                x += bw + t.targetGap;
            }
            return y + h + t.targetGap;
        }

        float Stack(TMP_Text text, float x, float y, float width, float gapAfter)
        {
            if (text == null || !text.gameObject.activeSelf) return y;
            float h = Preferred(text, width);
            Place(text.rectTransform, x, y, width, h);
            return y + h + gapAfter;
        }

        /// <summary>Distance from the top of a TMP text box (ascender line) down to its cap line, in mm.</summary>
        static float CapInset(TMP_Text text)
        {
            if (text == null || text.font == null) return 0f;
            var f = text.font.faceInfo;
            return f.pointSize > 0f ? (f.ascentLine - f.capLine) / f.pointSize * text.fontSize : 0f;
        }

        /// <summary>Distance from the top of a TMP text box down to its first baseline, in mm.</summary>
        static float Baseline(TMP_Text text)
        {
            if (text == null || text.font == null) return 0f;
            var f = text.font.faceInfo;
            return f.pointSize > 0f ? f.ascentLine / f.pointSize * text.fontSize : 0f;
        }

        static float Preferred(TMP_Text text, float width)
        {
            if (text == null) return 0f;
            return Mathf.Ceil(text.GetPreferredValues(text.text, width, 0f).y);
        }

        /// <summary>Top-left placement of a panel element, in mm from the panel's top-left corner. Tracks the content bottom.</summary>
        void Place(RectTransform rt, float x, float yFromTop, float w, float h)
        {
            PlaceIn(rt, x, yFromTop, w, h);
            _contentBottom = Mathf.Max(_contentBottom, yFromTop + h);
        }

        /// <summary>Top-left placement relative to the element's own parent (no effect on the panel height).</summary>
        static void PlaceIn(RectTransform rt, float x, float yFromTop, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -yFromTop);
            rt.sizeDelta = new Vector2(w, h);
        }

        static bool Active(Button b) => b != null && b.gameObject.activeSelf;

        static void SetButton(Button b, bool on)
        {
            if (b != null && b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
        }

        static void SetText(TMP_Text t, string s, Color color)
        {
            if (t == null) return;
            bool on = !string.IsNullOrEmpty(s);
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            if (!on) return;
            if (t.text != s) t.text = s;
            if (t.color != color) t.color = color;
        }
    }
}
