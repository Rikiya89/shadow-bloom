using System.Collections.Generic;
using UnityEngine;

namespace ShadowBloom
{
    /// <summary>Outline family of a bloom's petals. Appended only: values may be cached or serialized.</summary>
    public enum PetalShape
    {
        /// <summary>One rose lobe: the original rounded lotus petal.</summary>
        Round,
        /// <summary>Leaf-like, with a clear tip.</summary>
        Pointed,
        /// <summary>Heart-shaped tip, like a cherry blossom.</summary>
        Notched,
        /// <summary>Narrow rays, like a chrysanthemum.</summary>
        Slender,
        /// <summary>Broad and blunt, widest near the tip.</summary>
        Spoon
    }

    public static class PetalShapes
    {
        /// <summary>Each curve family blooms into its own flower, so the garden shows which shadows were solved.</summary>
        public static PetalShape For(CurveKind kind)
        {
            // TODO(you): which flower each shadow becomes. This is an art choice; any mapping works.
            switch (kind)
            {
                case CurveKind.Star: return PetalShape.Pointed;
                case CurveKind.Rose: return PetalShape.Notched;
                case CurveKind.Viviani: return PetalShape.Slender;
                case CurveKind.TorusKnot: return PetalShape.Spoon;
                case CurveKind.Orbit: return PetalShape.Slender;
                case CurveKind.Lissajous: return PetalShape.Notched;
                case CurveKind.Bloom: return PetalShape.Pointed;
                default: return PetalShape.Round;   // ring, seed
            }
        }

