using UnityEngine;

namespace ShadowBloom
{
    public enum Difficulty
    {
        Tutorial,
        Easy,
        Medium,
        Hard,
        Expert
    }

    public enum CompletionBehavior
    {
        BloomThenNext,
        FinalBloom
    }

    /// <summary>
    /// One curated puzzle. Rotations are plane-local: identity means the curve's local Y is aligned with
    /// the surface normal. The target pattern is the projection at <see cref="solutionEuler"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Shadow Bloom/Puzzle Stage", fileName = "Stage")]
    public sealed class PuzzleStage : ScriptableObject
    {
        [Header("Identity")]
        public string stageId = "stage";
        public string displayName = "STAGE";
        [TextArea] public string hint = "";
        public Difficulty difficulty;
        public bool isTutorial;

        [Header("Geometry")]
        public CurveParams curve;
        [Tooltip("Samples per strip used for matching (render uses curve.samples).")]
        [Range(32, 256)] public int matchSamples = 96;

        [Header("Puzzle")]
        public Vector3 initialEuler;
        public Vector3 solutionEuler;
        [Tooltip("Normalised chamfer error accepted as a match. See ProjectionMatcher.")]
        [Range(0.01f, 0.3f)] public float tolerance = 0.06f;
        [Range(0f, 3f)] public float holdSeconds = 0.8f;

        [Header("Visual")]
        public Color lineColor = Color.white;
        public Color highlightColor = GeometryPalette.Paper;
        [Range(0.5f, 2f)] public float lineWidthScale = 1f;
        [Range(3, 16)] public int bloomPetals = 6;
        [Range(1, 6)] public int bloomLayers = 3;

        [Header("Completion")]
        public CompletionBehavior completion;

        public Quaternion InitialRotation => Quaternion.Euler(initialEuler);
        public Quaternion SolutionRotation => Quaternion.Euler(solutionEuler);

        public CurveParams MatchCurve
        {
            get
            {
                var c = curve;
                c.samples = matchSamples;
                return c;
            }
        }
    }
}
