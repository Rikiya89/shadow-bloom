using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ShadowBloom.Tests
{
    public class RecoveryTests
    {
        GameObject _root;
        static void Field(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Recovery test");
            _root.SetActive(false); // configure components without starting the scene lifecycle
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void TrackingLoss_ClearsPartialHold_Recovers_AndRespectsExplicitPause()
        {
            var puzzle = _root.AddComponent<PuzzleController>();
            var game = _root.AddComponent<GameStateController>();
            var tracker = new SolveTracker(0.05f, 0.8f);
            var flow = new GameFlow(5);
            flow.ConfirmSurface();
            Field(puzzle, "_tracker", tracker);
            Field(game, "_puzzle", puzzle);
            Field(game, "<Flow>k__BackingField", flow);
            puzzle.InputEnabled = true;
            tracker.Tick(0f, 0.4f);
            Assert.That(puzzle.HoldProgress, Is.GreaterThan(0));
            game.SetInputStatus(false, false);
            Assert.That(puzzle.InputEnabled, Is.False);
            Assert.That(puzzle.HoldProgress, Is.Zero);
            game.SetInputStatus(true, false);
            Assert.That(puzzle.InputEnabled);
            Assert.That(puzzle.HoldProgress, Is.Zero);
            flow.Pause();
            game.SetInputStatus(false, false);
            game.SetInputStatus(true, false);
            Assert.That(puzzle.InputEnabled, Is.False);
            Assert.That(flow.State, Is.EqualTo(GameState.Paused));
            flow.Resume();
            game.SetInputStatus(false, false);
            game.SetInputStatus(true, false);
            Assert.That(puzzle.InputEnabled);
        }

        [Test]
        public void MissingRoom_FallsBack_AndLateFailureDoesNotMoveManualPlacement()
        {
            var placement = _root.AddComponent<SurfacePlacementController>();
            var frame = new GameObject("Frame").transform;
            frame.SetParent(_root.transform);
            var head = new GameObject("Head").transform;
            head.SetParent(_root.transform);
            head.position = new Vector3(0, 1.2f, 0);
            var marker = new GameObject("Marker").transform;
            marker.SetParent(_root.transform);
            Field(placement, "_surfaceFrame", frame);
            Field(placement, "_head", head);
            Field(placement, "_manualHandle", marker);
            placement.BeginSearch();
            placement.ReportUnavailable("No room data");
            Assert.That(placement.Status, Is.EqualTo(PlacementStatus.Manual));
            marker.position = new Vector3(0.1f, 0.7f, 0.4f);
            var selected = marker.position;
            placement.ReportUnavailable("Late failure");
            Assert.That(marker.position, Is.EqualTo(selected));
            placement.ConfirmManual();
            Assert.That(placement.Status, Is.EqualTo(PlacementStatus.Ready));
            Assert.That(frame.position, Is.EqualTo(selected));
            placement.ConfirmManual();
            Assert.That(frame.position, Is.EqualTo(selected));
            placement.BeginManual();
            Assert.That(placement.Status, Is.EqualTo(PlacementStatus.Manual));
        }
    }
}
