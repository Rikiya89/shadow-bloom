using TMPro;
using UnityEngine;

namespace ShadowBloom
{
    public enum UiTone { Neutral, Accent, Success, Warning }

    /// <summary>
    /// Design tokens for the world-space UI ("Spatial Observatory", Neo-Swiss grid). Canvas units are millimetres (canvas scale
    /// 0.001), so every size here is a physical size on the panel. Colours are tuned for passthrough: a near-opaque
    /// deep-blue surface behind all text, never pure black or white. See docs/UI_DESIGN_SYSTEM.md.
    /// </summary>
    [CreateAssetMenu(menuName = "Shadow Bloom/UI Theme", fileName = "UiTheme")]
    public sealed class UiTheme : ScriptableObject
    {
        [Header("Colour tokens")]
        // Neo-Swiss: paper, ink and greys made from ink opacity, plus exactly one small accent (deep vermilion). Hierarchy comes
        // from size, weight and opacity, never from a second hue. 98 %: the project renders in linear space, where
        // more transparency lets the room show through the paper.
        public Color surface = new Color32(0xF2, 0xF0, 0xEB, 0xFA);        // paper #F2F0EB @ 98 %
        [Tooltip("Paper one step darker: alignment track, notice card.")]
        public Color surfaceRaised = new Color32(0xDE, 0xDB, 0xD4, 0xFF);  // #DEDBD4
        public Color text = new Color32(0x14, 0x14, 0x14, 0xFF);           // ink #141414
        public Color textMuted = new Color32(0x14, 0x14, 0x14, 0x99);      // ink @ 60 %
        [Tooltip("Body/subtitle in Japanese. Kanji strokes in Noto Serif JP are much thinner than Latin, so 60 % ink is too faint.")]
        public Color textMutedJapanese = new Color32(0x14, 0x14, 0x14, 0xF2); // ink @ 95 %
        [Tooltip("The single accent, used small: current-stage tick, HOLD STEADY, focus ring. Deep vermilion, not signal red.")]
        public Color accent = new Color32(0xA3, 0x3A, 0x2E, 0xFF);         // deep vermilion #A33A2E
        [Tooltip("Solved / complete. Ink, not a second hue: success is carried by words and full ticks.")]
        public Color success = new Color32(0x14, 0x14, 0x14, 0xFF);        // ink
        [Tooltip("Recovery/attention only (hands lost): the accent, always with the sentence and a bar.")]
        public Color warning = new Color32(0xA3, 0x3A, 0x2E, 0xFF);        // deep vermilion
        [Tooltip("Text on accent and ink fills.")]
        public Color onAccent = new Color32(0xF7, 0xF5, 0xF0, 0xFF);       // #F7F5F0

        [Header("Typography (mm)")]
        [Tooltip("Book typeface for every panel text (Source Serif 4; bold = Semibold). Built by Shadow Bloom > Build UI Fonts.")]
        public TMP_FontAsset font;
        public float eyebrowSize = 7f;
        public float eyebrowSpacing = 8f;
        public float titleSize = 17f;
        [Tooltip("Neo-Swiss: large sentence-case titles set tight (negative tracking), flush left.")]
        public float titleSpacing = -0.5f;
        public float bodySize = 9.5f;
        public float hintSize = 9f;
        public float captionSize = 7f;
        public float primaryLabelSize = 10.5f;
        public float secondaryLabelSize = 8.5f;
        public float buttonLabelSpacing = 0f;
        [Tooltip("Large stage index numeral ('01') in its own grid column.")]
        public float indexSize = 32f;

        [Header("Metrics (mm)")]
        public float panelWidth = 196f;
        public float padding = 14f;
        [Tooltip("Vertical rhythm between text blocks.")]
        public float gap = 6f;
        [Tooltip("Minimum clear space between adjacent touch targets (Meta hand UI guidance: ~12 mm).")]
        public float targetGap = 12f;
        public float primaryHeight = 36f;
        public float secondaryHeight = 28f;
        public float alignmentBarHeight = 2.5f;
        [Tooltip("Grid column for the stage index numeral; title, text and alignment start after it.")]
        public float indexColumn = 44f;
        [Tooltip("Full-width rule under the header, in the state's signature colour. Not thinner: finer lines crawl in the headset.")]
        public float ruleThickness = 1.6f;
        [Tooltip("Divider between the content and the buttons (faded with alpha, never thinner than this).")]
        public float dividerThickness = 1.2f;
        public float noticeHeightMin = 26f;
        [Tooltip("Button label inset from the block's or outline's left and right edges.")]
        public float primaryLabelInset = 6f;
        [Tooltip("Square outline of secondary buttons. Not thinner: finer lines crawl in the headset.")]
        public float outlineThickness = 1.5f;
        [Tooltip("Secondary button outline opacity (ink) at rest; full ink on hover.")]
        [Range(0f, 1f)] public float cellRuleAlpha = 0.85f;
        [Tooltip("Quiet button (Restart from stage 1) outline opacity: fainter than a normal button.")]
        [Range(0f, 1f)] public float quietRuleAlpha = 0.25f;
        [Tooltip("Unused since the Neo-Swiss pass: panels and buttons are square, plain quads.")]
        public float panelCornerMultiplier = 3f;
        [Tooltip("Unused since the Neo-Swiss pass: panels and buttons are square, plain quads.")]
        public float buttonCornerMultiplier = 3f;
        [Tooltip("Square focus ring (SquareRing.png, 3 px stroke): stroke = 3 mm / value, so 2 gives 1.5 mm.")]
        public float focusRingCornerMultiplier = 2f;

        [Header("Progress pips (mm)")]
        [Tooltip("Size encodes position (done / current / upcoming), so progress never relies on colour alone.")]
        public float pipDoneSize = 4.5f;
        public float pipCurrentSize = 7f;
        public float pipUpcomingSize = 4f;
        public float pipSpacing = 5f;

        [Header("Motion (s)")]
        public float hoverSeconds = 0.12f;
        public float pressSeconds = 0.16f;
        public float confirmSeconds = 0.18f;
        public float revealSeconds = 0.22f;
        public float hideSeconds = 0.12f;
        [Tooltip("Content rises this far (mm) while revealing; none with reduced motion.")]
        public float revealRise = 5f;
        [Range(0.9f, 1f)] public float pressScale = 0.97f;

        public Color Tone(UiTone tone) => tone switch
        {
            UiTone.Accent => text,   // view tones stay ink: the deep red is reserved for the current tick and HOLD STEADY
            UiTone.Success => success,
            UiTone.Warning => warning,
            _ => text
        };

#if UNITY_EDITOR
        /// <summary>Editor-only: raised when the asset is edited in the Inspector (drives the UI preview).</summary>
        public static event System.Action<UiTheme> Edited;
        void OnValidate() => Edited?.Invoke(this);
#endif

        static UiTheme _fallback;
        /// <summary>Token defaults, used when no theme asset is assigned (tests, older scenes).</summary>
        public static UiTheme Default
        {
            get
            {
                if (_fallback == null)
                {
                    _fallback = CreateInstance<UiTheme>();
                    _fallback.hideFlags = HideFlags.HideAndDontSave;
                }
                return _fallback;
            }
        }
    }
}
