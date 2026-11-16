# SHADOW BLOOM

A seated mixed-reality puzzle for Meta Quest 3/3S, prepared for the Meta XR hackathon submission.

A glowing mathematical curve floats above your table. Rotate it with one hand until its shadow matches the dashed target, hold the match, and watch the shadow bloom into a flower.

## The experience

- Eight curated stages introduce rings, petals, knots and other mathematical curves.
- Hand tracking supports one-hand rotation, direct poke and ray-pinch UI.
- MR Utility Kit finds a table and adapts the puzzle's placement and scale. Manual placement is available when spatial data is unavailable.
- Solved stages leave flowers in a tabletop garden. Completing the campaign unlocks Daily Shadow.
- English and Japanese UI, sound controls, reduced motion and an Assist setting are included.

## Try it in the Editor

1. Clone this repository and open it in **Unity 6000.6.7f1**.
2. Allow Unity Package Manager to restore the dependencies in `Packages/manifest.json`.
3. Open `Assets/ShadowBloom/Scenes/ShadowBloom.unity` and press Play.
4. With no XR runtime active, use the desktop controls below.

| Input | Action |
| --- | --- |
| Left-drag | Rotate the curve |
| Space | Start / Next / Resume / Play again |
| R / P / M | Retry / Pause / Move puzzle |
| A | Toggle Assist |
| D | Daily Shadow, after unlocking it |
| S | Snap to the solution for debugging |
| H | Hide desktop help |

The desktop path uses a simulated room. It is useful for exploring the game, but does not reproduce headset hand tracking or passthrough.

## Play on Quest

Sit at a table with hand tracking enabled. Allow spatial data access to use a scanned table, or place the marker manually. Start the puzzle, grab the curve, and turn your hand while watching its shadow. Hold the match until the flower blooms, then choose **Next stage**.

This repository contains the Unity source project. APKs, demo videos and store uploads are distributed separately; this repository does not include a release binary.

See [Quest build instructions](docs/BUILD.md) to build from source.

## How it works

The shadow is computed by projecting sampled curve points onto the table plane. A symmetric chamfer-distance matcher compares the live projection with the target, and a sustained match triggers a procedural bloom. The object rotates around a fixed pivot so the shadow stays centred on the puzzle.

| Area | Location |
| --- | --- |
| Geometry, matching, placement and game flow | `Assets/ShadowBloom/Scripts/Core/` |
| Runtime controllers and rendering | `Assets/ShadowBloom/Scripts/Runtime/` |
| Meta SDK adapters | `Assets/ShadowBloom/Scripts/Meta/` |
| Editor tools | `Assets/ShadowBloom/Scripts/Editor/` |
| Game prefabs | `Assets/ShadowBloom/Prefabs/` |
| Automated tests | `Assets/ShadowBloom/Tests/` |

Read the [technical overview](docs/ARCHITECTURE.md), [spatial UI design system](docs/UI_DESIGN_SYSTEM.md) and [testing instructions](docs/TESTING.md).

## Technology

Unity 6000.6.7f1 · URP 17.6.0 · Meta XR Core SDK 207.0.1 · Interaction SDK 207.0.1 · MR Utility Kit 207.0.1 · OpenXR 1.18.0.

Meta packages are restored from the scoped registry declared in `Packages/manifest.json`. Project settings and package versions are included for reproducibility.

## Status

This is a hackathon submission source snapshot. Automated tests are included, but no new Unity test run, Android build or headset validation was performed while preparing this public snapshot. Headset interaction, passthrough readability and device performance require separate on-device verification.

## Author and third-party notices

Created by **Rikiya Okawa**. See [third-party notices](THIRD_PARTY_NOTICES.md) for SDKs and fonts. No open-source license is granted for the project's original code or assets in this snapshot; third-party components retain their own licenses.
