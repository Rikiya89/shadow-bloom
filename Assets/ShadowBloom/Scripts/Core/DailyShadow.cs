using System;
using System.Linq;
using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// One generated puzzle per local calendar day, one family per weekday: star (Mon), petals, infinity, knot,
    /// orbit or seed (Fri, alternating weeks), the Daily-only weave (Sat), the bloom (Sun).
    /// Only the solution yaw and the starting pose are random: a yaw rotates the target shadow rigidly, so each
    /// family keeps the tolerance calibrated for its curated stage. Randomness is an integer hash of the day, so
    /// the same date gives the same puzzle on every platform and runtime.
    /// </summary>
    public static class DailyShadow
    {
        public const int GardenCapacity = 7;
        public const int MaxStartAttempts = 24;
        public const string IdPrefix = "daily_";
        static readonly DateTime Epoch = new DateTime(2026, 1, 1);

        /// <summary>Day 1 = 2026-01-01. Never below 1, so 0 can mean "never".</summary>
        public static int DayNumber(DateTime localDate) => Math.Max(1, (localDate.Date - Epoch).Days + 1);

        public static DateTime DateOf(int day) => Epoch.AddDays(Math.Max(1, day) - 1);

        public static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public static float R(int day, int k) => (float)(Hash(unchecked((uint)day * 0x9E3779B9u + (uint)k)) / 4294967296.0);

        /// <summary>Stage id (campaign or Daily-only) used as the template for this weekday.</summary>
        public static string TemplateId(DayOfWeek d, int day) => d switch
        {
            DayOfWeek.Monday => "06_star",
            DayOfWeek.Tuesday => "02_petals",
            DayOfWeek.Wednesday => "07_infinity",
            DayOfWeek.Thursday => "03_knot",
            DayOfWeek.Friday => (Math.Max(1, day) - 1) / 7 % 2 == 0 ? "04_orbit" : "08_seed",
            DayOfWeek.Saturday => "09_weave",
            _ => "05_bloom"
        };

        /// <summary>Curve family of the day's puzzle, without generating it (matches <see cref="Create"/>).</summary>
        public static CurveKind KindFor(int day)
        {
            day = Math.Max(1, day);
            switch (TemplateId(DateOf(day).DayOfWeek, day))
            {
                case "06_star": return CurveKind.Star;
                case "02_petals": return CurveKind.Rose;
                case "07_infinity": return CurveKind.Viviani;
                case "03_knot": return CurveKind.TorusKnot;
                case "04_orbit": return CurveKind.Orbit;
                case "08_seed": return CurveKind.Seed;
                case "09_weave": return CurveKind.Lissajous;
                default: return CurveKind.Bloom;
            }
        }

        public static PuzzleStage Create(int day)
        {
            day = Math.Max(1, day);
            var templates = DefaultStages.CreateAll().Concat(DefaultStages.CreateDailyOnly()).ToArray();
            string id = TemplateId(DateOf(day).DayOfWeek, day);
            var s = templates.First(t => t.stageId == id);
            for (int i = 0; i < templates.Length; i++)
            {
                if (templates[i] == s) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(templates[i]);
                else UnityEngine.Object.DestroyImmediate(templates[i]);
            }

            var c = s.curve;
            float v = R(day, 0);
            switch (c.kind)
            {
                case CurveKind.Rose:
                    // 6+ petals cast a near-blob from most angles: no start pose is clearly unsolved.
                    c.petals = v < 0.5f ? 4 : 5;
                    break;
                case CurveKind.TorusKnot:
                    c.p = 2;
                    c.q = v < 0.5f ? 3 : 5;
                    break;
                case CurveKind.Orbit:
                    c.loops = v < 0.5f ? 3 : 4;
                    break;
                case CurveKind.Bloom:
                    c.petals = v < 0.5f ? 5 : 6;
                    break;
                case CurveKind.Star:
                    // {5/2} pentagram or {7/3} heptagram.
                    c.petals = v < 0.5f ? 5 : 7;
                    c.p = v < 0.5f ? 2 : 3;
                    break;
                case CurveKind.Lissajous:
                    // Only (odd p, even q): see CurveGenerator.Lissajous.
                    c.p = 3;
                    c.q = v < 0.5f ? 2 : 4;
                    break;
            }
            s.curve = c;

            s.name = s.stageId = IdPrefix + day;
            s.isTutorial = false;
            s.completion = CompletionBehavior.FinalBloom;
            s.solutionEuler = new Vector3(0f, 360f * R(day, 1), 0f);
            BloomFor(day, out s.bloomPetals, out s.bloomLayers);

            var puzzle = new StagePuzzle(s, 0.15f);
            float best = puzzle.EvaluatePlaneLocal(s.InitialRotation);
            for (int k = 0; k < MaxStartAttempts; k++)
            {
                float a = 360f * R(day, 2 + 3 * k) * Mathf.Deg2Rad;
                float tilt = 45f + 35f * R(day, 3 + 3 * k);
                float spin = 360f * R(day, 4 + 3 * k);
                var q = Quaternion.Euler(0f, spin, 0f) * Quaternion.AngleAxis(tilt, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                // First pose clearly unsolved wins; otherwise keep the most unsolved one seen (never the template's).
                float e = puzzle.EvaluatePlaneLocal(q);
                if (e > best || k == 0)
                {
                    best = e;
                    s.initialEuler = q.eulerAngles;
                }
                if (e >= 3f * s.tolerance) break;
            }
            return s;
        }

        /// <summary>The day's bloom shape (also its garden sculpture), without generating the puzzle.</summary>
        public static void BloomFor(int day, out int petals, out int layers)
        {
            petals = 5 + (int)(6f * R(day, 200));
            layers = 3 + (int)(3f * R(day, 201));
        }

        /// <summary>Streak after solving on <paramref name="today"/>; <paramref name="lastDay"/> 0 = never solved.</summary>
        public static int NextStreak(int streak, int lastDay, int today)
        {
            if (lastDay <= 0) return 1;
            if (today == lastDay) return Math.Max(1, streak);
            if (today == lastDay + 1) return streak + 1;
            // TODO(you): a day (or more) was missed, or the clock went backwards (today < lastDay).
            return 1;
        }
    }
}
