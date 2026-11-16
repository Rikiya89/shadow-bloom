using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// Uniform-grid index over a projected shape's segments for fast point-to-polyline distance.
    /// Distances are exact up to <see cref="MaxDistance"/> and clamped beyond it (a point that far away is
    /// unambiguously "wrong", so the exact value adds nothing). Buffers grow once and are then reused.
    /// </summary>
    public sealed class SegmentGrid
    {
        public float MaxDistance { get; private set; }

        Vector2[] _a = new Vector2[0];
        Vector2[] _b = new Vector2[0];
        int _segCount;
        int[] _cellStart = new int[0];
        int[] _cellCount = new int[0];
        int[] _items = new int[0];
        Vector2 _origin;
        float _cell;
        float _invCell;
        int _nx, _ny;

        /// <param name="offset">Added to every point (used to centre shapes on their centroid).</param>
        public void Build(ProjectedShape shape, Vector2 offset, float cellSize, float maxDistance)
        {
            MaxDistance = maxDistance;
            _cell = Mathf.Max(1e-5f, cellSize);
            _invCell = 1f / _cell;

            int segs = 0;
            for (int s = 0; s < shape.StripCount; s++) segs += shape.StripClosed[s] ? shape.StripLength[s] : shape.StripLength[s] - 1;
            Ensure(ref _a, segs);
            Ensure(ref _b, segs);

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            int k = 0;
            var pts = shape.Points;
            for (int s = 0; s < shape.StripCount; s++)
            {
                int start = shape.StripStart[s], len = shape.StripLength[s];
                int n = shape.StripClosed[s] ? len : len - 1;
                for (int j = 0; j < n; j++)
                {
                    var a = pts[start + j] + offset;
                    var b = pts[start + (j + 1) % len] + offset;
                    _a[k] = a;
                    _b[k] = b;
                    k++;
                    min = Vector2.Min(min, Vector2.Min(a, b));
                    max = Vector2.Max(max, Vector2.Max(a, b));
                }
            }
            _segCount = k;

            _origin = min - Vector2.one * _cell;
            _nx = Mathf.Max(1, Mathf.CeilToInt((max.x - _origin.x) * _invCell) + 2);
            _ny = Mathf.Max(1, Mathf.CeilToInt((max.y - _origin.y) * _invCell) + 2);
            int cells = _nx * _ny;
            Ensure(ref _cellStart, cells);
            Ensure(ref _cellCount, cells);
            System.Array.Clear(_cellCount, 0, cells);

            // Pass 1: count, pass 2: fill (CSR layout, no per-cell lists).
            int total = 0;
            for (int i = 0; i < _segCount; i++)
            {
                SegCells(i, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++) { _cellCount[y * _nx + x]++; total++; }
            }
            Ensure(ref _items, total);
            int run = 0;
            for (int c = 0; c < cells; c++)
            {
                _cellStart[c] = run;
                run += _cellCount[c];
                _cellCount[c] = 0;
            }
            for (int i = 0; i < _segCount; i++)
            {
                SegCells(i, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int c = y * _nx + x;
                        _items[_cellStart[c] + _cellCount[c]++] = i;
                    }
            }
        }

        /// <summary>Distance from p to the nearest indexed segment, clamped to MaxDistance.</summary>
        public float Distance(Vector2 p)
        {
            float maxSq = MaxDistance * MaxDistance;
            float best = maxSq;
            int cx = Mathf.FloorToInt((p.x - _origin.x) * _invCell);
            int cy = Mathf.FloorToInt((p.y - _origin.y) * _invCell);
            int maxRing = Mathf.CeilToInt(MaxDistance * _invCell) + 1;

            for (int r = 0; r <= maxRing; r++)
            {
                // Any segment in ring r is at least (r - 1) cells away.
                float ringMin = (r - 1) * _cell;
                if (r > 1 && ringMin * ringMin >= best) break;

                int y0 = cy - r, y1 = cy + r, x0 = cx - r, x1 = cx + r;
                for (int y = y0; y <= y1; y++)
                {
                    if (y < 0 || y >= _ny) continue;
                    bool edgeRow = y == y0 || y == y1;
                    int step = edgeRow ? 1 : Mathf.Max(1, x1 - x0);
                    for (int x = x0; x <= x1; x += step)
                    {
                        if (x < 0 || x >= _nx) continue;
                        int c = y * _nx + x;
                        int start = _cellStart[c], end = start + _cellCount[c];
                        for (int i = start; i < end; i++)
                        {
                            int s = _items[i];
                            float d = ProjectionMatcher.SqrDistancePointSegment(p, _a[s], _b[s]);
                            if (d < best) best = d;
                        }
                    }
                }
            }
            return Mathf.Sqrt(best);
        }

        void SegCells(int i, out int x0, out int y0, out int x1, out int y1)
        {
            var a = _a[i];
            var b = _b[i];
            x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - _origin.x) * _invCell), 0, _nx - 1);
            x1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.x, b.x) - _origin.x) * _invCell), 0, _nx - 1);
            y0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y, b.y) - _origin.y) * _invCell), 0, _ny - 1);
            y1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.y, b.y) - _origin.y) * _invCell), 0, _ny - 1);
        }

        static void Ensure<T>(ref T[] arr, int size)
        {
            if (arr.Length < size) arr = new T[Mathf.NextPowerOfTwo(size)];
        }
    }

    /// <summary>
    /// Fast runtime form of <see cref="ProjectionMatcher.ComputeError"/>. The target grid is built once; the
    /// current grid is rebuilt per evaluation (linear in segment count). Per-point distances are clamped at
    /// <c>ClampFactor * normScale</c>, which leaves every value inside the tolerance band exact.
    /// </summary>
    public sealed class FastShapeMatcher
    {
        public const float ClampFactor = 0.5f;
        const float CellFactor = 0.08f;

        readonly SegmentGrid _targetGrid = new SegmentGrid();
        readonly SegmentGrid _currentGrid = new SegmentGrid();
        ProjectedShape _target;
        float _normScale;

        public void SetTarget(ProjectedShape target, float normScale)
        {
            _target = target;
            _normScale = normScale;
            _targetGrid.Build(target, -target.Centroid(), normScale * CellFactor, normScale * ClampFactor);
        }

        public float ComputeError(ProjectedShape current)
        {
            if (_target == null || _normScale <= 1e-6f) return float.PositiveInfinity;
            var cc = current.Centroid();
            _currentGrid.Build(current, -cc, _normScale * CellFactor, _normScale * ClampFactor);

            var tc = _target.Centroid();
            float a = 0f;
            var cp = current.Points;
            for (int i = 0; i < cp.Length; i++) a += _targetGrid.Distance(cp[i] - cc);
            float b = 0f;
            var tp = _target.Points;
            for (int i = 0; i < tp.Length; i++) b += _currentGrid.Distance(tp[i] - tc);

            a /= Mathf.Max(1, cp.Length);
            b /= Mathf.Max(1, tp.Length);
            return 0.5f * (a + b) / _normScale;
        }
    }
}
