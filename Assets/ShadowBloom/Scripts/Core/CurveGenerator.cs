using System;
using UnityEngine;

namespace ShadowBloom
{
    public enum CurveKind
    {
        Ring,
        Rose,
        TorusKnot,
        Orbit,
        Bloom,
        // Appended only: the kind is serialized as an int in the stage assets.
        Star,
        Viviani,
        Seed,
        Lissajous
    }

    /// <summary>Deterministic parameters for a unit-scale curve family. Radius ~1 in local units.</summary>
    [Serializable]
    public struct CurveParams
    {
        public CurveKind kind;
        [Range(32, 512)] public int samples;
        [Tooltip("Rose / bloom lobe count.")] public int petals;
        [Tooltip("Torus knot windings around the axis.")] public int p;
        [Tooltip("Torus knot windings through the hole.")] public int q;
        [Tooltip("Lobe depth for roses, tube radius for knots.")] public float minorRadius;
        [Tooltip("Out-of-plane amplitude (local Y).")] public float height;
        [Tooltip("Orbit loop count.")] public int loops;
        [Tooltip("Orbit loop tilt in degrees.")] public float tiltDegrees;
    }

    /// <summary>
    /// Builds curve strips in object-local space. Every family is designed so that its intended
    /// "reading" appears when viewed along local Y; stage solutions rotate it into place.
    /// </summary>
    public static class CurveGenerator
    {
        const float Tau = Mathf.PI * 2f;

        public static CurveSet Generate(in CurveParams c)
        {
            int n = Mathf.Max(32, c.samples);
            switch (c.kind)
            {
                case CurveKind.Ring:
                    return Single(Ring(n, 1f, 0f));
                case CurveKind.Rose:
                    return Single(Rose(n, Mathf.Max(2, c.petals), 1f, c.minorRadius, c.height, 0f));
                case CurveKind.TorusKnot:
                    return Single(TorusKnot(n, Mathf.Max(1, c.p), Mathf.Max(1, c.q), 1f - c.minorRadius, c.minorRadius, c.height));
                case CurveKind.Orbit:
                    return Orbit(n, Mathf.Max(2, c.loops), c.tiltDegrees);
                case CurveKind.Bloom:
                    return Bloom(n, c);
                case CurveKind.Star:
                    return Single(Star(n, c.petals, c.p, c.height));
                case CurveKind.Viviani:
                    return Single(Viviani(n, c.height));
                case CurveKind.Seed:
                    return Seed(n, c.minorRadius > 0f ? c.minorRadius : 0.5f, c.height);
                case CurveKind.Lissajous:
                    return Single(Lissajous(n, c.p, c.q, c.height));
                default:
                    throw new ArgumentOutOfRangeException(nameof(c.kind), c.kind, null);
            }
        }

        static CurveSet Single(Vector3[] strip) => new CurveSet(new[] { strip }, new[] { true });

        // Closed strips do not repeat the first point; consumers wrap the last segment.
        public static Vector3[] Ring(int n, float radius, float y)
        {
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = Tau * i / n;
                pts[i] = new Vector3(radius * Mathf.Cos(t), y, radius * Mathf.Sin(t));
            }
            return pts;
        }

