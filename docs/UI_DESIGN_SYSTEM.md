# SHADOW BLOOM — UI Design System: "Spatial Observatory"

*Minimal, mathematical, luminous.* The geometry is the artwork. The UI is one quiet instrument panel beside it.

**Style: Neo-Swiss, editorial.** A paper panel with ink type set in a book serif, and one small accent, a deep
vermilion. Greys are ink at lower opacity, never another hue; hierarchy comes from size, weight and opacity. Square
edges everywhere, flush-left type on a two-column grid (large stage numeral | content), sentence-case titles, a
full-width ink header rule. The one next action is a solid ink block; every other action is a square ink outline.
The red marks only the current stage and the moment to hold still. Basis: Neo-Swiss keeps the
International Typographic Style's grid, asymmetry and flush-left type and adapts them to interfaces, with a
greyscale palette plus a single accent, reserved for calls to action (see the references at the end of §2).

Source of truth: `Assets/ShadowBloom/UI/UiTheme.asset` (class `UiTheme`). Every value below is a serialized token
there. Canvas units are **millimetres** (world-space canvas scale 0.001), so sizes are physical sizes on the panel.

## 1. Hierarchy

| Level | What | Treatment |
|---|---|---|
| **Primary** | The 3D curve, the projected target on the table, the bloom | Nothing overlaps it. The panel sits beside the puzzle and **hides completely during the bloom and stage transitions** |
| **Secondary** | Stage eyebrow + progress ticks, stage title, alignment readout, one instruction | Panel typography: eyebrow 7 mm caps, title 17 mm sentence case, instruction 9 mm ink |
| **Tertiary** | Retry, Pause, Move, Sound, Motion, Start over, Quit game | Square ink outlines with flush-left labels under the content; never louder than the red primary block |

There is no screen-space HUD. There is exactly one world-space panel, anchored to the puzzle's surface frame.

## 2. Colour tokens

| Token | Value | Use |
|---|---|---|
| `surface` | paper `#F2F0EB` @ 98 % | Panel backing. Near-opaque, so text contrast never depends on the room behind it (the project blends in linear space, where more transparency lets a room show through) |
| `surfaceRaised` | `#DEDBD4` | Paper one step darker: alignment track, notice card |
| `text` | ink `#141414` | Titles, instruction, button labels and outlines, done ticks |
| `textMuted` | ink @ 60 % | Eyebrow, subtitle, idle alignment label, stage numeral |
| `textMutedJapanese` | ink @ 95 % | Subtitle/body when the UI is Japanese: Noto Serif JP strokes are too thin for 60 % ink |
| `accent` | deep vermilion `#A33A2E` | The single accent, used small: current-stage tick, `HOLD STEADY` label and bar, focus ring. View tones (`UiTone.Accent`) render in ink |
| `success` | ink `#141414` | Solved / complete. Not a second hue: success is carried by the words (`Aligned.`, `Bloom complete`) and full ticks |
| `warning` | deep vermilion | Recovery only (hands lost): the bar beside the sentence on the notice card |
| `onAccent` | `#F7F5F0` | Label on red and ink fills |

Rules:
- **One accent, used small.** Deep vermilion appears only on the current tick, the hold-still moment and the focus
  ring (and the hands-lost bar). The primary block and header rule are ink. Everything else is ink at 100 / 60 / 25 %
  (and paper). A brighter signal red was tried on 2026-11-09 and read as too loud in the headset. No blue, mint or amber in the
  panel; those belong to the game geometry.
- No pure white or pure black: paper and ink are slightly warm and soft.
- **Never colour alone.** Every state pairs colour with words or shape:
  - Alignment: label (`ROTATE TO ALIGN` → `CLOSER` → `ALMOST` → `HOLD STEADY`), bar length, and red only at hold.
  - Progress ticks: square, and size encodes done (4.5 mm) / current (7 mm) / upcoming (4 mm).
  - Setup status: a word next to the square mark (`SEARCHING`, `SURFACE FOUND`, `PLACE THE PUZZLE`).
  - Hands lost: the sentence itself, plus a red bar.
  - Focus: a red outline ring around the button plus a light ink tint, not only a colour change.
