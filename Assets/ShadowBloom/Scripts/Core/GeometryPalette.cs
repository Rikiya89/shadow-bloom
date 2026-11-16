using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// Game geometry palette. Paper light, hierarchy by value, opacity, width and dash; vermilion only means
    /// "hold still" and marks the flower heart. See docs/UI_DESIGN_SYSTEM.md section 2.
    /// </summary>
    public static class GeometryPalette
    {
        public static readonly Color Paper = new Color(0.949f, 0.941f, 0.922f, 1f);
        /// <summary>Paper x 0.72: resting curve and shadow, brightening to <see cref="Paper"/> as alignment rises.</summary>
        public static readonly Color PaperMuted = new Color(0.683f, 0.678f, 0.664f, 1f);
        public static readonly Color Ivory = new Color(1f, 0.93f, 0.8f, 1f);
        public static readonly Color Vermilion = new Color(0.894f, 0.314f, 0.235f, 1f);

        public const float TargetAlpha = 0.6f;
        public const float ShadowAlpha = 0.95f;
        public const float HoldAlpha = 0.9f;

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
