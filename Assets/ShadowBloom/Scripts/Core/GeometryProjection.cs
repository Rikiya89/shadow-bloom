using UnityEngine;

namespace ShadowBloom
{
    /// <summary>Orthonormal frame of the puzzle surface. Normal points away from the surface (up for a table).</summary>
    public readonly struct PlaneBasis
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Right;
        public readonly Vector3 Forward;
        public readonly Vector3 Normal;

        public PlaneBasis(Vector3 origin, Quaternion rotation)
        {
            Origin = origin;
            Right = rotation * Vector3.right;
            Normal = rotation * Vector3.up;
            Forward = rotation * Vector3.forward;
        }

        public Vector2 ToPlane2D(Vector3 worldOnPlane)
        {
            var d = worldOnPlane - Origin;
            return new Vector2(Vector3.Dot(d, Right), Vector3.Dot(d, Forward));
        }

        public Vector3 FromPlane2D(Vector2 p, float lift = 0f) => Origin + Right * p.x + Forward * p.y + Normal * lift;
    }

    /// <summary>Flat 2D polylines in a plane's local basis, sharing strip layout with the source CurveSet.</summary>
    public sealed class ProjectedShape
    {
        public readonly Vector2[] Points;
        public readonly int[] StripStart;
        public readonly int[] StripLength;
        public readonly bool[] StripClosed;

        public ProjectedShape(CurveSet layout)
        {
            Points = new Vector2[layout.PointCount];
            StripStart = layout.StripStart;
            StripLength = layout.StripLength;
            StripClosed = layout.StripClosed;
        }

        public int PointCount => Points.Length;
        public int StripCount => StripStart.Length;

        public Vector2 Centroid()
        {
            var sum = Vector2.zero;
            for (int i = 0; i < Points.Length; i++) sum += Points[i];
            return sum / Mathf.Max(1, Points.Length);
        }

        /// <summary>Root-mean-square distance from the centroid. Used as a constant scale for error normalisation.</summary>
        public float RmsRadius()
        {
            var c = Centroid();
            float sum = 0f;
            for (int i = 0; i < Points.Length; i++) sum += (Points[i] - c).sqrMagnitude;
            return Mathf.Sqrt(sum / Mathf.Max(1, Points.Length));
        }
    }

    /// <summary>
    /// Mathematical "shadow": each world-space curve point is moved along a fixed projection direction
    /// until it meets the plane, then expressed in the plane's 2D basis. No shadow maps involved.
    /// </summary>
    public static class GeometryProjection
    {
        /// <summary>Projects a world point onto the plane along direction (need not be normalised; must not be parallel to the plane).</summary>
        public static Vector2 ProjectPoint(Vector3 world, in PlaneBasis plane, Vector3 direction)
        {
            float denom = Vector3.Dot(direction, plane.Normal);
            if (Mathf.Abs(denom) < 1e-5f) direction = -plane.Normal;
            else if (denom > 0f) direction = -direction; // always travel towards the surface
            denom = Vector3.Dot(direction, plane.Normal);

            float height = Vector3.Dot(world - plane.Origin, plane.Normal);
            var onPlane = world - direction * (height / denom);
            return plane.ToPlane2D(onPlane);
        }

        /// <summary>Allocation-free projection of all curve points. Output must be built from the same CurveSet layout.</summary>
        public static void Project(CurveSet curve, in Matrix4x4 localToWorld, in PlaneBasis plane, Vector3 direction, ProjectedShape output)
        {
            var src = curve.Points;
            var dst = output.Points;
            for (int i = 0; i < src.Length; i++)
            {
                dst[i] = ProjectPoint(localToWorld.MultiplyPoint3x4(src[i]), plane, direction);
            }
        }

        /// <summary>
        /// Projects a curve whose pose is given relative to the plane (pivot height above the surface, plane-local rotation, uniform scale).
        /// Results are independent of the plane's world orientation when the direction is the plane normal.
        /// </summary>
        public static void ProjectPlaneLocal(CurveSet curve, float pivotHeight, Quaternion planeLocalRotation, float scale,
            in PlaneBasis plane, ProjectedShape output)
        {
            var planeRot = Quaternion.LookRotation(plane.Forward, plane.Normal);
            var world = Matrix4x4.TRS(plane.Origin + plane.Normal * pivotHeight, planeRot * planeLocalRotation, Vector3.one * scale);
            Project(curve, world, plane, -plane.Normal, output);
        }
    }
}
