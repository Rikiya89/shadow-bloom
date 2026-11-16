using Oculus.Interaction;
using UnityEngine;

namespace ShadowBloom.Meta
{
    /// <summary>
    /// Interaction SDK one-grab transformer that only rotates the puzzle about its fixed pivot.
    /// The object follows the grabbing hand's change in orientation (with optional gain and light smoothing),
    /// so a pinch-and-twist reads as "turning the object in my fingers". Position never changes, which keeps
    /// the projection centred on the target. Plugged into <see cref="Grabbable"/> by the scene builder.
    /// </summary>
    public sealed class RotateOnlyTransformer : MonoBehaviour, ITransformer
    {
        [SerializeField] PuzzleController _puzzle;
        [Tooltip("Hand rotation multiplier. >1 reduces wrist strain for large turns; 1 is most precise.")]
        [SerializeField, Range(0.5f, 2f)] float _gain = 1.15f;
        [Tooltip("Exponential smoothing rate (1/s). Higher = more responsive, lower = steadier.")]
        [SerializeField, Range(4f, 60f)] float _smoothing = 22f;

        IGrabbable _grabbable;
        Quaternion _grabStart;
        Quaternion _objectStart;
        bool _active;

        public void Initialize(IGrabbable grabbable) => _grabbable = grabbable;

        void OnEnable()
        {
            if (_puzzle != null) _puzzle.PoseReset += OnPoseReset;
        }

        // Retry while holding: continue from the reset pose instead of snapping back to the old grab.
        void OnPoseReset()
        {
            if (_active && _grabbable != null && _grabbable.GrabPoints.Count > 0) Rebase();
        }

        public void BeginTransform()
        {
            if (_grabbable.GrabPoints.Count == 0) return;
            Rebase();
            _active = true;
            if (_puzzle != null) _puzzle.NotifyGrab(true);
        }

        public void UpdateTransform()
        {
            if (!_active || _grabbable.GrabPoints.Count == 0) return;
            var target = _grabbable.Transform;

            // While input is disabled (paused / solved) keep rebasing so resuming never causes a jump.
            if (_puzzle != null && !_puzzle.InputEnabled)
            {
                Rebase();
                return;
            }

            var delta = _grabbable.GrabPoints[0].rotation * Quaternion.Inverse(_grabStart);
            delta.ToAngleAxis(out float angle, out var axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || Mathf.Abs(angle) < 1e-4f) delta = Quaternion.identity;
            else delta = Quaternion.AngleAxis(angle * _gain, axis);

            var desired = delta * _objectStart;
            var smoothed = Quaternion.Slerp(target.rotation, desired, 1f - Mathf.Exp(-_smoothing * Time.deltaTime));
            if (_puzzle != null) _puzzle.SetPivotWorldRotation(smoothed);
            else target.rotation = smoothed;
        }

        public void EndTransform()
        {
            if (!_active) return;
            _active = false;
            if (_puzzle != null) _puzzle.NotifyGrab(false);
        }

        void Rebase()
        {
            _grabStart = _grabbable.GrabPoints[0].rotation;
            _objectStart = _grabbable.Transform.rotation;
        }

        void OnDisable()
        {
            if (_puzzle != null) _puzzle.PoseReset -= OnPoseReset;
            EndTransform();
        }
    }
}
