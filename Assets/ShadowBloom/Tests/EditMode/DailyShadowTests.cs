using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ShadowBloom.Tests
{
    /// <summary>Daily Shadow generation, streak/garden save, garden slots, FoV-aware panel and Assist leniency.</summary>
    public class DailyShadowTests
    {
        static readonly int FirstDay = DailyShadow.DayNumber(new DateTime(2026, 11, 1));

        [Test]
        public void SameDay_GivesTheSamePuzzle()
        {
            var a = DailyShadow.Create(FirstDay + 3);
            var b = DailyShadow.Create(FirstDay + 3);
            Assert.That(a.stageId, Is.EqualTo(b.stageId));
            Assert.That(a.curve.kind, Is.EqualTo(b.curve.kind));
            Assert.That(a.curve.petals, Is.EqualTo(b.curve.petals));
            Assert.That(a.solutionEuler, Is.EqualTo(b.solutionEuler));
            Assert.That(a.initialEuler, Is.EqualTo(b.initialEuler));
            Assert.That(a.bloomPetals, Is.EqualTo(b.bloomPetals));
        }

        [Test]
        public void DayNumber_RoundTrips_AndNeverDropsBelowOne()
        {
            var d = new DateTime(2026, 11, 18, 23, 59, 0);
            Assert.That(DailyShadow.DateOf(DailyShadow.DayNumber(d)), Is.EqualTo(d.Date));
            Assert.That(DailyShadow.DayNumber(new DateTime(2020, 1, 1)), Is.EqualTo(1));
            Assert.That(DailyShadow.DayNumber(new DateTime(2026, 1, 1)), Is.EqualTo(1));
        }

        [Test]
        public void Weekday_PicksTheFamily_DifficultyRisesThroughTheWeek()
        {
            var expected = new Dictionary<DayOfWeek, CurveKind>
            {
                { DayOfWeek.Monday, CurveKind.Star }, { DayOfWeek.Tuesday, CurveKind.Rose },
                { DayOfWeek.Wednesday, CurveKind.Viviani }, { DayOfWeek.Thursday, CurveKind.TorusKnot },
                { DayOfWeek.Saturday, CurveKind.Lissajous }, { DayOfWeek.Sunday, CurveKind.Bloom }
            };
            var fridays = new HashSet<CurveKind>();
            // Two weeks, so Friday shows both of its alternating families.
            for (int day = FirstDay; day < FirstDay + 14; day++)
            {
                var s = DailyShadow.Create(day);
                var weekday = DailyShadow.DateOf(day).DayOfWeek;
                if (weekday == DayOfWeek.Friday) fridays.Add(s.curve.kind);
                else Assert.That(s.curve.kind, Is.EqualTo(expected[weekday]), $"day {day}");
                Assert.That(s.isTutorial, Is.False);
                Assert.That(s.completion, Is.EqualTo(CompletionBehavior.FinalBloom));
                Assert.That(s.stageId, Does.StartWith(DailyShadow.IdPrefix));
            }
            Assert.That(fridays, Is.EquivalentTo(new[] { CurveKind.Orbit, CurveKind.Seed }), "Friday alternates orbit and seed");
        }

        [Test]
        public void FourWeeks_HaveVariety()
        {
            var combos = new HashSet<string>();
            for (int day = FirstDay; day < FirstDay + 28; day++)
            {
                var c = DailyShadow.Create(day).curve;
                combos.Add($"{c.kind}:{c.petals}:{c.q}:{c.loops}");
            }
            Assert.That(combos.Count, Is.GreaterThanOrEqualTo(6));
        }

        /// <summary>A year of puzzles: solvable, not pre-solved, and as strict as the curated stage of the same family.</summary>
        [Test]
        public void YearSweep_EveryDailyIsAFairPuzzle()
        {
            var failures = new List<string>();
            var weak = new Dictionary<CurveKind, int>();
            var total = new Dictionary<CurveKind, int>();
            for (int day = FirstDay; day < FirstDay + 400; day++)
            {
                var s = DailyShadow.Create(day);
                var p = new StagePuzzle(s, 0.15f);
                float tol = s.tolerance;
                var sol = s.SolutionRotation;
                float atSolution = p.EvaluatePlaneLocal(sol);
                float atStart = p.EvaluatePlaneLocal(s.InitialRotation);
                float tilted = p.EvaluatePlaneLocal(sol * Quaternion.AngleAxis(30f, Vector3.right));
                float nudged = p.EvaluatePlaneLocal(Quaternion.AngleAxis(4f, Vector3.up) * sol);
                string tag = $"day {day} {s.curve.kind} petals={s.curve.petals} q={s.curve.q} loops={s.curve.loops}";
                if (atSolution > 0.25f * tol) failures.Add($"{tag}: solution error {atSolution:F4}");
                if (atStart < 2f * tol) failures.Add($"{tag}: starts nearly solved {atStart:F4}");
                total[s.curve.kind] = total.GetValueOrDefault(s.curve.kind) + 1;
                if (atStart < 3f * tol) weak[s.curve.kind] = weak.GetValueOrDefault(s.curve.kind) + 1;
                if (tilted <= tol) failures.Add($"{tag}: 30deg tilt accepted {tilted:F4}");
                if (nudged > tol) failures.Add($"{tag}: 4deg yaw rejected {nudged:F4}");
                UnityEngine.Object.DestroyImmediate(s);
            }
            foreach (var kv in total)
                TestContext.WriteLine($"{kv.Key}: {weak.GetValueOrDefault(kv.Key)}/{kv.Value} days start below 3x tolerance (best-of fallback)");
            Assert.That(failures, Is.Empty, string.Join("\n", failures.GetRange(0, Math.Min(20, failures.Count))));
        }

        [Test]
        public void Streak_CountsOncePerDay_AndConsecutiveDaysAdd()
        {
            Assert.That(DailyShadow.NextStreak(0, 0, 50), Is.EqualTo(1), "first ever");
            Assert.That(DailyShadow.NextStreak(3, 50, 50), Is.EqualTo(3), "same day");
            Assert.That(DailyShadow.NextStreak(3, 50, 51), Is.EqualTo(4), "next day");
            Assert.That(DailyShadow.NextStreak(3, 50, 60), Is.GreaterThanOrEqualTo(1), "after a gap the streak is still valid");
        }

        [Test]
        public void SaveV2_LoadsIntact_WithEmptyDailyState()
        {
            const string v2 = "{\"version\":2,\"completedStageIds\":[\"01_circle\",\"02_petals\"],\"lastStageIndex\":1," +
                              "\"highestUnlockedStageIndex\":2,\"soundMuted\":true}";
            var save = new ProgressSaveSystem(new MemoryProgressStore { Json = v2 });
            Assert.That(save.Data.version, Is.EqualTo(3));
            Assert.That(save.IsCompleted("02_petals"));
            Assert.That(save.Data.soundMuted);
            Assert.That(save.Data.dailyStreak, Is.Zero);
            Assert.That(save.Data.lastDailyDay, Is.Zero);
            Assert.That(save.Data.gardenDays, Is.Not.Null.And.Empty);
        }

        [Test]
        public void RecordDaily_GrowsStreakAndGarden_CapsAtSeven_AndPersists()
        {
            var store = new MemoryProgressStore();
            var save = new ProgressSaveSystem(store);
            save.RecordDaily(100);
            save.RecordDaily(100);
            Assert.That(save.Data.dailyStreak, Is.EqualTo(1));
            Assert.That(save.Data.gardenDays, Is.EqualTo(new[] { 100 }));
            Assert.That(save.SolvedToday(100));
            Assert.That(save.SolvedToday(101), Is.False);
            for (int d = 101; d <= 109; d++) save.RecordDaily(d);
            Assert.That(save.Data.dailyStreak, Is.EqualTo(10));
            Assert.That(save.Data.bestStreak, Is.EqualTo(10));
            Assert.That(save.Data.gardenDays.Count, Is.EqualTo(DailyShadow.GardenCapacity));
            Assert.That(save.Data.gardenDays[0], Is.EqualTo(103), "oldest dropped first");

            var reloaded = new ProgressSaveSystem(store);
            Assert.That(reloaded.Data.dailyStreak, Is.EqualTo(10));
            Assert.That(reloaded.Data.gardenDays, Is.EqualTo(save.Data.gardenDays));
        }

        [Test]
        public void CampaignComplete_OnlyWhenEveryStageIsDone()
        {
            var ids = new[] { "a", "b" };
            var save = new ProgressSaveSystem(new MemoryProgressStore());
            Assert.That(save.IsCampaignComplete(ids), Is.False);
            save.MarkCompleted("a", 0);
            Assert.That(save.IsCampaignComplete(ids), Is.False);
            save.MarkCompleted("b", 1);
            Assert.That(save.IsCampaignComplete(ids));
        }

        [TestCase(0.11f, 0.03f)]
        [TestCase(0.15f, 0.03f)]
        [TestCase(0.18f, 0.03f)]
        [TestCase(0.11f, 0.06f)]
        [TestCase(0.15f, 0.045f)]
        [TestCase(0.18f, 0.06f)]
        public void GardenSlots_StayOnTheTable_BehindThePuzzle_OutsideTheShadow(float r, float band)
        {
            float extent = GardenLayout.Extent * GardenLayout.Scale(band);
            float chord = 2f * (r + GardenLayout.RingOffset * GardenLayout.Scale(band)) * Mathf.Sin(GardenLayout.StepDeg * 0.5f * Mathf.Deg2Rad);
            Assert.That(2f * extent, Is.LessThan(chord), "neighbouring flowers never overlap");
            var used = new HashSet<int>(GardenLayout.Order);
            Assert.That(used.Count, Is.EqualTo(DailyShadow.GardenCapacity));
            for (int slot = 0; slot < 7; slot++)
            {
                var p = GardenLayout.SlotLocal(slot, r, band);
                float d = new Vector2(p.x, p.z).magnitude;
                Assert.That(d + extent, Is.LessThan(r + band), "inside the free band the placement reported");
                Assert.That(d - extent, Is.GreaterThanOrEqualTo(r), "clear of the target shadow");
                Assert.That(p.z, Is.GreaterThan(0f), "never between the player and the puzzle");
            }
        }

        [Test]
        public void RayHitsPanel_OnlyWhenTheHandPointsAtIt()
        {
            // A 20 x 16 cm panel facing -z, half a metre in front of the hand and 30 cm to the left.
            var bl = new Vector3(-0.4f, 0f, 0.5f);
            var tl = bl + Vector3.up * 0.16f;
            var br = bl + Vector3.right * 0.2f;
            var centre = bl + new Vector3(0.1f, 0.08f, 0f);
            var hand = new Vector3(0f, 0.1f, 0f);
            bool Hits(Vector3 target, float margin = 0.025f, float max = 1.5f) =>
                PanelPlacement.RayHitsPanel(hand, target - hand, bl, tl, br, margin, max);

            Assert.That(Hits(centre), "pointing at the middle of the panel");
            Assert.That(Hits(br + Vector3.right * 0.02f), "just outside an edge is inside the margin");
            Assert.That(Hits(br + Vector3.right * 0.06f), Is.False, "clearly beside the panel");
            Assert.That(Hits(new Vector3(0f, 0.28f, 0.42f)), Is.False, "pointing at the puzzle, straight ahead");
            Assert.That(PanelPlacement.RayHitsPanel(hand, hand - centre, bl, tl, br, 0.025f, 1.5f), Is.False, "pointing away");
            Assert.That(Hits(centre, max: 0.3f), Is.False, "further than the reach that counts");
            Assert.That(PanelPlacement.RayHitsPanel(hand, Vector3.right, bl, tl, br, 0.025f, 1.5f), Is.False, "along the panel");
            Assert.That(PanelPlacement.RayHitsPanel(hand, Vector3.zero, bl, tl, br, 0.025f, 1.5f), Is.False, "no direction");
        }

        static float PanelEdge(float r, float headDistance, out float swing)
        {
            float pivotH = SurfacePlacementSolver.PivotHeightFor(r);
            const float hw = 0.098f;
            var head = new Vector3(0f, pivotH + SurfacePlacementSolver.EyeToPuzzleDrop, -headDistance);
            float rho = r * 1.2f + 0.05f + hw;
            float bottom = Mathf.Max(0.1f, pivotH - 0.08f);
            var anchor = PanelPlacement.Solve(head, pivotH, rho, hw, bottom, 0.06f, 0.04f, PanelPlacement.DefaultEdgeDegrees, out swing);
            Assert.That(new Vector2(anchor.x, anchor.z).magnitude, Is.GreaterThanOrEqualTo(r * 1.2f + 0.05f + hw - 0.04f - 1e-4f),
                "the panel never moves into the grab volume");
            return PanelPlacement.EdgeAngle(head, new Vector3(0f, pivotH, 0f), anchor + new Vector3(0f, 0.06f, 0f), hw);
        }

        [TestCase(0.11f)]
        [TestCase(0.18f)]
        public void Panel_OuterEdge_StaysInsideTheComfortableFoV(float r)
        {
            float edge = PanelEdge(r, SurfacePlacementSolver.SeatedReach, out float swing);
            TestContext.WriteLine($"R={r}: swing {swing} deg, edge {edge:F1} deg");
            Assert.That(edge, Is.LessThanOrEqualTo(PanelPlacement.DefaultEdgeDegrees + 1e-3f));
            Assert.That(swing, Is.GreaterThan(0f), "the old left-of-puzzle spot was outside the comfortable FoV");
        }

        [Test]
        public void Panel_StaysInItsDefaultSpot_WhenAlreadyComfortable()
        {
            PanelEdge(0.11f, 2.5f, out float swing);
            Assert.That(swing, Is.Zero);
        }

        static IEnumerable<PuzzleStage> Stages() => DefaultStages.CreateAll();

        [TestCaseSource(nameof(Stages))]
        public void Assist_StillRejectsRandomOrientations(PuzzleStage stage)
        {
            stage.tolerance *= GameSettings.AssistLeniency;
            var puzzle = new StagePuzzle(stage, 0.15f);
            var rng = new System.Random(7);
            int accepted = 0;
            const int n = 400;
            for (int i = 0; i < n; i++)
            {
                double u1 = rng.NextDouble(), u2 = rng.NextDouble() * 2 * Math.PI, u3 = rng.NextDouble() * 2 * Math.PI;
                double a = Math.Sqrt(1 - u1), b = Math.Sqrt(u1);
                var q = new Quaternion((float)(a * Math.Sin(u2)), (float)(a * Math.Cos(u2)), (float)(b * Math.Sin(u3)), (float)(b * Math.Cos(u3)));
                if (puzzle.IsMatch(puzzle.EvaluatePlaneLocal(q))) accepted++;
            }
            float fraction = accepted / (float)n;
            TestContext.WriteLine($"{stage.stageId} assist: {accepted}/{n} ({fraction:P1})");
            Assert.That(fraction, Is.LessThan(stage.isTutorial ? 0.15f : 0.06f));
        }
    }
}
