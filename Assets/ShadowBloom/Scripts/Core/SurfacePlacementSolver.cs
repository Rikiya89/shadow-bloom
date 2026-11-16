using UnityEngine;

namespace ShadowBloom
{
    public enum SurfaceKind
    {
        Table,
        OtherHorizontal,
        Manual,
        Fallback
    }

    /// <summary>A rectangular horizontal-ish surface: centre, outward normal and in-plane axes with full extents.</summary>
    public struct SurfaceCandidate
    {
        public Vector3 Center;
        public Vector3 Normal;
        public Vector3 AxisU;
        public Vector3 AxisV;
        public Vector2 Size;
        public SurfaceKind Kind;

        public SurfaceCandidate(Vector3 center, Vector3 normal, Vector3 axisU, Vector3 axisV, Vector2 size, SurfaceKind kind)
        {
            Center = center;
            Normal = normal.normalized;
            AxisU = axisU.normalized;
            AxisV = axisV.normalized;
            Size = size;
            Kind = kind;
        }
    }

    public struct PuzzlePlacement
    {
        /// <summary>Point on the surface under the puzzle.</summary>
        public Vector3 Position;
        /// <summary>Up = surface normal, forward = away from the player.</summary>
        public Quaternion Rotation;
        public float PuzzleRadius;
        public float PivotHeight;
        public SurfaceKind Kind;
        /// <summary>
        /// Free surface behind and beside the target shadow that the garden may use, in metres
        /// (<see cref="GardenLayout.MinBand"/> to <see cref="GardenLayout.MaxBand"/>). 0 = unknown, treated as the minimum.
        /// </summary>
        public float GardenBand;
    }

    /// <summary>
    /// Derives puzzle pose and scale from real surface geometry: the puzzle sits on the player's side of the
    /// surface within comfortable seated reach, inset from the edges, sized to the available area.
    /// </summary>
    public static class SurfacePlacementSolver
    {
        public const float MinRadius = 0.11f;
        public const float MaxRadius = 0.18f;
        public const float SeatedReach = 0.42f;      // head-to-puzzle horizontal distance that keeps elbows relaxed
        public const float MinHorizontalDot = 0.9f;  // ~25 degrees from level
        public const float MinSurfaceSide = 0.22f;
        public const float MaxComfortReach = 0.8f;  // head-to-puzzle horizontal distance a seated player can still reach
        public const float EyeToPuzzleDrop = 0.25f;  // puzzle centre this far below the eyes, whatever the surface height
        public const float MaxPivotHeight = 0.6f;

        public static float RadiusFor(Vector2 size) => Mathf.Clamp(Mathf.Min(size.x, size.y) * 0.4f, MinRadius, MaxRadius);

        /// <summary>Minimum lift that keeps the curve clear of the surface in any rotation.</summary>
        public static float PivotHeightFor(float radius) => radius * 1.3f + 0.05f;

        /// <summary>
        /// Lift that puts the puzzle centre at a comfortable height below the eyes, so a low table does not leave
        /// the puzzle down at the player's knees. The shadow is an orthographic projection along the normal, so
        /// the lift does not change the shape being matched.
        /// </summary>
        public static float PivotHeightFor(float radius, float eyeHeightAboveSurface) =>
            Mathf.Clamp(eyeHeightAboveSurface - EyeToPuzzleDrop, PivotHeightFor(radius), Mathf.Max(PivotHeightFor(radius), MaxPivotHeight));

        /// <summary>Scores a candidate for seated play; returns float.NegativeInfinity when unusable.</summary>
        public static float Score(in SurfaceCandidate s, Vector3 headPosition) => Score(s, headPosition, Vector3.zero);

        /// <param name="headForward">When non-zero, surfaces whose nearest point is behind or beside the player are rejected.</param>
        public static float Score(in SurfaceCandidate s, Vector3 headPosition, Vector3 headForward)
        {
            if (Vector3.Dot(s.Normal, Vector3.up) < MinHorizontalDot) return float.NegativeInfinity;
            if (Mathf.Min(s.Size.x, s.Size.y) < MinSurfaceSide) return float.NegativeInfinity;

            // Seated desk/table height relative to the eyes; excludes couch seats, beds, shelves above the head.
            float below = headPosition.y - s.Center.y;
            if (below < 0.2f || below > 0.8f) return float.NegativeInfinity;

            var facing = Vector3.ProjectOnPlane(headForward, Vector3.up);
            if (facing.sqrMagnitude > 1e-4f)
            {
                var toCenter = Vector3.ProjectOnPlane(s.Center - headPosition, Vector3.up);
                float reachIntoSurface = 0.5f * Mathf.Min(s.Size.x, s.Size.y);
                // Allow surfaces we sit at/over (centre within half their size), otherwise require ~+-70 degrees.
                if (toCenter.magnitude > reachIntoSurface && Vector3.Dot(toCenter.normalized, facing.normalized) < 0.34f)
                    return float.NegativeInfinity;
            }

            var flat = s.Center - headPosition;
            flat.y = 0f;
            float distance = Mathf.Max(0f, flat.magnitude - 0.5f * Mathf.Max(s.Size.x, s.Size.y));
            if (distance > 1.6f) return float.NegativeInfinity;

            float kindBonus = s.Kind == SurfaceKind.Table ? 2f : 0f;
            float heightFit = 1f - Mathf.Abs(below - 0.5f);  // seated table top is ~0.4-0.6 m below the eyes
            return kindBonus + heightFit - distance * 1.5f;
        }

