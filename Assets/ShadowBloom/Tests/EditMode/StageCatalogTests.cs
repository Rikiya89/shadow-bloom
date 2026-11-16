using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ShadowBloom.Tests
{
    /// <summary>Copy rules for the stages added after the original five (01-05 predate the 52-character limit).</summary>
    public class StageCopyTests
    {
        static readonly string[] NewIds = { "06_star", "07_infinity", "08_seed", "09_weave" };

        static IEnumerable<PuzzleStage> NewStages() =>
            DefaultStages.CreateAll().Concat(DefaultStages.CreateDailyOnly()).Where(s => NewIds.Contains(s.stageId));

        [Test]
        public void NewStages_AllPresent()
        {
            CollectionAssert.AreEquivalent(NewIds, NewStages().Select(s => s.stageId));
        }

        [TestCaseSource(nameof(NewStages))]
        public void NewStageCopy_IsAsciiAndShort(PuzzleStage stage)
        {
            foreach (var text in new[] { stage.displayName, stage.hint })
            {
                Assert.That(text, Is.Not.Empty);
                Assert.That(text.All(c => c >= 32 && c < 127), $"'{text}' is ASCII");
            }
            Assert.That(stage.hint.Length, Is.LessThanOrEqualTo(52), $"'{stage.hint}' stays short");
        }
    }

    /// <summary>The game reads stage assets while the tests read DefaultStages: the added stages must not drift.</summary>
    public class StageAssetSyncTests
    {
        static IEnumerable<string> SyncedIds() => new[] { "06_star", "07_infinity", "08_seed" };

        [TestCaseSource(nameof(SyncedIds))]
        public void AddedStageAsset_MatchesDefaultStages(string id)
        {
            var asset = AssetDatabase.LoadAssetAtPath<PuzzleStage>($"Assets/ShadowBloom/Stages/{id}.asset");
            Assert.That(asset, Is.Not.Null, $"{id} asset exists (run Shadow Bloom/Sync Stage Catalog)");
            var code = DefaultStages.CreateAll().First(s => s.stageId == id);
            Assert.That(JsonUtility.ToJson(asset), Is.EqualTo(JsonUtility.ToJson(code)),
                $"{id} asset differs from DefaultStages: run Shadow Bloom/Sync Stage Catalog");
        }

        [Test]
        public void GamePrefab_ListsTheCampaignInPlayOrder()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShadowBloom/Prefabs/ShadowBloomGame.prefab");
            var game = prefab.GetComponentInChildren<GameStateController>(true);
            var listed = new SerializedObject(game).FindProperty("_stages");
            var expected = DefaultStages.CreateAll().Select(s => s.stageId).ToArray();
            Assert.That(listed.arraySize, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
                Assert.That(((PuzzleStage)listed.GetArrayElementAtIndex(i).objectReferenceValue).stageId, Is.EqualTo(expected[i]), $"slot {i}");
        }
    }
}
