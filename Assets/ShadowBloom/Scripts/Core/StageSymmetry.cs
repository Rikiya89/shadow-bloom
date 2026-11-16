namespace ShadowBloom
{
    /// <summary>
    /// Symmetries of a family's target shadow (the projection along local Y). Equivalent solutions are the solution
    /// yawed by 360/Folds, and, when Mirror is true, also flipped 180 degrees about local X (which mirrors z -> -z).
    /// Only list symmetries of the 2D shadow: the snap settles on these poses.
    /// </summary>
    public static class StageSymmetry
    {
        public static int Folds(in CurveParams c) => c.kind switch
        {
            CurveKind.Ring => 72,
            CurveKind.Rose => c.petals,
            CurveKind.TorusKnot => c.q,
            CurveKind.Bloom => c.petals,
            CurveKind.Star => c.petals,
            CurveKind.Viviani => 2,
            CurveKind.Seed => 6,
            CurveKind.Lissajous => 2,
            _ => 1
        };

        public static bool Mirror(CurveKind k) => k == CurveKind.Ring || k == CurveKind.Rose || k == CurveKind.TorusKnot
            || k == CurveKind.Star || k == CurveKind.Viviani || k == CurveKind.Seed || k == CurveKind.Lissajous;
    }
}