- A paper panel is the brightest object in a dark room. It is hidden during the bloom and transitions, so it never
  competes with the signature animation; check comfort in a dim room on the headset.

- **Geometry: Swiss line art.** The game geometry speaks the panel's language (`GeometryPalette`, `Core/GeometryPalette.cs`).
  Lines are paper light, because the room is dark; hierarchy comes from value, opacity, width and solid vs dashed, never
  from hue. There is no blue, mint, coral or rose in the geometry.

  | Token | Value | Use |
  |---|---|---|
  | `Paper` | `#F2F0EB` | Aligned curve, target, ghost, placement ring, petals, past flowers |
  | `PaperMuted` | `#AEADA9` (paper x 0.72) | Resting curve and shadow; they brighten to paper as alignment rises (a full-paper rest would clip to white by about 16 % alignment and lose the cue) |
  | `Ivory` | `#FFEDCC` | Flower tips only |
  | `Vermilion` | `#E4503C` | Hold, flower heart, unfolding edge. The panel's hue, lifted so a thin emissive line reads in passthrough |

  The solved shadow (solid) sits inside the wider target band (dashed, paper at 60 %). As the match is held the band
  fills in and turns vermilion, the same "hold still" signal as the panel's HOLD STEADY bar. The bloom starts in that
  red (no pop on the solve frame), petals unfold with a vermilion leading edge and settle to paper linework with a small
  vermilion heart. Past garden flowers rest as plain paper; today's flower alone keeps the red heart (and its extra height
  and width). Projection ribbons keep their dark edge for light/busy-table contrast. The shader caps brightness by scaling
  the whole colour, so vermilion keeps its hue at full glow. The panel is hidden while the bloom plays, so the two
  never share the view. Headset check pending: see `HEADSET_TEST_CHECKLIST.md`.

