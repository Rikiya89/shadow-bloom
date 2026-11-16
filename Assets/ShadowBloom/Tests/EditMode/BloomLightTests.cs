using NUnit.Framework;
using UnityEngine;

namespace ShadowBloom.Tests
{
    public class BloomLightTests
    {
        GameObject _go;
        BloomEffectController _bloom;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("BloomLightTest");
            _bloom = _go.AddComponent<BloomEffectController>();   // serialized defaults; Awake does not run in Edit mode
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        // The palette's brightest channel is about 1 and the target is LDR, so any intensity above 1 is flattened by
        // the shader's hue-preserving cap. The swell, rest level and breath must therefore all stay at or below 1.
        [TestCase(false)]
        [TestCase(true)]
        public void BrightnessLevels_StayUnderTheLdrCap_SoSwellRestAndBreathRemainVisible(bool finale)
        {
            float lead = _bloom.LockLead, main = finale ? 3.2f : 2f, settle = _bloom.SettleDuration;
            float peak = 0f, restMin = float.MaxValue, restMax = 0f;
            for (float t = 0f; t < lead + main + settle + 6f; t += 0.01f)
            {
                float i = _bloom.ShaderIntensity(_bloom.Evaluate(t, finale, false).Level);
                peak = Mathf.Max(peak, i);
                if (t >= lead + main + settle)
                {
                    restMin = Mathf.Min(restMin, i);
                    restMax = Mathf.Max(restMax, i);
                }
            }

            Assert.That(peak, Is.LessThanOrEqualTo(1f + 1e-4f), "swell peak is clipped by the LDR cap");
            Assert.That(peak, Is.GreaterThan(0.9f), "the swell peak should use the available brightness");
            Assert.That(restMax, Is.LessThan(peak * 0.9f), "rest must sit visibly below the swell");
            Assert.That(restMax - restMin, Is.GreaterThan(0.08f * restMax), "the breath must change brightness");
        }
    }
}
