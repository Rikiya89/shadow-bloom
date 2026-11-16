using System;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.MRUtilityKit;
using ShadowBloom.Meta;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShadowBloom.EditorTools
{
    /// <summary>
    /// The game lives in prefabs under Assets/ShadowBloom/Prefabs and the scene holds instances of them. This converts
    /// the original generated scene in place, bakes objects that used to be created at Play time, wires the three
    /// references that point outside the game (head, camera rig, MRUK), and lets tools edit prefab assets.
    /// </summary>
    public static class ShadowBloomPrefabs
    {
        public const string Dir = ShadowBloomSceneBuilder.Root + "/Prefabs";
        public const string GamePath = Dir + "/ShadowBloomGame.prefab";
        public const string PuzzlePath = Dir + "/Puzzle.prefab";
        public const string MarkerPath = Dir + "/PlacementMarker.prefab";
        public const string GameUiPath = Dir + "/GameUI.prefab";
        public const string EventSystemPath = Dir + "/PointableCanvasModule.prefab";
        public const string GameRootName = "ShadowBloom";
        public const string EventSystemName = "Pointable Canvas Module";

        [MenuItem("Shadow Bloom/Convert Scene To Prefabs", priority = 1)]
        static void ConvertMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log("[ShadowBloom] " + ConvertScene());
        }

        /// <summary>One-time migration of the generated scene: bake, save the prefabs, keep the scene references.</summary>
        public static string ConvertScene()
        {
            var scene = OpenGameScene();
            var root = FindRoot(scene, GameRootName) ?? throw new InvalidOperationException($"Scene root '{GameRootName}' missing.");
            if (IsInstanceOf(root, GamePath)) return "Already converted.";
            var eventSystem = FindRoot(scene, EventSystemName) ?? throw new InvalidOperationException($"Scene root '{EventSystemName}' missing.");

            // Keep the scene's own wiring rather than re-deriving it.
            var head = GetRef<Transform>(root.GetComponentInChildren<SurfacePlacementController>(true), "_head");
            var cameraRig = GetRef<Transform>(root.GetComponentInChildren<DesktopDebugInput>(true), "_cameraRig");
            var mruk = GetRef<MRUK>(root.GetComponentInChildren<MrukSurfaceSource>(true), "_mruk");

            string baked = Bake(root);
            var game = CreatePrefabs(root, eventSystem);
            WireSceneReferences(game, head, cameraRig, mruk);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return $"{baked} Prefabs saved in {Dir}. Scene overrides: {string.Join(", ", SceneOverrides(game))}.";
        }

        /// <summary>Bakes Play-time objects into the scene without creating prefabs (migration checkpoint).</summary>
        public static string BakeScene()
        {
            var scene = OpenGameScene();
            var root = FindRoot(scene, GameRootName) ?? throw new InvalidOperationException($"Scene root '{GameRootName}' missing.");
            string msg = Bake(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return msg;
        }

        static string Bake(GameObject root)
        {
            var ui = root.GetComponentInChildren<WorldSpaceUIController>(true) ?? throw new InvalidOperationException("WorldSpaceUIController missing.");
            return BakeRuntimeObjects(root) + " " + ShadowBloomUiUpgrade.BakeExtraButtons(ui);
        }

        /// <summary>
        /// Adds, as real objects, what the game used to create at Play time: the table garden and the two extra audio
        /// sources. Idempotent. The runtime code keeps its fallbacks for scenes without them.
        /// </summary>
        public static string BakeRuntimeObjects(GameObject root)
        {
            var added = new List<string>();
            var game = root.GetComponentInChildren<GameStateController>(true);
            var puzzle = root.GetComponentInChildren<PuzzleController>(true);
            var audio = root.GetComponentInChildren<FeedbackAudio>(true);
            if (game == null || puzzle == null || audio == null) throw new InvalidOperationException("Game, puzzle or audio component missing.");

            var gameSo = new SerializedObject(game);
            var gardenProp = gameSo.FindProperty("_garden");
            if (gardenProp.objectReferenceValue == null)
            {
                var t = puzzle.transform.Find("Garden");
                var garden = t != null ? t.GetComponent<GardenController>() : null;
                if (garden == null)
                {
                    var go = new GameObject("Garden");
                    go.transform.SetParent(puzzle.transform, false);
                    garden = go.AddComponent<GardenController>();
                    added.Add("Garden");
                }
                gardenProp.objectReferenceValue = garden;
                gameSo.ApplyModifiedPropertiesWithoutUndo();
            }

            var audioSo = new SerializedObject(audio);
            var flat = audioSo.FindProperty("_flatSource");
            var ui = audioSo.FindProperty("_uiSource");
            var spatial = audioSo.FindProperty("_spatialSource");
            if (flat.objectReferenceValue == null)
            {
                var s = audio.GetComponent<AudioSource>();   // RequireComponent: the original flat source
                FeedbackAudio.ConfigureFlat(s);
                flat.objectReferenceValue = s;
            }
            if (ui.objectReferenceValue == null)
            {
                var s = audio.gameObject.AddComponent<AudioSource>();
                FeedbackAudio.ConfigureFlat(s);
                ui.objectReferenceValue = s;
                added.Add("UI AudioSource");
            }
            if (spatial.objectReferenceValue == null)
            {
                var t = audio.transform.Find("PuzzleAudioEmitter");
                var go = t != null ? t.gameObject : new GameObject("PuzzleAudioEmitter");
                go.transform.SetParent(audio.transform, false);
                var s = go.GetComponent<AudioSource>();
                if (s == null) s = go.AddComponent<AudioSource>();
                FeedbackAudio.ConfigureSpatial(s);
                spatial.objectReferenceValue = s;
                added.Add("PuzzleAudioEmitter");
            }
            audioSo.ApplyModifiedPropertiesWithoutUndo();
            return added.Count == 0 ? "Runtime objects already baked." : "Baked " + string.Join(", ", added) + ".";
        }

        /// <summary>Saves the nested prefabs (inner first) and connects the scene objects to them. Returns the game instance.</summary>
        public static GameObject CreatePrefabs(GameObject root, GameObject eventSystem)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder(ShadowBloomSceneBuilder.Root, "Prefabs");
            Save(root.transform.Find("SurfaceFrame")?.gameObject, PuzzlePath);
            Save(root.transform.Find("PlacementMarker")?.gameObject, MarkerPath);
            Save(root.transform.Find("UI")?.gameObject, GameUiPath);
            Save(root, GamePath);
            Save(eventSystem, EventSystemPath);
            return root;
        }

        static void Save(GameObject go, string path)
        {
            if (go == null) throw new InvalidOperationException($"Object for {path} missing.");
            if (IsInstanceOf(go, path)) return;
            PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction, out bool ok);
            if (!ok) throw new InvalidOperationException($"Saving {path} failed.");
        }

        /// <summary>The only references that leave the game prefab; they live as overrides on the scene instance.</summary>
        public static void WireSceneReferences(GameObject game, Transform head, Transform cameraRig, MRUK mruk)
        {
            SetRef(game.GetComponentInChildren<SurfacePlacementController>(true), "_head", head);
            SetRef(game.GetComponentInChildren<DesktopDebugInput>(true), "_cameraRig", cameraRig);
            SetRef(game.GetComponentInChildren<MrukSurfaceSource>(true), "_mruk", mruk);
        }

        /// <summary>Opens a prefab asset, edits the first <typeparamref name="T"/> in it and saves it.</summary>
        public static string EditPrefab<T>(string path, Func<T, string> edit) where T : Component
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var c = root.GetComponentInChildren<T>(true);
                if (c == null) throw new InvalidOperationException($"{typeof(T).Name} not found in {path}.");
                string msg = edit(c);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return msg;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static IEnumerable<string> SceneOverrides(GameObject instance)
        {
            var rootTransform = PrefabUtility.GetCorrespondingObjectFromSource(instance.transform);
            var rootObject = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            return PrefabUtility.GetPropertyModifications(instance)
                .Where(m => m.target != null && m.target != rootTransform && m.target != rootObject)
                .Select(m => $"{m.target.GetType().Name}.{m.propertyPath}");
        }

        static Scene OpenGameScene()
        {
            var active = SceneManager.GetActiveScene();
            return active.path == ShadowBloomSceneBuilder.ScenePath ? active : EditorSceneManager.OpenScene(ShadowBloomSceneBuilder.ScenePath);
        }

        static GameObject FindRoot(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

        static bool IsInstanceOf(GameObject go, string path) =>
            PrefabUtility.IsAnyPrefabInstanceRoot(go) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == path;

        static T GetRef<T>(Component c, string field) where T : Object =>
            c == null ? null : new SerializedObject(c).FindProperty(field).objectReferenceValue as T;

        static void SetRef(Component c, string field, Object value)
        {
            if (c == null) throw new InvalidOperationException($"Component with {field} missing.");
            var so = new SerializedObject(c);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }
    }
}
