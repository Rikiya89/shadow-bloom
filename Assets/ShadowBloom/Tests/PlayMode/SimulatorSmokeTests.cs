using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR;

namespace ShadowBloom.Tests
{
    /// <summary>
    /// Opt-in smoke test under the Meta XR Simulator's OpenXR runtime (set SB_XRSIM=1 plus the simulator's
    /// XR_RUNTIME_JSON / META_XRSIM_CONFIG_JSON; see docs/HEADSET_TEST_CHECKLIST.md). Records what the runtime reports
    /// (device profile, eye resolution, refresh rate, hands) and where placement landed in the simulator's room.
    /// Game commands here are scripted, so they verify flow under the XR runtime, not simulated or real hand input.
    /// </summary>
    public class SimulatorSmokeTests
    {
        const string ScenePath = "Assets/ShadowBloom/Scenes/ShadowBloom.unity";
        const string ProgressKey = "shadowbloom.progress.v1";

        [UnityTest, Timeout(180000)]
        public IEnumerator BootsUnderSimulator_PlacesAndPlays()
        {
            if (Environment.GetEnvironmentVariable("SB_XRSIM") != "1") Assert.Ignore("Set SB_XRSIM=1 with the simulator runtime env.");
            string saved = PlayerPrefs.GetString(ProgressKey, "");
            PlayerPrefs.DeleteKey(ProgressKey);
            var howToPlay = new HowToPlaySeenScope();
            var report = new StringBuilder("[ShadowBloom XRSim]\n");
            try
            {
#if UNITY_EDITOR
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                    new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
                for (int i = 0; i < 30; i++) yield return null;
                report.AppendLine($"at scene load: runtime loaded {XRPresence.RuntimeLoaded}, session running {XRSettings.isDeviceActive}");
                // The simulator's session reaches READY/FOCUSED well after the loader starts.
                float t0 = Time.realtimeSinceStartup;
                while (!XRSettings.isDeviceActive && Time.realtimeSinceStartup - t0 < 90f) yield return null;
                report.AppendLine($"session running after {Time.realtimeSinceStartup - t0:F1} s: {XRSettings.isDeviceActive}");
                for (int i = 0; i < 30; i++) yield return null;

                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);
                float hz = 0f;
                bool displayRunning = displays.Count > 0 && displays[0].running;
                if (displays.Count > 0) displays[0].TryGetDisplayRefreshRate(out hz);
                report.AppendLine($"profile env: {Environment.GetEnvironmentVariable("SB_XRSIM_PROFILE")}");
                report.AppendLine($"XR device active: {XRSettings.isDeviceActive}, device: '{XRSettings.loadedDeviceName}', display running: {displayRunning}");
                report.AppendLine($"eye texture: {XRSettings.eyeTextureWidth} x {XRSettings.eyeTextureHeight}, refresh: {hz:F0} Hz");

                var hands = new List<InputDevice>();
                InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HandTracking, hands);
                var all = new List<InputDevice>();
                InputDevices.GetDevices(all);
                report.AppendLine($"input devices: {string.Join(", ", all.ConvertAll(d => d.name))}; hand-tracking devices: {hands.Count}");

                var desktop = UnityEngine.Object.FindAnyObjectByType<DesktopDebugInput>(FindObjectsInactive.Include);
                report.AppendLine($"desktop debug path active: {desktop != null && desktop.Active}");

                var game = UnityEngine.Object.FindAnyObjectByType<GameStateController>();
                var placement = UnityEngine.Object.FindAnyObjectByType<SurfacePlacementController>();
                var puzzle = UnityEngine.Object.FindAnyObjectByType<PuzzleController>();
                // Room data from the simulator may arrive after the 8 s manual fallback; late results are still taken
                // while the marker is untouched, so wait for a real surface for a while.
                float end = Time.realtimeSinceStartup + 45f;
                while (!placement.FoundRealSurface && Time.realtimeSinceStartup < end) yield return null;
                var p = placement.Placement;
                var head = placement.Head;
                report.AppendLine($"head: {head.position:F2} fwd {head.forward:F2}");
                report.AppendLine($"placement: {placement.Status}, real surface: {placement.FoundRealSurface}, kind: {p.Kind}, " +
                                  $"radius: {p.PuzzleRadius:F3}, at {p.Position:F2}, message: '{placement.Message}'");

                Assert.That(XRSettings.isDeviceActive, "simulator OpenXR runtime active");
                Assert.That(placement.Status, Is.Not.EqualTo(PlacementStatus.Searching), "placement never stalls");

                if (placement.Status == PlacementStatus.Manual) placement.ConfirmManual();
                game.Primary();
                for (int stage = 0; stage < game.Flow.StageCount; stage++)
                {
                    puzzle.DebugSetSolution();
                    end = Time.realtimeSinceStartup + 6f;
                    while (game.Flow.State != GameState.Solved && Time.realtimeSinceStartup < end) yield return null;
                    Assert.AreEqual(GameState.Solved, game.Flow.State, $"stage {stage + 1}");
                    game.Primary();
                    end = Time.realtimeSinceStartup + 6f;
                    while (game.Flow.State == GameState.Transition && Time.realtimeSinceStartup < end) yield return null;
                }
                report.AppendLine($"scripted {game.Flow.StageCount}-stage run under XR runtime: {game.Flow.State}");
                Assert.AreEqual(GameState.Completed, game.Flow.State);
            }
            finally
            {
                Debug.Log(report.ToString());
                string file = Environment.GetEnvironmentVariable("SB_STATS_FILE");
                if (!string.IsNullOrEmpty(file)) File.WriteAllText(file, report.ToString());
                PlayerPrefs.SetString(ProgressKey, saved);
                howToPlay.Restore();
                PlayerPrefs.Save();
            }
        }
    }
}
