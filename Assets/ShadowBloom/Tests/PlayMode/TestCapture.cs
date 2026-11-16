using System;
using System.IO;
using UnityEngine;

namespace ShadowBloom.Tests
{
    /// <summary>
    /// Render captures for visual QA (only when SB_CAPTURE_DIR is set). The background stands in for passthrough:
    /// mid-grey by default, or a bright room with SB_CAPTURE_BG=light. These are Editor renders, not headset footage.
    /// </summary>
    static class TestCapture
    {
        public static string Dir => Environment.GetEnvironmentVariable("SB_CAPTURE_DIR");
        public static bool Enabled => !string.IsNullOrEmpty(Dir);

        public static Color Background => Environment.GetEnvironmentVariable("SB_CAPTURE_BG") == "light"
            ? new Color(0.80f, 0.79f, 0.76f, 1f)
            : new Color(0.32f, 0.33f, 0.35f, 1f);

        /// <summary>The player's view (main camera = head on the desktop path).</summary>
        public static void Head(string name)
        {
            if (!Enabled || Camera.main == null) return;
            Render(Camera.main, name, 960, 720);
        }

        /// <summary>Straight-on view of the panel from 0.36 m, for typography and spacing.</summary>
        public static void Panel(RectTransform canvasRoot, string name)
        {
            if (!Enabled || canvasRoot == null) return;
            var go = new GameObject("PanelCaptureCam", typeof(Camera));
            try
            {
                var cam = go.GetComponent<Camera>();
                cam.enabled = false;
                cam.fieldOfView = 42f;
                cam.nearClipPlane = 0.02f;
                var center = canvasRoot.TransformPoint(canvasRoot.rect.center);
                go.transform.SetPositionAndRotation(center - canvasRoot.forward * 0.36f, Quaternion.LookRotation(canvasRoot.forward, canvasRoot.up));
                Render(cam, name, 720, 720);
            }
            finally { UnityEngine.Object.Destroy(go); }
        }

        static void Render(Camera cam, string name, int w, int h)
        {
            Directory.CreateDirectory(Dir);
            // Light-room captures used to change only the background, leaving the table dark.
            // Swap only the Editor proxy's material, and restore it after the capture.
            string surface = Environment.GetEnvironmentVariable("SB_CAPTURE_BG");
            var top = GameObject.Find("DesktopTableProxy/Top")?.GetComponent<Renderer>();
            var previousMaterial = top != null ? top.sharedMaterial : null;
            Material captureMaterial = null;
            Texture2D pattern = null;
            if (top != null && (surface == "light" || surface == "busy"))
            {
                captureMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                captureMaterial.SetColor("_BaseColor", new Color(0.82f, 0.8f, 0.76f));
                if (surface == "busy")
                {
                    pattern = new Texture2D(32, 32, TextureFormat.RGB24, false) { filterMode = FilterMode.Point };
                    for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        pattern.SetPixel(x, y, ((x / 2 + y / 2) % 2 == 0) ? new Color(0.24f, 0.31f, 0.34f) : new Color(0.85f, 0.78f, 0.65f));
                    pattern.Apply();
                    captureMaterial.SetTexture("_BaseMap", pattern);
                    captureMaterial.SetColor("_BaseColor", Color.white);
                }
                top.sharedMaterial = captureMaterial;
            }
            try
            {
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                var prevTarget = cam.targetTexture;
                var prevBg = cam.backgroundColor;
                var prevFlags = cam.clearFlags;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Background;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;
                cam.backgroundColor = prevBg;
                cam.clearFlags = prevFlags;
                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
            }
            finally
            {
                if (top != null) top.sharedMaterial = previousMaterial;
                if (captureMaterial != null) UnityEngine.Object.Destroy(captureMaterial);
                if (pattern != null) UnityEngine.Object.Destroy(pattern);
            }
        }
    }
}
