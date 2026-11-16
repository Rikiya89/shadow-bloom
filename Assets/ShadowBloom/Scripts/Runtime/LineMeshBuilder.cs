using UnityEngine;
using UnityEngine.Rendering;

namespace ShadowBloom
{
    /// <summary>Procedural line geometry. Tubes are built once per stage; ribbons are rewritten in place.</summary>
    public static class LineMeshBuilder
    {
        /// <summary>Thin tube around every strip. Normals feed the shader's view-dependent core glow.</summary>
        public static Mesh BuildTube(CurveSet curve, float radius, int sides, Mesh mesh = null)
        {
            sides = Mathf.Max(3, sides);
            int ringCount = 0;
            for (int s = 0; s < curve.StripCount; s++) ringCount += curve.StripLength[s] + (curve.StripClosed[s] ? 1 : 0);

            var verts = new Vector3[ringCount * sides];
            var normals = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var colors = new Color32[verts.Length];
            var tris = new int[(ringCount - curve.StripCount) * sides * 6];

            int v = 0, t = 0;
            for (int s = 0; s < curve.StripCount; s++)
            {
                int start = curve.StripStart[s];
                int len = curve.StripLength[s];
                bool closed = curve.StripClosed[s];
                int rings = len + (closed ? 1 : 0);
                int firstVert = v;

                var prevNormal = Vector3.zero;
                float arc = 0f;
                for (int r = 0; r < rings; r++)
                {
                    int i = r % len;
                    var p = curve.Points[start + i];
                    var tangent = Tangent(curve.Points, start, len, i, closed);

                    // Parallel transport keeps the tube from twisting along the curve.
                    var n = r == 0 ? AnyPerpendicular(tangent) : Vector3.ProjectOnPlane(prevNormal, tangent);
                    if (n.sqrMagnitude < 1e-8f) n = AnyPerpendicular(tangent);
                    n.Normalize();
                    prevNormal = n;
                    var b = Vector3.Cross(tangent, n);

                    if (r > 0) arc += (p - curve.Points[start + (r - 1) % len]).magnitude;
                    float key = (float)r / (rings - 1);

                    for (int k = 0; k < sides; k++)
                    {
                        float a = Mathf.PI * 2f * k / sides;
                        var dir = n * Mathf.Cos(a) + b * Mathf.Sin(a);
                        verts[v] = p + dir * radius;
                        normals[v] = dir;
                        uvs[v] = new Vector2(key, arc);
                        colors[v] = new Color32(255, 255, 255, 255);
                        v++;
                    }
                }

                for (int r = 0; r < rings - 1; r++)
                {
                    int a0 = firstVert + r * sides;
                    int b0 = a0 + sides;
                    for (int k = 0; k < sides; k++)
                    {
                        int k1 = (k + 1) % sides;
                        tris[t++] = a0 + k; tris[t++] = b0 + k; tris[t++] = a0 + k1;
                        tris[t++] = a0 + k1; tris[t++] = b0 + k; tris[t++] = b0 + k1;
                    }
                }
            }

            mesh ??= new Mesh { name = "CurveTube" };
            mesh.Clear();
            mesh.indexFormat = verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Vector3 Tangent(Vector3[] pts, int start, int len, int i, bool closed)
        {
            int prev = closed ? (i - 1 + len) % len : Mathf.Max(0, i - 1);
            int next = closed ? (i + 1) % len : Mathf.Min(len - 1, i + 1);
            var t = pts[start + next] - pts[start + prev];
            return t.sqrMagnitude > 1e-12f ? t.normalized : Vector3.forward;
        }

        static Vector3 AnyPerpendicular(Vector3 t)
        {
            var c = Vector3.Cross(t, Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right);
            return c.normalized;
        }
    }

