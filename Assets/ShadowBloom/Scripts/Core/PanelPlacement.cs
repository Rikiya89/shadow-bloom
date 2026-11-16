using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// FoV-aware panel position. Beside the puzzle on the player's left by default; when that puts the panel's
    /// outer edge too far from the head-to-puzzle line, it swings around the puzzle toward the back until the edge
    /// is within <c>edgeDegrees</c>. 34 degrees keeps it inside the comfortable centre of the narrowest supported
    /// device (Quest 3S, ~96 degrees horizontal). All coordinates are in the puzzle's surface frame.
    /// </summary>
    public static class PanelPlacement
    {
        public const float DefaultEdgeDegrees = 34f;
        public const float MaxSwingDegrees = 75f;
        const float StepDegrees = 2.5f;

        /// <param name="headLocal">Head position in the surface frame.</param>
        /// <param name="radialOffset">Pivot to panel-centre distance in the surface plane (grab volume + clearance + half width).</param>
        /// <param name="bottom">Panel bottom-edge height above the surface.</param>
        /// <param name="centreAbove">Panel centre height above its bottom edge.</param>
        /// <param name="towardPlayer">Pull toward the player applied in the default (unswung) position.</param>
        /// <returns>Bottom-centre anchor in the surface frame.</returns>
        public static Vector3 Solve(Vector3 headLocal, float pivotHeight, float radialOffset, float halfWidth, float bottom,
            float centreAbove, float towardPlayer, float edgeDegrees, out float swingDegrees)
        {
            var pivot = new Vector3(0f, pivotHeight, 0f);
            for (float phi = 0f; phi <= MaxSwingDegrees + 1e-3f; phi += StepDegrees)
            {
                var c = Anchor(phi, radialOffset, bottom + centreAbove, towardPlayer);
                if (EdgeAngle(headLocal, pivot, c, halfWidth) <= edgeDegrees)
                {
                    swingDegrees = phi;
                    return new Vector3(c.x, bottom, c.z);
                }
            }
            swingDegrees = MaxSwingDegrees;
            var last = Anchor(MaxSwingDegrees, radialOffset, bottom, towardPlayer);
            return last;
        }

        public static Vector3 Anchor(float phiDegrees, float radialOffset, float y, float towardPlayer)
        {
            float p = phiDegrees * Mathf.Deg2Rad;
            return new Vector3(-radialOffset * Mathf.Cos(p), y, radialOffset * Mathf.Sin(p) - towardPlayer * Mathf.Cos(p));
        }

        /// <summary>
        /// Whether a pointing ray lands on the panel (grown by <paramref name="margin"/> metres on every side).
        /// The panel is given by three world-space corners, as RectTransform.GetWorldCorners returns them.
        /// </summary>
        public static bool RayHitsPanel(Vector3 origin, Vector3 direction, Vector3 bottomLeft, Vector3 topLeft,
            Vector3 bottomRight, float margin, float maxDistance)
        {
            var right = bottomRight - bottomLeft;
            var up = topLeft - bottomLeft;
            float width = right.magnitude, height = up.magnitude;
            if (width < 1e-5f || height < 1e-5f || direction.sqrMagnitude < 1e-10f) return false;
            right /= width;
            up /= height;
            var normal = Vector3.Cross(right, up);
            direction.Normalize();
            float facing = Vector3.Dot(direction, normal);
            if (Mathf.Abs(facing) < 1e-4f) return false;                  // ray runs along the panel
            float distance = Vector3.Dot(bottomLeft - origin, normal) / facing;
            if (distance <= 0f || distance > maxDistance) return false;  // behind the hand, or too far to mean it
            var hit = origin + direction * distance - bottomLeft;
            float x = Vector3.Dot(hit, right), y = Vector3.Dot(hit, up);
            return x >= -margin && x <= width + margin && y >= -margin && y <= height + margin;
        }

        /// <summary>Angle from the head-to-pivot line to the panel's far edge, in degrees.</summary>
        public static float EdgeAngle(Vector3 head, Vector3 pivot, Vector3 panelCentre, float halfWidth)
        {
            var toPanel = panelCentre - head;
            float d = toPanel.magnitude;
            if (d < 1e-4f) return 180f;
            return Vector3.Angle(pivot - head, toPanel) + Mathf.Atan(halfWidth / d) * Mathf.Rad2Deg;
        }
    }
}
