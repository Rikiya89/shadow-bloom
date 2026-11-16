using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace ShadowBloom.Meta
{
    /// <summary>
    /// Forwards the Interaction SDK hover state of the puzzle's grab interactables (hand grab, pinch grab) to
    /// <see cref="PuzzleController.NotifyHover"/>, so the curve brightens when a hand is close enough to grab it.
    /// It only reads state; the one change to grabbing it installs is <see cref="PanelAimGrabFilter"/>.
    /// Select/grab is still reported by RotateOnlyTransformer.
    /// </summary>
    public sealed class PuzzleHoverFeedback : MonoBehaviour
    {
        [SerializeField] PuzzleController _puzzle;
        [Tooltip("Where to look for interactables. Defaults to this object (the puzzle pivot).")]
        [SerializeField] Transform _searchRoot;

        readonly List<IInteractableView> _views = new List<IInteractableView>();

        void Start()
        {
            var root = _searchRoot != null ? _searchRoot : transform;
            // The puzzle steps aside for a hand that points at the panel (see PanelAimGrabFilter).
            var panelAim = GetComponent<PanelAimGrabFilter>();
            if (panelAim == null) panelAim = gameObject.AddComponent<PanelAimGrabFilter>();
            var filters = new List<IGameObjectFilter> { panelAim };
            foreach (var grab in root.GetComponentsInChildren<Oculus.Interaction.HandGrab.HandGrabInteractable>(true))
                grab.InjectOptionalInteractorFilters(filters);
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb is IInteractableView view)
                {
                    _views.Add(view);
                    view.WhenStateChanged += OnStateChanged;
                }
            }
        }

        void OnDestroy()
        {
            foreach (var v in _views) v.WhenStateChanged -= OnStateChanged;
        }

        void OnStateChanged(InteractableStateChangeArgs args)
        {
            if (_puzzle == null) return;
            bool hovered = false;
            foreach (var v in _views)
            {
                if (v.State == InteractableState.Hover) { hovered = true; break; }
            }
            _puzzle.NotifyHover(hovered);
        }
    }
}
