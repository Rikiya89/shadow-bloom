using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShadowBloom.Tests
{
    /// <summary>
    /// Daily Shadow on the real scene (desktop path): unlock rule, one-stage daily run, streak and garden save,
    /// return to the campaign, the Assist toggle, and the FoV-aware panel placement.
    /// </summary>
    public class DailyPlayModeTests
    {
        const string ScenePath = "Assets/ShadowBloom/Scenes/ShadowBloom.unity";
        const string ProgressKey = "shadowbloom.progress.v1";
        static readonly DateTime Today = new DateTime(2026, 11, 18, 20, 0, 0);   // a Wednesday: the infinity

        string _savedProgress;

        HowToPlaySeenScope _howToPlay;
        bool _hadProgress;
        GameStateController _game;
        PuzzleController _puzzle;
        SurfacePlacementController _placement;

        IEnumerator Load(bool campaignDone, string gardenJson = null)
        {
            _hadProgress = PlayerPrefs.HasKey(ProgressKey);
            _savedProgress = PlayerPrefs.GetString(ProgressKey, "");
            PlayerPrefs.DeleteKey(ProgressKey);
            _howToPlay = new HowToPlaySeenScope();
            if (campaignDone)
            {
                var ids = DefaultStages.CreateAll().Select(s => s.stageId);
                PlayerPrefs.SetString(ProgressKey, "{\"version\":3,\"completedStageIds\":[\"" + string.Join("\",\"", ids) + "\"]" +
                                                   (gardenJson ?? "") + "}");
            }
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
            _game.Clock = () => Today;
            yield return WaitUntil(() => _placement.Status != PlacementStatus.Searching, 12f, "placement");
            yield return Wait(0.6f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadProgress) PlayerPrefs.SetString(ProgressKey, _savedProgress);
            else PlayerPrefs.DeleteKey(ProgressKey);
            _howToPlay?.Restore();
            PlayerPrefs.Save();
        }

        [UnityTest]
        public IEnumerator Daily_IsLocked_UntilTheCampaignIsComplete()
        {
            yield return Load(campaignDone: false);
            Assert.That(_game.DailyUnlocked, Is.False);
            Assert.That(VisibleButton("Daily"), Is.Null, "no daily offer on a first launch");
            _game.StartDaily();
            Assert.That(_game.IsDaily, Is.False);
        }

        [UnityTest]
        public IEnumerator Daily_FullRun_GrowsStreakAndGarden_ThenReturnsToStages()
        {
            yield return Load(campaignDone: true);
            Assert.That(_placement.Status, Is.Not.EqualTo(PlacementStatus.Searching));
            Assert.That(_game.Garden.ActiveCount, Is.EqualTo(7), "the campaign's flowers (all but the finale)");

            yield return PressVisible("Daily");
            int day = DailyShadow.DayNumber(Today);
            Assert.That(_game.IsDaily);
            Assert.That(_game.CurrentStage.stageId, Is.EqualTo(DailyShadow.IdPrefix + day));
            Assert.That(_game.CurrentStage.curve.kind, Is.EqualTo(CurveKind.Viviani));
            Assert.That(_game.Flow.State, Is.EqualTo(GameState.Playing));
            Assert.That(_puzzle.IsSolved, Is.False);

            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Completed, 12f, "daily completion");
            Assert.That(_game.Progress.dailyStreak, Is.EqualTo(1));
            Assert.That(_game.Progress.gardenDays, Is.EqualTo(new[] { day }));
            Assert.That(_game.Garden.ActiveCount, Is.EqualTo(7), "today's flower takes the oldest campaign flower's slot");
            Assert.That(_game.Garden.TodayIndex, Is.EqualTo(6), "today's flower is the newest and the highlighted one");
            Assert.That(_game.Garden.Visible, Is.False, "the finale owns the table");
            var progress = PlayerPrefs.GetString(ProgressKey, "");
            Assert.That(progress, Does.Contain("\"dailyStreak\":1"), "persisted");

            yield return Wait(0.6f);
            TestCapture.Head("daily_completed");
            var ui = UnityEngine.Object.FindAnyObjectByType<WorldSpaceUIController>();
            TestCapture.Panel((RectTransform)ui.transform, "daily_completed_panel");
            Assert.That(ui.CurrentView.Title, Is.EqualTo(UiCopy.DailyDoneTitle));
            _game.Primary();   // "Stages"
            yield return null;
            Assert.That(_game.IsDaily, Is.False);
            Assert.That(_game.Flow.StageIndex, Is.Zero);
            Assert.That(_game.Flow.State, Is.EqualTo(GameState.Tutorial));
            Assert.That(_game.Stages.Length, Is.EqualTo(DefaultStages.CreateAll().Length), "back on the campaign list");
            Assert.That(_game.Garden.Visible, "the garden returns with the next puzzle");
            yield return Wait(0.6f);
            TestCapture.Head("daily_return_to_garden");
        }

        [UnityTest]
        public IEnumerator SynthesisedSounds_AreAudible_Finite_AndNeverClip()
        {
            yield return Load(campaignDone: false);
            var audio = UnityEngine.Object.FindAnyObjectByType<FeedbackAudio>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var clips = typeof(FeedbackAudio).GetFields(flags).Where(f => f.FieldType == typeof(AudioClip))
                .Select(f => (AudioClip)f.GetValue(audio))
                .Concat((AudioClip[])typeof(FeedbackAudio).GetField("_ticks", flags).GetValue(audio)).ToArray();
            Assert.That(clips.Length, Is.GreaterThanOrEqualTo(12));
            foreach (var clip in clips)
            {
                var d = new float[clip.samples];
                clip.GetData(d, 0);
                float peak = 0f;
                foreach (float x in d)
                {
                    Assert.That(float.IsNaN(x) || float.IsInfinity(x), Is.False, clip.name);
                    peak = Mathf.Max(peak, Mathf.Abs(x));
                }
                TestContext.WriteLine($"{clip.name}: {clip.length:F2} s, peak {peak:F2}, end {Mathf.Abs(d[d.Length - 1]):F4}");
                Assert.That(peak, Is.InRange(0.05f, 1f), clip.name + " is audible and does not clip");
                Assert.That(Mathf.Abs(d[d.Length - 1]), Is.LessThan(0.01f), clip.name + " ends in silence (no click)");
            }
        }

        [UnityTest]
        public IEnumerator Campaign_SolvedStage_PlantsItsFlower_ForTheNextStage()
        {
            yield return Load(campaignDone: false);
            _game.Primary();
            yield return null;
            Assert.That(_game.Garden.ActiveCount, Is.Zero, "nothing solved yet");

            _puzzle.DebugSetSolution();
            yield return WaitUntil(() => _game.Flow.State == GameState.Solved, 12f, "stage 1 solve");
            Assert.That(_game.Garden.ActiveCount, Is.EqualTo(1));
            Assert.That(_game.Garden.Visible, Is.False, "the bloom owns the table");

            float end = Time.realtimeSinceStartup + 20f;
            while (_game.Flow.StageIndex == 0 || !_game.Flow.IsInteractive)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for stage 2");
                if (_game.Flow.State == GameState.Solved) _game.Primary();   // "Next stage", once the bloom settles
                yield return Wait(0.5f);
            }
            Assert.That(_game.Garden.Visible, "the flower rests beside the next puzzle");
            Assert.That(_game.Garden.ActiveCount, Is.EqualTo(1));
            Assert.That(_game.Garden.TodayIndex, Is.Zero, "the newest campaign flower carries the heart");
            yield return Wait(0.6f);
            TestCapture.Head("garden_campaign_stage2");
        }

        [UnityTest]
        public IEnumerator Assist_TogglesFromPausePanel_LoosensTheStage_AndNeverTouchesProgress()
        {
            bool assist = GameSettings.Assist;
            try
            {
                GameSettings.Assist = false;
                yield return Load(campaignDone: false);
                _game.Primary();
                _game.TogglePause();
                yield return Wait(0.6f);
                string progress = PlayerPrefs.GetString(ProgressKey, "");
                float baseTol = _game.CurrentStage.tolerance;
                yield return PressVisible("Assist");
                Assert.That(GameSettings.Assist);
                Assert.That(_puzzle.Tolerance, Is.EqualTo(baseTol * GameSettings.AssistLeniency).Within(1e-5f));
                Assert.AreEqual(progress, PlayerPrefs.GetString(ProgressKey, ""), "settings never touch progress");
            }
            finally
            {
                GameSettings.Assist = assist;
            }
        }

        [UnityTest]
        public IEnumerator Panel_FarEdge_StaysInsideTheComfortableFoV()
        {
            yield return Load(campaignDone: false);
            var ui = UnityEngine.Object.FindAnyObjectByType<WorldSpaceUIController>();
            var frame = _puzzle.transform;
            var head = _placement.Head.position;
            var pivot = frame.TransformPoint(new Vector3(0f, _puzzle.PivotHeight, 0f));
            var centre = ui.transform.position + frame.up * WorldSpaceUIController.PanelCentreAbove;
            float halfWidth = UiTheme.Default.panelWidth * 0.0005f;
            float edge = PanelPlacement.EdgeAngle(head, pivot, centre, halfWidth);
            TestContext.WriteLine($"swing {ui.LastSwingDegrees} deg, far edge {edge:F1} deg");
            Assert.That(edge, Is.LessThanOrEqualTo(ui.EdgeDegrees + 0.5f));
        }

        /// <summary>Visual QA: a full week's garden around stage 1, and the FoV-swung panel. Run with SB_CAPTURE_DIR.</summary>
        [UnityTest]
        public IEnumerator FullGarden_RendersBehindTheShadow()
        {
            int today = DailyShadow.DayNumber(Today);
            var days = Enumerable.Range(today - 6, 7).ToArray();
            yield return Load(campaignDone: true, ",\"dailyStreak\":7,\"bestStreak\":7,\"lastDailyDay\":" + today +
                                                  ",\"gardenDays\":[" + string.Join(",", days) + "]");
            // Clock is injected after scene Start; refresh the garden with the same test date.
            _game.Garden.SetEntries(days, _puzzle.PuzzleRadius, today);
            Assert.That(_game.Garden.ActiveCount, Is.EqualTo(7));
            Assert.That(_game.Garden.TodayIndex, Is.EqualTo(6));
            var newest = _game.Garden.transform.Find("GardenBloom6");
            float todayHeight = newest.localPosition.y;
            Assert.That(newest.localScale, Is.EqualTo(Vector3.one), "the on-table footprint never grows");
            TestCapture.Head("garden_setup");
            _game.Primary();
            yield return WaitUntil(() => _game.Flow.IsInteractive, 5f, "stage 1");
            yield return Wait(1.2f);
            Assert.That(_game.Garden.Visible);
            foreach (var r in _game.Garden.GetComponentsInChildren<MeshRenderer>())
            {
                Assert.That(r.enabled && r.sharedMaterial != null, r.name);
                Assert.That(r.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0), r.name);
            }
            TestCapture.Head("garden_playing");
            string saved = PlayerPrefs.GetString(ProgressKey);
            _game.Garden.SetEntries(days, _puzzle.PuzzleRadius, today + 1);
            Assert.That(_game.Garden.TodayIndex, Is.EqualTo(-1), "a prior day's flower must not be labelled today");
            Assert.That(newest.localPosition.y, Is.LessThan(todayHeight), "today is distinguished by height, not colour alone");
            Assert.That(PlayerPrefs.GetString(ProgressKey), Is.EqualTo(saved), "highlight changes never write progress");
        }

        static UnityEngine.UI.Button VisibleButton(string name) =>
            UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Exclude)
                .FirstOrDefault(b => b.name == name && b.gameObject.activeInHierarchy);

        static IEnumerator PressVisible(string name)
        {
            yield return Wait(0.45f);   // UI cooldown
            var button = VisibleButton(name);
            Assert.NotNull(button, $"visible button '{name}'");
            button.onClick.Invoke();
            yield return null;
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
    }
}
