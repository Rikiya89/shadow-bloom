using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// Where garden flowers rest on the table, in the puzzle's surface frame (+x right, +y normal, +z away from the
    /// player, origin on the surface). A ring just outside the target shadow, on the far half only, so it is never
    /// between the player and the puzzle. The ring lives in a band of free table behind the shadow: at least
    /// <see cref="MinBand"/> (the solver's on-table margin), and up to <see cref="MaxBand"/> when the placement
    /// reports that much room (<see cref="PuzzlePlacement.GardenBand"/>). Flowers scale with the band and always
    /// stay inside it.
    /// </summary>
    public static class GardenLayout
    {
        public const float MinBand = 0.03f;
        public const float MaxBand = 0.06f;
        /// <summary>Flower radius and footprint at <see cref="MinBand"/>; both scale with the band.</summary>
        public const float MiniRadius = 0.010f;
        public const float Extent = MiniRadius * 1.4f * 1.05f;
        public const float RingOffset = 0.015f;
        public const float Lift = 0.002f;
        public const float FirstDeg = 15f, StepDeg = 25f;
        const float GoldenAngle = 137.50776f;

        /// <summary>Slot used by the n-th entry (0 = oldest): grows from back-centre outward.</summary>
        public static readonly int[] Order = { 3, 2, 4, 1, 5, 0, 6 };

        /// <summary>Flower size relative to the <see cref="MinBand"/> flower (1 to MaxBand / MinBand).</summary>
        public static float Scale(float band) => Mathf.Clamp(band, MinBand, MaxBand) / MinBand;

        public static float SlotDegrees(int slot) => FirstDeg + StepDeg * Mathf.Clamp(slot, 0, 6);

        public static Vector3 SlotLocal(int slot, float puzzleRadius, float band = MinBand)
        {
            float rg = puzzleRadius + RingOffset * Scale(band);
            float th = SlotDegrees(slot) * Mathf.Deg2Rad;
            return new Vector3(rg * Mathf.Cos(th), Lift, rg * Mathf.Sin(th));
        }

        /// <summary>Tallest stem at <see cref="MinBand"/>; scales with the band like the flowers.</summary>
        public const float MaxStem = 0.035f;
        /// <summary>The highlighted (newest) flower stands this much taller than its slot's height.</summary>
        public const float HighlightStem = 1.25f;
        // Ikebana proportions (tall, two thirds, one third), arranged off-centre so the skyline is not symmetric.
        static readonly float[] SlotHeight = { 1f / 3f, 2f / 3f, 1f, 1f / 3f, 2f / 3f, 1f / 3f, 2f / 3f };

        /// <summary>Stem length of the flower in <paramref name="slot"/>: height costs no table space.</summary>
        public static float StemHeight(int slot, float band, bool highlight) =>
            MaxStem * Scale(band) * SlotHeight[Mathf.Clamp(slot, 0, 6)] * (highlight ? HighlightStem : 1f);

        public static float YawFor(int day) => Mathf.Repeat(day * GoldenAngle, 360f);
    }
}
