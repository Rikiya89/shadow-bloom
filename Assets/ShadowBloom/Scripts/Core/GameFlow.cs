using System;

namespace ShadowBloom
{
    public enum GameState
    {
        Setup,
        Tutorial,
        Playing,
        Solved,
        Transition,
        Completed,
        Paused
    }

    /// <summary>
    /// Pure progression state machine. Every command returns false (and changes nothing) when it is not valid
    /// in the current state, so UI spam, double pinches or late callbacks cannot corrupt the flow.
    /// </summary>
    public sealed class GameFlow
    {
        public GameState State { get; private set; } = GameState.Setup;
        public GameState ResumeState { get; private set; } = GameState.Setup;
        public int StageIndex { get; private set; }
        public int StageCount { get; }
        public bool IsLastStage => StageIndex >= StageCount - 1;

        /// <summary>(previous, next)</summary>
        public event Action<GameState, GameState> StateChanged;
        /// <summary>Raised whenever a stage (re)starts: first entry, retry, next, restart.</summary>
        public event Action<int> StageStarted;

        readonly Func<int, bool> _isTutorialStage;

        public GameFlow(int stageCount, Func<int, bool> isTutorialStage = null, int startStage = 0)
        {
            if (stageCount <= 0) throw new ArgumentOutOfRangeException(nameof(stageCount));
            StageCount = stageCount;
            StageIndex = Math.Clamp(startStage, 0, stageCount - 1);
            _isTutorialStage = isTutorialStage ?? (_ => false);
        }

        public bool IsInteractive => State == GameState.Tutorial || State == GameState.Playing;

        public bool ConfirmSurface()
        {
            if (State != GameState.Setup) return false;
            StartStage(StageIndex);
            return true;
        }

        public bool RequestSetup()
        {
            if (State == GameState.Setup || State == GameState.Transition) return false;
            SetState(GameState.Setup);
            return true;
        }

        /// <summary>Tutorial hint was understood; keep the same puzzle running without the overlay.</summary>
        public bool DismissTutorial()
        {
            if (State != GameState.Tutorial) return false;
            SetState(GameState.Playing);
            return true;
        }

        public bool MarkSolved()
        {
            if (!IsInteractive) return false;
            SetState(GameState.Solved);
            return true;
        }

        /// <summary>From Solved: go to the next stage, or to Completed after the last one.</summary>
        public bool Next()
        {
            if (State != GameState.Solved) return false;
            SetState(IsLastStage ? GameState.Completed : GameState.Transition);
            return true;
        }

        public bool FinishTransition()
        {
            if (State != GameState.Transition) return false;
            StartStage(StageIndex + 1);
            return true;
        }

        public bool Retry()
        {
            if (!IsInteractive && State != GameState.Solved &&
                !(State == GameState.Paused && (ResumeState == GameState.Tutorial ||
                  ResumeState == GameState.Playing || ResumeState == GameState.Solved))) return false;
            StartStage(StageIndex);
            return true;
        }

        public bool Pause()
        {
            if (State == GameState.Paused || State == GameState.Setup) return false;
            ResumeState = State;
            SetState(GameState.Paused);
            return true;
        }

        public bool Resume()
        {
            if (State != GameState.Paused) return false;
            SetState(ResumeState);
            return true;
        }

        /// <summary>Jump to any stage (stage select, restart after completion).</summary>
        public bool StartAt(int index)
        {
            if (index < 0 || index >= StageCount) return false;
            if (State == GameState.Setup || State == GameState.Transition) return false;
            StartStage(index);
            return true;
        }

        /// <summary>Restart the run without erasing earned progress or preferences.</summary>
        public bool RestartExperience() => StartAt(0);

        void StartStage(int index)
        {
            StageIndex = index;
            SetState(_isTutorialStage(index) ? GameState.Tutorial : GameState.Playing);
            StageStarted?.Invoke(index);
        }

        void SetState(GameState next)
        {
            var prev = State;
            State = next;
            if (prev != next) StateChanged?.Invoke(prev, next);
        }
    }
}
