using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ShadowBloom.Tests
{
    public class MatcherTests
    {
        static IEnumerable<PuzzleStage> Stages() => DefaultStages.CreateAll();

        [TestCaseSource(nameof(Stages))]
        public void Solution_IsAccepted(PuzzleStage stage)
        {
            var puzzle = new StagePuzzle(stage, 0.15f);
            Assert.That(puzzle.EvaluatePlaneLocal(stage.SolutionRotation), Is.LessThan(1e-3f));
        }

        [TestCaseSource(nameof(Stages))]
        public void InitialRotation_IsClearlyRejected(PuzzleStage stage)
        {
            var puzzle = new StagePuzzle(stage, 0.15f);
            Assert.That(puzzle.EvaluatePlaneLocal(stage.InitialRotation), Is.GreaterThan(stage.tolerance * 2f));
        }

        static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward, new Vector3(1f, 0f, 1f).normalized };

        [TestCaseSource(nameof(Stages))]
        public void SmallHandJitter_FourDegrees_IsAccepted(PuzzleStage stage)
        {
            var puzzle = new StagePuzzle(stage, 0.15f);
            foreach (var axis in Axes)
            {
                // Plane-local offset applied on top of the solution (world-ish wobble of the held object).
                float err = puzzle.EvaluatePlaneLocal(Quaternion.AngleAxis(4f, axis) * stage.SolutionRotation);
                Assert.That(err, Is.LessThan(stage.tolerance), $"axis {axis}: {err}");
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void ThirtyDegreeTilt_IsRejected(PuzzleStage stage)
        {
            var puzzle = new StagePuzzle(stage, 0.15f);
            foreach (var axis in new[] { Vector3.right, Vector3.forward, new Vector3(1f, 0f, -1f).normalized })
            {
                float err = puzzle.EvaluatePlaneLocal(Quaternion.AngleAxis(30f, axis) * stage.SolutionRotation);
                Assert.That(err, Is.GreaterThan(stage.tolerance), $"axis {axis}: {err}");
            }
        }

        [Test]
        public void Ring_IsYawInvariant_AndFlipInvariant()
        {
            var ring = Array.Find(DefaultStages.CreateAll(), s => s.curve.kind == CurveKind.Ring);
            var puzzle = new StagePuzzle(ring, 0.15f);
            Assert.That(puzzle.EvaluatePlaneLocal(Quaternion.Euler(0f, 77f, 0f)), Is.LessThan(ring.tolerance * 0.25f));
            Assert.That(puzzle.EvaluatePlaneLocal(Quaternion.Euler(180f, 0f, 0f)), Is.LessThan(ring.tolerance * 0.25f));
        }

        [Test]
        public void Rose_RotatedByOnePetal_IsAccepted_HalfPetal_IsRejected()
        {
            var rose = Array.Find(DefaultStages.CreateAll(), s => s.curve.kind == CurveKind.Rose);
            var puzzle = new StagePuzzle(rose, 0.15f);
            float petal = 360f / rose.curve.petals;
            var onePetal = Quaternion.Euler(0f, petal, 0f) * rose.SolutionRotation;
            var halfPetal = Quaternion.Euler(0f, petal * 0.5f, 0f) * rose.SolutionRotation;
            Assert.That(puzzle.EvaluatePlaneLocal(onePetal), Is.LessThan(rose.tolerance * 0.25f));
            Assert.That(puzzle.EvaluatePlaneLocal(halfPetal), Is.GreaterThan(rose.tolerance));
        }

        [Test]
        public void Knot_FlippedUpsideDown_CastsMirroredStar_HandledConsistently()
        {
            // Flipping 180 degrees about X mirrors the projection (z -> -z). The (2,3) knot's projection has
            // mirror symmetry about some axis, so the flipped pose matches at some yaw: verify the matcher finds it.
            var knot = Array.Find(DefaultStages.CreateAll(), s => s.curve.kind == CurveKind.TorusKnot);
            var puzzle = new StagePuzzle(knot, 0.15f);
            float best = float.MaxValue;
            for (int yaw = 0; yaw < 360; yaw++)
            {
                best = Mathf.Min(best, puzzle.EvaluatePlaneLocal(Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(180f, 0f, 0f)));
            }
            Assert.That(best, Is.LessThan(knot.tolerance));
        }

        [Test]
        public void Error_IsInvariantToCyclicOffset_AndReversal()
        {
            var curve = CurveGenerator.Generate(new CurveParams { kind = CurveKind.Rose, samples = 120, petals = 5, minorRadius = 0.4f, height = 0.2f });
            var a = new ProjectedShape(curve);
            GeometryProjection.ProjectPlaneLocal(curve, 0f, Quaternion.identity, 1f, new PlaneBasis(Vector3.zero, Quaternion.identity), a);

            var b = new ProjectedShape(curve);
            int n = a.PointCount;
            for (int i = 0; i < n; i++) b.Points[i] = a.Points[(n - 1 - i + 37) % n]; // reversed and shifted by 37 samples

            Assert.That(ProjectionMatcher.ComputeError(b, a, a.RmsRadius()), Is.LessThan(1e-5f));
        }

        [Test]
        public void Error_PenalisesScale_EvenThoughShapeIsSimilar()
        {
            var stage = DefaultStages.CreateAll()[0];
            var puzzle = new StagePuzzle(stage, 0.15f);
            var bigger = new StagePuzzle(stage, 0.15f * 1.4f);
            GeometryProjection.ProjectPlaneLocal(bigger.MatchCurve, 0f, Quaternion.identity, 0.15f * 1.4f,
                new PlaneBasis(Vector3.zero, Quaternion.identity), bigger.Current);
            float err = ProjectionMatcher.ComputeError(bigger.Current, puzzle.Target, puzzle.Target.RmsRadius());
            Assert.That(err, Is.GreaterThan(stage.tolerance));
        }

        [TestCaseSource(nameof(Stages))]
        public void FastMatcher_EqualsReference_InsideClampRange_AndNeverExceedsIt(PuzzleStage stage)
        {
            var puzzle = new StagePuzzle(stage, 0.15f);
            var poses = new[]
            {
                stage.SolutionRotation,
                Quaternion.AngleAxis(6f, Vector3.right) * stage.SolutionRotation,
                Quaternion.AngleAxis(12f, new Vector3(1f, 0f, 1f).normalized) * stage.SolutionRotation,
                Quaternion.AngleAxis(20f, Vector3.up) * stage.SolutionRotation,
                stage.InitialRotation
            };
            for (int i = 0; i < poses.Length; i++)
            {
                float fast = puzzle.EvaluatePlaneLocal(poses[i]);
                float exact = puzzle.ReferenceError();
                Assert.That(fast, Is.LessThanOrEqualTo(exact + 1e-4f), $"pose {i}");
                if (exact < stage.tolerance * 1.5f) Assert.That(fast, Is.EqualTo(exact).Within(1e-4f), $"pose {i}");
            }
        }

        [Test]
        public void ZeroScale_DoesNotProduceNaN()
        {
            var shape = new ProjectedShape(CurveGenerator.Generate(new CurveParams { kind = CurveKind.Ring, samples = 32 }));
            float err = ProjectionMatcher.ComputeError(shape, shape, 0f);
            Assert.That(float.IsPositiveInfinity(err));
        }

        [Test]
        public void AlignmentFeedback_IsMonotonic_AndSaturates()
        {
            Assert.That(AlignmentFeedback.Evaluate(0.01f, 0.05f, 0.2f), Is.EqualTo(1f));
            Assert.That(AlignmentFeedback.Evaluate(0.2f, 0.05f, 0.2f), Is.EqualTo(0f), "starting pose reads as neutral");
            Assert.That(AlignmentFeedback.Evaluate(10f, 0.05f, 0.2f), Is.EqualTo(0f));
            Assert.That(AlignmentFeedback.Evaluate(0.06f, 0.05f, 0f), Is.InRange(0f, 1f), "degenerate far error");
            float prev = 1f;
            for (float e = 0.05f; e < 0.4f; e += 0.01f)
            {
                float v = AlignmentFeedback.Evaluate(e, 0.05f, 0.2f);
                Assert.That(v, Is.LessThanOrEqualTo(prev + 1e-6f));
                prev = v;
            }
        }
    }
}
