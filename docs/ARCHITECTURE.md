# Technical overview


```
Assets/ShadowBloom/
  Scripts/Core/      ShadowBloom.Core: pure C#, no Meta dependency, fully EditMode-tested
    CurveGenerator      deterministic parametric curves (ring, rose, torus knot, orbit loops, bloom)
    GeometryProjection  world -> plane projection along a direction, into the plane's 2D basis
    ProjectionMatcher   reference chamfer error metric + SolveTracker (hold + hysteresis) + feedback curve
    SegmentGrid         uniform-grid index; FastShapeMatcher used at runtime
    StagePuzzle         target (from the known solution) vs current, for one stage
    PuzzleStage         ScriptableObject stage definition; DefaultStages = the 8 curated stages
    GameFlow            progression state machine (Setup/Tutorial/Playing/Solved/Transition/Completed/Paused)
    ProgressSaveSystem  completed-stage persistence (PlayerPrefs store at runtime)
    SurfacePlacementSolver  surface scoring, reach/facing checks, puzzle pose and scale from surface size
    BloomGeometry       bloom stroke data (lotus petal layers, rings, Seed of Life) + CPU mirror of the shader
    DailyShadow         date-seeded daily puzzle (weekday family, yaw-only solution, hashed RNG) + streak rule
    GardenLayout        garden slots on the table (inside the placement margin, behind the shadow)
    GardenPlan          which flowers rest in the garden (solved stages, then daily days; newest 7)
    PanelPlacement      FoV-aware panel position (far edge within 34 deg of the head-to-puzzle line)
  Scripts/Runtime/   ShadowBloom.Runtime: Unity components
    PuzzleController    renders curve + shadows, bounded-rate matching, solve, snap, tutorial ghost
    GameStateController orchestration; every command is a no-op when invalid (no soft-locks)
    SurfacePlacementController  search timeout -> manual marker fallback; never waits forever
    BloomEffectController  signature bloom: Lock -> Bloom -> Resolve on one clock, phase events for audio
    GameSettings        mute / reduced motion / assist (own PlayerPrefs keys)
    GardenController    up to 7 static mini-blooms (bloom shader, one small draw call each)
    XRPresence          "XR runtime loaded?" for Editor desktop-vs-XR decisions (works before the session runs)
    WorldSpaceUIController, FeedbackAudio (synthesised, spatial emitter on the puzzle), DesktopDebugInput,
    LineMeshBuilder, GlowLine shader, BloomPetal shader (vertex-shader unfold, screen-facing linework)
  Scripts/Meta/      ShadowBloom.Meta: SDK adapters
    RotateOnlyTransformer  ISDK ITransformer: rotation about a fixed pivot, gain + smoothing
    MrukSurfaceSource      MRUK anchors -> surface candidates (Editor: simulated room prefab)
    HandTrackingMonitor    informational "hands not visible" status
    PuzzleHoverFeedback    ISDK interactable hover state -> puzzle hover highlight (read-only)
  Scripts/Editor/    ShadowBloomSceneBuilder (rebuilds the scene from the prefabs), ShadowBloomPrefabs (prefab migration,
                     EditPrefab helper), ShadowBloomAudit (serialized-property dump), UI preview inspector + UiThemePreview,
                     QuestProjectConfigurator, BloomPreviewWindow (Edit/Play-mode bloom preview), ShadowBloomBatch
  Prefabs/           ShadowBloomGame (Systems + nested Puzzle, PlacementMarker, GameUI), PointableCanvasModule
  Tests/EditMode/    projection, matcher, random-orientation rejection, progression, completion/save, placement, bloom
  Tests/PlayMode/    full game loop, synthetic rooms + save/resume, opt-in XR Simulator smoke test
```

### Editing in the Inspector

The game is a set of ordinary prefabs in `Assets/ShadowBloom/Prefabs/`, and `ShadowBloom.unity` holds instances of
them. Open a prefab, change values in the Inspector, save. **Shadow Bloom > Build Scene** rebuilds the scene from the
prefabs (camera rig, MRUK, prefab instances and three scene references), so prefab edits survive a rebuild and
scene-only changes do not. Edit the prefabs rather than the scene copies; the only intended scene overrides are
`SurfacePlacementController._head`, `DesktopDebugInput._cameraRig` and `MrukSurfaceSource._mruk`.

