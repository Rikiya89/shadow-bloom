using NUnit.Framework;
using UnityEngine;

namespace ShadowBloom.Tests
{
    public class BloomGeometryTests
    {
        [Test]
        public void EveryStage_SeedsTheBloomWithItsActualSolvedOutline_InsideTheMeshBudget()
        {
            foreach (var stage in DefaultStages.CreateAll())
            {
                try
                {
                    var curve = CurveGenerator.Generate(stage.curve);
                    var seed = new ProjectedShape(curve);
                    GeometryProjection.ProjectPlaneLocal(curve, 0.2f, stage.SolutionRotation, 0.18f,
                        new PlaneBasis(Vector3.zero, Quaternion.identity), seed);
                    var g = new BloomGeometry();
                    var layout = BloomLayout.Default(stage.bloomPetals, stage.bloomLayers, 0.18f, true);
                    g.Build(layout, 0.55f, seed);
                    int v = 0;
                    foreach (int strip in System.Linq.Enumerable.Range(0, seed.StripCount))
                    {
                        int count = seed.StripLength[strip] + (seed.StripClosed[strip] ? 1 : 0);
                        for (int p = 0; p < count; p++)
                        {
                            var expected = seed.Points[seed.StripStart[strip] + p % seed.StripLength[strip]];
                            var actual = g.Positions[v + p * 3];
                            Assert.That(Vector2.Distance(expected, new Vector2(actual.x, actual.z)), Is.LessThan(1e-6f), stage.stageId);
                            Assert.That(g.Keys[v + p * 3].y, Is.LessThan(0f), "stationary seed shader flag");
                            Assert.That(g.Bounds.Contains(actual), stage.stageId + " seed inside culling bounds");
                        }
                        v += count * 6; // backing and core
                    }
                    Assert.That(g.Positions.Count, Is.LessThan(65535), stage.stageId + " fits 16-bit mesh indices");
                }
                finally { Object.DestroyImmediate(stage); }
            }
        }

        [Test]
        public void OpenSeedStrip_EndsAlongItsOwnTangent()
        {
            var curve = new CurveSet(new[] { new[] { Vector3.zero, Vector3.right, Vector3.right * 2f } }, new[] { false });
            var seed = new ProjectedShape(curve);
            GeometryProjection.ProjectPlaneLocal(curve, 0f, Quaternion.identity, 0.1f,
                new PlaneBasis(Vector3.zero, Quaternion.identity), seed);
            var g = new BloomGeometry();
            g.Build(BloomLayout.Default(6, 3, 0.18f, false), 0.55f, seed);
            // The seed is emitted first: backing ribbon, 3 vertices per point. Vertex 6 = last point.
            var p = g.Positions[6];
            var n = g.Neighbours[6];
            Assert.That(n.x - p.x, Is.GreaterThan(0f), "an open end continues forward, not back to the second point");
        }

        static BloomGeometry Build(bool finale, float overshoot = 0.55f, int petals = 6, int layers = 3)
        {
            var g = new BloomGeometry();
            g.Build(BloomLayout.Default(petals, layers, 0.12f, finale), overshoot);
            return g;
        }

        static float Ease(float clock, float delay, float stagger, float overshoot) =>
            BloomGeometry.EaseOutBack(Mathf.Clamp01((clock - delay) / Mathf.Max(1e-3f, 1f - stagger)), overshoot);

        [TestCase(false, 0f)]
        [TestCase(false, 0.55f)]
        [TestCase(true, 0.55f)]
        [TestCase(true, 2f)]
        public void Bounds_ContainDeformedGeometry_AtEveryClock(bool finale, float overshoot)
        {
            var layout = BloomLayout.Default(finale ? 10 : 6, finale ? 5 : 3, 0.12f, finale);
            var g = new BloomGeometry();
            g.Build(layout, overshoot);
            var b = g.Bounds;
            b.Expand(0.0001f);
            const float twist = 0.6f;
            for (int c = 0; c <= 40; c++)
            {
                float clock = c / 40f;
                for (int i = 0; i < g.Positions.Count; i += 7)
                {
                    float e = Ease(clock, g.Elements[i].w, layout.Stagger, overshoot);
                    var p = BloomGeometry.Deform(g.Positions[i], g.Elements[i], g.Styles[i], e, twist);
                    Assert.That(b.Contains(p), $"clock {clock:F2} vertex {i} at {p} outside {b}");
                }
            }
        }

        [Test]
        public void ShadowRing_SettlesExactlyOnTheSolvedProjectionRadius()
        {
            var g = Build(false);
            // The first stroke is the solved shadow's circle (layer 0, no delay).
            for (int i = 0; i < 30; i++)
            {
                var p = BloomGeometry.Deform(g.Positions[i], g.Elements[i], g.Styles[i], 1f, 0.6f);
                Assert.AreEqual(0.12f, new Vector2(p.x, p.z).magnitude, 1e-4f);
                Assert.AreEqual(0f, g.Elements[i].w, "starts with no delay");
            }
        }

