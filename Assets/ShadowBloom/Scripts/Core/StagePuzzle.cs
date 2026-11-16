using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// Runtime-agnostic evaluation of one stage: target projection (from the known solution) vs. the current pose.
    /// The target lives in plane-2D coordinates, which for a normal-aligned projection do not depend on where
    /// the plane is in the world, so it only needs rebuilding when the puzzle scale changes.
    /// </summary>
    public sealed class StagePuzzle
    {
        public readonly PuzzleStage Stage;
        public readonly CurveSet MatchCurve;
        public ProjectedShape Target { get; }
        public ProjectedShape Current { get; }
        public float Scale { get; private set; } = 1f;

        float _normScale = 1f;
        readonly FastShapeMatcher _matcher = new FastShapeMatcher();

        static readonly PlaneBasis Canonical = new PlaneBasis(Vector3.zero, Quaternion.identity);

        public StagePuzzle(PuzzleStage stage, float scale = 1f)
        {
            Stage = stage;
            MatchCurve = CurveGenerator.Generate(stage.MatchCurve);
            Target = new ProjectedShape(MatchCurve);
            Current = new ProjectedShape(MatchCurve);
            SetScale(scale);
        }

        public void SetScale(float scale)
        {
            Scale = Mathf.Max(1e-4f, scale);
            GeometryProjection.ProjectPlaneLocal(MatchCurve, 0f, Stage.SolutionRotation, Scale, Canonical, Target);
            _normScale = Target.RmsRadius();
            _matcher.SetTarget(Target, _normScale);
        }

        /// <summary>Full world-space path: transform curve, project along the plane normal, compare.</summary>
        public float Evaluate(in Matrix4x4 curveLocalToWorld, in PlaneBasis plane)
        {
            GeometryProjection.Project(MatchCurve, curveLocalToWorld, plane, -plane.Normal, Current);
            return _matcher.ComputeError(Current);
        }

        /// <summary>Convenience for tests and debug tools: pose given relative to a canonical plane.</summary>
        public float EvaluatePlaneLocal(Quaternion planeLocalRotation)
        {
            GeometryProjection.ProjectPlaneLocal(MatchCurve, 0.2f, planeLocalRotation, Scale, Canonical, Current);
            return _matcher.ComputeError(Current);
        }

        /// <summary>Exact brute-force error for the current projection (reference for tests).</summary>
        public float ReferenceError() => ProjectionMatcher.ComputeError(Current, Target, _normScale);

        public bool IsMatch(float error) => error <= Stage.tolerance;
    }
}
