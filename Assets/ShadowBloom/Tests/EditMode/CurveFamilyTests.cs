using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShadowBloom.Tests
{
    /// <summary>Geometry of the star, infinity (Viviani), seed and weave (Lissajous) families, and the snap symmetries.</summary>
    public class CurveFamilyTests
    {
        const float Tau = Mathf.PI * 2f;

        static IEnumerable<PuzzleStage> AllStages() => DefaultStages.CreateAll().Concat(DefaultStages.CreateDailyOnly());

        [Test]
        public void Star_TipsLieOnTheUnitCircle_AtTheTopOfTheCrown_AndTheShadowHasFiveFoldSymmetry()
        {
            const int n = 5;
            const float h = 0.3f;
            var pts = CurveGenerator.Star(160, n, 2, h);
            Assert.That(pts.Length % n, Is.Zero, "sample count is a multiple of the point count");
            for (int m = 0; m < n; m++)
            {
                var tip = pts[m * pts.Length / n];
                Assert.AreEqual(1f, new Vector2(tip.x, tip.z).magnitude, 1e-5f, $"tip {m}");
                Assert.AreEqual(h, tip.y, 1e-5f, $"tip {m}");
            }

            var star = DefaultStages.CreateAll().First(s => s.curve.kind == CurveKind.Star);
            var puzzle = new StagePuzzle(star, 0.15f);
            Assert.That(puzzle.EvaluatePlaneLocal(Quaternion.Euler(0f, 72f, 0f) * star.SolutionRotation), Is.LessThan(1e-3f));
        }

        [Test]
        public void Viviani_SideViewIsExactlyTheCircleDecoy_AndTheShadowFitsTheFigureEightBox()
        {
            foreach (var p in CurveGenerator.Viviani(192, 0.5f))
            {
                Assert.AreEqual(0.5f, new Vector2(p.y, p.z).magnitude, 1e-5f);
                Assert.That(Mathf.Abs(p.x), Is.LessThanOrEqualTo(1f + 1e-5f));
                Assert.That(Mathf.Abs(p.z), Is.LessThanOrEqualTo(0.5f + 1e-5f));
            }
        }

        [Test]
        public void Seed_IsSevenLevelCircles_TheOuterSixThroughTheCentre()
        {
            const float r = 0.5f;
            var set = CurveGenerator.Generate(new CurveParams { kind = CurveKind.Seed, samples = 48, minorRadius = r, height = 0.22f });
            Assert.That(set.StripCount, Is.EqualTo(7));
            for (int s = 0; s < 7; s++)
            {
                var strip = Enumerable.Range(set.StripStart[s], set.StripLength[s]).Select(i => set.Points[i]).ToArray();
                Assert.That(set.StripClosed[s]);
                var centre = strip.Aggregate(Vector3.zero, (a, b) => a + b) / strip.Length;
                foreach (var p in strip)
                {
                    Assert.AreEqual(r, new Vector2(p.x - centre.x, p.z - centre.z).magnitude, 1e-4f, $"strip {s}");
                    Assert.AreEqual(centre.y, p.y, 1e-6f, "every circle is level");
                }
                if (s == 0) Assert.That(new Vector2(centre.x, centre.z).magnitude, Is.LessThan(1e-4f), "middle circle is centred");
                else Assert.That(strip.Min(p => new Vector2(p.x, p.z).magnitude), Is.LessThan(1e-3f), $"strip {s} passes the centre");
            }
        }

        [Test]
        public void Lissajous_EvenOddPairIsBumped_AndTheWeaveShadowDoesNotRetraceItself()
        {
            var guarded = CurveGenerator.Lissajous(64, 4, 3, 0.45f);
            var bumped = CurveGenerator.Lissajous(64, 5, 3, 0.45f);
            for (int i = 0; i < guarded.Length; i++) Assert.AreEqual(bumped[i], guarded[i]);

            static Vector2 Shadow(int p, int q, float t) => new Vector2(Mathf.Cos(p * t), Mathf.Sin(q * t));
            int distinct = 0, degenerate = 0;
            const int n = 256;
            for (int i = 0; i < n; i++)
            {
                float t = Tau * i / n;
                if (Vector2.Distance(Shadow(3, 2, t), Shadow(3, 2, Mathf.PI - t)) > 1e-3f) distinct++;
                if (Vector2.Distance(Shadow(4, 3, t), Shadow(4, 3, Mathf.PI - t)) < 1e-3f) degenerate++;
            }
            Assert.That(distinct, Is.GreaterThanOrEqualTo(n * 9 / 10), "(3,2) is a proper closed figure");
            Assert.That(degenerate, Is.EqualTo(n), "the check can fail: unguarded (4,3) re-traces every point");
        }

        [TestCaseSource(nameof(AllStages))]
        public void EverySnapEquivalent_CastsTheTargetShadow(PuzzleStage stage)
        {
            var puzzle = new StagePuzzle(stage, 0.15f);
            int folds = StageSymmetry.Folds(stage.curve);
            for (int flip = 0; flip < (StageSymmetry.Mirror(stage.curve.kind) ? 2 : 1); flip++)
            for (int i = 0; i < folds; i++)
            {
                var q = Quaternion.Euler(0f, 360f * i / folds, 0f) * stage.SolutionRotation *
                        (flip == 0 ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f));
                Assert.That(puzzle.EvaluatePlaneLocal(q), Is.LessThan(stage.tolerance * 0.25f), $"{stage.stageId} fold {i} flip {flip}");
            }
        }
    }
}
