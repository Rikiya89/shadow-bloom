using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ShadowBloom.Meta
{
    /// <summary>
    /// Feeds MRUK scene data into <see cref="SurfacePlacementController"/>: every horizontal anchor surface
    /// becomes a candidate (tables preferred by the solver). On device this component loads the room itself
    /// instead of MRUK's startup load, because that one opens Space Setup whenever no room comes back, which with
    /// a seated (stationary) boundary is every launch. Denied permission or no scan goes straight to manual
    /// placement, and the placement controller's own timeout covers anything else, so this never blocks.
    ///
    /// In the Editor (no headset) it can swap MRUK's data source to a bundled room prefab before MRUK.Awake,
    /// which exercises the real anchor-to-placement path without a device.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class MrukSurfaceSource : MonoBehaviour
    {
        [SerializeField] SurfacePlacementController _placement;
        [SerializeField] MRUK _mruk;
        [Tooltip("Editor only: room prefab used instead of device data. Leave empty to keep MRUK's settings.")]
        [SerializeField] GameObject _editorRoomPrefab;

        readonly List<SurfaceCandidate> _candidates = new List<SurfaceCandidate>();
        bool _reported;
        bool _loadFromDevice;

        void Awake()
        {
            if (_mruk == null) _mruk = FindAnyObjectByType<MRUK>();
#if UNITY_EDITOR
            // With an XR runtime active in the Editor (Meta XR Simulator) keep device data, so the simulator's
            // synthetic room drives placement. Without one (desktop debug path) use the bundled room prefab.
            bool xrRuntime = XRPresence.RuntimeLoaded;
            if (_mruk != null && _editorRoomPrefab != null && _mruk.SceneSettings != null && !xrRuntime)
            {
                _mruk.SceneSettings.DataSource = MRUK.SceneDataSource.Prefab;
                _mruk.SceneSettings.RoomPrefabs = new[] { _editorRoomPrefab };
            }
            if (_mruk != null && _mruk.SceneSettings != null)
                Debug.Log($"[ShadowBloom] MRUK data source: {_mruk.SceneSettings.DataSource} (XR runtime loaded: {xrRuntime})");
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            // Runs before MRUK.Awake (execution order), which reads this flag.
            if (_mruk != null && _mruk.SceneSettings != null && _mruk.SceneSettings.DataSource == MRUK.SceneDataSource.Device)
            {
                _mruk.SceneSettings.LoadSceneOnStartup = false;
                _loadFromDevice = true;
            }
#endif
        }

        void Start()
        {
            if (_mruk == null || MRUK.Instance == null)
            {
                _placement.ReportUnavailable("Room data unavailable.");
                return;
            }
            MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
            if (_loadFromDevice) RequestPermissionThenLoad();
        }

        void RequestPermissionThenLoad()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            {
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => LoadFromDevice();
                callbacks.PermissionDenied += _ => ReportUnavailable("Room access was denied.");
                Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission, callbacks);
                return;
            }
#endif
            LoadFromDevice();
        }

        async void LoadFromDevice()
        {
            var result = await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
            if (this == null || result == MRUK.LoadDeviceResult.Success) return;  // success arrives via OnSceneLoaded
            ReportUnavailable(result == MRUK.LoadDeviceResult.NoScenePermission
                ? "Room access was denied."
                : "No room scan found.");
        }

        void ReportUnavailable(string reason)
        {
            if (_reported) return;
            _reported = true;
            _placement.ReportUnavailable(reason);
        }

        void OnDestroy()
        {
            if (_mruk != null) _mruk.SceneLoadedEvent.RemoveListener(OnSceneLoaded);
        }

        void OnSceneLoaded()
        {
            if (_reported) return;
            _reported = true;

            var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            if (room == null)
            {
                _placement.ReportUnavailable("No room scan found.");
                return;
            }

            _candidates.Clear();
            foreach (var anchor in room.Anchors)
            {
                if (anchor != null && TryMakeCandidate(anchor, out var c)) _candidates.Add(c);
            }
            _placement.ReportCandidates(_candidates);
        }

        const MRUKAnchor.SceneLabels HorizontalFurniture =
            MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.OTHER |
            MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.BED;

        static bool TryMakeCandidate(MRUKAnchor anchor, out SurfaceCandidate candidate)
        {
            candidate = default;
            if (!anchor.HasAnyLabel(HorizontalFurniture)) return false;
            var kind = anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) ? SurfaceKind.Table : SurfaceKind.OtherHorizontal;
            var t = anchor.transform;

            if (anchor.PlaneRect.HasValue)
            {
                // A plane rect lies in the anchor's local XY, so its normal is local +/-Z; take the upward one.
                var rect = anchor.PlaneRect.Value;
                var normal = t.forward;
                if (Vector3.Dot(normal, Vector3.up) < 0f) normal = -normal;
                var center = t.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0f));
                var scale = t.lossyScale;
                var size = new Vector2(rect.width * Mathf.Abs(scale.x), rect.height * Mathf.Abs(scale.y));
                candidate = new SurfaceCandidate(center, normal, t.right, t.up, size, kind);
                return true;
            }

            if (anchor.VolumeBounds.HasValue)
            {
                // No plane: use the volume face whose local axis points most upward.
                var b = anchor.VolumeBounds.Value;
                Vector3[] axes = { t.right, t.up, t.forward };
                int up = 0;
                float best = 0f;
                for (int i = 0; i < 3; i++)
                {
                    float d = Mathf.Abs(Vector3.Dot(axes[i], Vector3.up));
                    if (d > best) { best = d; up = i; }
                }
                float sign = Mathf.Sign(Vector3.Dot(axes[up], Vector3.up));
                var localTop = b.center;
                localTop[up] += sign * b.extents[up];
                int a0 = (up + 1) % 3, a1 = (up + 2) % 3;
                var size = new Vector2(b.size[a0], b.size[a1]);
                candidate = new SurfaceCandidate(t.TransformPoint(localTop), axes[up] * sign, axes[a0], axes[a1], size, kind);
                return true;
            }
            return false;
        }
    }
}
