using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace ShadowBloom.Meta
{
    /// <summary>
    /// Lets a hand that is pointing at the panel press its buttons while the puzzle can be grabbed.
    ///
    /// In the Interaction SDK rig only one interactor per hand may hover, and hand grab outranks the ray. The
    /// puzzle's grab volume is large (1.2 puzzle radii around the pivot), so during play a hand near the puzzle
    /// always hovers it: the laser disappears and a pinch aimed at Pause, Retry or Move grabs the puzzle instead
    /// (device log 2026-11-11: runs of 0.2 s grabs with no button press). This filter makes the puzzle refuse a
    /// hand whose pointer ray lands on the visible panel, so that hand's ray interactor takes over. A hand that
    /// already holds the puzzle is never refused, so turning the puzzle past the panel does not drop it.
    /// Installed on the puzzle's grab interactables by <see cref="PuzzleHoverFeedback"/>.
    /// </summary>
    public sealed class PanelAimGrabFilter : MonoBehaviour, IGameObjectFilter
    {
        [Tooltip("Extra room around the panel that still counts as aiming at it, metres.")]
        [SerializeField] float _margin = 0.025f;
        [Tooltip("Rays that reach the panel further away than this are ignored, metres.")]
        [SerializeField] float _maxDistance = 1.5f;

        WorldSpaceUIController _panel;
        readonly Vector3[] _corners = new Vector3[4];
        readonly Dictionary<GameObject, HandGrabInteractor> _hands = new Dictionary<GameObject, HandGrabInteractor>();
        readonly HashSet<GameObject> _yielding = new HashSet<GameObject>();

        /// <summary>True lets the interactor hover and grab the puzzle; false hands the hand over to its ray.</summary>
        public bool Filter(GameObject interactor)
        {
            bool aiming = AimsAtPanel(interactor);
            // One log line per change, for device sessions (adb logcat -s Unity | grep ShadowBloom).
            if (aiming ? _yielding.Add(interactor) : _yielding.Remove(interactor))
                Debug.Log($"[ShadowBloom] {interactor.name} {(aiming ? "aims at the panel: grab yields to the ray" : "left the panel: grab is back")}");
            return !aiming;
        }

        bool AimsAtPanel(GameObject interactor)
        {
            if (!_hands.TryGetValue(interactor, out var hand))
            {
                hand = interactor.GetComponent<HandGrabInteractor>();   // null for controller grab: never filtered
                _hands[interactor] = hand;
            }
            if (hand == null || hand.HasSelectedInteractable || hand.Hand == null) return false;

            if (_panel == null) _panel = FindAnyObjectByType<WorldSpaceUIController>();
            if (_panel == null || !_panel.isActiveAndEnabled || !_panel.IsVisible) return false;
            if (!hand.Hand.GetPointerPose(out var pose)) return false;

            ((RectTransform)_panel.transform).GetWorldCorners(_corners);   // bottom-left, top-left, top-right, bottom-right
            return PanelPlacement.RayHitsPanel(pose.position, pose.forward, _corners[0], _corners[1], _corners[3],
                _margin, _maxDistance);
        }
    }
}
