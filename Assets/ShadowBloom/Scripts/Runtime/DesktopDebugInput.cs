using UnityEngine;
using UnityEngine.InputSystem;

namespace ShadowBloom
{
    /// <summary>
    /// Headset-free test path (Editor / desktop). Activates only when no XR device is running, puts the camera
    /// rig at a seated pose and maps mouse drag to rotation and keys to the game commands.
    /// Not used on Quest: with an active XR device the component disables itself.
    /// </summary>
    public sealed class DesktopDebugInput : MonoBehaviour
    {
        [SerializeField] GameStateController _game;
        [SerializeField] PuzzleController _puzzle;
        [SerializeField] Transform _cameraRig;
        [Tooltip("Seated eye pose for desktop play. Default matches the table in MRUK room Office08 at (-0.18, 0.66, 1.89); re-measure if the editor room prefab changes.")]
        [SerializeField] Vector3 _seatedEyePosition = new Vector3(0f, 1.2f, 0f);
        [SerializeField] float _seatedYaw;
        [SerializeField] float _seatedPitch = 38f;
        [SerializeField] float _degreesPerPixel = 0.35f;
        [Tooltip("Force on even with an XR device (for Link testing with mouse).")]
        [SerializeField] bool _forceEnable;
        [Tooltip("Stand-ins for the real room (e.g. a virtual table top), shown only in desktop mode.")]
        [SerializeField] GameObject[] _desktopOnly;

        public bool Active { get; private set; }
        bool _dragging;
        bool _showHelp = true;

        // Awake (not Start) so the seated pose exists before placement solves its first head-relative pose.
        void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Active = false;  // never on the headset, even if XR has not started yet
#else
            Active = _forceEnable || !XRPresence.RuntimeLoaded;
#endif
            if (_desktopOnly != null)
            {
                foreach (var go in _desktopOnly) if (go != null) go.SetActive(Active);
            }
            if (!Active)
            {
                enabled = false;
                return;
            }
            if (_cameraRig == null) _cameraRig = FindRigFallback();
            if (_cameraRig != null)
            {
                // The camera is a child of the rig; MRUK/placement read the head pose, so this is a real seated test.
                _cameraRig.SetPositionAndRotation(_seatedEyePosition, Quaternion.Euler(_seatedPitch, _seatedYaw, 0f));
            }
        }

        /// <summary>
        /// Used when the prefab is dropped into a scene where Rig was not assigned. Return the transform to move to the
        /// seated pose, or null to leave the camera where it is.
        /// </summary>
        Transform FindRigFallback()
        {
            // TODO(you): choose the fallback policy (see PLAN.md §2 learning-mode contribution).
            return null;
        }

        void Update()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse != null) HandleMouse(mouse);
            if (kb == null) return;

            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) _game.Primary();
            if (kb.rKey.wasPressedThisFrame) _game.Retry();
            if (kb.pKey.wasPressedThisFrame) _game.TogglePause();
            if (kb.mKey.wasPressedThisFrame) _game.Reposition();
            if (kb.dKey.wasPressedThisFrame) _game.StartDaily();
            if (kb.aKey.wasPressedThisFrame) _game.ToggleAssist();
            if (kb.hKey.wasPressedThisFrame) _showHelp = !_showHelp;
#if UNITY_EDITOR || DEBUG
            if (kb.sKey.wasPressedThisFrame) _puzzle.DebugSetSolution();
#endif
        }

        void HandleMouse(Mouse mouse)
        {
            var cam = Camera.main;
            if (cam == null) return;

            // Mouse-over stands in for a hand entering grab range.
            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            bool over = Physics.Raycast(ray, out var hit, 5f) && hit.transform == _puzzle.Pivot;
            _puzzle.NotifyHover(over || _dragging);

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _dragging = true;
                _puzzle.NotifyGrab(true);
            }
            if (mouse.leftButton.wasReleasedThisFrame && _dragging)
            {
                _dragging = false;
                _puzzle.NotifyGrab(false);
            }
            if (!_dragging) return;

            // Trackball: horizontal drag spins about the camera's up, vertical drag tumbles about its right.
            var d = mouse.delta.ReadValue() * _degreesPerPixel;
            var delta = Quaternion.AngleAxis(-d.x, cam.transform.up) * Quaternion.AngleAxis(d.y, cam.transform.right);
            _puzzle.ApplyWorldRotationDelta(delta);
        }

        void OnGUI()
        {
            if (!Active || !_showHelp) return;
            var flow = _game.Flow;
            string text =
                $"SHADOW BLOOM desktop debug   state: {flow.State}   stage: {flow.StageIndex + 1}/{flow.StageCount}\n" +
                $"error: {_puzzle.Error:F3}  tolerance: {_puzzle.Tolerance:F3}  " +
                $"alignment: {_puzzle.Alignment:F2}  hold: {_puzzle.HoldProgress:P0}\n" +
                "drag LMB: rotate   Space: primary   R: retry   P: pause   M: move puzzle   S: snap to solution   H: hide";
            GUI.Label(new Rect(12, 12, 900, 70), text);
        }
    }
}
