using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShadowBloom.Tests
{
    /// <summary>
    /// Headset-free MR checks on the real scene: synthetic room data (tables of different sizes and slopes, missing
    /// data, late results) is fed into the same SurfacePlacementController entry points MRUK uses, then the game is
    /// played on the resulting surface. Also covers repeat-completion and save/resume end to end.
    /// This exercises placement and game logic only; it says nothing about real scene-scan or hand-tracking quality.
    /// </summary>
    public class SyntheticRoomPlayModeTests
    {
        const string ScenePath = "Assets/ShadowBloom/Scenes/ShadowBloom.unity";
        const string ProgressKey = "shadowbloom.progress.v1";

        string _savedProgress;

        HowToPlaySeenScope _howToPlay;
        bool _hadProgress;
        GameStateController _game;
        PuzzleController _puzzle;
        SurfacePlacementController _placement;
        BloomEffectController _bloom;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _hadProgress = PlayerPrefs.HasKey(ProgressKey);
            _savedProgress = PlayerPrefs.GetString(ProgressKey, "");
            PlayerPrefs.DeleteKey(ProgressKey);
            _howToPlay = new HowToPlaySeenScope();
            Application.targetFrameRate = 72;
            yield return LoadScene();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadProgress) PlayerPrefs.SetString(ProgressKey, _savedProgress);
            else PlayerPrefs.DeleteKey(ProgressKey);
            _howToPlay?.Restore();
            PlayerPrefs.Save();
        }

        IEnumerator LoadScene()
        {
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
            _bloom = UnityEngine.Object.FindAnyObjectByType<BloomEffectController>();
            Assert.NotNull(_game);
            Assert.NotNull(_placement);
            yield return WaitUntil(() => _placement.Status != PlacementStatus.Searching, 12f, "initial placement");
        }

        // ---- Synthetic rooms --------------------------------------------------------------------------

        /// <summary>A table top in front of the seated head: centre <paramref name="ahead"/> m forward, 0.48 m below the eyes.</summary>
        SurfaceCandidate Table(Vector2 size, float slopeDegrees = 0f, float ahead = 0.45f, SurfaceKind kind = SurfaceKind.Table)
        {
            var head = _placement.Head;
            var f = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, f);
            var tilt = Quaternion.AngleAxis(slopeDegrees, f);   // rolls the top sideways
            var center = head.position + f * ahead - Vector3.up * 0.48f;
            return new SurfaceCandidate(center, tilt * Vector3.up, tilt * right, f, size, kind);
        }

        IEnumerator Report(params SurfaceCandidate[] candidates)
        {
            _placement.BeginSearch();
            _placement.ReportCandidates(new List<SurfaceCandidate>(candidates));
            yield return null;
        }

        void AssertPuzzleSitsOn(in SurfaceCandidate s)
        {
            var p = _placement.Placement;
            // On the surface plane, inside its rectangle with room for the shadow.
            var rel = p.Position - s.Center;
            Assert.That(Vector3.Dot(rel, s.Normal), Is.EqualTo(0f).Within(1e-4f), "on the plane");
            float margin = p.PuzzleRadius;
            Assert.That(Mathf.Abs(Vector3.Dot(rel, s.AxisU)), Is.LessThanOrEqualTo(s.Size.x * 0.5f - margin + 1e-4f), "inside U");
            Assert.That(Mathf.Abs(Vector3.Dot(rel, s.AxisV)), Is.LessThanOrEqualTo(s.Size.y * 0.5f - margin + 1e-4f), "inside V");
            // Pivot floats above the surface along its own normal, in comfortable reach.
            var pivotRel = _puzzle.Pivot.position - p.Position;
            Assert.That(Vector3.Dot(pivotRel, s.Normal), Is.EqualTo(p.PivotHeight).Within(1e-3f), "pivot height along normal");
            Assert.That(Vector3.ProjectOnPlane(pivotRel, s.Normal).magnitude, Is.LessThan(1e-3f), "pivot over the placement");
            var flat = Vector3.ProjectOnPlane(p.Position - _placement.Head.position, Vector3.up);
            Assert.That(flat.magnitude, Is.LessThanOrEqualTo(SurfacePlacementSolver.MaxComfortReach), "within seated reach");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator SmallTable_PlacesASmallPuzzle_InsideTheTop()
        {
            var table = Table(new Vector2(0.3f, 0.3f));
            yield return Report(table);
            Assert.AreEqual(PlacementStatus.Ready, _placement.Status);
            // Phase 2 sizing (kept by the merge): 0.4 x min side, clamped to [0.11, 0.18].
            Assert.That(_puzzle.PuzzleRadius, Is.EqualTo(0.12f).Within(1e-4f), "0.4 x min side, inside the 0.11-0.18 range");
            Assert.That(_puzzle.PuzzleRadius, Is.LessThan(SurfacePlacementSolver.MaxRadius), "a small table gets a smaller puzzle");
            AssertPuzzleSitsOn(table);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator LargeTable_ClampsScale_AndKeepsThePuzzleInReach()
        {
            var table = Table(new Vector2(1.8f, 0.9f), ahead: 0.75f);
            yield return Report(table);
            Assert.AreEqual(PlacementStatus.Ready, _placement.Status);
            Assert.That(_puzzle.PuzzleRadius, Is.EqualTo(SurfacePlacementSolver.MaxRadius).Within(1e-4f));
            AssertPuzzleSitsOn(table);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator BestSurface_IsChosen_FromAMixedRoom()
        {
            var shelf = Table(new Vector2(0.6f, 0.4f), kind: SurfaceKind.OtherHorizontal);
            shelf.Center += Vector3.up * 0.4f;                          // at eye height: rejected
            var wall = Table(new Vector2(1f, 1f), slopeDegrees: 90f);  // vertical
            var desk = Table(new Vector2(0.8f, 0.5f));
            yield return Report(shelf, wall, desk);
            Assert.AreEqual(PlacementStatus.Ready, _placement.Status);
            AssertPuzzleSitsOn(desk);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator SlopedTable_IsUsed_AndItsStagesStillSolve()
        {
            var table = Table(new Vector2(0.9f, 0.6f), slopeDegrees: 15f);
            yield return Report(table);
            Assert.AreEqual(PlacementStatus.Ready, _placement.Status);
            AssertPuzzleSitsOn(table);
            Assert.That(Vector3.Angle(_placement.Placement.Rotation * Vector3.up, table.Normal), Is.LessThan(0.1f));

            // Solutions are surface-relative, so every stage solves on a tilted frame too.
            _game.Primary();
            for (int stage = 0; stage < _game.Flow.StageCount; stage++)
            {
                _puzzle.DebugSetSolution();
                yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, $"solve stage {stage + 1} on slope");
                Assert.That(_puzzle.Error, Is.LessThan(_puzzle.Stage.tolerance));
                if (_game.Flow.IsLastStage) break;
                _game.Primary();
                yield return WaitUntil(() => _game.Flow.State == GameState.Playing, 5f, "transition");
            }
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TooSteep_OrTooSmall_OrTooFar_FallsBackToManualMarker()
        {
            yield return Report(Table(new Vector2(0.9f, 0.6f), slopeDegrees: 35f));
            Assert.AreEqual(PlacementStatus.Manual, _placement.Status, "35 degree slope");

            yield return Report(Table(new Vector2(0.18f, 0.18f)));
            Assert.AreEqual(PlacementStatus.Manual, _placement.Status, "tiny surface");

            yield return Report(Table(new Vector2(0.6f, 0.4f), ahead: 2.5f));
            Assert.AreEqual(PlacementStatus.Manual, _placement.Status, "table across the room");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator MissingSpatialData_ManualMarker_StillPlaysThrough()
        {
            _placement.BeginSearch();
            _placement.ReportCandidates(new List<SurfaceCandidate>());   // scan exists but has no surfaces
            Assert.AreEqual(PlacementStatus.Manual, _placement.Status, "empty room");

            _placement.BeginSearch();
            _placement.ReportUnavailable("Room data unavailable. Place the marker on your table.");   // permission denied
            Assert.AreEqual(PlacementStatus.Manual, _placement.Status);
            Assert.AreEqual(GameState.Setup, _game.Flow.State);
            yield return null;

            _game.Primary();   // "Confirm" on the marker
            Assert.AreEqual(PlacementStatus.Ready, _placement.Status);
            Assert.That(_game.Flow.IsInteractive, "manual placement starts the game");
            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "solve on manual placement");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator LateRoomData_IsIgnored_OnceThePlayerMovedTheMarker()
        {
            _placement.BeginSearch();
            _placement.ReportUnavailable("timeout");
            var handle = FindActiveHandle();
            Assume.That(handle, Is.Not.Null, "manual marker object");
            handle.transform.position += handle.transform.right * 0.1f;   // player drags the marker 10 cm
            yield return null;
            yield return null;
            var moved = _placement.Placement.Position;

            _placement.ReportCandidates(new List<SurfaceCandidate> { Table(new Vector2(0.8f, 0.5f)) });
            Assert.AreEqual(PlacementStatus.Manual, _placement.Status, "late MRUK result must not yank the puzzle");
            Assert.That(Vector3.Distance(moved, _placement.Placement.Position), Is.LessThan(1e-4f));
        }

        GameObject FindActiveHandle()
        {
            var field = typeof(SurfacePlacementController).GetField("_manualHandle",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (field?.GetValue(_placement) as Transform)?.gameObject;
        }

        // ---- Repeat completion / save ------------------------------------------------------------------

        [UnityTest, Timeout(60000)]
        public IEnumerator HoldingTheSolution_BloomsOnce_AndSavesOnce_RetryBloomsAgain()
        {
            int blooms = 0;
            _bloom.BloomStarted += () => blooms++;
            _game.Primary();

            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "first solve");
            for (int i = 0; i < 3; i++)
            {
                _puzzle.DebugSetSolution();   // keep presenting the answer (hand still holding it)
                yield return Wait(0.5f);
            }
            Assert.AreEqual(1, blooms, "one bloom per solve");
            string id = _game.CurrentStage.stageId;
            var data = JsonUtility.FromJson<ProgressData>(PlayerPrefs.GetString(ProgressKey, "{}"));
            Assert.AreEqual(1, data.completedStageIds.FindAll(s => s == id).Count, "saved once");

            _game.Retry();
            yield return null;
            Assert.That(_game.Flow.IsInteractive);
            Assert.False(_bloom.IsPlaying, "retry clears the bloom");
            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 4f, "solve after retry");
            yield return Wait(0.6f);
            Assert.AreEqual(2, blooms, "a retried stage blooms again");
            data = JsonUtility.FromJson<ProgressData>(PlayerPrefs.GetString(ProgressKey, "{}"));
            Assert.AreEqual(1, data.completedStageIds.FindAll(s => s == id).Count, "still saved once");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator SavedProgress_ResumesAtFirstIncompleteStage_AfterRelaunch()
        {
            var stages = _game.Stages;
            var seeded = new ProgressData { lastStageIndex = 1 };
            seeded.completedStageIds.Add(stages[0].stageId);
            seeded.completedStageIds.Add(stages[1].stageId);
            PlayerPrefs.SetString(ProgressKey, JsonUtility.ToJson(seeded));
            PlayerPrefs.Save();

            yield return LoadScene();   // relaunch
            Assert.AreEqual(2, _game.Flow.StageIndex);
            if (_placement.Status == PlacementStatus.Manual) _placement.ConfirmManual();
            _game.Primary();
            Assert.AreEqual(GameState.Playing, _game.Flow.State, "stage 3 is not the tutorial");
            Assert.AreEqual(stages[2].stageId, _puzzle.Stage.stageId);

            seeded.completedStageIds.AddRange(stages.Skip(2).Select(s => s.stageId));
            PlayerPrefs.SetString(ProgressKey, JsonUtility.ToJson(seeded));
            yield return LoadScene();
            Assert.AreEqual(0, _game.Flow.StageIndex, "everything complete: start over at stage 1");
        }

        // -------------------------------------------------------------------------------------------------

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
    }
}
