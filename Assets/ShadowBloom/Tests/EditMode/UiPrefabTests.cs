using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowBloom.Tests
{
    public class UiPrefabTests
    {
        const string GameUiPath = "Assets/ShadowBloom/Prefabs/GameUI.prefab";

        // Every label reference must point at the button's own "Label" text. The upgrade once took a button's first
        // TMP child, which pointed four references at a decoration and wrote runtime copy into it.
        [TestCase("_primary", "_primaryLabel")]
        [TestCase("_pause", "_pauseLabel")]
        [TestCase("_sound", "_soundLabel")]
        [TestCase("_motion", "_motionLabel")]
        [TestCase("_restart", "_restartLabel")]
        [TestCase("_daily", "_dailyLabel")]
        [TestCase("_assist", "_assistLabel")]
        [TestCase("_howToPlay", "_howToPlayLabel")]
        public void ButtonLabelReferences_PointAtTheButtonsOwnLabel(string buttonField, string labelField)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameUiPath);
            Assert.That(prefab, Is.Not.Null, GameUiPath);
            var ui = prefab.GetComponentInChildren<WorldSpaceUIController>(true);
            var so = new SerializedObject(ui);
            var button = so.FindProperty(buttonField).objectReferenceValue as Button;
            var label = so.FindProperty(labelField).objectReferenceValue as TMP_Text;
            Assert.That(button, Is.Not.Null, buttonField);
            Assert.That(label, Is.Not.Null, labelField);
            Assert.That(label.name, Is.EqualTo("Label"), $"{labelField} points at '{label.name}'");
            Assert.That(label.transform.parent, Is.EqualTo(button.transform), $"{labelField} belongs to {buttonField}");
        }

        // The panel's book face (Source Serif 4) is a static ASCII atlas: every panel text must use it, and its bold
        // must be the real Semibold face rather than TMP's synthetic bold.
        [Test]
        public void PanelTexts_UseTheThemesBookFont_WithASemiboldBoldFace()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/ShadowBloom/UI/UiTheme.asset");
            Assert.That(theme.font, Is.Not.Null, "UiTheme.font");
            StringAssert.Contains("SourceSerif4-Regular", theme.font.name);
            var bold = theme.font.fontWeightTable[7].regularTypeface;
            Assert.That(bold, Is.Not.Null, "bold face");
            StringAssert.Contains("Semibold", bold.name);
            for (char c = (char)32; c < 127; c++)
            {
                Assert.That(theme.font.HasCharacter(c), $"regular missing '{c}'");
                Assert.That(bold.HasCharacter(c), $"semibold missing '{c}'");
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameUiPath);
            foreach (var text in prefab.GetComponentsInChildren<TMP_Text>(true))
                Assert.That(text.font, Is.EqualTo(theme.font), $"{text.name} font");
        }
    }
}