        public static PuzzlePlacement Solve(in SurfaceCandidate s, Vector3 headPosition, Vector3 headForward)
        {
            var n = s.Normal.sqrMagnitude > 0.5f ? s.Normal : Vector3.up;
            var u = Vector3.ProjectOnPlane(s.AxisU, n).normalized;
            if (u.sqrMagnitude < 0.5f) u = Vector3.ProjectOnPlane(Vector3.right, n).normalized;
            var v = Vector3.Cross(n, u);

            float radius = RadiusFor(s.Size);

            // Player's head projected onto the surface plane, expressed in (u, v) around the centre.
            var headOnPlane = headPosition - n * Vector3.Dot(headPosition - s.Center, n);
            var rel = headOnPlane - s.Center;
            var head2 = new Vector2(Vector3.Dot(rel, u), Vector3.Dot(rel, v));

            var away = -head2;
            if (away.sqrMagnitude < 1e-4f)
            {
                var f = Vector3.ProjectOnPlane(headForward, n);
                away = new Vector2(Vector3.Dot(f, u), Vector3.Dot(f, v));
                if (away.sqrMagnitude < 1e-6f) away = Vector2.up;
            }
            away.Normalize();

            var desired = head2 + away * SeatedReach;

            float margin = radius + 0.03f;  // keeps the projected shadow on the surface
            var half = s.Size * 0.5f;
            desired.x = half.x > margin ? Mathf.Clamp(desired.x, -half.x + margin, half.x - margin) : 0f;
            desired.y = half.y > margin ? Mathf.Clamp(desired.y, -half.y + margin, half.y - margin) : 0f;

            var position = s.Center + u * desired.x + v * desired.y;
            var forward = position - headOnPlane;
            forward = Vector3.ProjectOnPlane(forward, n);
            if (forward.sqrMagnitude < 1e-4f) forward = u * away.x + v * away.y;

            return new PuzzlePlacement
            {
                Position = position,
                Rotation = Quaternion.LookRotation(forward.normalized, n),
                PuzzleRadius = radius,
                PivotHeight = PivotHeightFor(radius, Vector3.Dot(headPosition - position, n)),
                Kind = s.Kind,
                GardenBand = GardenBandFor(desired, half, new Vector2(Vector3.Dot(forward.normalized, u), Vector3.Dot(forward.normalized, v)), radius)
            };
        }

        /// <summary>
        /// How far past the shadow the garden's ring can reach before any of its slots leaves the surface. The puzzle
        /// sits on the player's side, so the far side (where the garden is) usually has more room than the margin.
        /// </summary>
        /// <param name="centre">Puzzle centre in the surface's (u, v) frame, relative to the surface centre.</param>
        /// <param name="forward">Unit direction away from the player, in the same frame.</param>
        public static float GardenBandFor(Vector2 centre, Vector2 half, Vector2 forward, float radius)
        {
            var right = new Vector2(forward.y, -forward.x);
            float reach = float.MaxValue;
            for (int slot = 0; slot < GardenLayout.Order.Length; slot++)
            {
                float th = GardenLayout.SlotDegrees(slot) * Mathf.Deg2Rad;
                var dir = right * Mathf.Cos(th) + forward * Mathf.Sin(th);
                if (Mathf.Abs(dir.x) > 1e-5f) reach = Mathf.Min(reach, (half.x - Mathf.Sign(dir.x) * centre.x) / Mathf.Abs(dir.x));
                if (Mathf.Abs(dir.y) > 1e-5f) reach = Mathf.Min(reach, (half.y - Mathf.Sign(dir.y) * centre.y) / Mathf.Abs(dir.y));
            }
            return Mathf.Clamp(reach - radius, GardenLayout.MinBand, GardenLayout.MaxBand);
        }

        /// <summary>
        /// Picks the best-scoring candidate whose solved placement is within seated reach.
        /// Returns false when none qualifies (caller falls back to manual placement).
        /// </summary>
        public static bool TrySelect(System.Collections.Generic.IReadOnlyList<SurfaceCandidate> candidates, Vector3 headPosition,
            Vector3 headForward, out PuzzlePlacement placement, out int index)
        {
            placement = default;
            index = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                float s = Score(candidates[i], headPosition, headForward);
                if (s <= bestScore) continue;
                var p = Solve(candidates[i], headPosition, headForward);
                var flat = p.Position - headPosition;
                flat.y = 0f;
                if (flat.magnitude > MaxComfortReach) continue;
                bestScore = s;
                placement = p;
                index = i;
            }
            return index >= 0;
        }

        /// <summary>Virtual surface in front of a seated player, used when no room data exists.</summary>
        public static SurfaceCandidate HeadRelativeFallback(Vector3 headPosition, Vector3 headForward, float dropBelowHead = 0.5f)
        {
            var f = Vector3.ProjectOnPlane(headForward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            var center = headPosition + f * SeatedReach - Vector3.up * dropBelowHead;
            return new SurfaceCandidate(center, Vector3.up, Vector3.Cross(Vector3.up, f), f, new Vector2(0.6f, 0.5f), SurfaceKind.Fallback);
        }
    }
}