        /// <summary>
        /// Petal outline in polar form: radius (0..1 of the petal length) at <paramref name="u"/> in [-1, 1] across
        /// the petal's angular width. 0 at both edges (the base), 1 or just under at the centre (the tip).
        /// </summary>
        public static float Radius(PetalShape shape, float u)
        {
            float a = Mathf.Clamp01(Mathf.Abs(u));
            switch (shape)
            {
                case PetalShape.Pointed: return 1f - Mathf.Pow(a, 1.25f);
                case PetalShape.Notched: return Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a * Mathf.PI * 0.5f)), 0.7f) * (1f - 0.22f * Mathf.Exp(-(a * a) / 0.04f));
                case PetalShape.Slender: return 1f - Mathf.Pow(a, 1.6f);
                case PetalShape.Spoon: return 1f - Mathf.Pow(a, 3.5f);
                default: return Mathf.Cos(a * Mathf.PI * 0.5f);
            }
        }

        /// <summary>Angular width of a petal relative to the round petal.</summary>
        public static float Width(PetalShape shape) => shape == PetalShape.Slender ? 0.5f : 1f;
    }

    /// <summary>Composition of one bloom. Lengths in metres, angles in degrees.</summary>
    public struct BloomLayout
    {
        public int Petals;
        public int Layers;
        public float Radius;            // solved shadow radius (= puzzle radius)
        public bool Finale;
        public float Expansion;         // outer extent as a multiple of Radius
        public float Stagger;           // max element delay on the 0..1 bloom clock
        public float TiltInner;         // inner petals stand up...
        public float TiltOuter;         // ...outer petals open flat, like a lotus
        public float Cup;               // petal edge curl
        public float StartScale;
        public int SamplesPerPetal;
        public int RingSamples;
        public float HaloWidth;         // dark backing ribbon width multiple; <= 1 disables the backing pass
        public float HaloAlpha;
        public PetalShape Petal;
        /// <summary>Garden flowers: a stem of this length below the flower head (the surface rings are left out). 0 = none.</summary>
        public float Stem;

        public static BloomLayout Default(int petals, int layers, float radius, bool finale) => new BloomLayout
        {
            Petals = Mathf.Clamp(petals, 3, 16),
            Layers = Mathf.Clamp(layers, 1, 6),
            Radius = Mathf.Max(0.01f, radius),
            Finale = finale,
            Expansion = finale ? 1.65f : 1.4f,
            Stagger = finale ? 0.6f : 0.35f,
            TiltInner = 60f,
            TiltOuter = 16f,
            Cup = 0.5f,
            StartScale = 0.25f,
            SamplesPerPetal = 40,
            RingSamples = 96,
            HaloWidth = 4f,
            HaloAlpha = 0.35f
        };
    }

    /// <summary>
    /// Builds the bloom's stroke mesh data in element-local form for the BloomPetal shader, which unfolds it.
    /// <see cref="Deform"/> mirrors the shader exactly so bounds and timing can be tested on the CPU.
    ///
    /// Petal (one rose lobe) of length L and half-angle w, s in [0,1]:
    ///   phi = (2s-1) w,  r = L cos(phi pi / 2w),  q = (r cos phi, cup (r sin phi)^2 / L, r sin phi)
    /// Layer l (lf = l/(n-1)): L = lerp(0.72R, Expansion R, lf), base b = 0.18 R lf,
    ///   tilt = lerp(TiltInner, TiltOuter, lf), delay = Stagger (0.15 + 0.85 lf), phase = (l odd) pi / k.
    /// </summary>
    public sealed class BloomGeometry
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector4> Neighbours = new List<Vector4>();
        public readonly List<Vector4> Elements = new List<Vector4>();
        public readonly List<Vector4> Styles = new List<Vector4>();
        public readonly List<Vector2> Keys = new List<Vector2>();
        public readonly List<Color32> Colors = new List<Color32>();
        public readonly List<int> Indices = new List<int>();

        /// <summary>Conservative bounds of the deformed geometry over the whole animation (overshoot included).</summary>
        public Bounds Bounds { get; private set; }
        public int StrokeCount { get; private set; }

        readonly List<Vector3> _stroke = new List<Vector3>();
        readonly List<float> _strokeKeys = new List<float>();
        float _maxReach;

        public static float EaseOutBack(float u, float s)
        {
            float v = u - 1f;
            return 1f + (s + 1f) * v * v * v + s * v * v;
        }

        /// <summary>Peak eased value over u in [0,1] (above 1 when overshooting).</summary>
        public static float MaxEase(float overshoot)
        {
            float m = 1f;
            for (int i = 0; i <= 64; i++) m = Mathf.Max(m, EaseOutBack(i / 64f, overshoot));
            return m;
        }

        /// <summary>CPU mirror of BloomPetal.shader Deform(). twist in radians.</summary>
        public static Vector3 Deform(Vector3 q, Vector4 element, Vector4 style, float e, float twist)
        {
            float sc = Mathf.LerpUnclamped(style.w, 1f, e);
            float a = element.z * e;
            float s = Mathf.Sin(a), c = Mathf.Cos(a);
            var p = new Vector3(q.x * c - q.y * s, q.x * s + q.y * c, q.z) * sc;
            p.x += element.y * sc;
            p.y += style.x * e;
            float yaw = element.x + twist * (1f - e);
            s = Mathf.Sin(yaw); c = Mathf.Cos(yaw);
            return new Vector3(p.x * c - p.z * s, p.y, p.x * s + p.z * c);
        }

        public void Build(in BloomLayout l, float overshoot, ProjectedShape seed = null)
        {
            Positions.Clear(); Neighbours.Clear(); Elements.Clear(); Styles.Clear();
            Keys.Clear(); Colors.Clear(); Indices.Clear();
            StrokeCount = 0;
            _maxReach = 0f;

            float R = l.Radius;
            float eMax = MaxEase(overshoot);

            // Preserve the solved outline as the seed. Preview/garden callers can use the circular fallback.
            if (l.Stem > 0f) Stem(l.Stem, l);
            else
            {
                if (seed == null) Ring(R, 0f, 0f, 0f, 0.003f, 1f, 0f, l);
                else ShadowSeed(seed, l);
                Ring(R * l.Expansion * 1.05f, 0f, 0f, 0.12f * l.Stagger, 0.003f, 1f / (l.Expansion * 1.05f), 1f, l);
            }

            if (l.Finale)
            {
                // Seed of Life on the surface: six circles through the centre, then the crown above the lotus.
                float rs = R * 0.5f;
                Ring(rs, 0f, 0f, 0.06f, 0.004f, 0.2f, 0.15f, l);
                for (int i = 0; i < 6; i++)
                    Ring(rs, Mathf.PI * 2f * i / 6f, rs, 0.08f + 0.02f * i, 0.004f, 0.2f, 0.15f, l);
                Ring(R * 0.32f, 0f, 0f, l.Stagger * 0.95f, R * 0.8f, 0.3f, 0.35f, l);
            }

            int layers = Mathf.Max(1, l.Layers);
            for (int li = 0; li < layers; li++)
            {
                float lf = layers == 1 ? 1f : (float)li / (layers - 1);
                int k = l.Finale && li < 2 ? Mathf.Max(3, l.Petals / 2) : l.Petals;
                float len = layers == 1 ? R * l.Expansion * 0.8f : Mathf.Lerp(R * 0.72f, R * l.Expansion, lf);
                float b = 0.18f * R * lf;
                float tilt = Mathf.Lerp(l.TiltInner, l.TiltOuter, lf) * Mathf.Deg2Rad;
                float delay = l.Stagger * (0.15f + 0.85f * lf);
                float phase = (li % 2) * Mathf.PI / k;
                float halfAngle = Mathf.PI / k * 0.92f * PetalShapes.Width(l.Petal);
                for (int j = 0; j < k; j++)
                {
                    float theta = Mathf.PI * 2f * j / k + phase;
                    Petal(len, halfAngle, l.Cup, new Vector4(theta, b, tilt, delay), lf, l);
                }
            }

            float reach = _maxReach * Mathf.Max(1f, Mathf.LerpUnclamped(l.StartScale, 1f, eMax)) + R * 0.8f * Mathf.Max(0f, eMax - 1f);
            float pad = 0.01f;
            Bounds = new Bounds(Vector3.zero, Vector3.one * 2f * (reach + pad));
        }

        void Petal(float len, float halfAngle, float cup, Vector4 element, float layer01, in BloomLayout l)
        {
            _stroke.Clear(); _strokeKeys.Clear();
            int n = Mathf.Max(8, l.SamplesPerPetal);
            for (int i = 0; i <= n; i++)
            {
                float s = (float)i / n;
                float phi = (2f * s - 1f) * halfAngle;
                float r = len * PetalShapes.Radius(l.Petal, 2f * s - 1f);
                float lat = r * Mathf.Sin(phi);
                _stroke.Add(new Vector3(r * Mathf.Cos(phi), cup * lat * lat / len, lat));
                _strokeKeys.Add(1f - Mathf.Abs(2f * s - 1f));   // grows from the base, both edges meeting at the tip
            }
            Emit(element, new Vector4(0f, 1f, 1f, l.StartScale), layer01, element.y, l);
        }

        /// <summary>A thin stem from the surface up to the flower head at the origin, leaning slightly at its foot.</summary>
        void Stem(float height, in BloomLayout l)
        {
            _stroke.Clear(); _strokeKeys.Clear();
            const int n = 10;
            for (int i = 0; i <= n; i++)
            {
                float down = 1f - (float)i / n;
                _stroke.Add(new Vector3(0.18f * height * down * down, -height * down, 0f));
                _strokeKeys.Add(0f);
            }
            // A resting element (no tilt, no delay) on the outer colour, so the stem is plain paper.
            Emit(Vector4.zero, new Vector4(0f, 1f, 1f, 1f), 1f, 0f, l, closed: false);
        }

        void ShadowSeed(ProjectedShape seed, in BloomLayout layout)
        {
            for (int strip = 0; strip < seed.StripCount; strip++)
            {
                _stroke.Clear(); _strokeKeys.Clear();
                int start = seed.StripStart[strip], count = seed.StripLength[strip];
                if (count < 2) continue;
                int n = count + (seed.StripClosed[strip] ? 1 : 0);
                for (int i = 0; i < n; i++)
                {
                    var p = seed.Points[start + i % count];
                    _stroke.Add(new Vector3(p.x, 0.003f, p.y));
                    _strokeKeys.Add(0f); // outline is already complete when the match locks
                }
                // Negative layer key marks a stationary seed: the GPU must not twist it away from the target.
                Emit(Vector4.zero, new Vector4(0f, 1f, 1f, 1f), -1f, 0f, layout, seed.StripClosed[strip]);
            }
        }

        void Ring(float radius, float theta, float baseRadius, float delay, float lift, float startScale, float layer01, in BloomLayout l)
        {
            _stroke.Clear(); _strokeKeys.Clear();
            int n = Mathf.Max(16, l.RingSamples);
            for (int i = 0; i <= n; i++)
            {
                float s = (float)i / n;
                float a = Mathf.PI * 2f * s;
                _stroke.Add(new Vector3(radius * Mathf.Cos(a), 0f, radius * Mathf.Sin(a)));
                _strokeKeys.Add(1f - Mathf.Abs(2f * s - 1f));
            }
            Emit(new Vector4(theta, baseRadius, 0f, delay), new Vector4(lift, 1f, 1f, startScale), layer01, baseRadius, l);
        }

        // One stroke as a 3-vertex-wide soft ribbon, plus an optional halo. A closed stroke repeats its first point
        // last (its end continues into the second point); an open stroke's end extends its own last segment.
        void Emit(Vector4 element, Vector4 style, float layer01, float baseRadius, in BloomLayout l, bool closed = true)
        {
            foreach (var q in _stroke) _maxReach = Mathf.Max(_maxReach, q.magnitude + baseRadius + Mathf.Abs(style.x));
            // Backing first: within one draw, later triangles blend over earlier ones, so the core stays on top.
            if (l.HaloWidth > 1f)
            {
                var backing = style;
                backing.y = l.HaloWidth;
                backing.z = l.HaloAlpha;
                AddRibbon(element, backing, layer01, closed);
            }
            AddRibbon(element, style, layer01, closed);
            StrokeCount++;
        }

        void AddRibbon(Vector4 element, Vector4 style, float layer01, bool closed)
        {
            int first = Positions.Count;
            int n = _stroke.Count;
            for (int i = 0; i < n; i++)
            {
                var q = _stroke[i];
                var next = i + 1 < n ? _stroke[i + 1] : closed ? _stroke[1] : q + (q - _stroke[i - 1]);
                var key = new Vector2(_strokeKeys[i], layer01);
                for (int side = -1; side <= 1; side++)
                {
                    Positions.Add(q);
                    Neighbours.Add(new Vector4(next.x, next.y, next.z, side));
                    Elements.Add(element);
                    Styles.Add(style);
                    Keys.Add(key);
                    Colors.Add(new Color32(255, 255, 255, side == 0 ? (byte)255 : (byte)0));
                }
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a0 = first + i * 3, b0 = a0 + 3;
                Indices.Add(a0); Indices.Add(b0); Indices.Add(a0 + 1);
                Indices.Add(a0 + 1); Indices.Add(b0); Indices.Add(b0 + 1);
                Indices.Add(a0 + 1); Indices.Add(b0 + 1); Indices.Add(a0 + 2);
                Indices.Add(a0 + 2); Indices.Add(b0 + 1); Indices.Add(b0 + 2);
            }
        }
    }
}
