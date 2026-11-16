using Oculus.Interaction.Input;
using UnityEngine;

namespace ShadowBloom.Meta
{
    /// <summary>
    /// Reports whether any hand (Interaction SDK <see cref="IHand"/>) is tracked with high confidence, and whether
    /// controllers are in use, to <see cref="GameStateController"/>. Matching suspends while input is unavailable and resumes automatically when it returns.
    /// </summary>
    public sealed class HandTrackingMonitor : MonoBehaviour
    {
        [SerializeField] GameStateController _game;
        [Tooltip("Hand data sources from the interaction rig. Found automatically when empty.")]
        [SerializeField] Hand[] _hands;
        [SerializeField] float _pollInterval = 0.25f;

        float _timer;

        void Start()
        {
#if !(UNITY_ANDROID && !UNITY_EDITOR)
            // Desktop/Editor without a headset: there are no hands to lose, so stay silent.
            if (!XRPresence.RuntimeLoaded)
            {
                enabled = false;
                return;
            }
#endif
            if (_hands == null || _hands.Length == 0) _hands = FindObjectsByType<Hand>(FindObjectsInactive.Exclude);
        }

        void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < _pollInterval) return;
            _timer = 0f;

            bool tracked = false;
            foreach (var h in _hands)
            {
                if (h != null && h.isActiveAndEnabled && h.IsConnected && h.IsHighConfidence)
                {
                    tracked = true;
                    break;
                }
            }

            var connected = OVRInput.GetConnectedControllers();
            bool controllers = (connected & OVRInput.Controller.Touch) != 0 || (connected & OVRInput.Controller.LTouch) != 0 ||
                               (connected & OVRInput.Controller.RTouch) != 0;
            _game.SetInputStatus(tracked, controllers);
        }
    }
}
