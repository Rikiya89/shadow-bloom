using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ShadowBloom.Tests
{
    public class Phase2Tests
    {
        static IEnumerable<PuzzleStage> AuthoredStages() => AssetDatabase.FindAssets("t:PuzzleStage", new[] { "Assets/ShadowBloom/Stages" })
            .Select(g => AssetDatabase.LoadAssetAtPath<PuzzleStage>(AssetDatabase.GUIDToAssetPath(g))).OrderBy(s => s.stageId);

        [Test]
        public void AuthoredCatalogue_HasEightDeterministicDistinctSolvableStages()
        {
            var stages = AuthoredStages().ToArray();
            Assert.That(stages.Length, Is.EqualTo(8));
            Assert.That(stages.Select(s => s.stageId).Distinct().Count(), Is.EqualTo(8));
            var puzzles = stages.Select(s => new StagePuzzle(s, 0.12f)).ToArray();
            for (int i = 0; i < stages.Length; i++)
            {
                var stage = stages[i];
                var a = CurveGenerator.Generate(stage.curve);
                var b = CurveGenerator.Generate(stage.curve);
                CollectionAssert.AreEqual(a.Points, b.Points, stage.stageId);
                Assert.That(puzzles[i].EvaluatePlaneLocal(stage.SolutionRotation), Is.LessThan(0.001f), stage.stageId);
                Assert.That(puzzles[i].EvaluatePlaneLocal(stage.InitialRotation), Is.GreaterThan(stage.tolerance * 2f), stage.stageId);
                var surface = new PlaneBasis(new Vector3(2f, 0.8f, -3f), Quaternion.Euler(14f, 117f, 8f));
                var pose = Matrix4x4.TRS(surface.Origin + surface.Normal * 0.2f,
                    Quaternion.Euler(14f, 117f, 8f) * stage.SolutionRotation, Vector3.one * 0.12f);
                Assert.That(puzzles[i].Evaluate(pose, surface), Is.LessThan(0.001f));
                for (int j = 0; j < i; j++)
                    Assert.That(ProjectionMatcher.ComputeError(puzzles[i].Target, puzzles[j].Target, puzzles[j].Target.RmsRadius()),
                        Is.GreaterThan(Mathf.Max(stage.tolerance, stages[j].tolerance)), $"Targets {i} and {j}");
            }
        }

        [TestCase(0, 77f)]
        [TestCase(1, 72f)]
        [TestCase(2, 120f)]
        [TestCase(4, 72f)]
        public void AuthoredStages_AcceptEquivalentSymmetry(int index, float yaw)
        {
            var stage = AuthoredStages().ElementAt(index);
            var puzzle = new StagePuzzle(stage, 0.12f);
            Assert.That(puzzle.EvaluatePlaneLocal(Quaternion.AngleAxis(yaw, Vector3.up) * stage.SolutionRotation), Is.LessThan(stage.tolerance));
        }

        [Test]
        public void AuthoredStages_WrongPosesCannotPassThroughFastMatcherClamping()
        {
            foreach (var stage in AuthoredStages())
            {
                var puzzle = new StagePuzzle(stage);
                for (int tilt = 30; tilt <= 80; tilt += 10)
                {
                    var error = puzzle.EvaluatePlaneLocal(Quaternion.AngleAxis(tilt, Vector3.right) * stage.SolutionRotation);
                    Assert.That(error, Is.GreaterThan(stage.tolerance), $"{stage.stageId}: {tilt}");
                    Assert.That(puzzle.IsMatch(error), Is.EqualTo(puzzle.IsMatch(puzzle.ReferenceError())));
                }
            }
        }

        [Test]
        public void PauseDuringTransition_ResumeAdvancesExactlyOnce()
        {
            var flow = new GameFlow(5);
            flow.ConfirmSurface(); flow.MarkSolved(); flow.Next();
            Assert.That(flow.Pause());
            Assert.That(flow.Pause(), Is.False);
            Assert.That(flow.FinishTransition(), Is.False);
            Assert.That(flow.MarkSolved(), Is.False);
            flow.Resume();
            Assert.That(flow.State, Is.EqualTo(GameState.Transition));
            Assert.That(flow.FinishTransition());
            Assert.That(flow.FinishTransition(), Is.False);
            Assert.That(flow.StageIndex, Is.EqualTo(1));
        }

        [Test]
        public void PausedRetryAndRestart_ResetCorrectStageWithoutDuplicateSolve()
        {
            var flow = new GameFlow(5, i => i == 0);
            flow.ConfirmSurface(); flow.MarkSolved(); flow.Next(); flow.FinishTransition();
            flow.Pause();
            Assert.That(flow.Retry());
            Assert.That(flow.StageIndex, Is.EqualTo(1));
            Assert.That(flow.State, Is.EqualTo(GameState.Playing));
            flow.Pause();
            Assert.That(flow.RestartExperience());
            Assert.That(flow.StageIndex, Is.Zero);
            Assert.That(flow.State, Is.EqualTo(GameState.Tutorial));
            Assert.That(flow.MarkSolved());
            Assert.That(flow.MarkSolved(), Is.False);
        }

        [Test]
        public void Save_MigratesVersionOneAndPersistsPreferencesAndHighestUnlock()
        {
            var ids = new[] { "a", "b", "c", "d", "e" };
            var store = new MemoryProgressStore { Json = "{\"version\":1,\"completedStageIds\":[\"a\",\"a\",null],\"lastStageIndex\":0}" };
            var save = new ProgressSaveSystem(store);
            Assert.That(save.HighestUnlockedStage(ids), Is.EqualTo(1));
            Assert.That(save.Data.soundMuted, Is.False);
            Assert.That(save.Data.completedStageIds.Count, Is.EqualTo(1));
            save.SetSoundMuted(true);
            save.MarkCompleted("b", 1);
            save.SetLastStage(0); // replay does not relock earned stages
            var loaded = new ProgressSaveSystem(store);
            Assert.That(loaded.HighestUnlockedStage(ids), Is.EqualTo(2));
            Assert.That(loaded.Data.soundMuted);
            Assert.That(loaded.FirstIncomplete(ids), Is.EqualTo(2));
            Assert.That(loaded.Data.version, Is.EqualTo(3));
            foreach (var id in ids) loaded.MarkCompleted(id, System.Array.IndexOf(ids, id));
            Assert.That(loaded.HighestUnlockedStage(ids), Is.EqualTo(4));
            Assert.That(loaded.FirstIncomplete(ids), Is.Zero);
        }

        [Test]
        public void Save_DuplicateCompletionsAndInvalidIds_DoNotCorruptProgress()
        {
            var store = new MemoryProgressStore();
            var save = new ProgressSaveSystem(store);
            save.MarkCompleted("a", 0); save.MarkCompleted("a", 0);
            save.MarkCompleted(null, 1); save.MarkCompleted("", 1); save.MarkCompleted("b", -1);
            Assert.That(new ProgressSaveSystem(store).Data.completedStageIds, Is.EqualTo(new[] { "a" }));
        }
    }
}
