using UnityEditor;

namespace ShadowBloom.EditorTools
{
    public enum UiPreviewState { Setup, Playing, Paused, Solved, Completed, HowToPlay }

    /// <summary>
    /// Representative panel contents for the Edit-mode UI preview, copied from GameStateController's view builder
    /// (stage 1 of 8). Preview only; the game never reads these.
    /// </summary>
    public static class UiPreviewViews
    {
        const string StagePath = ShadowBloomSceneBuilder.Root + "/Stages/01_circle.asset";
        const int Count = 5;

        public static UiView For(UiPreviewState state)
        {
            var stage = AssetDatabase.LoadAssetAtPath<PuzzleStage>(StagePath);
            string title = stage != null ? stage.displayName : "Stage";
            string hint = stage == null ? "" : stage.isTutorial ? UiCopy.TutorialText(0) : stage.hint;
            UiView v = state switch
            {
                UiPreviewState.Setup => new UiView
                {
                    Eyebrow = UiCopy.SetupFound, EyebrowTone = UiTone.Success, EyebrowDot = true,
                    Title = UiCopy.GameTitle, Subtitle = UiCopy.Tagline, Hint = UiCopy.SetupBegin,
                    Primary = UiCopy.Start, Reposition = true, Progress = -1
                },
                UiPreviewState.Playing => new UiView
                {
                    Eyebrow = UiCopy.StageEyebrow(0, Count), Progress = 0, ProgressCount = Count,
                    Title = title, Hint = hint, Alignment = true, Retry = true, Pause = true, Reposition = true
                },
                UiPreviewState.Paused => new UiView
                {
                    Eyebrow = UiCopy.Paused, EyebrowTone = UiTone.Accent, Title = title,
                    Progress = 0, ProgressCount = Count, Primary = UiCopy.Resume,
                    Retry = true, Reposition = true, Settings = true, Restart = true, HowToPlay = true
                },
                UiPreviewState.HowToPlay => new UiView
                {
                    Eyebrow = UiCopy.HowToPlayEyebrow, EyebrowTone = UiTone.Accent,
                    Progress = 0, ProgressCount = UiCopy.HowToPlayCount,
                    Title = UiCopy.HowToPlayTitle(0), Subtitle = UiCopy.HowToPlayBody(0),
                    Primary = UiCopy.HowToPlayNext, Pause = true
                },
                UiPreviewState.Solved => new UiView
                {
                    Eyebrow = UiCopy.StageEyebrow(0, Count), Progress = 0, ProgressCount = Count, ProgressCurrentDone = true,
                    Title = title, Subtitle = UiCopy.Aligned, SubtitleTone = UiTone.Success, Accent = UiTone.Success,
                    Primary = UiCopy.NextStage, Retry = true
                },
                _ => new UiView
                {
                    Eyebrow = UiCopy.CompleteEyebrow(Count), EyebrowTone = UiTone.Success,
                    Title = UiCopy.Complete, TitleTone = UiTone.Success, Accent = UiTone.Success,
                    Progress = Count, ProgressCount = Count, ProgressComplete = true,
                    Primary = UiCopy.PlayAgain, Daily = true, DailyLabel = UiCopy.DailyShadow
                }
            };
            v.PauseLabel = UiCopy.Pause;
            return v;
        }
    }
}
