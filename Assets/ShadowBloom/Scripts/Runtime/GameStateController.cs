using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShadowBloom
{
    public sealed class PlayerPrefsProgressStore : IProgressStore
    {
        const string Key = "shadowbloom.progress.v1";
        public string Load() => PlayerPrefs.GetString(Key, "");
        public void Save(string json)
        {
            PlayerPrefs.SetString(Key, json);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Top-level orchestration: binds the pure <see cref="GameFlow"/> state machine to the puzzle, placement,
    /// bloom, UI, audio and save systems. Every input path (poke UI, desktop keys) funnels into the four
    /// commands below, and every command is a no-op when invalid, so no input sequence can soft-lock the game.
    /// </summary>
    public sealed class GameStateController : MonoBehaviour
    {
        [SerializeField] PuzzleStage[] _stages;
        [SerializeField] PuzzleController _puzzle;
        [SerializeField] SurfacePlacementController _placement;
        [SerializeField] BloomEffectController _bloom;
        [SerializeField] WorldSpaceUIController _ui;
        [SerializeField] FeedbackAudio _audio;
        [Tooltip("Table garden (child of SurfaceFrame). Created at runtime if left empty.")]
        [SerializeField] GardenController _garden;
        [SerializeField] float _transitionSeconds = 0.7f;
        [Tooltip("Hand loss shorter than this is treated as a tracking blip and not announced.")]
        [SerializeField] float _handLossGrace = 1.2f;
        [Tooltip("Seconds without touching the puzzle before the ghost hint demonstrates the motion again.")]
        [SerializeField] float _idleHintSeconds = 15f;
        [Tooltip("Safety net: if the bloom never reports Finished, the Solved panel still appears this long after it should have.")]
        [SerializeField] float _solvedRevealFallback = 1.5f;

        public GameFlow Flow { get; private set; }
        public PuzzleStage CurrentStage => _active[Flow.StageIndex];
        /// <summary>The stages of the running flow: the campaign, or today's single Daily Shadow.</summary>
        public PuzzleStage[] Stages => _active;
        public bool IsDaily => _dailyMode;
        public bool DailyUnlocked => _save.IsCampaignComplete(_campaignIds);
        public ProgressData Progress => _save.Data;
        public GardenController Garden => _garden;
        /// <summary>Local date source for the Daily Shadow (tests inject a fixed date).</summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

        ProgressSaveSystem _save;
        PuzzleStage[] _active;
        List<string> _campaignIds;
        bool _dailyMode;
        int _dailyDay;
        float _transitionTimer;
        bool _handsTracked = true;
        bool _controllersActive;
        float _handsLostAt = -1f;
        bool _stageLoaded;
        float _idleTime;
        bool _idleGhost;
        // UI presentation only (no effect on flow): the Solved panel waits for the bloom; stage 1 coaching.
        bool _solvedRevealed = true;
        float _solvedWait;
        bool _stageGrabbed;
        int _coachStep;
        // How-to-play card on the panel (-1 = none). The stage is running behind it but takes no input.
        int _howToPlayCard = -1;
        // Index of the last card in the current run: first launch shows FirstRunCardCount, the pause panel shows all.
        int _howToPlayLast = UiCopy.HowToPlayCount - 1;

        bool ShowingHowToPlay => _howToPlayCard >= 0;

        // First-minute timing (log only): each milestone fires once per app run. Read with: adb logcat -s Unity | grep first-minute
        readonly System.Collections.Generic.HashSet<string> _milestones = new System.Collections.Generic.HashSet<string>();
        float _sessionStart = -1f;

        void Milestone(string name)
        {
            if (name == "start" && _sessionStart < 0f) _sessionStart = Time.unscaledTime;
            if (_sessionStart < 0f || !_milestones.Add(name)) return;
            Debug.Log($"[ShadowBloom] first-minute {name} t={Time.unscaledTime - _sessionStart:F1}s");
        }

        void Awake()
        {
            if (_stages == null || _stages.Length == 0 || _stages.Any(s => s == null))
            {
                Debug.LogWarning("[ShadowBloom] Stage list missing; using built-in default stages.");
                _stages = DefaultStages.CreateAll();
            }

            _save = new ProgressSaveSystem(new PlayerPrefsProgressStore());
            _active = _stages;
            _campaignIds = _stages.Select(s => s.stageId).ToList();
            BindFlow(CampaignFlow(_save.FirstIncomplete(_campaignIds)));
            _audio.SetAnchor(_puzzle.Pivot);

            if (_garden == null)
            {
                _garden = new GameObject("Garden").AddComponent<GardenController>();
                _garden.transform.SetParent(_puzzle.transform, false);
            }
            _garden.Init(_bloom);
        }

        void OnEnable()
        {
            _placement.Changed += OnPlacementChanged;
            _puzzle.Solved += OnSolved;
            _puzzle.GrabChanged += OnGrabChanged;
            _bloom.Finished += OnBloomFinished;
            _bloom.BloomStarted += OnBloomStarted;
            _puzzle.HoverChanged += OnHoverChanged;
            _ui.SoundPressed += ToggleSound;
            _ui.MotionPressed += ToggleReducedMotion;
            GameSettings.Changed += ApplySettings;
            ApplySettings();
            _ui.PrimaryPressed += Primary;
            _ui.RetryPressed += Retry;
            _ui.PausePressed += TogglePause;
            _ui.RepositionPressed += Reposition;
            _ui.RestartPressed += RestartExperience;
            _ui.ButtonPressed += _audio.UiTap;
            _ui.DailyPressed += StartDaily;
            _ui.AssistPressed += ToggleAssist;
            _ui.HowToPlayPressed += ShowHowToPlay;
        }

        void OnDisable()
        {
            _placement.Changed -= OnPlacementChanged;
            _puzzle.Solved -= OnSolved;
            _puzzle.GrabChanged -= OnGrabChanged;
            _bloom.Finished -= OnBloomFinished;
            _bloom.BloomStarted -= OnBloomStarted;
            _puzzle.HoverChanged -= OnHoverChanged;
            _ui.SoundPressed -= ToggleSound;
            _ui.MotionPressed -= ToggleReducedMotion;
            GameSettings.Changed -= ApplySettings;
            _ui.PrimaryPressed -= Primary;
            _ui.RetryPressed -= Retry;
            _ui.PausePressed -= TogglePause;
            _ui.RepositionPressed -= Reposition;
            _ui.RestartPressed -= RestartExperience;
            _ui.ButtonPressed -= _audio.UiTap;
            _ui.DailyPressed -= StartDaily;
            _ui.AssistPressed -= ToggleAssist;
            _ui.HowToPlayPressed -= ShowHowToPlay;
        }

        void Start()
        {
            // Loaded so placement can size it, but hidden: nothing appears on the table until the player confirms it.
            LoadStage(Flow.StageIndex);
            _puzzle.SetVisible(false);
            OnPlacementChanged();
            _garden.SetVisible(GardenVisible(Flow.State), instant: true);
        }

        GameFlow CampaignFlow(int startStage) => new GameFlow(_stages.Length, i => _stages[i].isTutorial, startStage);

        void BindFlow(GameFlow flow)
        {
            if (Flow != null)
            {
                Flow.StateChanged -= OnStateChanged;
                Flow.StageStarted -= OnStageStarted;
            }
            Flow = flow;
            Flow.StateChanged += OnStateChanged;
            Flow.StageStarted += OnStageStarted;
        }

        // ---- Daily Shadow ---------------------------------------------------------------------------

        /// <summary>
        /// Today's generated puzzle as a one-stage flow. Offered once the campaign is complete, from the completion
        /// screen or from setup (like Start, it confirms a manually placed marker). Solving it advances the streak and plants a garden bloom.
        /// </summary>
        public void StartDaily()
        {
            if (_dailyMode || !DailyUnlocked) return;
            bool fromSetup = Flow.State == GameState.Setup && _placement.Status != PlacementStatus.Searching;
            if (!fromSetup && Flow.State != GameState.Completed) return;
            if (fromSetup && _placement.Status == PlacementStatus.Manual) _placement.ConfirmManual();

            _dailyDay = DailyShadow.DayNumber(Clock());
            _active = new[] { DailyShadow.Create(_dailyDay) };
            _dailyMode = true;
            BindFlow(new GameFlow(1));
            Flow.ConfirmSurface();
        }

        /// <summary>Back to the campaign from stage 1 (earned progress, streak and garden are kept).</summary>
        void ExitDaily()
        {
            if (!_dailyMode) return;
            var daily = _active[0];
            _dailyMode = false;
            _active = _stages;
            BindFlow(CampaignFlow(0));
            Flow.ConfirmSurface();
            if (daily != null) Destroy(daily);
        }

        /// <param name="highlightDay">The day whose flower stands taller: the Daily just solved, else the local date.</param>
        void RefreshGarden(int highlightDay) => _garden.SetEntries(
            GardenPlan.Compose(_stages, _save.IsCompleted, _save.Data.gardenDays, highlightDay, DailyShadow.GardenCapacity),
            _puzzle.PuzzleRadius, _placement.Placement.GardenBand);

        void RefreshGarden() => RefreshGarden(DailyShadow.DayNumber(Clock()));

        bool StageIsFinale => Flow.IsLastStage || CurrentStage.completion == CompletionBehavior.FinalBloom;

        // Not during Setup: like the puzzle, the garden appears only once the table is confirmed.
        static bool GardenOwnsTable(GameState s) => s == GameState.Tutorial || s == GameState.Playing;

        /// <summary>The garden rests on the table only while nothing else (bloom, completion) is using it.</summary>
        bool GardenVisible(GameState s) => GardenOwnsTable(s) || (s == GameState.Paused && GardenOwnsTable(Flow.ResumeState));

        // ---- Commands -------------------------------------------------------------------------------

        public void Primary()
        {
            switch (Flow.State)
            {
                case GameState.Setup:
                    if (_placement.Status == PlacementStatus.Searching) return;
                    if (_placement.Status == PlacementStatus.Manual) _placement.ConfirmManual();
                    Milestone("start");
                    Flow.ConfirmSurface();
                    break;
                case GameState.Tutorial:
                case GameState.Playing:
                    if (ShowingHowToPlay) NextHowToPlayCard();
                    break;
                case GameState.Solved:
                    Flow.Next();
                    break;
                case GameState.Paused:
                    Flow.Resume();
                    break;
                case GameState.Completed:
                    if (_dailyMode) ExitDaily();
                    else Flow.StartAt(0);
                    break;
            }
        }

        /// <summary>Also offered on the pause panel: resume first, then restart the stage.</summary>
        public void Retry()
        {
            if (Flow.State == GameState.Paused && Flow.ResumeState != GameState.Completed) Flow.Resume();
            Flow.Retry();
        }

        /// <summary>Pause panel: replay from stage 1. Earned progress and preferences are kept.</summary>
        public void RestartExperience()
        {
            if (_dailyMode) ExitDaily();
            else Flow.RestartExperience();
        }

        /// <summary>Pause panel: back to the stage, with the how-to-play cards in front of it.</summary>
        public void ShowHowToPlay()
        {
            if (Flow.State != GameState.Paused || _dailyMode) return;
            if (Flow.ResumeState != GameState.Tutorial && Flow.ResumeState != GameState.Playing) return;
            _howToPlayCard = 0;
            _howToPlayLast = UiCopy.HowToPlayCount - 1;
            Flow.Resume();
        }

        void NextHowToPlayCard()
        {
            Debug.Log($"[ShadowBloom] how-to-play card {_howToPlayCard + 1} of {_howToPlayLast + 1} done");
            if (++_howToPlayCard <= _howToPlayLast)
            {
                RefreshUI();
                return;
            }
            _howToPlayCard = -1;
            Milestone("card-done");
            GameSettings.HowToPlaySeen = true;
            _idleTime = 0f;
            UpdateInputGate();
            RefreshUI();
        }

        public void ToggleSound() => GameSettings.Muted = !GameSettings.Muted;
        public void ToggleReducedMotion() => GameSettings.ReducedMotion = !GameSettings.ReducedMotion;
        public void ToggleAssist() => GameSettings.Assist = !GameSettings.Assist;

        void ApplySettings()
        {
            _audio.Muted = GameSettings.Muted;
            _bloom.ReducedMotion = GameSettings.ReducedMotion;
            _puzzle.ReducedMotion = GameSettings.ReducedMotion;
            bool assist = GameSettings.Assist;
            _puzzle.Leniency = assist ? GameSettings.AssistLeniency : 1f;
            _puzzle.HoldScale = assist ? GameSettings.AssistHoldScale : 1f;
            _ui.SetSettingsLabels(GameSettings.Muted, GameSettings.ReducedMotion, assist);
        }

        public void TogglePause()
        {
            if (Flow.State == GameState.Paused) Flow.Resume();
            else Flow.Pause();
        }

        public void Reposition()
        {
            if (Flow.State == GameState.Setup)
            {
                if (_placement.Status == PlacementStatus.Ready) _placement.BeginManual();
                RefreshUI();
                return;
            }
            if (Flow.RequestSetup()) _placement.BeginManual();
        }

        /// <summary>Tracking loss suspends manipulation/matching; recovery never requires a controller.</summary>
        public void SetInputStatus(bool handsTracked, bool controllersActive)
        {
            if (handsTracked == _handsTracked && controllersActive == _controllersActive) return;
            if (!handsTracked && _handsTracked) _handsLostAt = Time.time;
            _handsTracked = handsTracked;
            _controllersActive = controllersActive;
            if (!handsTracked && !controllersActive) _puzzle.NotifyGrab(false);
            UpdateInputGate();
        }

        // ---- Events ---------------------------------------------------------------------------------

        void UpdateInputGate()
        {
            bool enabled = Flow.IsInteractive && !ShowingHowToPlay && (_handsTracked || _controllersActive);
            if (enabled != _puzzle.InputEnabled)
                Debug.Log($"[ShadowBloom] puzzle input {(enabled ? "on" : "off")} (state {Flow.State}, cards {ShowingHowToPlay}, " +
                          $"hands {_handsTracked}, controllers {_controllersActive})");
            _puzzle.InputEnabled = enabled;
            if (enabled) Milestone("input-on");
        }

        void OnStateChanged(GameState prev, GameState next)
        {
            Debug.Log($"[ShadowBloom] state {prev} -> {next}");
            if (next == GameState.Solved) Milestone("solved");
            UpdateInputGate();
            bool paused = next == GameState.Paused;
            _puzzle.PresentationPaused = paused;
            _audio.Paused = paused;
            bool resuming = prev == GameState.Paused && next == Flow.ResumeState;
            if (!paused && !resuming)
            {
                _puzzle.SetTutorialGhost(next == GameState.Tutorial);
                _idleTime = 0f;
                _idleGhost = false;
            }
            // The bloom clock freezes while paused, so its sequence (and the finale's hand-off) is never missed.
            _bloom.Paused = next == GameState.Paused;
            _garden.SetVisible(GardenVisible(next));

            // Setup again (pause -> Move): the puzzle hides while the marker is moved; Confirm reloads and shows it.
            if (next == GameState.Setup)
            {
                _puzzle.RestoreCurve();
                _puzzle.SetVisible(false);
            }
            // Resuming mid-transition continues it; re-entering would restart the fade, outro, timer and sweep.
            if (next == GameState.Transition && prev != GameState.Paused)
            {
                // Continuity: the bloom fades and the solved stage draws itself off; the next one draws on.
                _transitionTimer = _transitionSeconds;
                _bloom.FadeOut(_transitionSeconds * 0.7f);
                _puzzle.BeginOutro(_transitionSeconds * 0.8f);
                _audio.Transition();
            }
            // Pausing on the completion screen (system menu, headset off) must keep the final sculpture.
            if (next == GameState.Completed && prev != GameState.Paused) _audio.Complete();
            if (prev == GameState.Completed && next != GameState.Paused) _bloom.Stop();
            RefreshUI();
        }

        void OnStageStarted(int index)
        {
            _bloom.Stop();
            LoadStage(index);
            // Build the bloom now (seeded with this stage's outline), so the solve frame does no mesh work.
            _bloom.Prepare(CurrentStage.bloomPetals, CurrentStage.bloomLayers, _puzzle.PuzzleRadius, StageIsFinale,
                CurrentStage, _puzzle.TargetShape);
            UpdateInputGate();
            _puzzle.SetTutorialGhost(Flow.State == GameState.Tutorial);
            // A (re)started stage always starts a fresh idle period. Retry keeps the state unchanged, and a Retry from
            // the pause panel resumes first, so neither path passes the reset in OnStateChanged.
            _idleTime = 0f;
            _idleGhost = false;
            _audio.ResetAlignment();
            if (!_dailyMode) _save.SetLastStage(index);
            _stageGrabbed = false;
            _coachStep = UiCopy.StepGrab;
            _solvedRevealed = true;
            // First time on stage 1: explain the goal before the first grab (again on retry until read to the end).
            _howToPlayCard = !_dailyMode && CurrentStage.isTutorial && !GameSettings.HowToPlaySeen ? 0 : -1;
            _howToPlayLast = UiCopy.FirstRunCardCount - 1;
            if (_howToPlayCard >= 0) Milestone("card-shown");
            UpdateInputGate();
            _ui.SetHintDimmed(false);
            RefreshUI();
        }

        void LoadStage(int index)
        {
            var p = _placement.Placement;
            float radius = p.PuzzleRadius > 0f ? p.PuzzleRadius : 0.12f;
            float height = p.PivotHeight > 0f ? p.PivotHeight : SurfacePlacementSolver.PivotHeightFor(radius);
            _puzzle.LoadStage(_active[index], radius, height);
            _puzzle.SetVisible(true);
            _stageLoaded = true;
        }

        void OnPlacementChanged()
        {
            var p = _placement.Placement;
            if (_stageLoaded && p.PuzzleRadius > 0f && (!Mathf.Approximately(p.PuzzleRadius, _puzzle.PuzzleRadius) ||
                                                        !Mathf.Approximately(p.PivotHeight, _puzzle.PivotHeight)))
                _puzzle.ApplyPlacement(p.PuzzleRadius, p.PivotHeight);
            _ui.PlaceBeside(_puzzle.transform, _puzzle.PuzzleRadius, p.PivotHeight, _placement.Head.position);
            RefreshGarden();
            if (Flow.State == GameState.Setup) RefreshUI();
        }

        void OnSolved()
        {
            // The Solved panel stays out of the bloom's way until it settles (RevealSolved).
            _solvedRevealed = false;
            _solvedWait = 0f;
            if (!Flow.MarkSolved()) return;
            if (_dailyMode)
            {
                _save.RecordDaily(_dailyDay);
                // Hidden until the table is free again. Highlight the day actually recorded, even past midnight.
                RefreshGarden(_dailyDay);
            }
            else
            {
                _save.MarkCompleted(CurrentStage.stageId, Flow.StageIndex);
                // The stage's flower joins the garden; like a daily's, it shows once the table is free again.
                RefreshGarden();
            }
            _audio.Lock();   // the bloom sound follows on the bloom clock (OnBloomStarted)
            _bloom.Play(CurrentStage.bloomPetals, CurrentStage.bloomLayers, _puzzle.PuzzleRadius, StageIsFinale,
                CurrentStage, _puzzle.TargetShape, _puzzle.SolvedTargetDisplayColor(CurrentStage));
        }

        void OnBloomStarted()
        {
            _audio.Bloom(_bloom.Finale);
            _puzzle.ExpandPlate(_bloom.Extent);
            // The actual shadow seeds the bloom mesh; the puzzle recedes so it cannot compete with the petals.
            // Only for a real solve: a cosmetic Editor preview must not hide the puzzle that is still being played.
            if (Flow.State == GameState.Solved) _puzzle.DissolveCurve(_bloom.MainDuration * 0.45f);
        }

        void OnHoverChanged(bool hovered)
        {
            if (hovered && Flow.IsInteractive && !_puzzle.IsGrabbed) _audio.Hover();
        }

        void OnBloomFinished()
        {
            if (Flow.State == GameState.Solved) Milestone("bloom-finished");
            // The last stage's finale goes straight to the completion screen; other stages wait for "Next".
            if (Flow.State == GameState.Solved && Flow.IsLastStage) Flow.Next();
            else RevealSolved();
        }

        void RevealSolved()
        {
            if (_solvedRevealed) return;
            _solvedRevealed = true;
            if (Flow.State == GameState.Solved) RefreshUI();
        }

        void OnGrabChanged(bool grabbed)
        {
            // The puzzle is hidden during setup and takes no input behind the cards; a grab there means nothing.
            if (Flow.State == GameState.Setup || ShowingHowToPlay) return;
            if (grabbed) _audio.Grab(); else _audio.Release();
            if (grabbed) { _stageGrabbed = true; Milestone("first-grab"); }
            _idleTime = 0f;
            if (grabbed && _idleGhost)
            {
                _idleGhost = false;
                _puzzle.SetTutorialGhost(false);
            }
            if (grabbed && Flow.State == GameState.Tutorial) Flow.DismissTutorial();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) Flow?.Pause();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) Flow?.Pause();
        }

        // ---- Frame ----------------------------------------------------------------------------------

        void Update()
        {
            if (Flow.State == GameState.Transition)
            {
                _transitionTimer -= Time.deltaTime;
                if (_transitionTimer <= 0f) Flow.FinishTransition();
            }

            if (Flow.IsInteractive && _puzzle.InputEnabled)
            {
                _ui.SetAlignment(_puzzle.Alignment, _puzzle.IsWithinTolerance, _puzzle.HoldProgress);
                _audio.Alignment(_puzzle.Alignment);
            }

            if (Flow.IsInteractive && !ShowingHowToPlay)
            {
                if (_puzzle.IsWithinTolerance) Milestone("match");
                if (CurrentStage.isTutorial)
                {
                    // One instruction at a time; each gives way once the player has done it.
                    int step = UiCopy.NextTutorialStep(_coachStep, _stageGrabbed || _puzzle.IsGrabbed, _puzzle.Alignment, _puzzle.IsWithinTolerance);
                    if (step != _coachStep)
                    {
                        _coachStep = step;
                        _ui.SetHint(UiCopy.TutorialText(step));
                    }
                }
                else
                {
                    // The stage hint quiets once the player is working; it returns with the idle ghost.
                    _ui.SetHintDimmed(_stageGrabbed && !_idleGhost);
                }
            }

            // Safety net for the Solved reveal: counts only unpaused time in Solved.
            if (Flow.State == GameState.Solved && !_solvedRevealed)
            {
                _solvedWait += Time.deltaTime;
                if (_solvedWait > _bloom.TotalDuration + _solvedRevealFallback) RevealSolved();
            }

            // Stuck hint: after a quiet spell the ghost shows the motion again, on any stage, until the next grab.
            if (Flow.State == GameState.Playing && !_puzzle.IsGrabbed && !_idleGhost && !ShowingHowToPlay)
            {
                _idleTime += Time.deltaTime;
                if (_idleTime >= _idleHintSeconds)
                {
                    _idleGhost = true;
                    _puzzle.SetTutorialGhost(true);
                }
            }

            _ui.SetStatus(InputStatusMessage());
        }

        string InputStatusMessage()
        {
            if (_handsTracked || _controllersActive) return null;
            if (Time.time - _handsLostAt < _handLossGrace) return null;
            return UiCopy.HandsLost;
        }

        void RefreshUI()
        {
            var stage = CurrentStage;
            int index = Flow.StageIndex, count = _active.Length;
            string eyebrow = _dailyMode ? UiCopy.DailyEyebrow(DailyShadow.DateOf(_dailyDay).DayOfWeek) : UiCopy.StageEyebrow(index, count);
            int pip = _dailyMode ? -1 : index;
            bool dailyOffer = !_dailyMode && DailyUnlocked;
            int today = DailyShadow.DayNumber(Clock());
            string dailyLabel = _save.SolvedToday(today) ? UiCopy.DailyReplay : UiCopy.DailyShadow;
            UiView v;
            switch (Flow.State)
            {
                case GameState.Setup:
                    var status = _placement.Status;
                    v = new UiView
                    {
                        Eyebrow = status switch
                        {
                            PlacementStatus.Ready => UiCopy.SetupFound,
                            PlacementStatus.Manual => UiCopy.SetupManual,
                            _ => UiCopy.SetupSearching
                        },
                        EyebrowTone = status == PlacementStatus.Ready ? UiTone.Success : UiTone.Accent,
                        EyebrowDot = true,
                        Title = UiCopy.GameTitle,
                        // While placing by hand the steps replace the tagline; the hint says why (no scan, Move...).
                        Subtitle = status == PlacementStatus.Manual ? UiCopy.SetupSteps : UiCopy.Tagline,
                        Hint = status != PlacementStatus.Ready ? UiCopy.PlacementMessage(_placement.Message)
                            : dailyOffer && !_save.SolvedToday(today) ? UiCopy.DailyReady : UiCopy.SetupBegin,
                        Daily = dailyOffer && status != PlacementStatus.Searching, DailyLabel = dailyLabel,
                        Primary = status switch
                        {
                            PlacementStatus.Ready => UiCopy.Start,
                            PlacementStatus.Manual => UiCopy.Confirm,
                            _ => null
                        },
                        Reposition = status == PlacementStatus.Ready,
                        Progress = -1
                    };
                    break;
                case GameState.Tutorial when ShowingHowToPlay:
                case GameState.Playing when ShowingHowToPlay:
                    int cards = _howToPlayLast + 1;
                    v = new UiView
                    {
                        Eyebrow = UiCopy.HowToPlayEyebrow, EyebrowTone = UiTone.Accent,
                        Progress = _howToPlayCard, ProgressCount = cards,
                        Title = UiCopy.HowToPlayTitle(_howToPlayCard),
                        Subtitle = UiCopy.HowToPlayBody(_howToPlayCard), SubtitleTone = UiTone.Neutral,
                        Primary = _howToPlayCard == cards - 1 ? UiCopy.HowToPlayDone : UiCopy.HowToPlayNext,
                        Pause = true
                    };
                    break;
                case GameState.Tutorial:
                case GameState.Playing:
                    v = new UiView
                    {
                        Eyebrow = eyebrow, Progress = pip, ProgressCount = count,
                        Title = UiCopy.StageName(stage),
                        Hint = stage.isTutorial ? UiCopy.TutorialText(_coachStep) : UiCopy.StageHint(stage),
                        // Tutorial and Playing share one layout, so the first grab (which ends the tutorial state)
                        // changes only the instruction line instead of re-revealing the panel mid-gesture.
                        Alignment = true,
                        Retry = true, Pause = true, Reposition = true
                    };
                    break;
                case GameState.Solved:
                    // Nothing competes with the bloom; the result and Next appear once it settles.
                    if (!_solvedRevealed) { v = UiView.HiddenView; break; }
                    v = new UiView
                    {
                        Eyebrow = eyebrow, Progress = pip, ProgressCount = count, ProgressCurrentDone = true,
                        Title = UiCopy.StageName(stage),
                        Subtitle = UiCopy.Aligned, SubtitleTone = UiTone.Success, Accent = UiTone.Success,
                        // The last stage also offers "Finish", so completion never depends on the finale's timing.
                        Primary = Flow.IsLastStage ? UiCopy.Finish : UiCopy.NextStage,
                        Retry = true
                    };
                    break;
                case GameState.Transition:
                    v = UiView.HiddenView;
                    break;
                case GameState.Paused:
                    bool fromEnd = Flow.ResumeState == GameState.Completed;
                    v = new UiView
                    {
                        Eyebrow = UiCopy.Paused, EyebrowTone = UiTone.Accent,
                        Title = fromEnd ? (_dailyMode ? UiCopy.DailyDoneTitle : UiCopy.Complete) : UiCopy.StageName(stage),
                        Progress = fromEnd ? -1 : pip, ProgressCount = count,
                        Primary = UiCopy.Resume,
                        Retry = !fromEnd, Reposition = !fromEnd, Settings = true, Restart = !fromEnd && !_dailyMode,
                        HowToPlay = !_dailyMode && (Flow.ResumeState == GameState.Tutorial || Flow.ResumeState == GameState.Playing)
                    };
                    break;
                case GameState.Completed when _dailyMode:
                    var d = _save.Data;
                    v = new UiView
                    {
                        Eyebrow = eyebrow, EyebrowTone = UiTone.Success,
                        Title = UiCopy.DailyDoneTitle, TitleTone = UiTone.Success, Accent = UiTone.Success,
                        Subtitle = UiCopy.DailyDoneSubtitle(d.dailyStreak, d.gardenDays.Count, DailyShadow.GardenCapacity),
                        Hint = UiCopy.DailyGardenHint,
                        Progress = -1,
                        Primary = UiCopy.BackToStages
                    };
                    break;
                case GameState.Completed:
                    v = new UiView
                    {
                        Eyebrow = UiCopy.CompleteEyebrow(count), EyebrowTone = UiTone.Success,
                        Title = UiCopy.Complete, TitleTone = UiTone.Success, Accent = UiTone.Success,
                        Progress = count, ProgressCount = count, ProgressComplete = true,
                        Hint = dailyOffer && !_save.SolvedToday(today) ? UiCopy.DailyReady : null,
                        Primary = UiCopy.PlayAgain,
                        Daily = dailyOffer, DailyLabel = dailyLabel
                    };
                    break;
                default:
                    v = UiView.HiddenView;
                    break;
            }
            v.PauseLabel = UiCopy.Pause;
            _ui.Show(v);
        }
    }
}
