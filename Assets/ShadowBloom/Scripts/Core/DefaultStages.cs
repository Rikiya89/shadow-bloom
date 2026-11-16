using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// The eight curated stages (in play order), defined in code so the editor builder and the tests share one source of truth.
    /// The editor builder saves these as assets; designers then tune the assets.
    /// Every solution keeps curve-local Y on the surface normal (the curve's designed "reading"), plus a yaw.
    ///
    /// Tolerances were calibrated by sweeping error against angular offset (see README "Tolerance calibration"):
    /// each accepts roughly 6-12 degrees of yaw / 18-22 degrees of tilt and rejects a 30 degree tilt.
    /// Error curves differ per family (a ring's ellipse changes with 1-cos(tilt), petals are yaw-sensitive),
    /// which is why tolerances are per stage rather than global.
    ///
    /// Stage ids are creation order (01-05 first, then 06-09), not play order: play order is the array order, and
    /// keeping the original ids keeps saved progress valid.
    /// </summary>
    public static class DefaultStages
    {
        public static PuzzleStage[] CreateAll()
        {
            return new[]
            {
                Make("01_circle", "The circle", "Tilt the ring until its shadow becomes a circle.", Difficulty.Tutorial, true,
                    new CurveParams { kind = CurveKind.Ring, samples = 128 },
                    initial: new Vector3(68f, 0f, 0f), solution: Vector3.zero, tolerance: 0.025f, hold: 0.6f, petals: 6, layers: 2),

                // TODO(you): the one line a player reads for this new shape
                Make("06_star", "The star", "Lay the star flat. Match every point.", Difficulty.Easy, false,
                    new CurveParams { kind = CurveKind.Star, samples = 160, petals = 5, p = 2, height = 0.375f },
                    initial: new Vector3(-70f, 40f, 30f), solution: new Vector3(0f, 10f, 0f), tolerance: 0.075f, hold: 0.8f, petals: 5, layers: 3),

                Make("02_petals", "The petals", "Lay the flower down. Turn it until the petals line up.", Difficulty.Easy, false,
                    new CurveParams { kind = CurveKind.Rose, samples = 192, petals = 5, minorRadius = 0.45f, height = 0.28f },
                    initial: new Vector3(80f, -30f, 10f), solution: new Vector3(0f, 18f, 0f), tolerance: 0.07f, hold: 0.8f, petals: 5, layers: 3),

                // TODO(you): the one line a player reads for this new shape
                // The start pose turns local X almost onto the normal, so the first shadow is nearly the circle decoy.
                Make("07_infinity", "The infinity", "Not the circle this time. Find the figure eight.", Difficulty.Medium, false,
                    new CurveParams { kind = CurveKind.Viviani, samples = 192, height = 0.5f },
                    initial: new Vector3(8f, 25f, 82f), solution: new Vector3(0f, 15f, 0f), tolerance: 0.07f, hold: 0.9f, petals: 8, layers: 3),

                Make("03_knot", "The knot", "Find the angle where the knot casts a three-lobed star.", Difficulty.Medium, false,
                    new CurveParams { kind = CurveKind.TorusKnot, samples = 256, p = 2, q = 3, minorRadius = 0.34f, height = 1f },
                    initial: new Vector3(-70f, 20f, 35f), solution: new Vector3(0f, 30f, 0f), tolerance: 0.06f, hold: 0.8f, petals: 6, layers: 3),

                Make("04_orbit", "The orbit", "Three orbits, one shadow. Align every loop.", Difficulty.Hard, false,
                    new CurveParams { kind = CurveKind.Orbit, samples = 128, loops = 3, tiltDegrees = 58f },
                    initial: new Vector3(-75f, 20f, 35f), solution: new Vector3(0f, -25f, 0f), tolerance: 0.055f, hold: 0.9f, petals: 9, layers: 4),

                // TODO(you): the one line a player reads for this new shape
                Make("08_seed", "The seed", "Seven circles, one flower. Stack them up.", Difficulty.Hard, false,
                    new CurveParams { kind = CurveKind.Seed, samples = 96, minorRadius = 0.5f, height = 0.22f },
                    initial: new Vector3(60f, 120f, -45f), solution: Vector3.zero, tolerance: 0.04f, hold: 0.9f, petals: 6, layers: 4),

                Make("05_bloom", "The bloom", "Every layer must agree. Let it bloom.", Difficulty.Expert, false,
                    new CurveParams { kind = CurveKind.Bloom, samples = 160, petals = 5, minorRadius = 0.22f, height = 0.3f },
                    initial: new Vector3(-60f, 150f, 70f), solution: new Vector3(0f, 12f, 0f), tolerance: 0.04f, hold: 1f, petals: 10, layers: 5,
                    completion: CompletionBehavior.FinalBloom),
            };
        }

        /// <summary>Shapes that only appear in the Daily Shadow (no stage asset).</summary>
        public static PuzzleStage[] CreateDailyOnly()
        {
            return new[]
            {
                Make("09_weave", "The weave", "One thread, many shadows. Find the right one.", Difficulty.Hard, false,
                    new CurveParams { kind = CurveKind.Lissajous, samples = 256, p = 3, q = 2, height = 0.45f },
                    initial: new Vector3(-60f, 40f, 50f), solution: new Vector3(0f, 20f, 0f), tolerance: 0.05f, hold: 0.9f, petals: 7, layers: 4),
            };
        }

        static PuzzleStage Make(string id, string name, string hint, Difficulty difficulty, bool tutorial, CurveParams curve,
            Vector3 initial, Vector3 solution, float tolerance, float hold, int petals, int layers,
            CompletionBehavior completion = CompletionBehavior.BloomThenNext)
        {
            var s = ScriptableObject.CreateInstance<PuzzleStage>();
            s.name = id;
            s.stageId = id;
            s.displayName = name;
            s.hint = hint;
            s.difficulty = difficulty;
            s.isTutorial = tutorial;
            s.curve = curve;
            // Multi-strip families sample each strip more sparsely to keep matching under ~1 ms.
            s.matchSamples = curve.kind == CurveKind.Seed ? 48 : curve.kind == CurveKind.Orbit || curve.kind == CurveKind.Bloom ? 64 : 96;
            s.initialEuler = initial;
            s.solutionEuler = solution;
            s.tolerance = tolerance;
            s.holdSeconds = hold;
            s.bloomPetals = petals;
            s.bloomLayers = layers;
            s.completion = completion;
            return s;
        }
    }
}
