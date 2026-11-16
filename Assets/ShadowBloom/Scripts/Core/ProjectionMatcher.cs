using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// Shape error between two projected polylines.
    ///
    /// Metric: symmetric mean point-to-polyline distance ("chamfer" distance), after removing each shape's
    /// centroid, divided by a constant scale taken from the target (its RMS radius).
    ///
    ///   error = 0.5 * ( mean_{c in C} d(c, T) + mean_{t in T} d(t, C) ) / rms(T)
    ///
    /// Because it compares point sets against segments rather than sample index i against i, it is invariant to
    /// cyclic sample offsets, reversed winding, and to any symmetry of the curve (e.g. a 5-petal rose rotated by
    /// 72 degrees produces an identical point set and so an error near 0).
    ///
    /// The current shape's scale is never normalised, so a squashed or enlarged projection is penalised.
    /// Typical values: 0 = identical, ~0.03 = a few degrees off, &gt;0.25 = clearly different.
    /// </summary>
    public static class ProjectionMatcher
    {
        public static float ComputeError(ProjectedShape current, ProjectedShape target, float normScale)
        {
            if (normScale <= 1e-6f) return float.PositiveInfinity;
            var cc = current.Centroid();
            var tc = target.Centroid();
            float a = MeanDistanceToPolylines(current, cc, target, tc);
            float b = MeanDistanceToPolylines(target, tc, current, cc);
            return 0.5f * (a + b) / normScale;
        }

        static float MeanDistanceToPolylines(ProjectedShape queries, Vector2 queryOffset, ProjectedShape lines, Vector2 lineOffset)
        {
            var q = queries.Points;
            var lp = lines.Points;
            float sum = 0f;
            for (int i = 0; i < q.Length; i++)
            {
                var p = q[i] - queryOffset + lineOffset;
                float best = float.MaxValue;
                for (int s = 0; s < lines.StripCount; s++)
                {
                    int start = lines.StripStart[s];
                    int len = lines.StripLength[s];
                    int segCount = lines.StripClosed[s] ? len : len - 1;
                    for (int j = 0; j < segCount; j++)
                    {
                        var a = lp[start + j];
                        var b = lp[start + (j + 1) % len];
                        float d = SqrDistancePointSegment(p, a, b);
                        if (d < best) best = d;
                    }
                }
                sum += Mathf.Sqrt(best);
            }
            return sum / Mathf.Max(1, q.Length);
        }

        public static float SqrDistancePointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return (a + ab * t - p).sqrMagnitude;
        }
    }

    /// <summary>
    /// Turns a stream of error samples into "matched and held". Uses hysteresis so hand jitter right at the
    /// tolerance boundary does not flicker, and lets hold progress decay (rather than reset) when the player slips.
    /// </summary>
    public sealed class SolveTracker
    {
        public float Tolerance;
        public float HoldSeconds;
        public float ExitToleranceFactor = 1.2f;
        public float DecayRate = 2f;

        public bool IsWithin { get; private set; }
        public bool Solved { get; private set; }
        public float HoldProgress01 => HoldSeconds <= 0f ? (IsWithin ? 1f : 0f) : Mathf.Clamp01(_held / HoldSeconds);

        float _held;

        public SolveTracker(float tolerance, float holdSeconds)
        {
            Tolerance = tolerance;
            HoldSeconds = holdSeconds;
        }

        public void Reset()
        {
            IsWithin = false;
            Solved = false;
            _held = 0f;
        }

        /// <returns>True only on the tick where the puzzle becomes solved.</returns>
        public bool Tick(float error, float dt)
        {
            if (Solved) return false;

            IsWithin = IsWithin ? error <= Tolerance * ExitToleranceFactor : error <= Tolerance;
            _held = IsWithin ? _held + dt : Mathf.Max(0f, _held - dt * DecayRate);

            if (IsWithin && _held >= HoldSeconds)
            {
                Solved = true;
                return true;
            }
            return false;
        }
    }

    public static class AlignmentFeedback
    {
        /// <summary>
        /// Maps shape error to a 0..1 "closeness" that drives colour, pulse and audio pitch.
        /// 1 at or below tolerance, 0 at <paramref name="farError"/> (pass the stage's starting error, so every
        /// stage starts neutral white even though their error ranges differ).
        /// Squared so feedback stays calm far away and ramps up noticeably near the answer.
        /// </summary>
        public static float Evaluate(float error, float tolerance, float farError)
        {
            if (tolerance <= 0f) return 0f;
            float far = Mathf.Max(farError, tolerance * 1.5f);
            float t = Mathf.Clamp01((far - error) / (far - tolerance));
            return t * t;
        }
    }
}
