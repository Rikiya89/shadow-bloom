using UnityEngine;

namespace ShadowBloom.Tests
{
    /// <summary>
    /// Plays a fixture as a returning player (how-to-play cards already read, so Start goes straight into stage 1)
    /// and puts the machine's own value back afterwards. Tests of the cards themselves clear it.
    /// </summary>
    public sealed class HowToPlaySeenScope
    {
        const string Key = "shadowbloom.onboarding.howToPlaySeen";
        readonly bool _had;
        readonly int _value;

        public HowToPlaySeenScope(bool seen = true)
        {
            _had = PlayerPrefs.HasKey(Key);
            _value = PlayerPrefs.GetInt(Key, 0);
            PlayerPrefs.SetInt(Key, seen ? 1 : 0);
        }

        public void Restore()
        {
            if (_had) PlayerPrefs.SetInt(Key, _value);
            else PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