| To tune | Open | Component |
|---|---|---|
| Curve, shadow and target look, hover/grab feedback, plate size, ghost timing | `Puzzle.prefab` (root) | `PuzzleController` |
| Bloom flower: timing, shape, colours, glow | `Puzzle.prefab` > `Bloom` | `BloomEffectController` |
| Table garden: fade, width, brightness, today's flower | `Puzzle.prefab` > `Garden` | `GardenController` |
| Flow timing, idle hint, hand-loss grace, stage list | `ShadowBloomGame.prefab` > `Systems` | `GameStateController` |
| Volume, tick thresholds and notes, audio sources | `ShadowBloomGame.prefab` > `Systems`, `PuzzleAudioEmitter` | `FeedbackAudio`, `AudioSource` |
| Placement timeouts, desktop seated pose | `ShadowBloomGame.prefab` > `Systems` | `SurfacePlacementController`, `DesktopDebugInput` |
| Panel colours, sizes, typography, motion, progress ticks, grid cells | `Assets/ShadowBloom/UI/UiTheme.asset` | `UiTheme` (preview below) |
| Puzzles | `Assets/ShadowBloom/Stages/*.asset` | `PuzzleStage` |
| UI text | `Scripts/Runtime/UiCopy.cs` | code, not an asset |

**UI preview.** `WorldSpaceUIController` lays the panel out from `UiTheme` at runtime, so moving its children by hand
does not stick. Open `GameUI.prefab`, select `UI`, choose a state (Setup, Playing, Paused, Solved, Completed) under
**Edit-mode preview** and press **Preview**. While `UiTheme.asset` is selected, its edits re-lay out the open prefab
(and every panel during Play).

**Material or component?** Code writes these shader properties every frame through a `MaterialPropertyBlock`, so
changing them on the material has no effect; tune the component fields instead:
- `M_CurveTube`, `M_TutorialGhost`, `M_ProjectionCurrent`, `M_ProjectionTarget` (`PuzzleController`): `_Color`,
  `_Intensity`, `_Pulse`, `_Reveal`, `_Outline`, and `_DashFill` on the target.
- `M_Bloom` (`BloomEffectController`, `GardenController`): `_Bloom`, `_Stagger`, `_Twist`, `_Overshoot`, `_Width`,
  `_Intensity`, `_Alpha`, `_Warmth`, `_ColorA`, `_ColorB`, `_ColorC`, `_SeedColor`, `_SeedYaw` (the garden sets all
  but `_SeedColor` and `_SeedYaw`).

Other material properties (blend modes, `_CoreGlow`, `_DashFrequency`, render queue) and the plate, marker and
desktop-table materials are edited on the material itself.

### Interaction

- Puzzle: ISDK `Grabbable` (max 1 grab point, no throw) + `HandGrabInteractable`/`GrabInteractable` created by
  `QuickActionsAPI.AddGrabInteraction`, with a custom `RotateOnlyTransformer`. The object follows the change in the
  grabbing hand's orientation and never translates, so the shadow stays centred on the target.
- A second hand does not add two-hand scaling or translation. One-hand rotation is the stable path.
- The rig is the ISDK `OVRComprehensiveInteractionRig` (`OVRQuickActionsAPI.AddOVRInteractionRig`). Its locomotion
  (teleport/turn micro-gestures, sticks, player body) is disabled: this is a seated MR game.
- UI: `QuickActionsAPI.AddPokeCanvasInteraction` + `AddRayCanvasInteraction`. Buttons have a 0.4 s cooldown.

### Mathematical projection

For each sampled curve point `p` (object space):

1. `w = M * p` (world space, `M` = pivot local-to-world).
2. Move `w` along projection direction `d` onto the plane (origin `o`, normal `n`):
   `w' = w - d * ((w - o)·n / d·n)`. `d` is flipped to point toward the surface, and falls back to `-n` if nearly
   parallel to the plane.
