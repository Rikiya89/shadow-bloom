using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ShadowBloom.Tests
{
    /// <summary>
    /// The world-space panel in every game state, on the real scene (desktop path, scripted input):
    /// physical target sizes and spacing (mm), containment in the Interaction SDK poke/ray clip bounds (hands can
    /// only reach what is inside them), text that fits and has glyphs, the Solved panel waiting for the bloom, the
    /// hands-lost notice while the panel is otherwise hidden, Restart from the pause panel, and stage 1 coaching.
    /// With SB_CAPTURE_DIR set, every state is captured from the head and straight-on (SB_CAPTURE_BG=light for a
    /// bright room). These are Editor checks; comfort and readability still need a headset.
    /// </summary>
    public class UiPlayModeTests
    {
        const string ScenePath = "Assets/ShadowBloom/Scenes/ShadowBloom.unity";
        const string ProgressKey = "shadowbloom.progress.v1";
        const float MinTarget = 22f, MinGap = 11.9f;

        string _savedProgress;

        HowToPlaySeenScope _howToPlay;
        bool _hadProgress, _muted, _calm;
        GameStateController _game;
        PuzzleController _puzzle;
        SurfacePlacementController _placement;
        WorldSpaceUIController _ui;
        BloomEffectController _bloom;
        readonly StringBuilder _report = new StringBuilder();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _hadProgress = PlayerPrefs.HasKey(ProgressKey);
            _savedProgress = PlayerPrefs.GetString(ProgressKey, "");
            PlayerPrefs.DeleteKey(ProgressKey);
            _howToPlay = new HowToPlaySeenScope();
            _muted = GameSettings.Muted;
            _calm = GameSettings.ReducedMotion;
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
            _ui = UnityEngine.Object.FindAnyObjectByType<WorldSpaceUIController>();
            _bloom = UnityEngine.Object.FindAnyObjectByType<BloomEffectController>();
            Assert.NotNull(_ui, "WorldSpaceUIController");
            yield return WaitUntil(() => _placement.Status != PlacementStatus.Searching, 12f, "placement");
            _report.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadProgress) PlayerPrefs.SetString(ProgressKey, _savedProgress);
            else PlayerPrefs.DeleteKey(ProgressKey);
            _howToPlay?.Restore();
            PlayerPrefs.Save();
            GameSettings.Muted = _muted;
            GameSettings.ReducedMotion = _calm;
            string file = Environment.GetEnvironmentVariable("SB_UI_REPORT");
            if (_report.Length > 0)
            {
                Debug.Log("[ShadowBloom UI]\n" + _report);
                if (!string.IsNullOrEmpty(file)) File.AppendAllText(file, _report.ToString());
            }
        }

        // ---- Every state ------------------------------------------------------------------------------

        [UnityTest, Timeout(240000)]
        public IEnumerator EveryState_MeetsTargetSizes_StaysInsideHandClipBounds_AndFits()
        {
            yield return Settle();
            yield return Check("setup_" + _placement.Status.ToString().ToLowerInvariant());
            if (_placement.Status == PlacementStatus.Manual)
            {
                _placement.ConfirmManual();
                yield return Settle();
                yield return Check("setup_ready");
            }

            _game.Primary();
            yield return Settle();
            yield return Check("tutorial");

            _puzzle.NotifyGrab(true);
            yield return null;
            _puzzle.NotifyGrab(false);
            yield return Settle();
            yield return Check("playing");

            _game.TogglePause();
            yield return Settle();
            yield return Check("paused");
            _game.TogglePause();
            yield return Settle();

            for (int stage = 0; stage < _game.Flow.StageCount; stage++)
            {
                _puzzle.DebugSetSolution();
                yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "solve");
                if (_game.Flow.IsLastStage)
                {
                    yield return WaitUntil(() => _game.Flow.State == GameState.Completed, _bloom.TotalDuration + 4f, "finale");
                    break;
                }
                if (stage == 0)
                {
                    yield return new WaitForSeconds(0.9f);
                    Assert.That(_ui.CurrentView.Hidden, "the panel stays out of the bloom");
                    Assert.False(_ui.Panel.Find("Primary").gameObject.activeSelf, "no invisible Next during the bloom");
                    TestCapture.Head("ui_solved_blooming");
                }
                yield return WaitUntil(() => _ui.CurrentView.Primary == UiCopy.NextStage, _bloom.TotalDuration + 3f, "Next stage revealed");
                yield return Settle();
                if (stage == 0) yield return Check("solved");
                _game.Primary();
                yield return WaitUntil(() => _game.Flow.IsInteractive, 5f, "next stage");
                yield return Settle();
                if (stage == 1) yield return Check("playing_stage2");
            }
            yield return Settle();
            yield return Check("completed");
            Assert.AreEqual(UiCopy.PlayAgain, _ui.CurrentView.Primary);

            _game.TogglePause();
            yield return Settle();
            yield return Check("paused_completed");
            _game.TogglePause();
            yield return Settle();
        }

        // ---- Behaviour --------------------------------------------------------------------------------

        [UnityTest, Timeout(60000)]
        public IEnumerator LaterIdleHint_SuggestsOnlyAPartialTurn_AndNeverMovesThePuzzle()
        {
            _game.Primary();
            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "stage 1");
            _game.Primary();
            yield return WaitUntil(() => _game.Flow.IsInteractive && _game.Flow.StageIndex == 1, 5f, "stage 2");
            typeof(GameStateController).GetField("_idleHintSeconds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_game, 0.05f);
            var pose = _puzzle.Pivot.localRotation;
            float solutionAngle = Quaternion.Angle(pose, _game.CurrentStage.SolutionRotation);
            float maxTurn = 0f;
            var ghost = _puzzle.Pivot.Find("TutorialGhost");
            float end = Time.time + 3.5f;
            while (Time.time < end)
            {
                yield return null;
                maxTurn = Mathf.Max(maxTurn, Quaternion.Angle(Quaternion.identity, ghost.localRotation));
                Assert.That(Quaternion.Angle(pose, _puzzle.Pivot.localRotation), Is.LessThan(0.01f), "hint does not rotate the puzzle");
                Assert.That(_puzzle.IsSolved, Is.False);
            }
            Assert.That(ghost.GetComponent<Renderer>().enabled);
            Assert.That(maxTurn, Is.GreaterThan(5f), "a visible directional demonstration");
            Assert.That(maxTurn, Is.LessThanOrEqualTo(solutionAngle * 0.36f), "later hints never show the exact solution");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Pause_PreservesTheGhostPose_AndMuteStopsPausedAudio()
        {
            _game.Primary();
            yield return Settle();
            var ghost = _puzzle.Pivot.Find("TutorialGhost");
            Assert.That(ghost.GetComponent<Renderer>().enabled);
            _game.TogglePause();
            var pose = ghost.localRotation;
            yield return new WaitForSeconds(0.5f);
            Assert.That(ghost.GetComponent<Renderer>().enabled, "pause preserves the demonstration rather than restarting it");
            Assert.That(Quaternion.Angle(pose, ghost.localRotation), Is.LessThan(0.01f));
            _game.TogglePause();
            yield return new WaitForSeconds(0.25f);
            Assert.That(Quaternion.Angle(pose, ghost.localRotation), Is.GreaterThan(0.1f), "ghost resumes");

            var audio = UnityEngine.Object.FindAnyObjectByType<FeedbackAudio>();
            var emitter = audio.transform.Find("PuzzleAudioEmitter").GetComponent<AudioSource>();
            GameSettings.Muted = false;
            audio.Bloom(false);
            yield return new WaitForSeconds(0.1f);
            Assert.That(emitter.isPlaying, "the actual bloom one-shot is playing");
            _game.TogglePause();
            Assert.That(emitter.isPlaying, Is.False, "gameplay audio pauses");
            yield return new WaitForSeconds(0.3f);
            _game.TogglePause();
            yield return null;
            Assert.That(emitter.isPlaying, "one-shot resumes with the presentation clock");
            _game.TogglePause();
            GameSettings.Muted = true;
            _game.TogglePause();
            yield return null;
            Assert.That(emitter.isPlaying, Is.False, "muting stops a paused tail, so Resume cannot replay it");
            _game.TogglePause();
            _game.Reposition();
            yield return null;
            Assert.That(_game.Flow.State, Is.EqualTo(GameState.Setup));
            Assert.That(ghost.GetComponent<Renderer>().enabled, Is.False, "Move is a new setup, not a resume of the paused hint");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Pause_DuringTransition_ContinuesItInsteadOfRestarting()
        {
            _game.Primary();
            yield return Settle();
            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "stage 1");
            _game.Primary();   // Next
            Assert.That(_game.Flow.State, Is.EqualTo(GameState.Transition));
            yield return new WaitForSeconds(0.25f);
            var timer = typeof(GameStateController).GetField("_transitionTimer", BindingFlags.Instance | BindingFlags.NonPublic);
            _game.TogglePause();
            float left = (float)timer.GetValue(_game);
            Assert.That(left, Is.GreaterThan(0f), "paused mid-transition");
            _game.TogglePause();
            Assert.That(_game.Flow.State, Is.EqualTo(GameState.Transition));
            Assert.That((float)timer.GetValue(_game), Is.EqualTo(left), "resume continues the transition; it does not restart it");
            yield return WaitUntil(() => _game.Flow.IsInteractive && _game.Flow.StageIndex == 1, 5f, "stage 2");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator PauseThenRetry_TheIdleHintStillReturns()
        {
            _game.Primary();
            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "stage 1");
            _game.Primary();
            yield return WaitUntil(() => _game.Flow.IsInteractive && _game.Flow.StageIndex == 1, 5f, "stage 2");
            typeof(GameStateController).GetField("_idleHintSeconds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_game, 0.3f);
            var ghost = _puzzle.Pivot.Find("TutorialGhost").GetComponent<Renderer>();
            yield return WaitUntil(() => ghost.enabled, 3f, "idle hint");
            _game.TogglePause();
            _game.Retry();
            Assert.That(_game.Flow.State, Is.EqualTo(GameState.Playing));
            Assert.That(ghost.enabled, Is.False, "Retry clears the previous hint");
            yield return WaitUntil(() => ghost.enabled, 3f, "idle hint after a Retry from the pause panel");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Pause_FreezesTheSolvedSnap_ThenResumesIt()
        {
            _game.Primary();
            yield return Settle();
            _puzzle.DebugSetSolution();
            _puzzle.ApplyWorldRotationDelta(Quaternion.AngleAxis(8f, _puzzle.transform.right));
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "solve slightly off centre");
            _game.TogglePause();
            var pose = _puzzle.Pivot.localRotation;
            yield return new WaitForSeconds(0.7f);
            Assert.That(Quaternion.Angle(pose, _puzzle.Pivot.localRotation), Is.LessThan(0.01f), "the solved snap freezes along with the bloom");
            _game.TogglePause();
            yield return new WaitForSeconds(0.6f);
            Assert.That(Quaternion.Angle(pose, _puzzle.Pivot.localRotation), Is.GreaterThan(0.1f), "snap continues after Resume");
            yield return WaitUntil(() => _ui.CurrentView.Primary == UiCopy.NextStage, 6f, "Next after resumed bloom");
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator SolvedPanel_WaitsForTheBloom_WhileTheHandsLostNoticeStillShows()
        {
            _game.Primary();
            yield return Settle();
            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "solve");
            yield return Settle();   // the Playing panel fades out first (~0.12 s)
            Assert.That(_ui.CurrentView.Hidden, "panel steps aside for the bloom");
            Assert.False(_ui.IsVisible);

            _game.SetInputStatus(false, false);
            yield return new WaitForSeconds(1.5f);   // past the hand-loss grace period, bloom still unfolding
            var notice = _ui.Panel.Find("Notice");
            Assert.That(notice.gameObject.activeInHierarchy, "notice shown although the panel is otherwise hidden");
            Assert.That(_ui.IsVisible);
            Assert.AreEqual(UiCopy.HandsLost, notice.GetComponentInChildren<TMP_Text>().text);
            yield return Settle();
            TestCapture.Head("ui_notice_during_bloom");
            TestCapture.Panel((RectTransform)_ui.transform, "ui_notice_during_bloom_panel");
            yield return CheckButtonsAndClip("notice");

            _game.SetInputStatus(true, false);
            yield return null;
            Assert.False(notice.gameObject.activeInHierarchy, "notice clears when hands return");

            yield return WaitUntil(() => _ui.CurrentView.Primary == UiCopy.NextStage, _bloom.TotalDuration + 3f, "Next stage revealed");
            yield return PressVisible("Primary");
            Assert.AreEqual(GameState.Transition, _game.Flow.State, "the revealed Next stage button works");
            // Let the transition finish here: otherwise the old scene starts stage 2 (and saves) during the next
            // test's scene load, after its SetUp cleared the progress key.
            yield return WaitUntil(() => _game.Flow.IsInteractive, 3f, "stage 2 after the transition");
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator RestartFromThePausePanel_ReturnsToStage1_AndKeepsProgress()
        {
            _game.Primary();
            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "solve stage 1");
            _game.Primary();
            yield return WaitUntil(() => _game.Flow.IsInteractive && _game.Flow.StageIndex == 1, 5f, "stage 2");
            string progress = PlayerPrefs.GetString(ProgressKey, "");
            StringAssert.Contains("01_circle", progress);

            _game.TogglePause();
            yield return PressVisible("Restart");
            Assert.AreEqual(0, _game.Flow.StageIndex, "back to stage 1");
            Assert.That(_game.Flow.IsInteractive);
            // Earned progress survives; only the bookmark of the last stage played moves (existing save behaviour).
            string after = PlayerPrefs.GetString(ProgressKey, "");
            StringAssert.Contains("01_circle", after, "completed stage kept");
            StringAssert.Contains("\"highestUnlockedStageIndex\":1", after, "highest unlock kept");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Tutorial_ShowsOneInstruction_AndMovesOnOnceDemonstrated()
        {
            _game.Primary();
            yield return Settle();
            var hint = _ui.Panel.Find("Hint").GetComponent<TMP_Text>();
            Assert.AreEqual(UiCopy.TutorialText(UiCopy.StepGrab), hint.text);
            yield return Check("tutorial_grab");
            _puzzle.NotifyGrab(true);
            yield return null;
            yield return null;
            Assert.AreEqual(UiCopy.TutorialText(UiCopy.StepTurn), hint.text, "grab demonstrated -> next instruction");
            _puzzle.NotifyGrab(false);
            yield return null;
            Assert.AreEqual(UiCopy.TutorialText(UiCopy.StepTurn), hint.text, "never goes back to an instruction already followed");
            yield return new WaitForSeconds(0.25f); // instruction fade
            yield return Check("tutorial_turn");

            // Keep the hold visible long enough to inspect it; production stage timing is unchanged.
            _puzzle.HoldScale = 5f;
            _puzzle.DebugSetSolution();
            _puzzle.ApplyWorldRotationDelta(Quaternion.AngleAxis(30f, _puzzle.transform.right));
            yield return WaitUntil(() => hint.text == UiCopy.TutorialText(UiCopy.StepMatch), 3f, "match coaching");
            yield return new WaitForSeconds(0.25f);
            yield return Check("tutorial_match");

            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => hint.text == UiCopy.TutorialText(UiCopy.StepHold), 3f, "hold coaching");
            yield return new WaitForSeconds(0.25f);
            Assert.False(_puzzle.IsSolved, "hold coaching appears before the bloom");
            yield return Check("tutorial_hold");

            _puzzle.ApplyWorldRotationDelta(Quaternion.AngleAxis(30f, _puzzle.transform.right));
            yield return WaitUntil(() => hint.text == UiCopy.TutorialText(UiCopy.StepMatch), 3f, "slipping returns to match coaching");
        }

        /// <summary>
        /// The same uGUI pointer events the Interaction SDK poke/ray canvases forward (enter, down, up, click):
        /// hover and confirm states render, and a click on a panel that is still fading in is ignored by uGUI itself.
        /// Real hand poke/ray input still needs a headset or the simulator.
        /// </summary>
        [UnityTest, Timeout(60000)]
        public IEnumerator PointerPath_ShowsHoverAndConfirm_AndIgnoresClicksMidFade()
        {
            _game.Primary();
            _puzzle.NotifyGrab(true);
            yield return null;
            _puzzle.NotifyGrab(false);
            yield return Settle();
            var retry = _ui.Panel.Find("Retry").gameObject;
            var bg = retry.GetComponent<Image>();
            var ring = retry.transform.Find("Focus").GetComponent<Image>();
            var style = retry.GetComponent<UiButtonStyle>();
            var rest = (Vector4)bg.canvasRenderer.GetColor();
            Assert.That(ring.canvasRenderer.GetColor().a, Is.LessThan(0.05f), "no focus ring at rest");

            var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            UnityEngine.EventSystems.ExecuteEvents.Execute(retry, data, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(Vector4.Distance(rest, bg.canvasRenderer.GetColor()), Is.GreaterThan(0.05f), "hover changes the fill");
            Assert.That(ring.canvasRenderer.GetColor().a, Is.GreaterThan(0.5f), "hover shows the focus ring (not colour alone)");
            TestCapture.Panel((RectTransform)_ui.transform, "ui_button_hover_panel");

            int stageBefore = _game.Flow.StageIndex;
            UnityEngine.EventSystems.ExecuteEvents.Execute(retry, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            yield return null;
            UnityEngine.EventSystems.ExecuteEvents.Execute(retry, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(retry, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            Assert.That(style.IsConfirming, "accepted press shows the confirmed state");
            Assert.That(_game.Flow.IsInteractive && _game.Flow.StageIndex == stageBefore, "Retry restarted the stage");
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.False(style.IsConfirming, "confirmed state is brief");
            UnityEngine.EventSystems.ExecuteEvents.Execute(retry, data, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);

            _game.TogglePause();
            yield return null;   // the Playing panel is fading out; its Pause button is still active and visible
            var pause = _ui.Panel.Find("Pause").gameObject;
            Assert.That(pause.activeInHierarchy && !_ui.IsInteractive);
            UnityEngine.EventSystems.ExecuteEvents.Execute(pause, new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current),
                UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            yield return Settle();
            Assert.AreEqual(GameState.Paused, _game.Flow.State, "a pointer click on a fading panel does nothing (else it would have resumed)");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator PressesAreIgnored_UntilThePanelIsFullyRevealed()
        {
            _game.Primary();
            yield return Settle();
            _game.TogglePause();
            yield return null;   // mid fade
            Assert.False(_ui.IsInteractive);
            _ui.Panel.Find("Primary").GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(GameState.Paused, _game.Flow.State, "a poke during the reveal does nothing");
            yield return Settle();
            Assert.That(_ui.IsInteractive);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator PanelPlacement_ClearsTheGrabVolume_AtSmallAndLargePuzzles()
        {
            var frame = _puzzle.transform;
            var head = _placement.Head;
            foreach (float r in new[] { SurfacePlacementSolver.MinRadius, SurfacePlacementSolver.MaxRadius })
            {
                float pivot = SurfacePlacementSolver.PivotHeightFor(r);
                _ui.PlaceBeside(frame, r, pivot, head.position);
                yield return null;
                var m = Measure(frame, r, pivot);
                _report.AppendLine($"placement r={r:F2} m: {m}");
                Assert.That(m.Clearance, Is.GreaterThanOrEqualTo(0.03f), $"panel clears the grab volume at r={r}");
                Assert.That(m.BottomAboveSurface, Is.GreaterThanOrEqualTo(0.099f), "never down at the table");
            }
        }

        // ---- Checks -----------------------------------------------------------------------------------

        IEnumerator Check(string state)
        {
            yield return CheckButtonsAndClip(state);
            var root = (RectTransform)_ui.transform;
            foreach (var t in _ui.Panel.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(t.text)) continue;
                Assert.That(t.font.HasCharacters(t.text, out uint[] missing, true, true), $"{state}: '{t.text}' missing glyphs U+{string.Join(",", missing ?? new uint[0])}");
                var size = t.rectTransform.rect.size;
                var pref = t.GetPreferredValues(t.text, size.x, 0f);
                if (t.textWrappingMode == TextWrappingModes.NoWrap)
                    Assert.That(pref.x, Is.LessThanOrEqualTo(size.x + 0.5f), $"{state}: label '{t.text}' fits its button");
                else
                    Assert.That(pref.y, Is.LessThanOrEqualTo(size.y + 0.5f), $"{state}: '{t.text}' fits its block");
            }
            var m = Measure(_puzzle.transform, _puzzle.PuzzleRadius, _puzzle.PivotHeight);
            _report.AppendLine($"{state}: panel {root.rect.width:F0} x {root.rect.height:F0} mm; {m}");
            TestCapture.Head("ui_" + state);
            TestCapture.Panel(root, "ui_" + state + "_panel");
        }

        IEnumerator CheckButtonsAndClip(string state)
        {
            yield return null;
            var root = (RectTransform)_ui.transform;
            var rootRect = root.rect;
            var rects = new List<(string name, Rect r)>();
            foreach (var b in _ui.Panel.GetComponentsInChildren<Button>(false))
            {
                var r = LocalRect((RectTransform)b.transform, root);
                rects.Add((b.name, r));
                // A settled panel never shows enabled buttons in the faded "disabled" look (reveal gating is invisible).
                // Primary blocks show it in their fill; open grid cells have no fill at rest, so check their label.
                var style = b.GetComponent<UiButtonStyle>();
                if (style != null && style.Style != UiButtonStyle.Variant.Quiet && b.interactable)
                {
                    Graphic probe = style.Style == UiButtonStyle.Variant.Primary
                        ? b.GetComponent<Image>()
                        : b.transform.Find("Label").GetComponent<Graphic>();
                    Assert.That(probe.canvasRenderer.GetColor().a, Is.GreaterThan(0.9f), $"{state}: {b.name} rendered at full opacity");
                }
                // The filter uGUI's GraphicRaycaster applies before hit-testing (poke and ray both go through it): a
                // transparent open cell must still be a raycast target with a canvas depth.
                var hit = b.GetComponent<Image>();
                Assert.That(hit.raycastTarget && !hit.canvasRenderer.cull && hit.depth != -1, $"{state}: {b.name} is raycastable (depth {hit.depth})");
                Assert.That(r.height, Is.GreaterThanOrEqualTo(MinTarget), $"{state}: {b.name} height {r.height:F1} mm");
                Assert.That(r.width, Is.GreaterThanOrEqualTo(MinTarget), $"{state}: {b.name} width {r.width:F1} mm");
                Assert.That(rootRect.xMin <= r.xMin + 0.5f && r.xMax <= rootRect.xMax + 0.5f &&
                            rootRect.yMin <= r.yMin + 0.5f && r.yMax <= rootRect.yMax + 0.5f,
                    $"{state}: {b.name} {r} inside the canvas root {rootRect} (the ISDK clip bounds follow it)");
            }
            float minGap = float.PositiveInfinity, minW = float.PositiveInfinity, minH = float.PositiveInfinity;
            foreach (var (_, r) in rects) { minW = Mathf.Min(minW, r.width); minH = Mathf.Min(minH, r.height); }
            for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                var a = rects[i].r; var c = rects[j].r;
                bool sameRow = a.yMin < c.yMax && c.yMin < a.yMax;
                bool sameCol = a.xMin < c.xMax && c.xMin < a.xMax;
                float gap = sameRow ? Mathf.Max(c.xMin - a.xMax, a.xMin - c.xMax)
                          : sameCol ? Mathf.Max(c.yMin - a.yMax, a.yMin - c.yMax) : float.PositiveInfinity;
                minGap = Mathf.Min(minGap, gap);
                Assert.That(gap, Is.GreaterThanOrEqualTo(MinGap), $"{state}: {rects[i].name} / {rects[j].name} gap {gap:F1} mm");
            }
            // Every Interaction SDK clipper on the canvas must match the root rect, or buttons fall outside reach.
            int clippers = 0;
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb.GetType().Name != "BoundsClipper") continue;
                var p = mb.GetType().GetProperty("Size", BindingFlags.Public | BindingFlags.Instance);
                if (p == null) continue;
                var size = (Vector3)p.GetValue(mb);
                clippers++;
                if (rects.Count == 0) continue;
                Assert.That(size.x, Is.EqualTo(rootRect.width).Within(0.5f), $"{state}: clipper width");
                Assert.That(size.y, Is.EqualTo(rootRect.height).Within(0.5f), $"{state}: clipper height");
            }
            Assert.That(clippers, Is.EqualTo(2), "poke and ray canvas clippers");
            if (rects.Count > 0) _report.AppendLine($"{state}: {rects.Count} buttons, smallest {minW:F1} x {minH:F1} mm, min gap {(rects.Count > 1 ? minGap.ToString("F1") : "n/a")} mm");
        }

        struct PlacementMetrics
        {
            public float HeadDistance, ShoulderDistance, AngleFromPuzzle, BottomAboveSurface, Clearance;
            public override string ToString() =>
                $"eye->panel {HeadDistance:F2} m, left shoulder->panel {ShoulderDistance:F2} m (approx.), {AngleFromPuzzle:F0} deg from the puzzle direction, bottom {BottomAboveSurface:F2} m above the surface, {Clearance * 100f:F1} cm clear of the grab volume";
        }

        PlacementMetrics Measure(Transform frame, float radius, float pivotHeight)
        {
            var root = (RectTransform)_ui.transform;
            var head = _placement.Head.position;
            var corners = new Vector3[4];
            root.GetWorldCorners(corners);
            var center = root.TransformPoint(root.rect.center);
            var pivot = frame.TransformPoint(new Vector3(0f, pivotHeight, 0f));
            float grab = radius * 1.2f;
            float clearance = float.PositiveInfinity, bottom = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                // Nearest point of each panel edge to the grab sphere centre.
                var a = corners[i]; var b = corners[(i + 1) % 4];
                var ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(pivot - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                clearance = Mathf.Min(clearance, Vector3.Distance(a + ab * t, pivot) - grab);
                bottom = Mathf.Min(bottom, frame.InverseTransformPoint(a).y);
            }
            var toPanel = Vector3.ProjectOnPlane(center - head, Vector3.up);
            var toPuzzle = Vector3.ProjectOnPlane(pivot - head, Vector3.up);
            // Approximate seated left shoulder: 0.18 m left of and 0.25 m below the eyes (the panel sits on the left).
            var right = Vector3.Cross(Vector3.up, toPuzzle.normalized);
            var shoulder = head - right * 0.18f - Vector3.up * 0.25f;
            return new PlacementMetrics
            {
                HeadDistance = Vector3.Distance(head, center),
                ShoulderDistance = Vector3.Distance(shoulder, center),
                AngleFromPuzzle = Vector3.Angle(toPanel, toPuzzle),
                BottomAboveSurface = bottom,
                Clearance = clearance
            };
        }

        static Rect LocalRect(RectTransform rt, RectTransform root)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var w in c)
            {
                var l = (Vector2)root.InverseTransformPoint(w);
                min = Vector2.Min(min, l);
                max = Vector2.Max(max, l);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // ---- Helpers ----------------------------------------------------------------------------------

        IEnumerator Settle()
        {
            yield return null;
            yield return WaitUntil(() => !_ui.IsTransitioning, 3f, "panel transition");
            yield return null;
        }

        IEnumerator PressVisible(string name)
        {
            yield return Settle();
            var t = _ui.Panel.Find(name);
            Assert.NotNull(t, name);
            Assert.That(t.gameObject.activeInHierarchy && _ui.IsInteractive, $"'{name}' visible and interactive");
            t.GetComponent<Button>().onClick.Invoke();
            yield return null;
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
    }
}
