using System;
using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// One or more polylines ("strips") sampled from parametric curves, in object-local space.
    /// Local +Y is the axis that points along the surface normal at the identity rotation.
    /// </summary>
    public sealed class CurveSet
    {
        public readonly Vector3[] Points;
        public readonly int[] StripStart;
        public readonly int[] StripLength;
        public readonly bool[] StripClosed;

        public int StripCount => StripStart.Length;
        public int PointCount => Points.Length;

        public CurveSet(Vector3[][] strips, bool[] closed)
        {
            if (strips == null || strips.Length == 0) throw new ArgumentException("CurveSet needs at least one strip.");
            if (closed == null || closed.Length != strips.Length) throw new ArgumentException("closed must match strips.");

            int total = 0;
            for (int i = 0; i < strips.Length; i++) total += strips[i].Length;

            Points = new Vector3[total];
            StripStart = new int[strips.Length];
            StripLength = new int[strips.Length];
            StripClosed = (bool[])closed.Clone();

            int offset = 0;
            for (int i = 0; i < strips.Length; i++)
            {
                StripStart[i] = offset;
                StripLength[i] = strips[i].Length;
                Array.Copy(strips[i], 0, Points, offset, strips[i].Length);
                offset += strips[i].Length;
            }
        }

        /// <summary>Largest distance of any point from the local origin.</summary>
        public float BoundingRadius()
        {
            float max = 0f;
            for (int i = 0; i < Points.Length; i++) max = Mathf.Max(max, Points[i].magnitude);
            return max;
        }
    }
}