        [Test]
        public void Petals_AreEvenlySpaced_KFold()
        {
            const int k = 5;
            var g = Build(false, petals: k, layers: 1);
            var thetas = new System.Collections.Generic.SortedSet<float>();
            for (int i = 0; i < g.Elements.Count; i++)
            {
                if (g.Elements[i].z > 0f) thetas.Add(Mathf.Round(g.Elements[i].x * 1e4f) / 1e4f);   // tilted = petal
            }
            Assert.AreEqual(k, thetas.Count);
            float prev = float.NaN;
            foreach (var t in thetas)
            {
                if (!float.IsNaN(prev)) Assert.AreEqual(Mathf.PI * 2f / k, t - prev, 1e-3f);
                prev = t;
            }
        }

        [Test]
        public void Deform_AtStart_CollapsesTowardTheSurface_AndOpensUpwards()
        {
            var g = Build(false, overshoot: 0f);
            float maxStart = 0f, maxEnd = 0f;
            for (int i = 0; i < g.Positions.Count; i++)
            {
                if (g.Elements[i].z <= 0f) continue;
                maxStart = Mathf.Max(maxStart, BloomGeometry.Deform(g.Positions[i], g.Elements[i], g.Styles[i], 0f, 0f).y);
                maxEnd = Mathf.Max(maxEnd, BloomGeometry.Deform(g.Positions[i], g.Elements[i], g.Styles[i], 1f, 0f).y);
            }
            Assert.Less(maxStart, 0.01f, "petals begin nearly flat in the projection plane");
            Assert.Greater(maxEnd, 0.04f, "petals finish standing up into 3D");
        }

        [Test]
        public void Finale_IsRicher_AndStaysWithinVertexBudget()
        {
            var normal = Build(false);
            var finale = Build(true, petals: 10, layers: 5);
            Assert.Greater(finale.StrokeCount, normal.StrokeCount * 2);
            Assert.Less(finale.Positions.Count, 65535, "16-bit indices");
            Assert.AreEqual(0, finale.Indices.Count % 3);
        }

        [Test]
        public void EaseOutBack_HitsEndpoints()
        {
            Assert.AreEqual(0f, BloomGeometry.EaseOutBack(0f, 0.55f), 1e-6f);
            Assert.AreEqual(1f, BloomGeometry.EaseOutBack(1f, 0.55f), 1e-6f);
            Assert.AreEqual(1f, BloomGeometry.MaxEase(0f), 1e-6f);
            Assert.Greater(BloomGeometry.MaxEase(0.55f), 1f);
        }

        [Test]
        public void PetalFamilies_StartAndEndAtTheBase_AndNeverExceedThePetalLength(
            [Values] PetalShape shape)
        {
            Assert.That(PetalShapes.Radius(shape, -1f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(PetalShapes.Radius(shape, 1f), Is.EqualTo(0f).Within(1e-4f));
            float max = 0f;
            for (int i = 1; i < 200; i++)
            {
                float r = PetalShapes.Radius(shape, -1f + i / 100f);
                Assert.That(r, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f + 1e-5f), $"u = {-1f + i / 100f}");
                max = Mathf.Max(max, r);
            }
            Assert.That(max, Is.GreaterThan(0.75f), "the petal reaches (nearly) its full length");
            Assert.That(PetalShapes.Width(shape), Is.InRange(0.3f, 1f), "never wider than its slot between neighbours");
        }

        [Test]
        public void EveryPetalFamily_BuildsTheSameStrokes_InsideTheSameBounds([Values] PetalShape shape)
        {
            var round = new BloomGeometry();
            round.Build(BloomLayout.Default(6, 3, 0.12f, false), 0.55f);
            var layout = BloomLayout.Default(6, 3, 0.12f, false);
            layout.Petal = shape;
            var g = new BloomGeometry();
            g.Build(layout, 0.55f);
            Assert.That(g.StrokeCount, Is.EqualTo(round.StrokeCount));
            Assert.That(g.Bounds.extents.x, Is.LessThanOrEqualTo(round.Bounds.extents.x + 1e-5f), "no family reaches further than the round petal");
            foreach (var p in g.Positions) Assert.That(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z), Is.False);
        }

        [Test]
        public void GardenStem_ReachesDownToTheSurface_AndReplacesTheSurfaceRings()
        {
            var layout = BloomLayout.Default(6, 3, 0.02f, false);
            layout.HaloWidth = 0f;
            var flat = new BloomGeometry();
            flat.Build(layout, 0f);
            layout.Stem = 0.05f;
            var g = new BloomGeometry();
            g.Build(layout, 0f);
            Assert.That(g.StrokeCount, Is.EqualTo(flat.StrokeCount - 1), "two rings out, one stem in");
            float lowest = float.MaxValue;
            foreach (var p in g.Positions) lowest = Mathf.Min(lowest, p.y);
            Assert.That(lowest, Is.EqualTo(-0.05f).Within(1e-5f));
            Assert.That(g.Bounds.Contains(new Vector3(0f, -0.05f, 0f)), "the stem is never culled");
        }
    }
}
