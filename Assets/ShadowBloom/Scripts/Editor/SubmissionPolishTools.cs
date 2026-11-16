using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShadowBloom.EditorTools
{
    /// <summary>Small, repeatable edits to the existing prefabs and icon. Never regenerates the scene or interaction rig.</summary>
    public static class SubmissionPolishTools
    {
        public const string IconPath = "Assets/ShadowBloom/UI/ShadowBloomIcon.png";

        [MenuItem("Shadow Bloom/Apply Submission Polish")]
        public static void Apply()
        {
            try
            {
                // Tunes Puzzle.prefab (the scene holds an instance of it), so no scene is opened.
                ShadowBloomPrefabs.EditPrefab<PuzzleController>(ShadowBloomPrefabs.PuzzlePath, puzzle =>
                {
                    var bloom = puzzle.GetComponentInChildren<BloomEffectController>(true);
                    if (bloom == null) throw new InvalidOperationException("Bloom missing.");
                    var look = new SerializedObject(puzzle);
                    look.FindProperty("_tubeRadius").floatValue = 0.0034f;
                    look.FindProperty("_currentWidth").floatValue = 0.0036f;
                    look.FindProperty("_targetWidth").floatValue = 0.007f;
                    look.FindProperty("_targetColor").colorValue = GeometryPalette.WithAlpha(GeometryPalette.Paper, GeometryPalette.TargetAlpha);
                    look.ApplyModifiedPropertiesWithoutUndo();
                    var flower = new SerializedObject(bloom);
                    flower.FindProperty("_lockLead").floatValue = PuzzleController.SnapDuration;
                    flower.FindProperty("_ribbonWidth").floatValue = 0.0036f;
                    flower.FindProperty("_restLevel").floatValue = 0.9f;
                    flower.ApplyModifiedPropertiesWithoutUndo();
                    return "Puzzle prefab tuned";
                });

                GenerateIcon();
                AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
                importer.textureType = TextureImporterType.Default;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = false;
                importer.SaveAndReimport();
                // Keep companyName: changing it also changes the desktop PlayerPrefs namespace.
                PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown,
                    new[] { AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath) }, IconKind.Any);
                ConfigureAndroidIcons();
                AssetDatabase.SaveAssets();
                Debug.Log("[SubmissionPolish] Puzzle prefab tuned; geometric icon installed. Preference namespace, signing and versions unchanged.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        /// <summary>Android adaptive slots do not inherit the default icon. Set every platform slot explicitly.</summary>
        public static void ConfigureAndroidIcons()
        {
#if UNITY_ANDROID
            const string backgroundPath = "Assets/ShadowBloom/UI/ShadowBloomIconBackground.png";
            var background = new Texture2D(32, 32, TextureFormat.RGB24, false);
            var colors = new Color[32 * 32];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(0x10, 0x1C, 0x2A, 0xFF);
            background.SetPixels(colors);
            background.Apply();
            File.WriteAllBytes(backgroundPath, background.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(background);
            AssetDatabase.ImportAsset(backgroundPath, ImportAssetOptions.ForceSynchronousImport);
            var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(backgroundPath);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null || bg == null) throw new InvalidOperationException("Branding textures missing.");
            var platform = UnityEditor.Build.NamedBuildTarget.Android;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(platform))
            {
                var slots = PlayerSettings.GetPlatformIcons(platform, kind);
                foreach (var slot in slots)
                {
                    if (kind == UnityEditor.Android.AndroidPlatformIconKind.Adaptive)
                        slot.SetTextures(new[] { bg, icon });
                    else slot.SetTexture(icon);
                }
                PlayerSettings.SetPlatformIcons(platform, kind, slots);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[SubmissionPolish] Android adaptive background/foreground and legacy icons configured.");
#else
            Debug.LogWarning("[SubmissionPolish] Active build target is not Android: Android adaptive/round/legacy icon " +
                             "slots were NOT configured. Switch to Android (or pass -buildTarget Android) and run again.");
#endif
        }

        // Original geometric artwork, generated from polylines at import time. No external image dependency.
        static void GenerateIcon()
        {
            const int size = 512;
            var pixels = new Color[size * size];
            var navy = new Color32(0x10, 0x1C, 0x2A, 0xFF);
            var blue = new Color32(0x60, 0xB7, 0xFF, 0xFF);
            var mint = new Color32(0x82, 0xE6, 0xDB, 0xFF);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float glow = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), new Vector2(256, 250)) / 300f);
                pixels[y * size + x] = Color.Lerp(navy, new Color(0.1f, 0.2f, 0.3f), glow * 0.55f);
            }
            var centre = new Vector2(256, 172);
            for (int i = 0; i < 160; i++)
            {
                float a = i * Mathf.PI * 2f / 160f, b = (i + 1) * Mathf.PI * 2f / 160f;
                Line(pixels, size, centre + new Vector2(Mathf.Cos(a) * 143, Mathf.Sin(a) * 38),
                    centre + new Vector2(Mathf.Cos(b) * 143, Mathf.Sin(b) * 38), 2.4f, mint);
            }
            for (int petal = -3; petal <= 3; petal++)
            {
                float angle = petal * 23f * Mathf.Deg2Rad;
                var axis = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                var lateral = new Vector2(axis.y, -axis.x);
                float length = 198f - Mathf.Abs(petal) * 17f;
                var origin = new Vector2(256, 194);
                var prev = origin;
                for (int i = 1; i <= 120; i++)
                {
                    float phi = -Mathf.PI * 0.5f + Mathf.PI * i / 120f;
                    float r = Mathf.Cos(phi) * length;
                    var next = origin + axis * (r * Mathf.Cos(phi)) + lateral * (r * Mathf.Sin(phi) * 0.28f);
                    Line(pixels, size, prev, next, petal == 0 ? 3f : 2.3f, Color.Lerp(blue, new Color(0.85f, 0.94f, 1f), 1f - Mathf.Abs(petal) / 4f));
                    prev = next;
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(IconPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        static void Line(Color[] pixels, int size, Vector2 a, Vector2 b, float radius, Color color)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius - 1));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius - 1));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius + 1));
            var ab = b - a;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float coverage = Mathf.Clamp01(radius + 0.5f - Vector2.Distance(p, a + ab * t));
                pixels[y * size + x] = Color.Lerp(pixels[y * size + x], color, coverage);
            }
        }
    }
}