        /// <summary>k-lobed flower r = R(1 - d + d cos kt), with a saddle wave in Y so off-axis views read differently.</summary>
        public static Vector3[] Rose(int n, int k, float radius, float depth, float height, float phase)
        {
            depth = Mathf.Clamp01(depth);
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = Tau * i / n;
                float r = radius * (1f - depth + depth * Mathf.Cos(k * (t + phase)));
                pts[i] = new Vector3(r * Mathf.Cos(t), height * Mathf.Sin(k * (t + phase)), r * Mathf.Sin(t));
            }
            return pts;
        }

        /// <summary>(p,q) torus knot around local Y. Viewed along Y it shows q-fold symmetry.</summary>
        public static Vector3[] TorusKnot(int n, int p, int q, float major, float minor, float heightScale)
        {
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = Tau * i / n;
                float r = major + minor * Mathf.Cos(q * t);
                pts[i] = new Vector3(r * Mathf.Cos(p * t), heightScale * minor * Mathf.Sin(q * t), r * Mathf.Sin(p * t));
            }
            return pts;
        }

        /// <summary>Interlocking tilted rings with decreasing radii, so the in-plane orientation is unambiguous.</summary>
        static CurveSet Orbit(int n, int loops, float tiltDeg)
        {
            var strips = new Vector3[loops][];
            var closed = new bool[loops];
            for (int l = 0; l < loops; l++)
            {
                float radius = 1f - 0.12f * l;
                var rot = Quaternion.Euler(0f, 360f * l / loops, 0f) * Quaternion.Euler(tiltDeg, 0f, 0f);
                var ring = Ring(n, radius, 0f);
                for (int i = 0; i < ring.Length; i++) ring[i] = rot * ring[i];
                strips[l] = ring;
                closed[l] = true;
            }
            return new CurveSet(strips, closed);
        }

        /// <summary>Outer ring + (2,petals) torus knot star + inner rose: a layered petals-fold composition.</summary>
        static CurveSet Bloom(int n, in CurveParams c)
        {
            int k = Mathf.Max(3, c.petals);
            var outer = Ring(n, 1f, 0f);
            var star = TorusKnot(n * 2, 2, k, 0.62f, Mathf.Max(0.05f, c.minorRadius), c.height / Mathf.Max(0.05f, c.minorRadius));
            var inner = Rose(n, k, 0.36f, 0.6f, c.height * 0.6f, Mathf.PI / k);
            return new CurveSet(new[] { outer, star, inner }, new[] { true, true, true });
        }

        /// <summary>
        /// {n/k} star polygon folded into a crown: straight edges in XZ, tips at +h and edge midpoints at -h, so only
        /// the view along Y shows the flat star. Vertex 0 lies on +X (mirror-symmetric about X). The sample count is
        /// rounded up to a multiple of n so every tip is sampled exactly.
        /// </summary>
        public static Vector3[] Star(int samples, int n, int k, float h)
        {
            n = Mathf.Max(5, n);
            if (k <= 1 || 2 * k >= n || Gcd(n, k) != 1) k = 2;
            int count = n * Mathf.CeilToInt(samples / (float)n);
            var pts = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float s = n * (float)i / count;
                int j = Mathf.Min(n - 1, Mathf.FloorToInt(s));
                float f = s - j;
                float a0 = Tau * (j * k % n) / n, a1 = Tau * ((j + 1) * k % n) / n;
                var p = Vector3.Lerp(new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)), f);
                p.y = h * Mathf.Cos(Tau * f);
                pts[i] = p;
            }
            return pts;
        }

        /// <summary>
        /// Viviani's curve with its axis turned to local Y (a = 0.5): a figure-8 along Y, a circle of radius 0.5 along X
        /// (when hy = a) and a U-shaped arc along Z. t runs over [0, 4pi); t = 0 and 2pi meet in a true double point.
        /// </summary>
        public static Vector3[] Viviani(int n, float hy)
        {
            const float a = 0.5f;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = 2f * Tau * i / n;
                pts[i] = new Vector3(2f * a * Mathf.Sin(t * 0.5f), hy * Mathf.Cos(t), a * Mathf.Sin(t));
            }
            return pts;
        }

        /// <summary>
        /// Seed of Life: seven level circles of radius r (one centred, six around it at 60 degree steps, every circle
        /// through the centre), lifted alternately to +h / -h so tilting slides them apart.
        /// </summary>
        static CurveSet Seed(int n, float r, float h)
        {
            var strips = new Vector3[7][];
            var closed = new bool[7];
            for (int m = 0; m < 7; m++)
            {
                var centre = m == 0 ? Vector3.zero
                    : new Vector3(r * Mathf.Cos(Tau * m / 6f), (m % 2 == 0 ? 1f : -1f) * h, r * Mathf.Sin(Tau * m / 6f));
                var ring = Ring(n, r, 0f);
                for (int i = 0; i < ring.Length; i++) ring[i] += centre;
                strips[m] = ring;
                closed[m] = true;
            }
            return new CurveSet(strips, closed);
        }

        /// <summary>
        /// Woven Lissajous loop: X = cos(pt), Z = sin(qt), Y = h cos((p+q)t).
        /// For p even and q odd, t and pi - t give the same (X, Z), so the shadow re-traces itself into an open arc;
        /// such pairs are bumped to p + 1.
        /// </summary>
        public static Vector3[] Lissajous(int n, int p, int q, float h)
        {
            p = Mathf.Max(1, p);
            q = Mathf.Max(1, q);
            if (p % 2 == 0 && q % 2 == 1) p += 1;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = Tau * i / n;
                pts[i] = new Vector3(Mathf.Cos(p * t), h * Mathf.Cos((p + q) * t), Mathf.Sin(q * t));
            }
            return pts;
        }

        static int Gcd(int a, int b)
        {
            while (b != 0) (a, b) = (b, a % b);
            return Mathf.Abs(a);
        }
    }
}
