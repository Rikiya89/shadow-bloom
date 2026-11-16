using System.Linq;
using NUnit.Framework;

namespace ShadowBloom.Tests
{
    /// <summary>Repeat-completion guards and save -> load -> resume, at the pure-logic level.</summary>
    public class CompletionAndSaveTests
    {
        static string[] Ids => DefaultStages.CreateAll().Select(s => s.stageId).ToArray();

        [Test]
        public void MarkSolved_Twice_IsRejected_AndRaisesOneTransition()
        {
            var flow = new GameFlow(5);
            flow.ConfirmSurface();
            int solvedEvents = 0;
            flow.StateChanged += (_, next) => { if (next == GameState.Solved) solvedEvents++; };

            Assert.That(flow.MarkSolved(), Is.True);
            Assert.That(flow.MarkSolved(), Is.False, "late or duplicate solve callback");
            Assert.That(solvedEvents, Is.EqualTo(1));
            Assert.That(flow.StageIndex, Is.EqualTo(0));
        }

        [Test]
        public void Solve_IsRejected_OutsideInteractiveStates()
        {
            var flow = new GameFlow(2);
            Assert.That(flow.MarkSolved(), Is.False, "Setup");
            flow.ConfirmSurface();
            flow.Pause();
            Assert.That(flow.MarkSolved(), Is.False, "Paused");
            flow.Resume();
            flow.MarkSolved();
            flow.Next();
            Assert.That(flow.State, Is.EqualTo(GameState.Transition));
            Assert.That(flow.MarkSolved(), Is.False, "Transition");
            flow.FinishTransition();
            flow.MarkSolved();
            flow.Next();
            Assert.That(flow.State, Is.EqualTo(GameState.Completed));
            Assert.That(flow.MarkSolved(), Is.False, "Completed");
        }

        [Test]
        public void Next_Twice_AdvancesOnlyOneStage()
        {
            var flow = new GameFlow(5);
            flow.ConfirmSurface();
            flow.MarkSolved();
            Assert.That(flow.Next(), Is.True);
            Assert.That(flow.Next(), Is.False, "double pinch on Next");
            flow.FinishTransition();
            Assert.That(flow.FinishTransition(), Is.False);
            Assert.That(flow.StageIndex, Is.EqualTo(1));
        }

        [Test]
        public void SolveTracker_StaysLatched_UntilReset_ThenSolvesAgain()
        {
            var t = new SolveTracker(0.05f, 0.5f);
            int fired = 0;
            for (int i = 0; i < 40; i++) if (t.Tick(0f, 0.1f)) fired++;
            Assert.That(fired, Is.EqualTo(1), "holding the solution after the solve does not re-fire");

            t.Reset();   // Retry
            Assert.That(t.Solved, Is.False);
            fired = 0;
            for (int i = 0; i < 10; i++) if (t.Tick(0f, 0.1f)) fired++;
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void MarkCompleted_IsIdempotent_AndLastStageNeverGoesBackwards()
        {
            var store = new MemoryProgressStore();
            var save = new ProgressSaveSystem(store);
            save.MarkCompleted(Ids[2], 2);
            save.MarkCompleted(Ids[2], 2);
            save.MarkCompleted(Ids[0], 0);   // replaying stage 1 later

            var reloaded = new ProgressSaveSystem(store);
            Assert.That(reloaded.Data.completedStageIds.Count(id => id == Ids[2]), Is.EqualTo(1));
            Assert.That(reloaded.Data.completedStageIds.Count, Is.EqualTo(2));
            Assert.That(reloaded.Data.lastStageIndex, Is.EqualTo(2));
        }

        [Test]
        public void SaveLoad_ResumesFlow_AtFirstIncompleteStage()
        {
            var store = new MemoryProgressStore();
            var save = new ProgressSaveSystem(store);
            save.MarkCompleted(Ids[0], 0);
            save.MarkCompleted(Ids[1], 1);
            save.MarkCompleted(Ids[3], 3);   // completed out of order (e.g. an older build's stage list)

            var reloaded = new ProgressSaveSystem(store);
            var flow = new GameFlow(Ids.Length, i => i == 0, reloaded.FirstIncomplete(Ids));
            Assert.That(flow.StageIndex, Is.EqualTo(2), "first gap, not last played");
            flow.ConfirmSurface();
            Assert.That(flow.State, Is.EqualTo(GameState.Playing), "resumed stage is not the tutorial");
        }

        [Test]
        public void SaveLoad_AllComplete_RestartsAtFirstStage()
        {
            var store = new MemoryProgressStore();
            var save = new ProgressSaveSystem(store);
            for (int i = 0; i < Ids.Length; i++) save.MarkCompleted(Ids[i], i);
            Assert.That(new ProgressSaveSystem(store).FirstIncomplete(Ids), Is.EqualTo(0));
        }

        [Test]
        public void SaveLoad_UnknownIds_AreIgnored_AndReorderingKeepsProgress()
        {
            var store = new MemoryProgressStore
            {
                Json = "{\"version\":1,\"completedStageIds\":[\"removed_stage\",\"" + Ids[1] + "\"],\"lastStageIndex\":9}"
            };
            var save = new ProgressSaveSystem(store);
            Assert.That(save.FirstIncomplete(Ids), Is.EqualTo(0));
            var reordered = new[] { Ids[1], Ids[0], Ids[2], Ids[3], Ids[4] };
            Assert.That(save.FirstIncomplete(reordered), Is.EqualTo(1), "progress follows ids, not positions");
            var flow = new GameFlow(Ids.Length, startStage: save.Data.lastStageIndex);
            Assert.That(flow.StageIndex, Is.EqualTo(Ids.Length - 1), "out-of-range index is clamped");
        }

        [Test]
        public void SaveLoad_NullFieldsInJson_StartFresh()
        {
            var save = new ProgressSaveSystem(new MemoryProgressStore { Json = "{\"version\":1}" });
            Assert.That(save.Data.completedStageIds, Is.Not.Null.And.Empty);
            Assert.That(save.FirstIncomplete(Ids), Is.EqualTo(0));
        }

        [Test]
        public void ResetAll_ClearsProgress_InTheStore()
        {
            var store = new MemoryProgressStore();
            var save = new ProgressSaveSystem(store);
            save.MarkCompleted(Ids[0], 0);
            save.ResetAll();
            Assert.That(new ProgressSaveSystem(store).IsCompleted(Ids[0]), Is.False);
        }
    }
}
