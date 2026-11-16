using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ShadowBloom.EditorTools
{
    /// <summary>
    /// Batch-mode entry points (Unity -batchmode -executeMethod ...). Each logs a one-line result and exits
    /// with 0 on success, 1 on failure, so scripts and CI can rely on the exit code.
    /// </summary>
    public static class ShadowBloomBatch
    {
        public const string ApkPath = "Builds/Android/ShadowBloom.apk";

        public static void BuildScene() => Run(() => ShadowBloomSceneBuilder.BuildScene());

        /// <summary>Applies the spatial UI design system to GameUI.prefab in place (idempotent).</summary>
        public static void UpgradeUi() => Run(() =>
        {
            string msg = ShadowBloomPrefabs.EditPrefab<WorldSpaceUIController>(ShadowBloomPrefabs.GameUiPath, ShadowBloomUiUpgrade.Upgrade);
            AssetDatabase.SaveAssets();
            return msg;
        });

        /// <summary>Builds the Source Serif 4 TMP font assets and assigns them to UiTheme (idempotent).</summary>
        public static void BuildUiFonts() => Run(ShadowBloomFonts.Build);

        /// <summary>Builds the Noto Serif JP atlases from the Japanese UI copy and adds them as fallbacks (idempotent).</summary>
        public static void BuildJapaneseUiFont() => Run(ShadowBloomFonts.BuildJapanese);

        /// <summary>Writes the Swiss line-art geometry palette into the puzzle prefab, stages and materials (idempotent).</summary>
        public static void ApplySwissGeometry() => Run(SwissGeometryTools.Apply);

        public static void SyncStageCatalog() => Run(StageCatalogTools.Sync);

        /// <summary>Adds only the missing extra panel buttons (Daily, Assist, How to play) to GameUI.prefab (idempotent).</summary>
        public static void BakeUiButtons() => Run(() =>
        {
            string msg = ShadowBloomPrefabs.EditPrefab<WorldSpaceUIController>(ShadowBloomPrefabs.GameUiPath, ShadowBloomUiUpgrade.BakeExtraButtons);
            AssetDatabase.SaveAssets();
            return msg;
        });

        /// <summary>Bakes Play-time objects (garden, audio sources, Daily/Assist) into the scene; no prefabs.</summary>
        public static void BakeScene() => Run(ShadowBloomPrefabs.BakeScene);

        /// <summary>One-time migration: the scene's game hierarchy becomes prefab instances (see ShadowBloomPrefabs).</summary>
        public static void ConvertToPrefabs() => Run(ShadowBloomPrefabs.ConvertScene);

        public const string RebuildCheckPath = "Assets/ShadowBloom/Scenes/_RebuildCheck.unity";

        /// <summary>
        /// Rebuilds the scene from the prefabs into a temporary scene, checks the event system, audits it to
        /// SB_AUDIT_OUT and deletes it. The real scene and build settings are untouched.
        /// </summary>
        public static void RebuildCheck() => Run(() =>
        {
            try
            {
                string built = ShadowBloomSceneBuilder.BuildScene(RebuildCheckPath);
                int systems = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                int modules = UnityEngine.Object.FindObjectsByType<Oculus.Interaction.PointableCanvasModule>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                if (systems != 1 || modules != 1)
                    throw new InvalidOperationException($"{systems} EventSystems and {modules} PointableCanvasModules (expected 1 each).");
                string audit = ShadowBloomAudit.Dump(RebuildCheckPath,
                    Environment.GetEnvironmentVariable("SB_AUDIT_OUT") ?? "/tmp/shadow-bloom-prefab/audit-rebuild.txt");
                return $"{built} {audit}";
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
                AssetDatabase.DeleteAsset(RebuildCheckPath);
            }
        });

        /// <summary>Lays out GameUI.prefab in every preview state (Edit mode) and checks it; the prefab is not saved.</summary>
        public static void PreviewSmoke() => Run(() =>
        {
            var root = PrefabUtility.LoadPrefabContents(ShadowBloomPrefabs.GameUiPath);
            try
            {
                var ui = root.GetComponentInChildren<WorldSpaceUIController>(true);
                var sizes = new System.Collections.Generic.List<string>();
                foreach (UiPreviewState state in Enum.GetValues(typeof(UiPreviewState)))
                {
                    ui.Preview(UiPreviewViews.For(state));
                    var size = ((RectTransform)ui.transform).sizeDelta;
                    if (size.y <= 1f) throw new InvalidOperationException($"{state}: panel height {size.y} mm");
                    sizes.Add($"{state} {size.x:0}x{size.y:0} mm");
                }
                return "Preview OK: " + string.Join(", ", sizes);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        });

        /// <summary>Dumps the game hierarchy's serialized properties (SB_AUDIT_SCENE, SB_AUDIT_OUT) for diffing.</summary>
        public static void AuditGame() => Run(() => ShadowBloomAudit.Dump(
            Environment.GetEnvironmentVariable("SB_AUDIT_SCENE") ?? ShadowBloomSceneBuilder.ScenePath,
            Environment.GetEnvironmentVariable("SB_AUDIT_OUT") ?? "/tmp/shadow-bloom-prefab/audit.txt"));

        public static void BuildAndroid() => Run(() =>
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("Active build target is not Android (pass -buildTarget Android).");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ShadowBloomSceneBuilder.ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            string msg = $"Android build {s.result}: {s.outputPath}, {s.totalSize / (1024f * 1024f):F1} MB, " +
                         $"{s.totalTime.TotalSeconds:F0} s, {s.totalErrors} errors, {s.totalWarnings} warnings";
            if (s.result != BuildResult.Succeeded)
            {
                var errors = report.steps.SelectMany(st => st.messages).Where(m => m.type == LogType.Error).Select(m => m.content);
                throw new Exception(msg + "\n" + string.Join("\n", errors.Take(20)));
            }
            return msg;
        });

        /// <summary>
        /// Renders contact sheets of every stage's bloom over a dark and a light table (Edit mode, no Play, no
        /// headset) into SB_CAPTURE_DIR. Uses the same hidden preview rig as the Bloom Preview window and the scene
        /// controller's tuned parameters. Nothing is saved to the scene.
        /// </summary>
        public static void RenderBloomPreview() => Run(() =>
        {
            string dir = Environment.GetEnvironmentVariable("SB_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("Set SB_CAPTURE_DIR.");
            System.IO.Directory.CreateDirectory(dir);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ShadowBloomSceneBuilder.ScenePath);
            var template = UnityEngine.Object.FindAnyObjectByType<BloomEffectController>();
            var stages = UnityEngine.Object.FindAnyObjectByType<GameStateController>()?.Stages ?? DefaultStages.CreateAll();

            var origin = new Vector3(0f, -50f, 0f);   // away from everything in the scene
            const float radius = 0.12f;
            var temp = new System.Collections.Generic.List<UnityEngine.Object>();
            using var rig = new BloomPreviewRig(template);
            rig.Root.transform.SetPositionAndRotation(origin, Quaternion.identity);

            var table = GameObject.CreatePrimitive(PrimitiveType.Quad);
            table.hideFlags = HideFlags.HideAndDontSave;
            table.transform.SetPositionAndRotation(origin - Vector3.up * 0.001f, Quaternion.Euler(90f, 0f, 0f));
            table.transform.localScale = Vector3.one * 1.4f;
            var tableMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            table.GetComponent<MeshRenderer>().sharedMaterial = tableMat;
            temp.Add(table); temp.Add(tableMat);

            var camGo = EditorUtility.CreateGameObjectWithHideFlags("PreviewCam", HideFlags.HideAndDontSave, typeof(Camera));
            temp.Add(camGo);
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.32f, 0.33f, 0.35f, 1f);   // passthrough stand-in
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.02f;

            const int tw = 480, th = 360, cols = 4;
            var rt = RenderTexture.GetTemporary(tw, th, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
            cam.targetTexture = rt;
            var tile = new Texture2D(tw, th, TextureFormat.RGB24, false);
            int written = 0;
            try
            {
                var views = new[] { ("seated", new Vector3(0f, 0.48f, -0.5f)), ("top", new Vector3(0f, 0.62f, -0.001f)) };
                var surfaces = new[] { ("dark", new Color(0.1f, 0.1f, 0.11f)), ("light", new Color(0.82f, 0.8f, 0.76f)) };
                for (int si = 0; si < stages.Length; si++)
                {
                    var stage = stages[si];
                    bool finale = si == stages.Length - 1 || stage.completion == CompletionBehavior.FinalBloom;
                    rig.Bloom.PreviewAt(stage.bloomPetals, stage.bloomLayers, radius, finale, 0f, stage, rig.SeedColor(stage));
                    float total = rig.Bloom.TotalDuration;
                    var times = new float[8];
                    for (int i = 0; i < times.Length; i++) times[i] = Mathf.Lerp(rig.Bloom.LockLead + 0.05f, total + 1.2f, i / (times.Length - 1f));

                    foreach (var (viewName, offset) in views)
                    foreach (var (surfName, colour) in surfaces)
                    {
                        if (viewName == "top" && surfName == "light") continue;
                        tableMat.SetColor("_BaseColor", colour);
                        cam.transform.position = origin + offset;
                        cam.transform.LookAt(origin + Vector3.up * 0.05f, Vector3.forward);
                        if (viewName == "seated") cam.transform.LookAt(origin + Vector3.up * 0.06f);
                        int rows = (times.Length + cols - 1) / cols;
                        var sheet = new Texture2D(tw * cols, th * rows, TextureFormat.RGB24, false);
                        for (int i = 0; i < times.Length; i++)
                        {
                            rig.Bloom.PreviewAt(stage.bloomPetals, stage.bloomLayers, radius, finale, times[i], stage, rig.SeedColor(stage));
                            cam.Render();
                            RenderTexture.active = rt;
                            tile.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
                            tile.Apply();
                            sheet.SetPixels((i % cols) * tw, (rows - 1 - i / cols) * th, tw, th, tile.GetPixels());
                        }
                        RenderTexture.active = null;
                        sheet.Apply();
                        string file = System.IO.Path.Combine(dir, $"bloom_s{si + 1}_{viewName}_{surfName}.png");
                        System.IO.File.WriteAllBytes(file, sheet.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(sheet);
                        written++;
                        Debug.Log($"[ShadowBloomBatch] {file}: t = {string.Join(", ", times.Select(t => t.ToString("0.00")))} s");
                    }
                }
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(tile);
                foreach (var o in temp) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }
            return $"Bloom preview: {written} contact sheets in {dir}";
        });

        static void Run(Func<string> action)
        {
            try
            {
                Debug.Log("[ShadowBloomBatch] " + action());
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[ShadowBloomBatch] FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
