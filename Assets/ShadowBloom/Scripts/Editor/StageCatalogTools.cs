using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShadowBloom.EditorTools
{
    /// <summary>
    /// Keeps the stage assets and the game prefab's stage list in step with <see cref="DefaultStages.CreateAll"/>
    /// (idempotent). The original five assets are hand-tuned and never overwritten; the stages added later are
    /// refreshed from code every run, because the game reads the assets while the tests read DefaultStages.
    /// Daily-only stages stay code-only.
    /// </summary>
    public static class StageCatalogTools
    {
        public const string StagesDir = "Assets/ShadowBloom/Stages";
        static readonly string[] Tuned = { "01_circle", "02_petals", "03_knot", "04_orbit", "05_bloom" };

        public static string AssetPath(string id) => $"{StagesDir}/{id}.asset";

        [MenuItem("Shadow Bloom/Sync Stage Catalog")]
        public static string Sync()
        {
            var code = DefaultStages.CreateAll();
            int created = 0, refreshed = 0;
            try
            {
                foreach (var stage in code)
                {
                    string path = AssetPath(stage.stageId);
                    var asset = AssetDatabase.LoadAssetAtPath<PuzzleStage>(path);
                    if (asset == null)
                    {
                        AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(stage), path);
                        AssetDatabase.LoadAssetAtPath<PuzzleStage>(path).name = stage.stageId;
                        created++;
                    }
                    else if (!Tuned.Contains(stage.stageId))
                    {
                        EditorUtility.CopySerialized(stage, asset);
                        asset.name = stage.stageId;
                        EditorUtility.SetDirty(asset);
                        refreshed++;
                    }
                }
                AssetDatabase.SaveAssets();

                var assets = code.Select(s => AssetDatabase.LoadAssetAtPath<PuzzleStage>(AssetPath(s.stageId))).ToArray();
                if (assets.Any(a => a == null)) throw new InvalidOperationException("A stage asset failed to load.");

                string prefab = ShadowBloomPrefabs.EditPrefab<GameStateController>(ShadowBloomPrefabs.GamePath, game =>
                {
                    var so = new SerializedObject(game);
                    var list = so.FindProperty("_stages");
                    list.arraySize = assets.Length;
                    for (int i = 0; i < assets.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = assets[i];
                    so.ApplyModifiedPropertiesWithoutUndo();
                    return $"game prefab lists {assets.Length} stages";
                });
                AssetDatabase.SaveAssets();
                return $"Stage catalog: {created} created, {refreshed} refreshed, {prefab}";
            }
            finally
            {
                foreach (var s in code) UnityEngine.Object.DestroyImmediate(s);
            }
        }
    }
}
