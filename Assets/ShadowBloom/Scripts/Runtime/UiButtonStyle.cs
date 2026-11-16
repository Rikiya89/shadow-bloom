using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShadowBloom
{
    /// <summary>
    /// Shared look and interaction states for every panel button: default, hover/focus, press, disabled and
    /// confirmed. Hands reach buttons through the Interaction SDK poke and ray canvases, which forward standard
    /// uGUI pointer events, so this works for poke, ray pinch and the desktop mouse alike.
    /// Colours animate with <see cref="Graphic.CrossFadeColor"/> (canvas-renderer tint, no mesh rebuild), and the
    /// component does no work while idle. The Button's own transition is off so this is the only visual driver.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class UiButtonStyle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public enum Variant { Primary, Secondary, Quiet }

        [SerializeField] Variant _variant = Variant.Secondary;
        [SerializeField] UiTheme _theme;
        [SerializeField] Image _background;
        [Tooltip("Thin ring shown on hover/focus, so focus does not rely on a colour shift alone.")]
        [SerializeField] Image _focusRing;
        [SerializeField] TMP_Text _label;
        [Tooltip("Neo-Swiss outline: square ink border of secondary and quiet buttons (primary is a solid block).")]
        [SerializeField] Image _rule;

        Button _button;
        bool _hovered, _pressed, _interactable = true;
        float _confirmUntil = -1f;
        float _scale = 1f;
        bool _animatingScale;

        public Variant Style => _variant;
        public bool IsHovered => _hovered;
        public bool IsConfirming => Time.unscaledTime < _confirmUntil;
        UiTheme Theme => _theme != null ? _theme : UiTheme.Default;

        public void Configure(Variant variant, UiTheme theme, Image background, Image focusRing, TMP_Text label,
            Image rule = null)
        {
            _variant = variant;
            _theme = theme;
            _background = background;
            _focusRing = focusRing;
            _label = label;
            _rule = rule;
            ApplyStatic();
            Refresh(true);
        }

        void Awake()
        {
            _button = GetComponent<Button>();
            _button.transition = Selectable.Transition.None;
            ApplyStatic();
        }

        void OnEnable()
        {
            _hovered = _pressed = false;
            _confirmUntil = -1f;
            _scale = 1f;
            transform.localScale = Vector3.one;
            _interactable = _button == null || _button.interactable;
            Refresh(true);
        }

        /// <summary>Static (non-animated) styling: base colours, label size and placement, outline.</summary>
        public void ApplyStatic()
        {
            var t = Theme;
            bool primary = _variant == Variant.Primary;
            if (_background != null)
            {
                _background.color = Color.white;   // state colour is the canvas-renderer tint
                // Outlined buttons rest with no fill (alpha 0). A culled transparent mesh gets no canvas depth, and
                // uGUI's GraphicRaycaster (which the ISDK poke / ray canvases use) skips depth -1, so keep it drawn.
                _background.canvasRenderer.cullTransparentMesh = false;
            }
            if (_focusRing != null)
            {
                _focusRing.color = Color.white;
                _focusRing.raycastTarget = false;
                _focusRing.pixelsPerUnitMultiplier = t.focusRingCornerMultiplier;
            }
            if (_label != null)
            {
                _label.color = Color.white;
                if (t.font != null && _label.font != t.font) _label.font = t.font;
                _label.fontSize = _variant == Variant.Primary ? t.primaryLabelSize : t.secondaryLabelSize;
                _label.fontStyle = FontStyles.Bold;
                _label.characterSpacing = t.buttonLabelSpacing;
                // Flush left on the grid, vertically centred, inset from the block's or outline's left edge.
                _label.alignment = TextAlignmentOptions.Left;   // middle by line metrics: every label shares a baseline
                _label.margin = new Vector4(t.primaryLabelInset, 0f, t.primaryLabelInset, 0f);
                _label.textWrappingMode = TextWrappingModes.NoWrap;
                _label.overflowMode = TextOverflowModes.Truncate;   // the atlas has no ellipsis glyph
                _label.raycastTarget = false;
            }
            if (_rule != null)
            {
                // SquareRing.png has a 3 px stroke; at multiplier m one sprite pixel is 1/m mm on the panel.
                _rule.color = Color.white;
                _rule.raycastTarget = false;
                _rule.pixelsPerUnitMultiplier = 3f / Mathf.Max(0.1f, t.outlineThickness);
                var rt = _rule.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                _rule.gameObject.SetActive(!primary);
            }
        }

        /// <summary>Brief success state after an accepted press: the press registered.</summary>
        public void Confirm()
        {
            _confirmUntil = Time.unscaledTime + Theme.confirmSeconds;
            Refresh(false);
        }

        public void OnPointerEnter(PointerEventData e) { _hovered = true; Refresh(false); }
        public void OnPointerExit(PointerEventData e) { _hovered = false; _pressed = false; Refresh(false); }
        public void OnPointerDown(PointerEventData e) { _pressed = true; Refresh(false); }
        public void OnPointerUp(PointerEventData e) { _pressed = false; Refresh(false); }

        void Update()
        {
            // The button's own flag, not IsInteractable(): the panel's CanvasGroup gates input while it fades in,
            // and that must not flash every button through the disabled look on each reveal.
            bool interactable = _button.interactable;
            if (interactable != _interactable)
            {
                _interactable = interactable;
                if (!interactable) _hovered = _pressed = false;
                Refresh(false);
            }
            if (_confirmUntil > 0f && Time.unscaledTime >= _confirmUntil)
            {
                _confirmUntil = -1f;
                Refresh(false);
            }
            if (_animatingScale)
            {
                float target = _pressed && !GameSettings.ReducedMotion ? Theme.pressScale : 1f;
                _scale = Mathf.MoveTowards(_scale, target, Time.unscaledDeltaTime * (1f - Theme.pressScale) / Mathf.Max(0.01f, Theme.pressSeconds * 0.5f));
                transform.localScale = new Vector3(_scale, _scale, 1f);
                if (Mathf.Approximately(_scale, target)) _animatingScale = false;
            }
        }

        void Refresh(bool instant)
        {
            var t = Theme;
            ComputeColors(t, out var fill, out var label, out var ring, out var rule);
            float d = instant ? 0f : (_pressed ? t.pressSeconds * 0.5f : t.hoverSeconds);
            if (_background != null) _background.CrossFadeColor(fill, d, true, true);
            if (_label != null) _label.CrossFadeColor(label, d, true, true);
            if (_focusRing != null) _focusRing.CrossFadeColor(ring, d, true, true);
            if (_rule != null) _rule.CrossFadeColor(rule, d, true, true);
            _animatingScale = true;
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        void ComputeColors(UiTheme t, out Color fill, out Color label, out Color ring, out Color rule)
        {
            bool primary = _variant == Variant.Primary;
            bool quiet = _variant == Variant.Quiet;
            // Primary: the one solid ink block. Secondary: a square ink outline with an ink label
            // and no fill. Quiet: a faint outline and a muted label (rare, consequential action). Hierarchy comes from
            // fill versus outline and from opacity, never from a second hue.
            Color baseFill = primary ? t.text : WithAlpha(t.text, 0f);
            Color baseLabel = primary ? t.onAccent : quiet ? t.textMuted : t.text;
            Color baseRule = primary ? WithAlpha(t.text, 0f) : WithAlpha(t.text, quiet ? t.quietRuleAlpha : t.cellRuleAlpha);
            ring = WithAlpha(t.text, 0f);
            rule = baseRule;

            if (!_interactable)
            {
                fill = WithAlpha(baseFill, baseFill.a * 0.4f);
                label = WithAlpha(baseLabel, baseLabel.a * 0.4f);
                rule = WithAlpha(baseRule, baseRule.a * 0.4f);
                return;
            }
            if (IsConfirming)
            {
                // Accepted press: the button inverts to a solid ink block for a moment.
                fill = primary ? Color.Lerp(t.text, t.surface, 0.35f) : t.text;
                label = t.onAccent;
                ring = WithAlpha(t.text, 0.9f);
                rule = WithAlpha(t.text, primary ? 0f : 1f);
                return;
            }
            fill = baseFill;
            label = baseLabel;
            if (_hovered)
            {
                // Focus is an accent outline ring around the button, plus a light ink tint: never colour alone.
                fill = primary ? Color.Lerp(t.text, t.surface, 0.18f) : WithAlpha(t.text, 0.08f);
                label = primary ? t.onAccent : t.text;
                ring = WithAlpha(t.accent, 0.95f);
                if (!primary) rule = t.text;
            }
            if (_pressed)
            {
                fill = primary ? Color.Lerp(t.text, t.surface, 0.28f) : WithAlpha(t.text, 0.16f);
            }
        }
    }
}
