using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ShadowBloom.Tests
{
    public class ProgressionTests
    {
        static GameFlow NewFlow(int count = 5) => new GameFlow(count, i => i == 0);

        [Test]
        public void FullRun_VisitsEveryStage_ThenCompletes()
        {
            var flow = NewFlow();
            var started = new List<int>();
            flow.StageStarted += started.Add;

            Assert.That(flow.State, Is.EqualTo(GameState.Setup));
            Assert.That(flow.ConfirmSurface());
            Assert.That(flow.State, Is.EqualTo(GameState.Tutorial));

            for (int i = 0; i < 5; i++)
            {
                Assert.That(flow.MarkSolved(), $"solve {i}");
                Assert.That(flow.Next());
                if (i < 4)
                {
                    Assert.That(flow.State, Is.EqualTo(GameState.Transition));
                    Assert.That(flow.FinishTransition());
                    Assert.That(flow.State, Is.EqualTo(GameState.Playing));
                }
            }
            Assert.That(flow.State, Is.EqualTo(GameState.Completed));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, started);
        }

        [Test]
        public void InvalidCommands_AreRejected_WithoutSideEffects()
        {
            var flow = NewFlow();
            Assert.That(flow.MarkSolved(), Is.False);
            Assert.That(flow.Next(), Is.False);
            Assert.That(flow.Retry(), Is.False);
            Assert.That(flow.Pause(), Is.False);
            Assert.That(flow.FinishTransition(), Is.False);
            Assert.That(flow.State, Is.EqualTo(GameState.Setup));

            flow.ConfirmSurface();
            Assert.That(flow.ConfirmSurface(), Is.False);
            flow.MarkSolved();
            Assert.That(flow.MarkSolved(), Is.False, "double solve");
            flow.Next();
            Assert.That(flow.Next(), Is.False, "double next");
            Assert.That(flow.Retry(), Is.False, "retry during transition");
            Assert.That(flow.StageIndex, Is.EqualTo(0));
        }

        [Test]
        public void PauseResume_RestoresPreviousState()
        {
            var flow = NewFlow();
            flow.ConfirmSurface();
            flow.DismissTutorial();
            Assert.That(flow.Pause());
            Assert.That(flow.State, Is.EqualTo(GameState.Paused));
            Assert.That(flow.IsInteractive, Is.False);
            Assert.That(flow.MarkSolved(), Is.False, "cannot solve while paused");
            Assert.That(flow.Resume());
            Assert.That(flow.State, Is.EqualTo(GameState.Playing));

            flow.MarkSolved();
            flow.Pause();
            flow.Resume();
            Assert.That(flow.State, Is.EqualTo(GameState.Solved));
        }

        [Test]
        public void Retry_RestartsSameStage_FromSolvedOrPlaying()
        {
            var flow = NewFlow();
            int starts = 0;
            flow.StageStarted += _ => starts++;
            flow.ConfirmSurface();
            flow.MarkSolved();
            Assert.That(flow.Retry());
            Assert.That(flow.State, Is.EqualTo(GameState.Tutorial));
            Assert.That(flow.StageIndex, Is.EqualTo(0));
            Assert.That(starts, Is.EqualTo(2));
        }

        [Test]
        public void RepeatedTransitions_AndRestartAfterCompletion_Work()
        {
            var flow = new GameFlow(2);
            for (int run = 0; run < 3; run++)
            {
                if (run == 0) flow.ConfirmSurface(); else Assert.That(flow.StartAt(0));
                flow.MarkSolved(); flow.Next(); flow.FinishTransition();
                flow.MarkSolved(); flow.Next();
                Assert.That(flow.State, Is.EqualTo(GameState.Completed), $"run {run}");
            }
        }

        [Test]
        public void RequestSetup_ThenConfirm_KeepsStage()
        {
            var flow = NewFlow();
            flow.ConfirmSurface(); flow.MarkSolved(); flow.Next(); flow.FinishTransition();
            Assert.That(flow.RequestSetup());
            Assert.That(flow.State, Is.EqualTo(GameState.Setup));
            flow.ConfirmSurface();
            Assert.That(flow.StageIndex, Is.EqualTo(1));
            Assert.That(flow.State, Is.EqualTo(GameState.Playing));
        }

        [Test]
        public void SolveTracker_RequiresSustainedMatch()
        {
            var t = new SolveTracker(0.05f, 0.8f);
            for (int i = 0; i < 7; i++) Assert.That(t.Tick(0.01f, 0.1f), Is.False);
            Assert.That(t.Tick(0.01f, 0.1f), Is.True, "solves after 0.8s");
            Assert.That(t.Tick(0.01f, 0.1f), Is.False, "fires once");
        }

        [Test]
        public void SolveTracker_BriefPassThrough_DoesNotSolve_AndHysteresisHolds()
        {
            var t = new SolveTracker(0.05f, 0.8f);
            t.Tick(0.04f, 0.3f);
            t.Tick(0.2f, 0.3f);     // slipped far away: hold decays
            Assert.That(t.HoldProgress01, Is.EqualTo(0f));
            Assert.That(t.Solved, Is.False);

            t.Reset();
            t.Tick(0.049f, 0.4f);
            Assert.That(t.IsWithin);
            t.Tick(0.055f, 0.2f);   // within the 20% exit band: still counts
            Assert.That(t.IsWithin);
            Assert.That(t.Tick(0.055f, 0.2f), Is.True);
        }

        [Test]
        public void ProgressSave_RoundTrips_AndFindsFirstIncomplete()
        {
            var store = new MemoryProgressStore();
            var save = new ProgressSaveSystem(store);
            var ids = DefaultStages.CreateAll().Select(s => s.stageId).ToList();
            Assert.That(save.FirstIncomplete(ids), Is.EqualTo(0));
            save.MarkCompleted(ids[0], 0);
            save.MarkCompleted(ids[1], 1);

            var reloaded = new ProgressSaveSystem(store);
            Assert.That(reloaded.IsCompleted(ids[1]));
            Assert.That(reloaded.FirstIncomplete(ids), Is.EqualTo(2));
            Assert.That(reloaded.Data.lastStageIndex, Is.EqualTo(1));
        }

        [Test]
        public void ProgressSave_CorruptData_StartsFresh()
        {
            var store = new MemoryProgressStore { Json = "{not json" };
            var save = new ProgressSaveSystem(store);
            Assert.That(save.Data.completedStageIds, Is.Empty);
        }

        [Test]
        public void DefaultStages_HaveUniqueIds_AndValidParameters()
        {
            var stages = DefaultStages.CreateAll();
            Assert.That(stages.Length, Is.EqualTo(8));
            Assert.That(stages.Select(s => s.stageId).Distinct().Count(), Is.EqualTo(8));
            Assert.That(stages.Count(s => s.isTutorial), Is.EqualTo(1));
            Assert.That(stages[0].isTutorial, "the tutorial comes first");
            Assert.That(stages.Count(s => s.completion == CompletionBehavior.FinalBloom), Is.EqualTo(1));
            Assert.That(stages[^1].completion, Is.EqualTo(CompletionBehavior.FinalBloom));
            foreach (var s in stages)
            {
                Assert.That(s.tolerance, Is.InRange(0.01f, 0.3f));
                Assert.That(CurveGenerator.Generate(s.curve).PointCount, Is.GreaterThan(0));
            }
        }
    }
}
