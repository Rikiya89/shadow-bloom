using System;
using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// Player comfort settings, stored apart from progress (its own PlayerPrefs keys) so saves are never touched.
    /// </summary>
    public static class GameSettings
    {
        const string MutedKey = "shadowbloom.settings.muted";
        const string ReducedMotionKey = "shadowbloom.settings.reducedMotion";
        const string AssistKey = "shadowbloom.settings.assist";
        const string HowToPlaySeenKey = "shadowbloom.onboarding.howToPlaySeen";

        /// <summary>Assist multiplies each stage's tolerance by this and its hold time by <see cref="AssistHoldScale"/>.</summary>
        public const float AssistLeniency = 1.5f;
        public const float AssistHoldScale = 0.6f;

        public static event Action Changed;

        public static bool Muted
        {
            get => PlayerPrefs.GetInt(MutedKey, 0) == 1;
            set => Set(MutedKey, value);
        }

        /// <summary>No spin, twist, overshoot, breathing or pulsing; feedback stays visible as steady brightness.</summary>
        public static bool ReducedMotion
        {
            get => PlayerPrefs.GetInt(ReducedMotionKey, 0) == 1;
            set => Set(ReducedMotionKey, value);
        }

        /// <summary>Looser match and shorter hold, for unsteady hands, limited wrist range or first-time players.</summary>
        public static bool Assist
        {
            get => PlayerPrefs.GetInt(AssistKey, 0) == 1;
            set => Set(AssistKey, value);
        }

        /// <summary>The how-to-play cards were read to the end once; after that they open only from the pause panel.</summary>
        public static bool HowToPlaySeen
        {
            get => PlayerPrefs.GetInt(HowToPlaySeenKey, 0) == 1;
            set => Set(HowToPlaySeenKey, value);
        }

        static void Set(string key, bool value)
        {
            if (PlayerPrefs.GetInt(key, 0) == (value ? 1 : 0)) return;
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
