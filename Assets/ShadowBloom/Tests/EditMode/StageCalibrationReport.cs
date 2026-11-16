using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShadowBloom.Tests
{
    /// <summary>
    /// Opt-in tolerance calibration table (PLAN.md 3.5). Prints, per stage: error at the solution and start pose,
    /// the worst 4 degree jitter (a4), the gentlest 30 degree tilt (a30), the widest accepted yaw, the worst
    /// snap-equivalent pose, the share of random orientations accepted, and the tolerance the rule suggests.
    /// </summary>
    public class StageCalibrationReport
    {
        static readonly Vector3[] JitterAxes = { Vector3.right, Vector3.up, Vector3.forward, new Vector3(1f, 0f, 1f).normalized };
        static readonly Vector3[] TiltAxes = { Vector3.right, Vector3.forward, new Vector3(1f, 0f, -1f).normalized };

        static Quaternion RandomRotation(System.Random rng)
        {
            double u1 = rng.NextDouble(), u2 = rng.NextDouble() * 2 * Mathf.PI, u3 = rng.NextDouble() * 2 * Mathf.PI;
            double a = System.Math.Sqrt(1 - u1), b = System.Math.Sqrt(u1);
            return new Quaternion((float)(a * System.Math.Sin(u2)), (float)(a * System.Math.Cos(u2)),
                                  (float)(b * System.Math.Sin(u3)), (float)(b * System.Math.Cos(u3)));
        }

        static float Factor(Difficulty d) => d == Difficulty.Easy ? 0.55f : d == Difficulty.Medium ? 0.5f : 0.45f;

        [Test, Explicit("Calibration report, run on demand")]
        public void Report()
        {
            TestContext.WriteLine("| id | tol | e_sol | e_start | a4 | a30 | yawDeg | snap | falseAccept | suggested tol | checks |");
            TestContext.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var s in DefaultStages.CreateAll().Concat(DefaultStages.CreateDailyOnly()))
            {
                var p = new StagePuzzle(s, 0.15f);
                var sol = s.SolutionRotation;
                float eSol = p.EvaluatePlaneLocal(sol);
                float eStart = p.EvaluatePlaneLocal(s.InitialRotation);
                float a4 = JitterAxes.Max(ax => p.EvaluatePlaneLocal(Quaternion.AngleAxis(4f, ax) * sol));
                float a30 = Mathf.Min(TiltAxes.Min(ax => p.EvaluatePlaneLocal(Quaternion.AngleAxis(30f, ax) * sol)),
                    p.EvaluatePlaneLocal(sol * Quaternion.AngleAxis(30f, Vector3.right)));

                float yaw = 0f;
                for (float y = 0.5f; y <= 90f; y += 0.5f)
                {
                    if (p.EvaluatePlaneLocal(Quaternion.Euler(0f, y, 0f) * sol) >= s.tolerance) break;
                    yaw = y;
                }

                int folds = StageSymmetry.Folds(s.curve);
                bool mirror = StageSymmetry.Mirror(s.curve.kind);
                float snap = 0f;
                for (int flip = 0; flip < (mirror ? 2 : 1); flip++)
                for (int i = 0; i < folds; i++)
                {
                    var q = Quaternion.Euler(0f, 360f * i / folds, 0f) * sol * (flip == 0 ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f));
                    snap = Mathf.Max(snap, p.EvaluatePlaneLocal(q));
                }

                var rng = new System.Random(1234);
                int accepted = 0;
                const int n = 2000;
                for (int i = 0; i < n; i++)
                    if (p.EvaluatePlaneLocal(RandomRotation(rng)) < s.tolerance) accepted++;

                float suggested = Mathf.Round((a4 + Factor(s.difficulty) * (a30 - a4)) / 0.005f) * 0.005f;
                string checks = $"{(suggested >= 1.3f * a4 ? "lo-ok" : "LO-FAIL")} {(suggested <= 0.85f * a30 ? "hi-ok" : "HI-FAIL")} " +
                                $"{(eStart >= 3f * s.tolerance ? "start-ok" : "START-FAIL")} {(snap < 0.25f * s.tolerance ? "snap-ok" : "SNAP-FAIL")}";
                TestContext.WriteLine($"| {s.stageId} | {s.tolerance:F3} | {eSol:F4} | {eStart:F3} | {a4:F4} | {a30:F4} | {yaw:F1} | {snap:F4} | " +
                                      $"{accepted / (float)n:P1} | {suggested:F3} | {checks} |");
                if (string.CompareOrdinal(s.stageId, "06") >= 0)
                {
                    var starts = s.curve.kind == CurveKind.Viviani
                        ? new[] { new Vector3(12f, 40f, 78f), new Vector3(0f, 0f, 70f) }
                        : new[] { new Vector3(75f, -30f, 20f), new Vector3(-70f, 40f, 30f), new Vector3(60f, 120f, -45f) };
                    TestContext.WriteLine($"  {s.stageId} start candidates (need >= {3f * s.tolerance:F3}): " +
                        string.Join(", ", starts.Select(e => $"{e} = {p.EvaluatePlaneLocal(Quaternion.Euler(e)):F3}")));
                }
                Object.DestroyImmediate(s);
            }
        }
    }
}
