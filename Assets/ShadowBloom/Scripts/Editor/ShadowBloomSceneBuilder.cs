using System.Linq;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using Oculus.Interaction.OVR.Editor.QuickActions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ShadowBloom.EditorTools
{
    /// <summary>
    /// Rebuilds the scene from the prefabs in Assets/ShadowBloom/Prefabs. The prefabs are the source of truth and are
    /// edited by hand; a rebuild adds only what lives outside them: the OVR camera rig with the Interaction SDK rig
    /// (public QuickActions API), MRUK, and the three scene references (head, camera rig, MRUK).
    /// </summary>
    public static class ShadowBloomSceneBuilder
    {
        public const string Root = "Assets/ShadowBloom";
        public const string ScenePath = Root + "/Scenes/ShadowBloom.unity";
        const string MrukPrefab = "Packages/com.meta.xr.mrutilitykit/Core/Tools/MRUK.prefab";

        [MenuItem("Shadow Bloom/Build Scene", priority = 0)]
        public static void BuildSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene();
        }

        /// <summary>Entry point for menu and automation: rebuilds the scene from the prefabs. Returns a short report.</summary>
        public static string BuildScene(string scenePath = ScenePath)
        {
            EnsureTmpEssentials();
            // NewScene(Single) unloads unreferenced assets: check the prefabs now, load them after it.
            foreach (var path in new[] { ShadowBloomPrefabs.GamePath, ShadowBloomPrefabs.EventSystemPath })
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    throw new System.InvalidOperationException($"{path} missing. Restore Assets/ShadowBloom/Prefabs from git.");
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- XR rig (OVRCameraRig + ISDK comprehensive interaction rig) -----------------------------
            OVRQuickActionsAPI.AddOVRInteractionRig(generateAsEditableCopy: false);
            var cameraRig = Object.FindAnyObjectByType<OVRCameraRig>();
            ConfigureCameraRig(cameraRig);
            int locomotionDisabled = DisableLocomotion(cameraRig.transform);

            // --- MRUK ------------------------------------------------------------------------------------
            var mrukGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MrukPrefab));
            var mruk = mrukGo.GetComponent<MRUK>();
            // Device only: a prefab fallback would invent a table on headsets without a room scan.
            // (MrukSurfaceSource swaps in a simulated room in the Editor only.)
            mruk.SceneSettings.DataSource = MRUK.SceneDataSource.Device;
            EditorUtility.SetDirty(mruk);

            // --- Game (prefabs are the source of truth) ---------------------------------------------------
            // Instances are named after the asset file; keep the scene names that tools and tests use.
            var game = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ShadowBloomPrefabs.GamePath), scene);
            game.name = ShadowBloomPrefabs.GameRootName;
            if (Object.FindAnyObjectByType<PointableCanvasModule>() == null)
            {
                var eventSystem = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ShadowBloomPrefabs.EventSystemPath), scene);
                eventSystem.name = ShadowBloomPrefabs.EventSystemName;
            }
            ShadowBloomPrefabs.WireSceneReferences(game, cameraRig.centerEyeAnchor, cameraRig.transform, mruk);

            EditorSceneManager.SaveScene(scene, scenePath);
            if (scenePath == ScenePath) RegisterInBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            return $"Built {scenePath} from {ShadowBloomPrefabs.GamePath}; disabled {locomotionDisabled} locomotion objects.";
        }

        // Seated MR: turning/teleporting would slide virtual content off the real table, so the rig's
        // locomotion interactors (hand micro-gestures and controller sticks) and player body are deactivated.
        static readonly string[] LocomotionObjects =
        {
            "LocomotionControllerInteractorGroup", "MicroGesturesLocomotionHandInteractorGroup", "PlayerController"
        };

        static int DisableLocomotion(Transform rig)
        {
            int count = 0;
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (!LocomotionObjects.Contains(t.name) || !t.gameObject.activeSelf) continue;
                t.gameObject.SetActive(false);
                count++;
            }
            return count;
        }

        // ---------------------------------------------------------------------------------------------
        static void ConfigureCameraRig(OVRCameraRig rig)
        {
            var manager = rig.GetComponent<OVRManager>();
            if (manager != null)
            {
                manager.isInsightPassthroughEnabled = true;
                EditorUtility.SetDirty(manager);
            }
            if (rig.GetComponent<OVRPassthroughLayer>() == null) rig.gameObject.AddComponent<OVRPassthroughLayer>();

            // Transparent clear so the passthrough underlay shows; no post-processing on the XR camera.
            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.nearClipPlane = 0.03f;
                var data = cam.GetComponent<UniversalAdditionalCameraData>() ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                EditorUtility.SetDirty(cam);
            }
        }

        static void EnsureTmpEssentials()
        {
            if (TMP_Settings.instance != null) return;
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh();
        }

        static void RegisterInBuildSettings(string path)
        {
            // Only the game scene ships; others (e.g. the template's SampleScene) stay listed but disabled.
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).Select(s => new EditorBuildSettingsScene(s.path, false)).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
