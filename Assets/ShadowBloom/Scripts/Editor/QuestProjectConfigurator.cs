using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShadowBloom.EditorTools
{
    /// <summary>
    /// Applies the Quest 3/3S settings this game depends on, then hands the remaining platform checklist
    /// (OpenXR loader, Meta feature group, min SDK, etc.) to Meta's own Project Setup Tool fixes.
    /// Does not switch the active build target (that is a long reimport; use File > Build Profiles).
    /// </summary>
    public static class QuestProjectConfigurator
    {
        public const string DefaultBundleId = "com.shadowbloom.mrpuzzle";

        [MenuItem("Shadow Bloom/Configure Quest Project (Android)", priority = 20)]
        public static void ConfigureMenu() => Debug.Log(Configure());

        public static string Configure()
        {
            var log = new StringBuilder();
            var android = NamedBuildTarget.Android;

            if (PlayerSettings.productName == "shadow_bllom") PlayerSettings.productName = "Shadow Bloom";
            var currentId = PlayerSettings.GetApplicationIdentifier(android);
            if (string.IsNullOrEmpty(currentId) || currentId.StartsWith("com.DefaultCompany") ||
                currentId.StartsWith("com.UnityTechnologies"))
            {
                PlayerSettings.SetApplicationIdentifier(android, DefaultBundleId);
                log.AppendLine($"bundle id -> {DefaultBundleId} (change before store upload)");
            }

            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            log.AppendLine("Android: IL2CPP, ARM64, Linear, Vulkan");

            var config = OVRProjectConfig.CachedProjectConfig;
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
            config.sceneSupport = OVRProjectConfig.FeatureSupport.Required;
            config.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
            OVRProjectConfig.CommitProjectConfig(config);
            log.AppendLine("OVRProjectConfig: hands+controllers, passthrough required, scene required, anchors enabled");

            AssetDatabase.SaveAssets();
            log.AppendLine("Next: Shadow Bloom > Apply Meta Project Setup Fixes, or review Edit > Project Settings > Meta XR.");
            return log.ToString();
        }

        /// <summary>
        /// Runs every auto-fixable Meta Project Setup Tool task for Android, including Recommended ones
        /// (e.g. SpaceWarp, graphics jobs, dynamic resolution, splash off). Some fixes can trigger large downloads
        /// such as the Meta XR Simulator, which blocks the Editor while it runs. Kept separate for that reason.
        /// </summary>
        [MenuItem("Shadow Bloom/Apply Meta Project Setup Fixes (Android)", priority = 21)]
        public static void ApplyMetaFixes()
        {
            if (!EditorUtility.DisplayDialog("Apply Meta Project Setup fixes",
                    "Apply all auto-fixable Required and Recommended Meta XR settings for Android?\n\n" +
                    "Some fixes may download additional tools and block the Editor for a while.", "Apply", "Cancel"))
            {
                return;
            }
            OVRProjectSetup.FixAllAsync(BuildTargetGroup.Android);
        }
    }
}
