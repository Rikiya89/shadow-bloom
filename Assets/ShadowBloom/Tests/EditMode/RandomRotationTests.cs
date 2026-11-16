using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ShadowBloom.Tests
{
    /// <summary>Wrong orientations in general (not just hand-picked ones) are rejected: flailing does not solve.</summary>
    public class RandomRotationTests
    {
        static IEnumerable<PuzzleStage> Stages() => DefaultStages.CreateAll();

        /// <summary>Uniform random rotation (Shoemake), deterministic.</summary>
        static Quaternion RandomRotation(System.Random rng)
        {
            double u1 = rng.NextDouble(), u2 = rng.NextDouble() * 2 * Mathf.PI, u3 = rng.NextDouble() * 2 * Mathf.PI;
            double a = System.Math.Sqrt(1 - u1), b = System.Math.Sqrt(u1);
            return new Quaternion((float)(a * System.Math.Sin(u2)), (float)(a * System.Math.Cos(u2)),
                                  (float)(b * System.Math.Sin(u3)), (float)(b * System.Math.Cos(u3)));
        }

        [TestCaseSource(nameof(Stages))]
        public void RandomOrientations_AreAlmostAlwaysRejected(PuzzleStage stage)
        {
            var puzzle = new StagePuzzle(stage, 0.15f);
            var rng = new System.Random(7);
            const int n = 400;
            int accepted = 0;
            for (int i = 0; i < n; i++)
            {
                if (puzzle.IsMatch(puzzle.EvaluatePlaneLocal(RandomRotation(rng)))) accepted++;
            }
            float fraction = accepted / (float)n;
            TestContext.WriteLine($"{stage.stageId}: {accepted}/{n} random orientations accepted ({fraction:P1})");
            // The ring is symmetric about its axis and under a flip, so a tilt cap on either side is a solution
            // (2 x (1 - cos 22deg) / 2 = ~7% of orientations). Every other stage should be far rarer.
            Assert.That(fraction, Is.LessThan(stage.isTutorial ? 0.1f : 0.03f));
        }

        [TestCaseSource(nameof(Stages))]
        public void AcceptedRandomOrientations_CastTheTargetShadow(PuzzleStage stage)
        {
            // Independent check with the exact reference metric: anything the runtime matcher accepts is a real match.
            var puzzle = new StagePuzzle(stage, 0.15f);
            var rng = new System.Random(11);
            for (int i = 0; i < 400; i++)
            {
                float fast = puzzle.EvaluatePlaneLocal(RandomRotation(rng));
                if (!puzzle.IsMatch(fast)) continue;
                Assert.That(puzzle.ReferenceError(), Is.LessThanOrEqualTo(stage.tolerance * 1.0001f));
            }
        }
    }
}
