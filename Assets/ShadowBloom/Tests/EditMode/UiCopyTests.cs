using System.Collections.Generic;
using NUnit.Framework;
using TMPro;

namespace ShadowBloom.Tests
{
    public class UiCopyTests
    {
        [Test]
        public void Coaching_MovesForwardOnlyWhenDemonstrated()
        {
            int s = UiCopy.StepGrab;
            s = UiCopy.NextTutorialStep(s, false, 0.9f, false);
            Assert.AreEqual(UiCopy.StepGrab, s, "nothing advances before the first grab");
            s = UiCopy.NextTutorialStep(s, true, 0.05f, false);
            Assert.AreEqual(UiCopy.StepTurn, s);
            s = UiCopy.NextTutorialStep(s, true, 0.2f, false);
            Assert.AreEqual(UiCopy.StepTurn, s, "small turns keep the turn instruction");
            s = UiCopy.NextTutorialStep(s, true, 0.4f, false);
            Assert.AreEqual(UiCopy.StepMatch, s);
            s = UiCopy.NextTutorialStep(s, true, 1f, true);
            Assert.AreEqual(UiCopy.StepHold, s);
            s = UiCopy.NextTutorialStep(s, true, 0.7f, false);
            Assert.AreEqual(UiCopy.StepMatch, s, "slipping out of tolerance returns to 'match', never to 'grab'");
            s = UiCopy.NextTutorialStep(s, false, 0f, false);
            Assert.AreEqual(UiCopy.StepMatch, s, "releasing does not repeat earlier instructions");
        }

        [Test]
        public void AlignmentLabels_AreQualitative_AndMonotonic()
        {
            Assert.AreEqual(UiCopy.AlignmentLevel.Far, UiCopy.Level(0f, false));
            Assert.AreEqual(UiCopy.AlignmentLevel.Closer, UiCopy.Level(0.3f, false));
            Assert.AreEqual(UiCopy.AlignmentLevel.Almost, UiCopy.Level(0.8f, false));
            Assert.AreEqual(UiCopy.AlignmentLevel.Hold, UiCopy.Level(0.2f, true), "in tolerance always reads 'hold'");
            var prev = UiCopy.AlignmentLevel.Far;
            for (float a = 0f; a <= 1f; a += 0.01f)
            {
                var l = UiCopy.Level(a, false);
                Assert.That(l, Is.GreaterThanOrEqualTo(prev));
                prev = l;
            }
            foreach (UiCopy.AlignmentLevel l in System.Enum.GetValues(typeof(UiCopy.AlignmentLevel)))
                StringAssert.DoesNotContain("%", UiCopy.AlignmentLabel(l), "never a percentage: the metric is not calibrated");
        }

        [TearDown]
        public void RestoreLanguage() => UiLocale.Override = null;

        [Test]
        public void EveryJapaneseString_IsShort_Translated_AndHasGlyphsInThePanelFont()
        {
            UiLocale.Override = UiLanguage.English;
            var english = new List<string>(UiCopy.AllFixedStrings());
            UiLocale.Override = UiLanguage.Japanese;
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/ShadowBloom/UI/UiTheme.asset");
            Assert.NotNull(theme, "UiTheme.asset");
            Assert.NotNull(theme.font, "panel font");
            int count = 0;
            foreach (var s in UiCopy.AllFixedStrings())
            {
                count++;
                // Full-width characters are about twice as wide as Latin ones, so the limit is about half.
                Assert.That(s.Length, Is.LessThanOrEqualTo(28), $"'{s}' stays short");
                if (s != UiCopy.GameTitle) Assert.That(english, Has.No.Member(s), $"'{s}' is not translated");
                // Static atlases only (no dynamic adds): a new kanji needs Shadow Bloom > Build Japanese UI Font.
                Assert.That(theme.font.HasCharacters(s, out uint[] missing, true, false),
                    $"'{s}' missing U+{string.Join(",", System.Array.ConvertAll(missing ?? new uint[0], m => m.ToString("X4")))} " +
                    "(run Shadow Bloom > Build Japanese UI Font)");
            }
            Assert.That(count, Is.GreaterThanOrEqualTo(english.Count), "every English string has a Japanese one");
        }

        [Test]
        public void StageNamesAndHints_FollowTheLanguage()
        {
            var stages = DefaultStages.CreateAll();
            UiLocale.Override = UiLanguage.English;
            Assert.That(UiCopy.StageName(stages[0]), Is.EqualTo(stages[0].displayName));
            Assert.That(UiCopy.StageHint(stages[1]), Is.EqualTo(stages[1].hint));
            UiLocale.Override = UiLanguage.Japanese;
            var names = new HashSet<string>();
            foreach (var stage in stages)
            {
                Assert.That(UiCopy.StageName(stage), Is.Not.EqualTo(stage.displayName));
                Assert.That(UiCopy.StageHint(stage), Is.Not.EqualTo(stage.hint));
                names.Add(UiCopy.StageName(stage));
            }
            Assert.That(names.Count, Is.EqualTo(stages.Length), "each stage has its own Japanese name");
        }

        [Test]
        public void EveryUiString_IsShortAscii_AndHasGlyphsInTheFont()
        {
            UiLocale.Override = UiLanguage.English;
            var font = TMP_Settings.defaultFontAsset;
            Assert.NotNull(font, "TMP default font");
            foreach (var s in UiCopy.AllFixedStrings())
            {
                foreach (char c in s) Assert.That(c < 128, $"'{s}' is ASCII (no arrows, ticks or ellipses)");
                Assert.That(s.Length, Is.LessThanOrEqualTo(52), $"'{s}' stays short");
                Assert.That(font.HasCharacters(s, out uint[] missing, true, true), $"'{s}' missing U+{string.Join(",", missing ?? new uint[0])}");
            }
        }

        [Test]
        public void FirstRunCardCount_IsWithinTheFullSet()
        {
            Assert.That(UiCopy.FirstRunCardCount, Is.InRange(1, UiCopy.HowToPlayCount));
        }

        [Test]
        public void Theme_MeetsHandUiGuidance()
        {
            var t = UiTheme.Default;
            Assert.That(t.secondaryHeight, Is.GreaterThanOrEqualTo(22f), "~22 mm physical touch target");
            Assert.That(t.primaryHeight, Is.GreaterThanOrEqualTo(22f));
            Assert.That(t.targetGap, Is.GreaterThanOrEqualTo(12f), "~12 mm between adjacent targets");
            float threeAcross = (t.panelWidth - 2f * t.padding - 2f * t.targetGap) / 3f;
            Assert.That(threeAcross, Is.GreaterThanOrEqualTo(48f), "three buttons in a row are still >= 48 mm wide");
            Assert.That(t.hoverSeconds, Is.InRange(0.1f, 0.15f));
            Assert.That(t.pressSeconds, Is.InRange(0.12f, 0.2f));
            Assert.That(t.revealSeconds, Is.InRange(0.2f, 0.3f));
        }
    }
}