3. 2D coordinates in the plane basis: `(dot(w'-o, right), dot(w'-o, forward))`.

The game projects along `-n` (straight down onto the surface). The target is the same projection of the same
curve at the stage's known solution rotation. Because the puzzle pose is stored relative to the surface frame,
results are identical on any surface orientation (tested on rotated, sloped, wall and ceiling frames).

### Shape error metric and tolerance

```
error = 0.5 * ( mean_{c in C} d(c, T) + mean_{t in T} d(t, C) ) / rms(T)
```

`C` and `T` are the current and target projections, centred on their centroids. `d(x, S)` is the distance from a
point to the nearest segment of polyline set `S` (symmetric chamfer distance). `rms(T)` is the target's RMS
radius, a constant per stage.

- **Cyclic offsets, reversed winding, symmetric shapes:** point-to-polyline distances ignore sample order, so a
  5-petal rose rotated by 72 degrees, a reversed curve, or a ring at any yaw scores about 0.
- **Scale is not normalised away:** a larger or squashed shadow is penalised.
- **Runtime:** `FastShapeMatcher` gives the same value using a uniform grid, with per-point distances clamped at
  0.5 x rms. Tests check it equals the reference inside the feedback band and never exceeds it.
- **Bounded cost:** matching runs at 12 Hz (`PuzzleController._matchHz`). The shadow mesh updates only when the
  pose changes. A hitch counts at most 0.25 s toward the hold.
- **Solve rule:** error at or below tolerance, sustained for `holdSeconds`. Exit threshold is 1.2 x tolerance
  (hysteresis). Slipping out decays progress at 2x instead of resetting it.

**Tolerance calibration** (error vs. offset from the solution, measured in the Editor):

| Stage | Tolerance | Accepted (approx.) | Rejected | Start error |
|---|---|---|---|---|
| 01 Circle | 0.025 | tilt < ~22 deg (any yaw) | tilt 30 deg = 0.068 | 0.33 |
| 02 Petals | 0.070 | tilt < ~22 deg, yaw < ~6 deg | tilt 30 deg = 0.088 | 0.19 |
| 03 Knot | 0.060 | tilt < ~18 deg, yaw < ~9 deg | tilt 30 deg >= 0.090 | 0.21 |
| 04 Orbit | 0.055 | about 8-10 deg any axis | tilt 30 deg >= 0.11 | 0.15 |
| 05 Bloom | 0.040 | tilt < ~18 deg, yaw < ~8 deg | tilt 30 deg = 0.064 | 0.16 |
| 06 Star | 0.075 | yaw < ~6.5 deg; 4 deg jitter <= 0.048 | tilt 30 deg >= 0.097 | 0.18 |
| 07 Infinity | 0.070 | yaw < ~9 deg; 4 deg jitter <= 0.031 | tilt 30 deg >= 0.108 | 0.28 |
| 08 Seed | 0.040 | yaw < ~6.5 deg; 4 deg jitter <= 0.026 | tilt 30 deg >= 0.058 | 0.12 |
| 09 Weave (Daily only) | 0.050 | yaw < ~6 deg; 4 deg jitter <= 0.031 | tilt 30 deg >= 0.074 | 0.16 |

Play order is 01 Circle, 06 Star, 02 Petals, 07 Infinity, 03 Knot, 04 Orbit, 08 Seed, 05 Bloom (ids are creation
order; keeping them keeps saved progress valid). Stages 06-09 were calibrated with the opt-in
`StageCalibrationReport` test: tolerance = a4 + f(a30 - a4), f = 0.55 / 0.50 / 0.45 for Easy / Medium / Hard,
where a4 is the worst 4 degree jitter and a30 the gentlest 30 degree tilt. The infinity starts near its decoy view
(a circle, the stage 1 shadow) on purpose.

The ring's ellipse changes with `1 - cos(tilt)`, so it needs a much tighter numeric tolerance than the petals,
which are very yaw-sensitive. That's why tolerances are set per stage.