References used for the Neo-Swiss pass (2026-11-09):
[International Typographic Style](https://en.wikipedia.org/wiki/International_Typographic_Style) (asymmetric grid,
grotesk type, flush left / ragged right);
[Neo-Swiss guide](https://github.com/kaw393939/swiss-design-lineage-ai-course/blob/main/style-guides/beginner/NEO-SWISS-GUIDE.md)
(greyscale plus one accent, accent for calls to action);
[Swiss design skill](https://www.ui-skills.com/skills/zeke/swiss-design) (opacity, not hue, for hierarchy; one
accent, Swiss red by default);
[Swiss design system (DESIGN.md)](https://designmd.app/library/minimalism-swiss-style) (zero radius, primary filled
with the accent, secondary as an outline).

## 3. Typography

Font: **Source Serif 4** (Adobe, SIL OFL 1.1; `Assets/ShadowBloom/Fonts`), a book serif with sturdy strokes and a
large x-height, so it holds up at headset resolution where hairline serifs (Garamond-style) would shimmer. Regular for
text; **bold is the real Semibold face** (`fontWeightTable`), never TMP's synthetic bold. Both are static SDF atlases
of printable ASCII built by **Shadow Bloom > Build UI Fonts** (`ShadowBloomFonts`, batch `BuildUiFonts`) and
assigned as `UiTheme.font`; LiberationSans stays as a fallback. All copy is ASCII, because the atlas has no arrows,
ticks or ellipses. `UiCopyTests` and `UiPrefabTests` check glyphs, the bold face and that every panel text uses it.
Text rows (eyebrow, alignment caption) take their height from the typeface's line box, never a fixed 8-9 mm.

| Style | Size (mm) | Weight | Tracking | Colour |
|---|---|---|---|---|
| Eyebrow (caps) | 7 | Bold | 8 | `textMuted`, or the tone of the state |
| Title (sentence case) | 17 | Semibold | -0.5 | `text` |
| Stage index (`01`) | 32 (`indexSize`) | Bold | 0 | `textMuted`. Never accent: it must not compete with the primary action. Digit tops sit on the title's cap line |
| Body / subtitle | 9.5 | Regular | 0 | `textMuted` (Japanese: `textMutedJapanese`) |
| Instruction | 9 | Regular | 0 | `text` (full ink against the 60 % subtitle; dims to 45 % after the first grab) |
| Caption (alignment label) | 7 | Bold | ≈10 | per level |
| Primary button label | 10.5 | Bold | 0 | `onAccent`, flush left, 6 mm inset (`primaryLabelInset`), vertically centred |
| Secondary button label | 8.5 | Bold | 0 | `text` (quiet: `textMuted`), flush left, 6 mm inset, vertically centred |

At the measured 0.6 m eye distance, 9 mm text spans ≈ 0.86°, and the 7 mm caps are the smallest element. Check
legibility on Quest 3S, which has lower resolution, before shrinking anything.

Copy rules: one instruction at a time; short imperative sentences; sentence case for titles and instructions ("The
circle", "Bloom complete"; the game name stays "Shadow Bloom"), caps only for eyebrows and status words. All strings live in `UiCopy`.

## 4. Metrics

| Token | Value | Guidance it meets |
|---|---|---|
| `panelWidth` | 196 mm | 3 buttons per row at ≥ 48 mm each |
| `padding` | 14 mm | |
| `gap` | 6 mm | Text rhythm |
| `targetGap` | **12 mm** | Meta hand UI: ~12 mm between adjacent targets |
| `primaryHeight` | 36 mm | ≥ 22 mm physical touch target |
| `secondaryHeight` | 28 mm | ≥ 22 mm physical touch target |
| Smallest button width | 48 mm (three in a row) | ≥ 48 dp hit target at panel scale |
| Corners | **none**: panel, buttons, notice, ticks and tracks are plain square quads; focus ring is a square outline (`SquareRing.png`), 2 mm outset, 1.5 mm stroke (`focusRingCornerMultiplier`) | |
| `indexColumn` | 44 mm | Left grid column for the stage numeral; title, text and alignment start after it |
| `ruleThickness` | 1.6 mm | Full-width header rule. Lines are never drawn finer than ≈ 1.2 mm: at 0.7 m that is under 2 px on Quest 3S/2 and crawls as the head moves |
| `dividerThickness` | 1.2 mm | Divider above a primary block, `text` @ 25 % (faded with alpha, not thinned); `outlineThickness` 1.5 mm is the square outline of secondary buttons (ink @ 85 %, `cellRuleAlpha`; quiet @ 25 %, `quietRuleAlpha`) |
| `alignmentBarHeight` | 2.5 mm | Alignment track and fill |
| `pipDoneSize` / `pipCurrentSize` / `pipUpcomingSize` / `pipSpacing` | 4.5 / 7 / 4 / 5 mm | Square ticks; size encodes progress position, not colour alone |

The PlayMode suite (`UiPlayModeTests`) asserts the ≥ 22 mm and ≥ 12 mm rules in **every** game state on the real
scene, and logs the measured values (see §9).

## 5. Placement (world space)

- **Where:** left of the puzzle, facing the player's head. The inner edge clears the puzzle's grab volume
  (pivot collider = 1.2 × puzzle radius) by a hand's width (`_handClearance` 5 cm), so a grabbing hand never meets
  the panel.
- **Height:** the bottom edge is anchored 8 cm below the pivot height but never closer than 10 cm to the table.
  The panel grows **upward**, so it never clips into furniture. A notice added at the top never moves the buttons.
- **Stability:** placement changes only when the puzzle is placed or moved, never when the player leans in. Content
  changes resize the panel upward from a fixed bottom edge.
- **Scale:** constant physical size (mm) on every surface size and table height.
- **Interaction distance:** the panel is a direct-touch panel for the left hand, with ray pinch as a supplement
  (both ISDK canvases are wired). See §9 for the measured distances and angles, and the open device question.

## 6. Components

### Panel (`WorldSpaceUIController`)
Stack, top to bottom. Only the parts a state needs are shown:
1. notice card (hands lost);
2. eyebrow row with a square status mark and square progress ticks;
3. header rule: full width, square ends, ink;
4. grid: stage index numeral (`01`, or the how-to-play card number) in the left column whenever the state has
   progress, except on completion; title, subtitle, instruction, alignment label and bar in the right column (full
   width when there is no numeral);
5. divider (only when a primary block follows: outlined buttons are already separated by their border);
6. primary block;
7. secondary rows of outlined buttons (rows always span the full width).

The canvas root is resized to the visible content every layout. The Interaction SDK poke and ray clip bounds are
driven by that rect (`RectTransformBoundsClipperDriver`), so hands can reach exactly what is drawn: no dead zones,
no unreachable buttons.

### Buttons (`UiButtonStyle`, one component on every button)

| Variant | Use | Resting look |
|---|---|---|
| Primary | The one next action (Start, Confirm, Next stage, Resume, Play again, Finish) | Solid square ink block, paper label flush left |
| Secondary | Retry, Pause, Move, Sound, Motion, Assist, How to play, Daily | Square ink outline (1.5 mm, ink @ 85 %), no fill, ink label flush left |
| Quiet | Start over (replays from stage 1), Quit game (rare, consequential) | Square outline at 25 % with a muted label |

| State | Feedback | Time |
|---|---|---|
| Default | variant colours | — |
| Hover / focus | light ink tint (8 %), outline to full ink, vermilion square focus ring 2 mm outside; primary lightens toward paper (18 %) | 120 ms |
| Press | deeper tint (16 %; primary 28 % toward paper), scale 0.97 (no scale with Motion: Calm) | 80 ms in / 160 ms |
| Disabled | 40 % opacity (fill, label and outline) | — |
| Confirmed | outlined buttons invert to a solid ink block with a paper label (primary flashes lighter), ink ring, plus a quiet UI tap sound | 180 ms |

Hover and press come from uGUI pointer events, which the ISDK poke and ray canvases forward, so they behave
identically for poke, ray pinch and the desktop mouse. Colours animate with `CrossFadeColor` (no canvas mesh
rebuilds), and the component does no work while idle.

### Guards against accidental input
- Presses are ignored until the panel is fully revealed (`CanvasGroup.interactable`). A panel appearing under a
  finger can't be triggered.
- 0.4 s cooldown across all buttons (double poke).
- Buttons that aren't shown are **inactive**, not just transparent.
- Outlined buttons rest with a fill at alpha 0 but keep its mesh (`cullTransparentMesh` off), so uGUI's raycaster, which the ISDK
  poke and ray canvases use, still hits them. `UiPlayModeTests` checks the raycaster's filter on every visible button.

## 7. States

| State | Panel |
|---|---|
| Setup: searching | dot + `SEARCHING`, title, tagline, "Looking for a table...". Puzzle and garden hidden in every setup state |
| Setup: found | dot + `SURFACE FOUND` (success), "Press Start. The puzzle appears on your table.", **Start**, Move |
| Setup: manual | dot + `SET UP YOUR TABLE`, three numbered steps (pinch the ring, set it on the table, Confirm) in place of the tagline, the reason as the hint ("No room scan found."), **Confirm** |
| How to play (first launch: one card; Pause > How to play: all three) | `HOW TO PLAY` + one pip per card, card title, two short sentences, **Next** (last card: **Start playing**), Pause. The stage is loaded behind the cards (ghost demo running) but takes no input |
| Tutorial (stage 1) | `STAGE 1 OF 8` + pips, title, **one** coaching line that advances as the player demonstrates it: "Pinch the floating ring to grab it." → "Turn the ring to move its solid shadow." → "Move the solid shadow onto the dashed circle." ↔ "Hold still to bloom." Never goes back to an instruction already followed. The ghost demo shows the motion |
| Playing | eyebrow + pips, title, stage hint (dims to 45 % after the first grab, returns with the 15 s idle ghost), alignment label + bar, Retry · Pause · Move |
| Near match | label `HOLD STEADY` (vermilion); the bar turns vermilion and fills with the hold progress. The puzzle itself keeps its calm pulse. No flashing |
| Solved | **Hidden while the bloom plays.** Revealed when the bloom reports Finished (with an unpaused-time fallback, so it can never stay hidden): `Aligned.` (success), success pip, **Next stage** (last stage: **Finish**), Retry |
| Transition | hidden; the next stage's panel fades in with its draw-on |
| Paused | `PAUSED`, stage title + pips, **Resume**, Retry · Move; a muted `SETTINGS` caption + hairline over Sound · Motion and Assist · How to play; a hairline, then quiet "Start over" (replays from stage 1, keeps earned progress) · "Quit game". Quit asks once: the first press relabels it "Really quit?" for 3 s, a second press closes the app (progress is already saved). Two buttons per row: three-up cells (48 mm) are too narrow for the settings labels |
| Completed | `ALL 5 SHADOWS ALIGNED` (success), title `BLOOM COMPLETE` in success, all pips success, **Play again**. The finale sculpture stays fully visible beside it |
| Daily offer / completion | Offer: "Solve Daily Shadow to add a garden flower." Completion: `TODAY'S BLOOM`, streak and garden count, "Today's flower is in your garden. More tomorrow.", **Stages**. The garden returns with the next puzzle; saved flowers do not imply persistent physical anchoring |
| Hands lost | warning card "Hands not visible. Hold them up in front of you." above the content after a 1.2 s grace. Shown **even while the rest of the panel is hidden** (bloom). Clears by itself when the hands return. No button is needed to recover |

No state hides every way forward: the Solved reveal has a fallback, the finale has Finish, Pause can always
Resume, and the hands-lost state recovers on its own.

## 8. Motion

| Motion | Duration | Easing |
|---|---|---|
| Hover | 120 ms | linear tint |
| Press | 80 ms in, 160 ms total | linear tint + scale |
| Confirm | 180 ms | — |
| Panel hide | 120 ms | linear fade |
| Panel reveal | 220 ms + 5 mm rise | ease-out cubic |
| New instruction | 220 ms fade-in | — |
| Hint dim | 600 ms | — |

The panel never animates while idle. **Motion: Calm** removes the rise and the press scale. Fades stay, because
they carry state. The Solved reveal waits for the bloom, so UI never competes with the signature animation.

## 9. Measured (Editor, desktop path; not headset data)

From `UiPlayModeTests` on the real scene (`Verification/UI/final/ui-report.txt`), MRUK `Office08` table, desktop
seated head pose. Editor geometry only: comfort, reach and legibility still need a Quest 3/3S.

| Measure | Result | Guidance |
|---|---|---|
| Smallest button (measured from the rendered rects) | 48.0 × 28.0 mm (three-across row); two-across 78 × 28; full-width 168 × 28 / 168 × 36 | ≥ 22 mm target ✓ |
| Gap between adjacent buttons | 12.0 mm in every state (setup, tutorial, playing, paused, solved, completed, paused-on-completion) | ~12 mm ✓ |
| Buttons inside the ISDK poke/ray clip bounds | all, every state; both clippers match the panel rect | required for hand reach ✓ |
| Panel size by state | completed 196 × 110, tutorial/playing 196 × 129–139, setup 196 × 143–173, solved 196 × 156, paused 196 × 219 mm | quiet states stay small |
| Clearance from the puzzle grab volume | 8.5–8.9 cm | hands never meet the panel ✓ |
| Panel bottom above the table | 0.11 m (radius 0.11) / 0.20 m (radius 0.18) | never at the table ✓ |
| Eye → panel | 0.60–0.61 m | (indirect-range lower edge) |
| Left shoulder → panel (approx. seated shoulder) | 0.44–0.48 m | direct touch ~42–46 cm ✓ for the left hand |
| Horizontal angle from the puzzle direction (historical UI pass) | 36°–44° before Phase 5 | Superseded by fresh measurements below |

**Neo-Swiss grid, measured 2026-11-07** (`UiPlayModeTests`, same desktop head pose, Editor only). Touch targets
unchanged: smallest 48 × 28 mm, every gap 12.0 mm. Panel height before → after: playing 129 → 150, tutorial 129 →
140, solved 156 → 169, setup ready 183 → 196, completed 177 → 190, paused 259 → 278 mm. The growth is the header
rule, the divider and instructions wrapping in the narrower grid column. Readability of the wrapped column and the
taller paused panel still need a headset check.

**Editorial pass, 2026-11-09** (after a headset try found the Swiss red too loud and the sans too plain): Source Serif
4, ink primary block and header rule, deep vermilion `#A33A2E` only on the current tick and HOLD STEADY. Text rows
size from the serif's line box. Touch targets unchanged.

**Paper + Swiss red pass, 2026-11-09** (replaces the strict pass below, which a headset try found hard to read
and still too blue): open numbered cells became square ink outlines with no numbers, and blue, mint and amber left
the panel for paper, ink and one Swiss red. Metrics, touch targets (smallest 48 × 28 mm, gaps 12.0 mm) and panel
heights are unchanged from the strict pass.

**Strict Neo-Swiss pass, measured 2026-11-09** (`UiPlayModeTests`, same desktop head pose, Editor only): square
edges, open numbered cells, 32 mm numeral in a 44 mm column, 17 mm sentence-case titles. Touch targets unchanged:
smallest 48 × 28 mm, every gap 12.0 mm. Panel heights: playing / tutorial 148, solved 172, completed 182, setup
ready 199, paused 284 mm. Open cells read as buttons only through their rule, index and label: confirm on the headset
that they are recognisable and that poke and ray presses land on every cell.

**Superseded by Phase 5 placement, remeasured 2026-10-26:** the current FoV solver swings the panel toward the
far side of the puzzle rather than leaving it at the old left-only position. Fresh baseline measurements in
`Verification/SubmissionPolish/before-dark/ui-report.txt`: centre 29° at radius 0.11 m and 27° at radius 0.18 m;
clearance 8.9–9.9 cm. Eye distance is 0.67–0.73 m; approximate left-shoulder reach is 0.53–0.64 m. This improves
viewing angle but leaves a reach trade-off. Ray pinch remains available. No further placement change is justified
without headset evidence; verify relaxed direct poke reach and readability in `QUEST_DEVICE_QA.md` U-2/U-3.

**Performance (Editor, not device data):** main thread 13.9 ms interactive / 14.0 ms bloom (Phase 3: 13.9–17.0 /
14.5–16.0). GC attribution (`GcAttribution`, two runs): disabling `WorldSpaceUIController`, all `UiButtonStyle`
instances or `GameStateController` leaves whole-Editor GC unchanged at ≈ 300 KB/frame, so the UI adds no
measurable per-frame allocation. The panel does no work while idle; animation uses `CanvasGroup.alpha` and
canvas-renderer tints.

## 10. Applying and extending

- **Where the panel lives:** `Assets/ShadowBloom/Prefabs/GameUI.prefab` (nested in `ShadowBloomGame.prefab`), with
  every button baked in, including Daily and Assist. Edit the prefab, not the scene instance.
- **Preview in Edit mode:** open `GameUI.prefab`, select `UI`, pick a state under **Edit-mode preview** and press
  **Preview**. While `UiTheme.asset` is selected, token edits re-lay out the open prefab (and every panel in Play).
  Hand-moving panel children does not stick: `Layout()` places them from the tokens.
- **Upgrade the panel:** menu **Shadow Bloom > Upgrade Spatial UI**, or batch
  `-executeMethod ShadowBloom.EditorTools.ShadowBloomBatch.UpgradeUi`. It edits `GameUI.prefab` (or the prefab open
  in Prefab Mode), is idempotent, and keeps every object name and reference.
- **Reuse model:** the `UiButtonStyle` component, the `UiTheme` asset and the upgrade method style any panel button;
  the prefab keeps names, references and Interaction SDK wiring intact.
- **New button:** duplicate an existing panel button, choose a `UiButtonStyle` variant, add the field to
  `WorldSpaceUIController`, and place it in `Layout()` with `Row(...)`.
- **New copy:** add it to `UiCopy` (ASCII, short). `UiCopyTests` checks glyphs and length.
- **Retune:** edit `UiTheme.asset`. `UiCopyTests.Theme_MeetsHandUiGuidance` guards the ergonomic minimums.
- The generated sprite `SquareRing.png` (in `Assets/ShadowBloom/UI/`, the focus ring) is an original procedural
  asset created by the upgrade. `RoundedFill.png` / `RoundedRing.png` are left from the earlier rounded style and
  are no longer referenced.
