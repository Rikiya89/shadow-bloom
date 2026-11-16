using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShadowBloom.Tests
{
    /// <summary>
    /// Drives the real generated scene through the desktop path (no headset): MRUK simulated room -> placement ->
    /// every campaign stage (solved with the debug snap) -> completion, twice, plus retry and pause edge cases.
    /// Also records Editor frame statistics (not device numbers) and, when SB_CAPTURE_DIR is set, saves renders.
    /// </summary>
    public class GameLoopPlayModeTests
    {
        const string ScenePath = "Assets/ShadowBloom/Scenes/ShadowBloom.unity";
        const string ProgressKey = "shadowbloom.progress.v1";

        string _savedProgress;
        bool _hadProgress;
        HowToPlaySeenScope _howToPlay;

        GameStateController _game;
        PuzzleController _puzzle;
        SurfacePlacementController _placement;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _hadProgress = PlayerPrefs.HasKey(ProgressKey);
            _savedProgress = PlayerPrefs.GetString(ProgressKey, "");
            PlayerPrefs.DeleteKey(ProgressKey);
            _howToPlay = new HowToPlaySeenScope();
            Application.targetFrameRate = 72;

#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("ShadowBloom");
#endif
            yield return null;
            _game = UnityEngine.Object.FindAnyObjectByType<GameStateController>();
            _puzzle = UnityEngine.Object.FindAnyObjectByType<PuzzleController>();
            _placement = UnityEngine.Object.FindAnyObjectByType<SurfacePlacementController>();
            Assert.NotNull(_game, "GameStateController");
            Assert.NotNull(_puzzle, "PuzzleController");
            Assert.NotNull(_placement, "SurfacePlacementController");

            yield return WaitUntil(() => _placement.Status != PlacementStatus.Searching, 12f, "placement");
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadProgress) PlayerPrefs.SetString(ProgressKey, _savedProgress);
            else PlayerPrefs.DeleteKey(ProgressKey);
            _howToPlay?.Restore();
            PlayerPrefs.Save();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FullRun_AllStagesTwice_WithStats()
        {
            Assert.AreEqual(GameState.Setup, _game.Flow.State);
            yield return Wait(0.8f);
            CaptureIfEnabled("panel_setup");
            _game.Primary();
            Assert.AreEqual(GameState.Tutorial, _game.Flow.State, "Start enters stage 1 tutorial");
            yield return Wait(0.8f);
            CaptureIfEnabled("panel_tutorial");

            var stats = new FrameStats();
            for (int run = 0; run < 2; run++)
            {
                for (int stage = 0; stage < _game.Flow.StageCount; stage++)
                {
                    Assert.AreEqual(stage, _game.Flow.StageIndex, $"run {run} stage index");
                    // Idle interactive frames (the common case on device) are measured separately.
                    yield return stats.Measure("interactive", 1f);
                    if (run == 0) CaptureIfEnabled($"stage{stage + 1}_play");
                    yield return SolveCurrent();

                    if (run == 0 && (stage == 0 || stage == _game.Flow.StageCount - 1))
                    {
                        string tag = stage == 0 ? "stage1" : "finale";
                        yield return CaptureSequence(tag, stats);
                    }
                    else
                    {
                        yield return stats.Measure("bloom", 1.5f);
                    }

                    if (_game.Flow.IsLastStage)
                    {
                        yield return WaitUntil(() => _game.Flow.State == GameState.Completed, 12f, "finale -> completed");
                        if (run == 0)
                        {
                            // System menu / headset off on the completion screen must not wipe the sculpture.
                            var bloom = UnityEngine.Object.FindAnyObjectByType<BloomEffectController>();
                            _game.TogglePause();
                            yield return Wait(0.5f);   // panel fade-out/fade-in
                            CaptureIfEnabled("panel_paused_completed");
                            _game.TogglePause();
                            yield return null;
                            Assert.AreEqual(GameState.Completed, _game.Flow.State);
                            Assert.That(bloom.IsPlaying && bloom.GetComponent<Renderer>().enabled, "finale sculpture survives pause");
                        }
                        _game.Primary();   // Play again
                    }
                    else
                    {
                        _game.Primary();   // Next
                        Assert.AreEqual(GameState.Transition, _game.Flow.State);
                        yield return WaitUntil(() => _game.Flow.State == GameState.Playing, 5f, "transition");
                    }
                }
            }
            Assert.AreEqual(0, _game.Flow.StageIndex, "Play again restarts at stage 1");
            stats.Report();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator RetryAndPause_NeverSoftLock()
        {
            _game.Primary();
            for (int i = 0; i < 3; i++)
            {
                _game.Retry();
                yield return null;
                Assert.That(_game.Flow.IsInteractive, $"retry {i}");
            }
            _game.TogglePause();
            Assert.AreEqual(GameState.Paused, _game.Flow.State);
            _game.TogglePause();
            Assert.That(_game.Flow.IsInteractive);

            _game.TogglePause();
            yield return PressVisible("Retry");   // the pause panel's Retry restarts the stage
            Assert.That(_game.Flow.IsInteractive, "retry from the pause panel");

            yield return SolveCurrent();
            _game.Retry();   // retry from Solved replays the stage
            yield return null;
            Assert.That(_game.Flow.IsInteractive);
            Assert.False(_puzzle.IsSolved);
        }

        /// <summary>
        /// Phase 4 merge regression: Retry/Next reloads the stage, and each load used to leave its two
        /// projection meshes behind. Also checks that hand loss on a live scene drops the grab and suspends play.
        /// </summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator RepeatedRetries_KeepProjectionMeshesBounded_AndHandLossSuspendsInput()
        {
            _game.Primary();
            yield return null;
            int baseline = CountProjectionMeshes();
            for (int i = 0; i < 20; i++)
            {
                _game.Retry();
                yield return null;
            }
            yield return null;   // Destroy is deferred to the end of the frame
            Assert.AreEqual(baseline, CountProjectionMeshes(), "projection meshes after 20 retries");

            _puzzle.NotifyGrab(true);
            _game.SetInputStatus(false, false);
            Assert.False(_puzzle.InputEnabled, "no hands, no controllers: input suspended");
            Assert.False(_puzzle.IsGrabbed, "hand loss releases the grab");
            _game.SetInputStatus(true, false);
            Assert.That(_puzzle.InputEnabled, "hands back: play resumes without any button");
            yield return SolveCurrent();
        }

        static int CountProjectionMeshes()
        {
            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
                if (m.name == "CurrentProjection" || m.name == "TargetProjection") n++;
            return n;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator PauseDuringFinale_StillReachesCompletion()
        {
            _game.Primary();
            for (int stage = 0; stage < _game.Flow.StageCount - 1; stage++)
            {
                yield return SolveCurrent();
                _game.Primary();
                yield return WaitUntil(() => _game.Flow.State == GameState.Playing, 5f, "transition");
            }

            yield return SolveCurrent();
            yield return Wait(0.5f);
            _game.TogglePause();                 // e.g. headset removed during the finale
            Assert.AreEqual(GameState.Paused, _game.Flow.State);
            yield return Wait(7f);               // longer than the whole finale
            Assert.AreEqual(GameState.Paused, _game.Flow.State, "nothing advances while paused");
            yield return PressVisible("Primary"); // Resume, through the UI as a player would

            // Either the resumed finale hands off by itself, or the visible primary button ("Finish") does.
            float end = Time.time + 6f;
            while (_game.Flow.State != GameState.Completed && Time.time < end) yield return null;
            if (_game.Flow.State != GameState.Completed) yield return PressVisible("Primary");
            Assert.AreEqual(GameState.Completed, _game.Flow.State);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Puzzle_StaysHiddenUntilTheTableIsConfirmed_AndHidesAgainForMove()
        {
            Assert.AreEqual(GameState.Setup, _game.Flow.State);
            Assert.False(_puzzle.IsVisible, "nothing appears before the player confirms the table");
            Assert.False(_game.Garden.Visible, "the garden waits for the table too");
            Assert.False(_puzzle.IsGrabbable, "the hidden puzzle's grab volume would hide the hand ray and block Confirm");
            _puzzle.NotifyGrab(true);   // a hand passing through the hidden puzzle
            yield return null;
            Assert.AreEqual(GameState.Setup, _game.Flow.State, "a grab during setup does not start the game");
            _puzzle.NotifyGrab(false);

            _game.Primary();   // Start (room scan) or Confirm (marker)
            Assert.AreEqual(GameState.Tutorial, _game.Flow.State);
            Assert.That(_puzzle.IsVisible, "the puzzle draws in on the confirmed table");
            Assert.That(_puzzle.IsGrabbable, "grabbable again once it is shown");
            Assert.That(_puzzle.InputEnabled, "a returning player goes straight into stage 1");

            _game.TogglePause();
            _game.Reposition();   // pause panel: Move
            Assert.AreEqual(GameState.Setup, _game.Flow.State);
            Assert.AreEqual(PlacementStatus.Manual, _placement.Status);
            Assert.False(_puzzle.IsVisible, "hidden while the marker is moved");
            Assert.False(_puzzle.IsGrabbable, "and not grabbable, so the ray reaches Confirm");
            yield return Wait(0.8f);
            var ui = UnityEngine.Object.FindAnyObjectByType<WorldSpaceUIController>();
            TestCapture.Head("onboarding_setup_manual");
            TestCapture.Panel((RectTransform)ui.transform, "onboarding_setup_manual_panel");
            _game.Primary();   // Confirm
            Assert.That(_puzzle.IsVisible);
            Assert.That(_game.Flow.IsInteractive);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator FirstMinute_MilestonesLogOncePerRun()
        {
            GameSettings.HowToPlaySeen = false;   // TearDown restores the machine's own value
            var lines = new System.Collections.Generic.List<string>();
            void OnLog(string msg, string stack, LogType type) { if (msg.Contains("first-minute ")) lines.Add(msg); }
            Application.logMessageReceived += OnLog;
            try
            {
                var ui = UnityEngine.Object.FindAnyObjectByType<WorldSpaceUIController>();
                _game.Primary();                                                  // Start
                yield return WaitUntil(() => ui.CurrentView.Title == UiCopy.HowToPlayTitle(0), 3f, "first card");
                for (int i = 0; i < UiCopy.FirstRunCardCount; i++) _game.Primary();
                _puzzle.DebugSetSolution();
                yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "solve");
                yield return WaitUntil(() => lines.Exists(l => l.Contains("bloom-finished")), 20f, "bloom finished");
            }
            finally { Application.logMessageReceived -= OnLog; }
            foreach (var name in new[] { "start", "card-shown", "card-done", "input-on", "match", "solved", "bloom-finished" })
                Assert.AreEqual(1, lines.FindAll(l => l.Contains("first-minute " + name + " t=")).Count, name);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator FirstLaunch_HowToPlayCards_ComeBeforeStageOne_AndReopenFromPause()
        {
            GameSettings.HowToPlaySeen = false;   // TearDown restores the machine's own value
            var ui = UnityEngine.Object.FindAnyObjectByType<WorldSpaceUIController>();
            _game.Primary();
            Assert.AreEqual(GameState.Tutorial, _game.Flow.State);
            Assert.False(_puzzle.InputEnabled, "no input behind the cards");
            Assert.False(_puzzle.IsGrabbable, "a live grab volume would hide the hand ray, so Next could never be pressed");
            yield return WaitUntil(() => ui.CurrentView.Title == UiCopy.HowToPlayTitle(0), 3f, "first card");
            Assert.AreEqual(UiCopy.FirstRunCardCount > 1 ? UiCopy.HowToPlayNext : UiCopy.HowToPlayDone, ui.CurrentView.Primary);
            yield return Wait(0.8f);   // panel reveal + the stage drawing in behind it
            TestCapture.Head("onboarding_howtoplay");
            TestCapture.Panel((RectTransform)ui.transform, "onboarding_howtoplay_panel");

            _puzzle.DebugSetSolution();
            yield return Wait(1f);
            Assert.AreEqual(GameState.Tutorial, _game.Flow.State, "the puzzle cannot be solved behind the cards");
            _puzzle.ResetPose();   // else the held answer solves the moment input returns

            for (int i = 1; i < UiCopy.FirstRunCardCount; i++) _game.Primary();
            yield return WaitUntil(() => ui.CurrentView.Title == UiCopy.HowToPlayTitle(UiCopy.FirstRunCardCount - 1), 3f, "last first-run card");
            Assert.AreEqual(UiCopy.HowToPlayDone, ui.CurrentView.Primary);
            yield return Wait(0.4f);
            TestCapture.Panel((RectTransform)ui.transform, "onboarding_howtoplay_last_panel");
            _game.Primary();
            Assert.That(_puzzle.InputEnabled, "the last card hands over to stage 1");
            Assert.That(_puzzle.IsGrabbable, "and the puzzle can be grabbed");
            Assert.That(GameSettings.HowToPlaySeen, "read once, not shown on the next launch");
            yield return WaitUntil(() => ui.CurrentView.Title == _game.CurrentStage.displayName, 3f, "stage 1 panel");

            _game.TogglePause();
            yield return WaitUntil(() => ui.CurrentView.HowToPlay, 3f, "pause panel offers How to play");
            _game.ShowHowToPlay();
            Assert.That(_game.Flow.IsInteractive, "the cards open over the running stage");
            yield return WaitUntil(() => ui.CurrentView.Title == UiCopy.HowToPlayTitle(0), 3f, "pause flow card 1");
            Assert.AreEqual(UiCopy.HowToPlayNext, ui.CurrentView.Primary, "pause shows all cards, so card 1 is not the last");
            Assert.False(_puzzle.InputEnabled);
            for (int i = 0; i < UiCopy.HowToPlayCount; i++) _game.Primary();
            Assert.That(_puzzle.InputEnabled);
            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "solve after the cards");
        }

        [Test]
        public void HoverAdapter_FindsTheGrabInteractables_UnderThePivot()
        {
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (var mb in _puzzle.Pivot.GetComponentsInChildren<MonoBehaviour>(true)) names.Add(mb.GetType().Name);
            Assert.That(names, Does.Contain("PuzzleHoverFeedback"));
            Assert.That(names, Does.Contain("HandGrabInteractable"));
            Assert.That(names, Does.Contain("GrabInteractable"));
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ComfortSettings_ToggleFromPausePanel_AndPersistSeparately()
        {
            bool muted = GameSettings.Muted, calm = GameSettings.ReducedMotion;
            try
            {
                _game.Primary();
                _game.TogglePause();
                yield return Wait(0.6f);   // after the panel's fade-out/fade-in (≈ 0.34 s) and the stage intro draw-on
                CaptureIfEnabled("panel_paused");
                string progress = PlayerPrefs.GetString(ProgressKey, "");
                yield return PressVisible("Sound");
                yield return PressVisible("Motion");
                Assert.AreEqual(!muted, GameSettings.Muted);
                Assert.AreEqual(!calm, GameSettings.ReducedMotion);
                Assert.AreEqual(progress, PlayerPrefs.GetString(ProgressKey, ""), "settings never touch progress");

                // A reduced-motion bloom still completes and reaches its resting state.
                _game.TogglePause();
                yield return SolveCurrent();
                var bloom = UnityEngine.Object.FindAnyObjectByType<BloomEffectController>();
                yield return WaitUntil(() => bloom.IsFinished, 6f, "reduced-motion bloom");
            }
            finally
            {
                GameSettings.Muted = muted;
                GameSettings.ReducedMotion = calm;
            }
        }

        /// <summary>Diagnostic: per-frame GC with each game component disabled in turn. Run with -testFilter.</summary>
        [UnityTest, Explicit, Timeout(120000)]
        public IEnumerator GcAttribution()
        {
            _game.Primary();
            var names = new[] { "none", "DesktopDebugInput", "GameStateController", "PuzzleController", "WorldSpaceUIController", "FeedbackAudio", "MrukSurfaceSource", "HandTrackingMonitor", "SurfacePlacementController", "UiButtonStyle" };
            var sb = new StringBuilder("[ShadowBloom GC attribution]\n");
            using var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 4);
            foreach (var n in names)
            {
                // Every instance of the type (UiButtonStyle sits on each panel button).
                var disabled = new List<Behaviour>();
                if (n != "none")
                    foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
                        if (mb.GetType().Name == n && mb.enabled) { mb.enabled = false; disabled.Add(mb); }
                Behaviour b = disabled.Count > 0 ? disabled[0] : null;
                for (int i = 0; i < 10; i++) yield return null;
                long sum = 0;
                for (int i = 0; i < 60; i++) { yield return null; sum += gc.LastValue; }
                sb.AppendLine($"{n} disabled (found={disabled.Count}): avg GC bytes/frame = {sum / 60}");
                foreach (var d in disabled) d.enabled = true;
            }
            Debug.Log(sb.ToString());
            string file = Environment.GetEnvironmentVariable("SB_STATS_FILE");
            if (!string.IsNullOrEmpty(file)) File.WriteAllText(file, sb.ToString());
        }

        /// <summary>Diagnostic: per-frame GC with PuzzleController's sub-features switched off one at a time.</summary>
        [UnityTest, Explicit, Timeout(120000)]
        public IEnumerator GcPuzzleBreakdown()
        {
            _game.Primary();
            var sb = new StringBuilder("[ShadowBloom GC puzzle breakdown]\n");
            using var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 4);
            IEnumerator Measure(string label)
            {
                for (int i = 0; i < 10; i++) yield return null;
                long sum = 0;
                for (int i = 0; i < 60; i++) { yield return null; sum += gc.LastValue; }
                sb.AppendLine($"{label}: avg GC bytes/frame = {sum / 60}");
            }
            yield return Measure("tutorial (ghost on, matching on)");
            _puzzle.SetTutorialGhost(false);
            yield return Measure("ghost off");
            _puzzle.InputEnabled = false;
            yield return Measure("ghost off, matching off");
            _puzzle.InputEnabled = true;
            for (int i = 0; i < 60; i++) { _puzzle.ApplyWorldRotationDelta(Quaternion.Euler(0f, 1f, 0f)); yield return null; }
            yield return Measure("after rotating (idle again)");
            _puzzle.enabled = false;
            yield return Measure("PuzzleController disabled");
            Debug.Log(sb.ToString());
            string file = Environment.GetEnvironmentVariable("SB_STATS_FILE");
            if (!string.IsNullOrEmpty(file)) File.WriteAllText(file, sb.ToString());
        }

        // ---------------------------------------------------------------------------------------------

        IEnumerator SolveCurrent()
        {
            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, $"solve stage {_game.Flow.StageIndex + 1}");
        }

        /// <summary>Presses a UI button by name, failing if it is not visible: what a player can actually reach.</summary>
        static IEnumerator PressVisible(string name)
        {
            yield return Wait(0.45f);   // UI cooldown
            UnityEngine.UI.Button button = null;
            foreach (var b in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Exclude))
                if (b.name == name && b.gameObject.activeInHierarchy) button = b;
            Assert.NotNull(button, $"visible button '{name}'");
            button.onClick.Invoke();
            yield return null;
        }

        static void CaptureIfEnabled(string name)
        {
            string dir = Environment.GetEnvironmentVariable("SB_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(dir)) Capture(dir, name);
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static IEnumerator WaitUntil(Func<bool> condition, float timeout, string what)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail($"Timed out waiting for {what}");
                yield return null;
            }
        }

        IEnumerator CaptureSequence(string tag, FrameStats stats)
        {
            string dir = Environment.GetEnvironmentVariable("SB_CAPTURE_DIR");
            float[] times = tag == "finale" ? new[] { 0.3f, 0.9f, 1.6f, 2.3f, 3.0f, 3.8f, 5f } : new[] { 0.15f, 0.6f, 1.1f, 1.7f, 2.6f, 3.6f };
            float start = Time.time;
            foreach (float t in times)
            {
                while (Time.time - start < t)
                {
                    stats.Sample("bloom");
                    yield return null;
                }
                if (!string.IsNullOrEmpty(dir)) Capture(dir, $"{tag}_t{t:0.00}_dark");
            }
            if (string.IsNullOrEmpty(dir)) yield break;

            // Same frame over a light table: proves line contrast does not depend on a dark surface.
            var top = GameObject.Find("DesktopTableProxy/Top");
            if (top == null) yield break;
            var r = top.GetComponent<Renderer>();
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_Color", new Color(0.82f, 0.8f, 0.76f, 1f));
            r.SetPropertyBlock(mpb);
            yield return null;
            Capture(dir, $"{tag}_t{Time.time - start:0.00}_light");
            r.SetPropertyBlock(new MaterialPropertyBlock());
        }

        static void Capture(string dir, string name)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Directory.CreateDirectory(dir);
            const int w = 960, h = 720;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevBg = cam.backgroundColor;
            // Stand-in for passthrough: a mid-grey room so alpha-coverage and contrast are visible in the PNG.
            cam.backgroundColor = TestCapture.Background;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prevTarget;
            cam.backgroundColor = prevBg;
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        /// <summary>Editor-only frame statistics via ProfilerRecorder. Written to the log and SB_STATS_FILE.</summary>
        sealed class FrameStats
        {
            readonly Dictionary<string, List<(double ms, long gc, long draws, long setpass)>> _samples = new();
            ProfilerRecorder _main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 4);
            ProfilerRecorder _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 4);
            ProfilerRecorder _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 4);
            ProfilerRecorder _setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 4);

            public IEnumerator Measure(string phase, float seconds)
            {
                float end = Time.time + seconds;
                while (Time.time < end)
                {
                    yield return null;
                    Sample(phase);
                }
            }

            public void Sample(string phase)
            {
                if (!_samples.TryGetValue(phase, out var list)) _samples[phase] = list = new();
                list.Add((_main.LastValue / 1e6, _gc.LastValue, _draws.LastValue, _setpass.LastValue));
            }

            public void Report()
            {
                var sb = new StringBuilder("[ShadowBloom stats] Editor play mode, not device data\n");
                foreach (var kv in _samples)
                {
                    double ms = 0, msMax = 0; long gc = 0, gcMax = 0, d = 0, dMax = 0, sp = 0; int gcFrames = 0;
                    foreach (var s in kv.Value)
                    {
                        ms += s.ms; msMax = Math.Max(msMax, s.ms);
                        gc += s.gc; gcMax = Math.Max(gcMax, s.gc); if (s.gc > 0) gcFrames++;
                        d += s.draws; dMax = Math.Max(dMax, s.draws); sp += s.setpass;
                    }
                    int n = Math.Max(1, kv.Value.Count);
                    sb.AppendLine($"{kv.Key}: frames={kv.Value.Count} mainThreadMs avg={ms / n:F2} max={msMax:F2} " +
                                  $"gcBytes/frame avg={gc / n} max={gcMax} framesWithGC={gcFrames} " +
                                  $"drawCalls avg={d / n} max={dMax} setPass avg={sp / n}");
                }
                Debug.Log(sb.ToString());
                string file = Environment.GetEnvironmentVariable("SB_STATS_FILE");
                if (!string.IsNullOrEmpty(file)) File.WriteAllText(file, sb.ToString());
                _main.Dispose(); _gc.Dispose(); _draws.Dispose(); _setpass.Dispose();
            }
        }
    }
}
