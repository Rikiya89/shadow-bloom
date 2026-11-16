using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ShadowBloom.Tests
{
    /// <summary>Which flowers rest in the table garden: campaign stages first, then Daily Shadow days.</summary>
    public class GardenPlanTests
    {
        const int Capacity = DailyShadow.GardenCapacity;
        static readonly PuzzleStage[] Stages = DefaultStages.CreateAll();

        static List<GardenFlower> Compose(int solvedStages, int[] days = null, int highlightDay = 0)
        {
            var done = new HashSet<string>(Stages.Take(solvedStages).Select(s => s.stageId));
            return GardenPlan.Compose(Stages, done.Contains, days ?? Array.Empty<int>(), highlightDay, Capacity);
        }

        [Test]
        public void NothingSolved_GardenIsEmpty()
        {
            Assert.That(Compose(0), Is.Empty);
        }

        [Test]
        public void EachSolvedStage_PlantsItsOwnBloom_NewestHighlighted()
        {
            var flowers = Compose(3);
            Assert.That(flowers.Count, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(flowers[i].Petals, Is.EqualTo(Stages[i].bloomPetals));
                Assert.That(flowers[i].Layers, Is.EqualTo(Stages[i].bloomLayers));
                Assert.That(flowers[i].Highlight, Is.EqualTo(i == 2));
            }
        }

        [Test]
        public void TheFinale_IsTheSculpture_NotAGardenFlower()
        {
            Assert.That(Stages.Count(s => s.completion != CompletionBehavior.FinalBloom), Is.EqualTo(Capacity),
                "the campaign fills the ring exactly; revisit GardenPlan if the stage count changes");
            Assert.That(Compose(Stages.Length).Count, Is.EqualTo(Capacity));
            Assert.That(Compose(Stages.Length).Count(f => f.Highlight), Is.EqualTo(1));
        }

        [Test]
        public void Dailies_ComeAfterTheCampaign_NewestWinTheSlots_OnlyTodayHighlighted()
        {
            var flowers = Compose(Stages.Length, new[] { 310, 311 }, highlightDay: 311);
            Assert.That(flowers.Count, Is.EqualTo(Capacity));
            DailyShadow.BloomFor(311, out int petals, out int layers);
            Assert.That(flowers[Capacity - 1].Petals, Is.EqualTo(petals));
            Assert.That(flowers[Capacity - 1].Layers, Is.EqualTo(layers));
            Assert.That(flowers[Capacity - 1].Highlight);
            Assert.That(flowers.Count(f => f.Highlight), Is.EqualTo(1), "a campaign flower no longer carries the heart");
            Assert.That(flowers[0].Petals, Is.EqualTo(Stages[2].bloomPetals), "the two oldest campaign flowers gave way");

            Assert.That(Compose(Stages.Length, new[] { 310, 311 }, highlightDay: 312).Any(f => f.Highlight), Is.False,
                "a prior day's flower is never labelled today");
        }

        [Test]
        public void EachFlower_KeepsItsStagesPetalFamily_AndDailiesMatchTheirPuzzle()
        {
            var flowers = Compose(Stages.Length);
            for (int i = 0; i < flowers.Count; i++)
                Assert.That(flowers[i].Shape, Is.EqualTo(PetalShapes.For(Stages[i].curve.kind)));
            Assert.That(flowers.Select(f => f.Shape).Distinct().Count(), Is.GreaterThanOrEqualTo(4), "the garden is not seven copies of one flower");

            for (int day = 300; day < 328; day++)
            {
                var puzzle = DailyShadow.Create(day);
                Assert.That(DailyShadow.KindFor(day), Is.EqualTo(puzzle.curve.kind), $"day {day}");
                UnityEngine.Object.DestroyImmediate(puzzle);
            }
        }

        [Test]
        public void Stems_HaveThreeHeights_OffCentre_AndTheNewestStandsTallest()
        {
            var heights = Enumerable.Range(0, Capacity).Select(s => GardenLayout.StemHeight(s, GardenLayout.MinBand, false)).ToArray();
            Assert.That(heights.Distinct().Count(), Is.EqualTo(3), "tall, middle and short");
            Assert.That(heights.Max(), Is.EqualTo(GardenLayout.MaxStem).Within(1e-6f));
            Assert.That(heights.Min(), Is.GreaterThan(0f));
            Assert.That(Array.IndexOf(heights, heights.Max()), Is.Not.EqualTo(3), "the tallest is not dead centre");
            Assert.That(heights.SequenceEqual(heights.Reverse()), Is.False, "the skyline is not mirror-symmetric");
            for (int s = 0; s < Capacity; s++)
            {
                Assert.That(GardenLayout.StemHeight(s, GardenLayout.MinBand, true), Is.GreaterThan(heights[s]));
                Assert.That(GardenLayout.StemHeight(s, GardenLayout.MaxBand, false), Is.EqualTo(2f * heights[s]).Within(1e-6f));
            }
        }

        [Test]
        public void AFullWeekOfDailies_ReplacesTheCampaignFlowers()
        {
            int[] days = Enumerable.Range(300, Capacity).ToArray();
            var flowers = Compose(Stages.Length, days, highlightDay: days[Capacity - 1]);
            for (int i = 0; i < Capacity; i++)
            {
                DailyShadow.BloomFor(days[i], out int petals, out int layers);
                Assert.That((flowers[i].Petals, flowers[i].Layers), Is.EqualTo((petals, layers)));
            }
        }
    }
}