    /// <summary>
    /// Flat soft-edged ribbon in a local XZ plane, three vertices across (edge, centre, edge) with vertex alpha
    /// 0-1-0. Buffers and topology are allocated once; <see cref="Update"/> only rewrites positions (no GC).
    /// </summary>
    public sealed class RibbonMesh
    {
        public readonly Mesh Mesh;
        readonly int[] _stripStart;
        readonly int[] _stripLength;
        readonly bool[] _stripClosed;
        readonly Vector3[] _verts;
        readonly Vector2[] _uvs;
        readonly Vector3[] _normals;

        public RibbonMesh(int[] stripStart, int[] stripLength, bool[] stripClosed, string name = "Ribbon")
        {
            _stripStart = stripStart;
            _stripLength = stripLength;
            _stripClosed = stripClosed;

            int samples = 0, segments = 0;
            for (int s = 0; s < stripStart.Length; s++)
            {
                int n = stripLength[s] + (stripClosed[s] ? 1 : 0);
                samples += n;
                segments += n - 1;
            }

            _verts = new Vector3[samples * 3];
            _uvs = new Vector2[_verts.Length];
            _normals = new Vector3[_verts.Length];
            var colors = new Color32[_verts.Length];
            var tris = new int[segments * 12];

            int v = 0, t = 0;
            for (int s = 0; s < stripStart.Length; s++)
            {
                int n = stripLength[s] + (stripClosed[s] ? 1 : 0);
                for (int i = 0; i < n; i++)
                {
                    colors[v] = new Color32(255, 255, 255, 0);
                    colors[v + 1] = new Color32(255, 255, 255, 255);
                    colors[v + 2] = new Color32(255, 255, 255, 0);
                    _normals[v] = _normals[v + 1] = _normals[v + 2] = Vector3.up;
                    if (i < n - 1)
                    {
                        int a = v, b = v + 3;
                        tris[t++] = a; tris[t++] = b; tris[t++] = a + 1;
                        tris[t++] = a + 1; tris[t++] = b; tris[t++] = b + 1;
                        tris[t++] = a + 1; tris[t++] = b + 1; tris[t++] = a + 2;
                        tris[t++] = a + 2; tris[t++] = b + 1; tris[t++] = b + 2;
                    }
                    v += 3;
                }
            }

            Mesh = new Mesh { name = name };
            Mesh.MarkDynamic();
            Mesh.indexFormat = _verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            Mesh.vertices = _verts;
            Mesh.normals = _normals;
            Mesh.uv = _uvs;
            Mesh.colors32 = colors;
            Mesh.triangles = tris;
        }

        /// <summary>Rewrites positions from plane-2D points (x, y) mapped to local (x, lift, y).</summary>
        public void Update(Vector2[] points, float width, float lift)
        {
            float half = width * 0.5f;
            int v = 0;
            for (int s = 0; s < _stripStart.Length; s++)
            {
                int start = _stripStart[s];
                int len = _stripLength[s];
                bool closed = _stripClosed[s];
                int n = len + (closed ? 1 : 0);
                float arc = 0f;
                for (int r = 0; r < n; r++)
                {
                    int i = r % len;
                    int prev = closed ? (i - 1 + len) % len : Mathf.Max(0, i - 1);
                    int next = closed ? (i + 1) % len : Mathf.Min(len - 1, i + 1);
                    var p = points[start + i];
                    var tan = points[start + next] - points[start + prev];
                    float m = tan.magnitude;
                    var side = m > 1e-9f ? new Vector2(-tan.y, tan.x) / m : Vector2.zero;

                    if (r > 0) arc += (p - points[start + (r - 1) % len]).magnitude;
                    float key = (float)r / (n - 1);

                    _verts[v] = new Vector3(p.x - side.x * half, lift, p.y - side.y * half);
                    _verts[v + 1] = new Vector3(p.x, lift, p.y);
                    _verts[v + 2] = new Vector3(p.x + side.x * half, lift, p.y + side.y * half);
                    _uvs[v] = _uvs[v + 1] = _uvs[v + 2] = new Vector2(key, arc);
                    v += 3;
                }
            }
            Mesh.SetVertices(_verts);
            Mesh.SetUVs(0, _uvs);
            Mesh.RecalculateBounds();
        }
    }
}
