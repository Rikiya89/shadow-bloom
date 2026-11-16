using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShadowBloom
{
    public enum PlacementStatus
    {
        Searching,
        Ready,
        Manual
    }

    /// <summary>
    /// Owns the puzzle's surface frame (up = surface normal). Room data adapters report candidate surfaces;
    /// if none arrive in time, or permission is denied, the puzzle is placed head-relative and the player
    /// drags a marker onto their real table. Nothing here waits indefinitely.
    /// </summary>
    public sealed class SurfacePlacementController : MonoBehaviour
    {
        [SerializeField] Transform _surfaceFrame;
        [SerializeField] Transform _head;
        [Tooltip("Grabbable marker used for manual placement. Not parented to the surface frame.")]
        [SerializeField] Transform _manualHandle;
        [SerializeField] float _searchTimeout = 8f;
        [Tooltip("Longest wait for a tracked head pose before placing anyway (e.g. eye-level tracking origin).")]
        [SerializeField] float _headWaitTimeout = 5f;
        [Tooltip("Floor-level origin: an untracked head sits at the rig origin, a seated head is ~1.1 m up. Metres.")]
        [SerializeField] float _minTrackedHeadHeight = 0.5f;

        public PlacementStatus Status { get; private set; } = PlacementStatus.Searching;
        public PuzzlePlacement Placement { get; private set; }
        public string Message { get; private set; } = "Looking for a table...";
        public bool FoundRealSurface { get; private set; }

        /// <summary>Raised whenever the surface frame moves or the status changes.</summary>
        public event Action Changed;

        float _searchStart;
        bool _handleMoved;
        Vector3 _handleStart;
        bool _headReady;
        // The fallback was solved from an untracked head (rig origin), so it sits ~0.5 m under the floor until re-solved.
        bool _fallbackUntracked;
        readonly List<SurfaceCandidate> _pendingCandidates = new List<SurfaceCandidate>();
        bool _hasPendingCandidates;
        string _pendingUnavailable;

        public Transform Head => _head != null ? _head : (Camera.main != null ? Camera.main.transform : transform);

        void Start()
        {
            if (Status == PlacementStatus.Searching) BeginSearch();
        }

        public void BeginSearch()
        {
            _searchStart = Time.unscaledTime;
            _handleMoved = false;
            _headReady = false;
            _hasPendingCandidates = false;
            _pendingUnavailable = null;
            FoundRealSurface = false;
            Status = PlacementStatus.Searching;
            Message = "Looking for a table...";
            ShowHandle(false);
            // Provisional placement so something sensible is visible while searching.
            ApplyPlacement(SurfacePlacementSolver.Solve(Fallback(), Head.position, Head.forward));
            Changed?.Invoke();
        }

        void Update()
        {
            if (Status == PlacementStatus.Searching && !_headReady)
            {
                if (HeadReady()) OnHeadReady();
            }
            else if (Status == PlacementStatus.Searching && Time.unscaledTime - _searchStart > _searchTimeout)
            {
                UseFallback("No table found.");
            }
            else if (Status == PlacementStatus.Manual && _manualHandle != null)
            {
                // The head pose can arrive after the wait timed out: bring an untouched marker up to the real table height.
                if (_fallbackUntracked && !_handleMoved && HeadTracked()) UseFallback(Message);
                FollowHandle();
            }
        }

        /// <summary>From a room-data adapter. Late results are accepted until the player moves the marker.</summary>
        public void ReportCandidates(IReadOnlyList<SurfaceCandidate> candidates)
        {
            if (Status == PlacementStatus.Ready) return;
            if (Status == PlacementStatus.Manual && _handleMoved) return;
            if (Status == PlacementStatus.Searching && !HeadReady())
            {
                // Room data can arrive before the headset reports a pose; scoring against the rig origin
                // would reject every table and drop the fallback near the floor.
                _pendingCandidates.Clear();
                _pendingCandidates.AddRange(candidates);
                _hasPendingCandidates = true;
                return;
            }

            if (!SurfacePlacementSolver.TrySelect(candidates, Head.position, Head.forward, out var placement, out int best))
            {
                UseFallback("No table within reach.");
                return;
            }

            FoundRealSurface = true;
            if (_manualHandle != null) FollowHandle();
            ShowHandle(false);
            Status = PlacementStatus.Ready;
            Message = candidates[best].Kind == SurfaceKind.Table ? "Table found." : "Surface found.";
            ApplyPlacement(placement);
            LogPlacement();
            Changed?.Invoke();
        }

        public void ReportUnavailable(string reason)
        {
            if (Status != PlacementStatus.Searching) return;
            if (!HeadReady())
            {
                _pendingUnavailable = reason;
                return;
            }
            UseFallback(reason);
        }

        bool HeadReady()
        {
            if (_headReady) return true;
            string reason = HeadReadyReason(XRPresence.RuntimeLoaded, Head.localPosition.y, _minTrackedHeadHeight,
                Time.unscaledTime - _searchStart, _headWaitTimeout);
            if (reason == null) return false;
            _headReady = true;
            Debug.Log($"[ShadowBloom] head ready ({reason}): head local y {Head.localPosition.y:F2}");
            return true;
        }

        /// <summary>
        /// Why the head pose can be trusted for a head-relative placement, or null to keep waiting. Gated on a loaded
        /// runtime, not XRSettings.isDeviceActive: on Quest that stays false for the first seconds of the session, which
        /// placed the fallback from the rig origin, under the floor (device log 2026-10-31: head local y 0.00).
        /// </summary>
        public static string HeadReadyReason(bool xrRuntime, float headLocalY, float minTrackedHeight, float waited, float timeout) =>
            !xrRuntime ? "no XR runtime"
            : headLocalY > minTrackedHeight ? "tracked"
            : waited > timeout ? "timeout"
            : null;

        bool HeadTracked() => Head.localPosition.y > _minTrackedHeadHeight;

        /// <summary>First real head pose: redo the provisional pose, restart the timeout, apply deferred reports.</summary>
        void OnHeadReady()
        {
            _searchStart = Time.unscaledTime;
            ApplyPlacement(SurfacePlacementSolver.Solve(Fallback(), Head.position, Head.forward));
            Changed?.Invoke();
            if (_hasPendingCandidates)
            {
                _hasPendingCandidates = false;
                ReportCandidates(_pendingCandidates);
            }
            else if (_pendingUnavailable != null)
            {
                var reason = _pendingUnavailable;
                _pendingUnavailable = null;
                UseFallback(reason);
            }
        }

        /// <summary>Player asked to move the puzzle: switch to marker placement starting at the current spot.</summary>
        public void BeginManual(string message = "Choose a new spot for the puzzle.")
        {
            FoundRealSurface = false;
            Status = PlacementStatus.Manual;
            Message = message;
            if (_manualHandle != null)
            {
                _manualHandle.SetPositionAndRotation(Placement.Position + Vector3.up * 0.01f, Quaternion.identity);
                _handleStart = _manualHandle.position;
            }
            _handleMoved = false;
            ShowHandle(true);
            Changed?.Invoke();
        }

        public void ConfirmManual()
        {
            if (Status != PlacementStatus.Manual) return;
            if (_manualHandle != null) FollowHandle();
            // Height is fixed while dragging (no mesh rebuilds per frame); settle it for the chosen table now.
            var placement = Placement;
            placement.PivotHeight = SurfacePlacementSolver.PivotHeightFor(placement.PuzzleRadius,
                Vector3.Dot(Head.position - placement.Position, placement.Rotation * Vector3.up));
            ApplyPlacement(placement);
            ShowHandle(false);
            Status = PlacementStatus.Ready;
            Message = "Placed.";
            LogPlacement();
            Changed?.Invoke();
        }

        void UseFallback(string reason)
        {
            ApplyPlacement(SurfacePlacementSolver.Solve(Fallback(), Head.position, Head.forward));
            // Desktop/editor heads never count as tracked by height; only a loaded XR runtime can still deliver a pose.
            _fallbackUntracked = XRPresence.RuntimeLoaded && !HeadTracked();
            LogPlacement();
            BeginManual(reason);
        }

        void LogPlacement()
        {
            var p = Placement;
            Debug.Log($"[ShadowBloom] placement {p.Kind}: surface {Head.position.y - p.Position.y:F2} m below eyes, " +
                      $"head local y {Head.localPosition.y:F2}, radius {p.PuzzleRadius:F2}, pivot {p.PivotHeight:F2}");
        }

        SurfaceCandidate Fallback() => SurfacePlacementSolver.HeadRelativeFallback(Head.position, Head.forward);

        void FollowHandle()
        {
            var p = _manualHandle.position;
            if (!_handleMoved && (p - _handleStart).sqrMagnitude > 0.02f * 0.02f) _handleMoved = true;
            if ((p - Placement.Position).sqrMagnitude < 1e-6f) return;

            var toHandle = Vector3.ProjectOnPlane(p - Head.position, Vector3.up);
            if (toHandle.sqrMagnitude < 1e-4f) toHandle = Vector3.ProjectOnPlane(Head.forward, Vector3.up);
            if (toHandle.sqrMagnitude < 1e-4f) toHandle = Vector3.forward;
            var f = toHandle.normalized;
            var placement = new PuzzlePlacement
            {
                Position = p,
                Rotation = Quaternion.LookRotation(f, Vector3.up),
                PuzzleRadius = Placement.PuzzleRadius > 0f ? Placement.PuzzleRadius : SurfacePlacementSolver.MaxRadius,
                PivotHeight = Placement.PivotHeight > 0f ? Placement.PivotHeight : SurfacePlacementSolver.PivotHeightFor(SurfacePlacementSolver.MaxRadius),
                Kind = SurfaceKind.Manual,
                GardenBand = Placement.GardenBand
            };
            ApplyPlacement(placement);
            Changed?.Invoke();
        }

        void ApplyPlacement(PuzzlePlacement placement)
        {
            Placement = placement;
            _surfaceFrame.SetPositionAndRotation(placement.Position, placement.Rotation);
        }

        void ShowHandle(bool show)
        {
            if (_manualHandle != null) _manualHandle.gameObject.SetActive(show);
        }
    }
}
