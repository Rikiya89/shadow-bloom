using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ShadowBloom.Tests
{
    public class GeometryPaletteTests
    {
        const string PuzzlePath = "Assets/ShadowBloom/Prefabs/Puzzle.prefab";
        const float Tol = 1e-3f;

        static void AssertColor(Color expected, Color actual, string what)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(Tol), what + " r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(Tol), what + " g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(Tol), what + " b");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(Tol), what + " a");
        }

        static (PuzzleController puzzle, BloomEffectController bloom, GardenController garden) LoadPuzzle()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PuzzlePath);
            Assert.That(prefab, Is.Not.Null, PuzzlePath);
            return (prefab.GetComponentInChildren<PuzzleController>(true),
                    prefab.GetComponentInChildren<BloomEffectController>(true),
                    prefab.GetComponentInChildren<GardenController>(true));
        }

        static Color Field(Object o, string name) => new SerializedObject(o).FindProperty(name).colorValue;

        [Test]
        public void PuzzlePrefab_UsesPalette()
        {
            var (puzzle, bloom, garden) = LoadPuzzle();
            AssertColor(GeometryPalette.PaperMuted, Field(puzzle, "_neutral"), "_neutral");
            AssertColor(GeometryPalette.Paper, Field(puzzle, "_highlight"), "_highlight");
            AssertColor(GeometryPalette.WithAlpha(GeometryPalette.Paper, GeometryPalette.TargetAlpha), Field(puzzle, "_targetColor"), "_targetColor");
            AssertColor(GeometryPalette.WithAlpha(GeometryPalette.PaperMuted, GeometryPalette.ShadowAlpha), Field(puzzle, "_shadowColor"), "_shadowColor");
            AssertColor(GeometryPalette.WithAlpha(GeometryPalette.Vermilion, GeometryPalette.HoldAlpha), Field(puzzle, "_holdColor"), "_holdColor");
            AssertColor(GeometryPalette.Vermilion, Field(bloom, "_innerColor"), "_innerColor");
            AssertColor(GeometryPalette.Paper, Field(bloom, "_outerColor"), "_outerColor");
            AssertColor(GeometryPalette.Ivory, Field(bloom, "_tipColor"), "_tipColor");
            AssertColor(GeometryPalette.Paper, Field(garden, "_pastColor"), "_pastColor");
        }

        [Test]
        public void Stages_HighlightIsPaper()
        {
            var guids = AssetDatabase.FindAssets("t:PuzzleStage", new[] { "Assets/ShadowBloom/Stages" });
            Assert.That(guids.Length, Is.EqualTo(DefaultStages.CreateAll().Length));
            foreach (var guid in guids)
            {
                var stage = AssetDatabase.LoadAssetAtPath<PuzzleStage>(AssetDatabase.GUIDToAssetPath(guid));
                AssertColor(GeometryPalette.Paper, stage.highlightColor, stage.name);
            }
            foreach (var stage in DefaultStages.CreateAll())
                AssertColor(GeometryPalette.Paper, stage.highlightColor, "default " + stage.name);
        }

        [Test]
        public void SolvedTarget_IsVermilion()
        {
            var (puzzle, _, _) = LoadPuzzle();
            var c = puzzle.SolvedTargetDisplayColor(DefaultStages.CreateAll()[0]);
            Color.RGBToHSV(c, out float h, out float s, out _);
            Assert.That(h <= 0.06f || h >= 0.97f, Is.True, $"hue {h:F3} should be vermilion");
            Assert.That(s, Is.GreaterThan(0.5f), "saturation");
        }

        [Test]
        public void Geometry_HasNoBlue()
        {
            var (puzzle, bloom, garden) = LoadPuzzle();
            var colors = new[]
            {
                GeometryPalette.Paper, GeometryPalette.PaperMuted, GeometryPalette.Ivory, GeometryPalette.Vermilion,
                Field(puzzle, "_neutral"), Field(puzzle, "_highlight"), Field(puzzle, "_targetColor"),
                Field(puzzle, "_shadowColor"), Field(puzzle, "_holdColor"), Field(bloom, "_innerColor"),
                Field(bloom, "_outerColor"), Field(bloom, "_tipColor"), Field(bloom, "_seedColor"), Field(garden, "_pastColor"),
            };
            foreach (var c in colors) Assert.That(c.b, Is.LessThanOrEqualTo(c.r + Tol), $"{c} leans blue");
        }

        [Test]
        public void Copy_DoesNotNameAColour()
        {
            foreach (var s in UiCopy.AllFixedStrings())
            {
                StringAssert.DoesNotContain("blue", s.ToLowerInvariant());
                StringAssert.DoesNotContain("mint", s.ToLowerInvariant());
            }
        }

        // What the LDR target shows: colour in linear x intensity, clipped per channel, as SolvedTargetDisplayColor does.
        static float Luminance(Color gammaColor, float intensity)
        {
            var l = gammaColor.linear;
            float r = Mathf.Clamp01(l.r * intensity), g = Mathf.Clamp01(l.g * intensity), b = Mathf.Clamp01(l.b * intensity);
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        [Test]
        public void AlignmentRamp_IsVisible()
        {
            var (puzzle, _, _) = LoadPuzzle();
            var neutral = Field(puzzle, "_neutral");
            var shadowColor = Field(puzzle, "_shadowColor");
            var highlight = GeometryPalette.Paper;

            float Curve(float a) => Luminance(Color.Lerp(Color.white * neutral, highlight, a), 1f + 0.8f * a);
            float Shadow(float a) => Luminance(Color.Lerp(shadowColor, highlight, 0.65f * a), 0.95f + 0.35f * a);

            Assert.That(Curve(1f) - Curve(0f), Is.GreaterThanOrEqualTo(0.3f), "curve ramp");
            Assert.That(Curve(0f) * 1.35f, Is.LessThan(0.95f), "grabbed but unaligned must not already look solved");
            Assert.That(Shadow(1f) - Shadow(0f), Is.GreaterThanOrEqualTo(0.3f), "shadow ramp");
        }
    }
}
