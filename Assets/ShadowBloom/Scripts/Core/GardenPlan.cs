using System;
using System.Collections.Generic;

namespace ShadowBloom
{
    /// <summary>One resting flower in the table garden: the bloom shape it repeats, its yaw, and whether it is the highlighted one.</summary>
    public readonly struct GardenFlower
    {
        public readonly int Petals;
        public readonly int Layers;
        public readonly float Yaw;
        public readonly bool Highlight;
        public readonly PetalShape Shape;

        public GardenFlower(int petals, int layers, float yaw, bool highlight, PetalShape shape = PetalShape.Round)
        {
            Shape = shape;
            Petals = petals;
            Layers = layers;
            Yaw = yaw;
            Highlight = highlight;
        }
    }

    /// <summary>
    /// Which flowers rest in the table garden. The garden grows during the campaign: every solved stage leaves a
    /// small copy of its bloom, so a first session already fills the table. The finale is not planted; it is the
    /// large sculpture in the middle of the ring. Daily Shadow flowers are planted after the campaign ones, and
    /// the newest flowers win the slots. Derived from saved progress, so it needs no save data of its own.
    /// </summary>
    public static class GardenPlan
    {
        public static GardenFlower Daily(int day, bool highlight)
        {
            DailyShadow.BloomFor(day, out int petals, out int layers);
            return new GardenFlower(petals, layers, GardenLayout.YawFor(day), highlight, PetalShapes.For(DailyShadow.KindFor(day)));
        }

        /// <summary>A campaign stage's flower repeats the bloom the player saw when solving it.</summary>
        public static GardenFlower Stage(PuzzleStage stage, int index, bool highlight) =>
            new GardenFlower(stage.bloomPetals, stage.bloomLayers, GardenLayout.YawFor(index + 1), highlight,
                PetalShapes.For(stage.curve.kind));

        /// <summary>Oldest first, at most <paramref name="capacity"/> flowers.</summary>
        /// <param name="highlightDay">The Daily whose flower is highlighted (today's, or the one just solved).</param>
        public static List<GardenFlower> Compose(IReadOnlyList<PuzzleStage> campaign, Func<string, bool> isCompleted,
            IReadOnlyList<int> gardenDays, int highlightDay, int capacity)
        {
            var flowers = new List<GardenFlower>();
            int dailies = gardenDays?.Count ?? 0;

            int lastStage = -1;
            if (campaign != null)
                for (int i = 0; i < campaign.Count; i++)
                    if (Plants(campaign[i]) && isCompleted(campaign[i].stageId)) lastStage = i;

            // TODO(you): the garden's policy. Two decisions live here:
            //   1. Which flowers keep a slot when there are more than `capacity` (now: campaign first, then
            //      dailies, newest win - so a week of dailies slowly replaces the campaign flowers).
            //   2. Which flower gets the vermilion heart (now: the newest campaign flower until the first
            //      daily is planted; after that only the daily matching `highlightDay`).
            for (int i = 0; campaign != null && i <= lastStage; i++)
                if (Plants(campaign[i]) && isCompleted(campaign[i].stageId))
                    flowers.Add(Stage(campaign[i], i, highlight: dailies == 0 && i == lastStage));
            for (int i = 0; i < dailies; i++)
                flowers.Add(Daily(gardenDays[i], gardenDays[i] == highlightDay));

            if (flowers.Count > capacity) flowers.RemoveRange(0, flowers.Count - capacity);
            return flowers;
        }

        static bool Plants(PuzzleStage stage) => stage != null && stage.completion != CompletionBehavior.FinalBloom;
    }
}
