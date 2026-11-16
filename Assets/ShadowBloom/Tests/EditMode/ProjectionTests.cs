using NUnit.Framework;
using UnityEngine;

namespace ShadowBloom.Tests
{
    public class ProjectionTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void ProjectPoint_AlongNormal_DropsHeight()
        {
            var plane = new PlaneBasis(new Vector3(1f, 0.7f, -2f), Quaternion.identity);
            var p = GeometryProjection.ProjectPoint(new Vector3(1.3f, 1.2f, -1.5f), plane, Vector3.down);
            Assert.That(p.x, Is.EqualTo(0.3f).Within(Eps));
            Assert.That(p.y, Is.EqualTo(0.5f).Within(Eps));
        }

        [Test]
        public void ProjectPoint_DirectionSignDoesNotMatter()
        {
            var plane = new PlaneBasis(Vector3.zero, Quaternion.Euler(20f, 40f, -10f));
            var w = new Vector3(0.2f, 0.5f, 0.1f);
            var a = GeometryProjection.ProjectPoint(w, plane, -plane.Normal);
            var b = GeometryProjection.ProjectPoint(w, plane, plane.Normal);
            Assert.That((a - b).magnitude, Is.LessThan(Eps));
        }

        [Test]
        public void ProjectPoint_Oblique_ShiftsByHeightTimesSlope()
        {
            var plane = new PlaneBasis(Vector3.zero, Quaternion.identity);
            // 45 degree light from +X: a point 0.5 above the origin lands 0.5 along -X.
            var p = GeometryProjection.ProjectPoint(new Vector3(0f, 0.5f, 0f), plane, new Vector3(-1f, -1f, 0f));
            Assert.That(p.x, Is.EqualTo(-0.5f).Within(Eps));
            Assert.That(p.y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void ProjectPoint_ParallelDirection_FallsBackToNormal()
        {
            var plane = new PlaneBasis(Vector3.zero, Quaternion.identity);
            var p = GeometryProjection.ProjectPoint(new Vector3(0.3f, 1f, 0.2f), plane, Vector3.right);
            Assert.That(p.x, Is.EqualTo(0.3f).Within(Eps));
            Assert.That(p.y, Is.EqualTo(0.2f).Within(Eps));
            Assert.That(float.IsNaN(p.x), Is.False);
        }

        static readonly Quaternion[] SurfaceOrientations =
        {
            Quaternion.identity,
            Quaternion.Euler(0f, 137f, 0f),          // table rotated in the room
            Quaternion.Euler(12f, -30f, 7f),         // slightly sloped surface
            Quaternion.Euler(-90f, 0f, 0f),          // a wall
            Quaternion.Euler(180f, 45f, 0f),         // upside-down (ceiling)
        };

        static readonly Vector3[] SurfaceOrigins =
        {
            Vector3.zero, new Vector3(1.2f, 0.74f, -0.6f), new Vector3(-3f, 2.5f, 4f), new Vector3(0f, -1f, 0f), new Vector3(5f, 0f, 5f)
        };

        [Test]
        public void PlaneLocalPose_GivesIdenticalProjection_OnAnySurfaceOrientation()
        {
            var curve = CurveGenerator.Generate(new CurveParams { kind = CurveKind.TorusKnot, samples = 96, p = 2, q = 3, minorRadius = 0.3f, height = 1f });
            var reference = new ProjectedShape(curve);
            var other = new ProjectedShape(curve);
            var local = Quaternion.Euler(33f, 71f, -12f);

            GeometryProjection.ProjectPlaneLocal(curve, 0.2f, local, 0.15f, new PlaneBasis(Vector3.zero, Quaternion.identity), reference);

            for (int i = 0; i < SurfaceOrientations.Length; i++)
            {
                var plane = new PlaneBasis(SurfaceOrigins[i], SurfaceOrientations[i]);
                GeometryProjection.ProjectPlaneLocal(curve, 0.2f, local, 0.15f, plane, other);
                for (int k = 0; k < curve.PointCount; k++)
                {
                    Assert.That((reference.Points[k] - other.Points[k]).magnitude, Is.LessThan(1e-4f), $"surface {i}, point {k}");
                }
            }
        }

        [Test]
        public void WorldSpaceEvaluate_AcceptsSolution_OnAnySurfaceOrientation()
        {
            foreach (var stage in DefaultStages.CreateAll())
            {
                var puzzle = new StagePuzzle(stage, 0.15f);
                for (int i = 0; i < SurfaceOrientations.Length; i++)
                {
                    var plane = new PlaneBasis(SurfaceOrigins[i], SurfaceOrientations[i]);
                    var pivot = Matrix4x4.TRS(plane.Origin + plane.Normal * 0.22f,
                        SurfaceOrientations[i] * stage.SolutionRotation, Vector3.one * 0.15f);
                    float err = puzzle.Evaluate(pivot, plane);
                    Assert.That(err, Is.LessThan(stage.tolerance * 0.25f), $"{stage.stageId} on surface {i}: {err}");

                    var wrong = Matrix4x4.TRS(plane.Origin + plane.Normal * 0.22f,
                        SurfaceOrientations[i] * stage.InitialRotation, Vector3.one * 0.15f);
                    Assert.That(puzzle.Evaluate(wrong, plane), Is.GreaterThan(stage.tolerance), $"{stage.stageId} initial on surface {i}");
                }
            }
        }

        [Test]
        public void ProjectedShape_RmsRadius_OfUnitCircle_IsOne()
        {
            var curve = CurveGenerator.Generate(new CurveParams { kind = CurveKind.Ring, samples = 256 });
            var shape = new ProjectedShape(curve);
            GeometryProjection.ProjectPlaneLocal(curve, 0f, Quaternion.identity, 1f, new PlaneBasis(Vector3.zero, Quaternion.identity), shape);
            Assert.That(shape.RmsRadius(), Is.EqualTo(1f).Within(1e-3f));
            Assert.That(shape.Centroid().magnitude, Is.LessThan(1e-4f));
        }
    }
}
