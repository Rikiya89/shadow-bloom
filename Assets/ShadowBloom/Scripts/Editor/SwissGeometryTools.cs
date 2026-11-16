using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ShadowBloom.EditorTools
{
    /// <summary>Writes the Swiss line-art geometry palette into Puzzle.prefab, the stage assets and the materials (idempotent).</summary>
    public static class SwissGeometryTools
    {
        const string StagesDir = "Assets/ShadowBloom/Stages";
        const string MaterialsDir = "Assets/ShadowBloom/Materials";

        [MenuItem("Shadow Bloom/Apply Swiss Geometry")]
        public static string Apply()
        {
            var log = new StringBuilder("Swiss geometry applied:");

            log.Append(' ').Append(ShadowBloomPrefabs.EditPrefab<PuzzleController>(ShadowBloomPrefabs.PuzzlePath, puzzle =>
            {
                var bloom = puzzle.GetComponentInChildren<BloomEffectController>(true);
                var garden = puzzle.GetComponentInChildren<GardenController>(true);
                if (bloom == null || garden == null) throw new InvalidOperationException("Bloom or Garden missing in Puzzle prefab.");

                var so = new SerializedObject(puzzle);
                so.FindProperty("_neutral").colorValue = GeometryPalette.PaperMuted;
                so.FindProperty("_highlight").colorValue = GeometryPalette.Paper;
                so.FindProperty("_targetColor").colorValue = GeometryPalette.WithAlpha(GeometryPalette.Paper, GeometryPalette.TargetAlpha);
                so.FindProperty("_shadowColor").colorValue = GeometryPalette.WithAlpha(GeometryPalette.PaperMuted, GeometryPalette.ShadowAlpha);
                so.FindProperty("_holdColor").colorValue = GeometryPalette.WithAlpha(GeometryPalette.Vermilion, GeometryPalette.HoldAlpha);
                so.ApplyModifiedPropertiesWithoutUndo();

                var bo = new SerializedObject(bloom);
                bo.FindProperty("_innerColor").colorValue = GeometryPalette.Vermilion;
                bo.FindProperty("_outerColor").colorValue = GeometryPalette.Paper;
                bo.FindProperty("_tipColor").colorValue = GeometryPalette.Ivory;
                bo.FindProperty("_seedColor").colorValue = GeometryPalette.Vermilion;
                bo.ApplyModifiedPropertiesWithoutUndo();

                var go = new SerializedObject(garden);
                go.FindProperty("_pastColor").colorValue = GeometryPalette.Paper;
                go.ApplyModifiedPropertiesWithoutUndo();
                return "Puzzle prefab";
            }));

            int stages = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:PuzzleStage", new[] { StagesDir }))
            {
                var stage = AssetDatabase.LoadAssetAtPath<PuzzleStage>(AssetDatabase.GUIDToAssetPath(guid));
                if (stage == null) continue;
                var so = new SerializedObject(stage);
                so.FindProperty("highlightColor").colorValue = GeometryPalette.Paper;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(stage);
                stages++;
            }
            log.Append($", {stages} stages");

            SetMaterial("M_Bloom", ("_ColorA", GeometryPalette.Vermilion), ("_ColorB", GeometryPalette.Paper),
                ("_ColorC", GeometryPalette.Ivory), ("_SeedColor", GeometryPalette.Vermilion));
            SetMaterial("M_ProjectionTarget", ("_Color", GeometryPalette.WithAlpha(GeometryPalette.Paper, GeometryPalette.TargetAlpha)));
            SetMaterial("M_TutorialGhost", ("_Color", GeometryPalette.WithAlpha(GeometryPalette.Paper, 0.5f)));
            SetMaterial("M_Marker", ("_Color", GeometryPalette.Paper));
            log.Append(", 4 materials");

            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        static void SetMaterial(string name, params (string prop, Color color)[] colors)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) throw new InvalidOperationException($"Material missing: {path}");
            foreach (var (prop, color) in colors) mat.SetColor(prop, color);
            EditorUtility.SetDirty(mat);
        }
    }
}
