using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowBloom.EditorTools
{
    /// <summary>
    /// Applies the "Spatial Observatory" UI design system to the existing world-space panel, in place.
    /// Idempotent, and works by name: every existing object, name and serialized reference is kept (tests and the
    /// Interaction SDK canvases depend on them); only missing parts are added. Used by the menu item, by
    /// <see cref="ShadowBloomBatch.UpgradeUi"/>, and by the scene builder, so a rebuilt scene matches.
    /// </summary>
    public static class ShadowBloomUiUpgrade
    {
        public const string UiDir = ShadowBloomSceneBuilder.Root + "/UI";
        public const string ThemePath = UiDir + "/UiTheme.asset";
        // Neo-Swiss: every surface is square. Fills use no sprite (a plain quad); the focus ring is a square outline.
        // RoundedFill.png / RoundedRing.png from the earlier rounded style stay in the project, unreferenced.
        const string RingPath = UiDir + "/SquareRing.png";
        // Sprite geometry (px). Sliced at multiplier m, 1 px = 1/m mm on the panel.
        const int SpriteSize = 64, Radius = 0, Stroke = 3, Border = 16;

        /// <summary>
        /// Daily, Assist and How to play used to be cloned from Motion at Play time (WorldSpaceUIController.EnsureClone). Bakes the
        /// same clones so they exist, and can be tuned, in Edit mode. Idempotent.
        /// </summary>
        public static string BakeExtraButtons(WorldSpaceUIController ui)
        {
            var panel = ui.transform.Find("Panel");
            var motion = panel != null ? panel.Find("Motion") : null;
            if (motion == null) return "Motion button missing; extra buttons not baked.";
            var so = new SerializedObject(ui);
            var added = new System.Collections.Generic.List<string>();
            foreach (var (name, field, labelField) in new[] { ("Daily", "_daily", "_dailyLabel"), ("Assist", "_assist", "_assistLabel"),
                         ("HowToPlay", "_howToPlay", "_howToPlayLabel") })
            {
                var t = panel.Find(name);
                if (t == null)
                {
                    t = Object.Instantiate(motion.gameObject, panel).transform;
                    t.name = name;
                    t.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();   // runtime binding only
                    t.gameObject.SetActive(false);
                    added.Add(name);
                }
                Set(so, field, t.GetComponent<Button>());
                Set(so, labelField, ButtonLabel(t));
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return added.Count == 0 ? "Extra buttons already baked." : $"Baked {string.Join(", ", added)} buttons.";
        }

        [MenuItem("Shadow Bloom/Upgrade Spatial UI", priority = 2)]
        static void Menu()
        {
            // The UI lives in GameUI.prefab: upgrade the prefab (open in Prefab Mode, else the asset), so the scene
            // instance never collects overrides. A scene without the prefab falls back to the in-scene upgrade.
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var staged = stage != null ? stage.prefabContentsRoot.GetComponentInChildren<WorldSpaceUIController>(true) : null;
            if (staged != null)
            {
                Debug.Log("[ShadowBloom] " + Upgrade(staged));
                EditorSceneManager.MarkSceneDirty(stage.scene);
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ShadowBloomPrefabs.GameUiPath) != null)
            {
                Debug.Log("[ShadowBloom] " + ShadowBloomPrefabs.EditPrefab<WorldSpaceUIController>(ShadowBloomPrefabs.GameUiPath, Upgrade));
                return;
            }
            var ui = Object.FindAnyObjectByType<WorldSpaceUIController>();
            if (ui == null) throw new System.InvalidOperationException("Open the Shadow Bloom scene first.");
            Debug.Log("[ShadowBloom] " + Upgrade(ui));
            EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        }

        public static string Upgrade(WorldSpaceUIController ui)
        {
            var theme = EnsureTheme();
            var ring = EnsureSprite(RingPath, ring: true);

            var root = (RectTransform)ui.transform;
            var panel = root.Find("Panel") as RectTransform;
            if (panel == null) throw new System.InvalidOperationException("UI/Panel missing.");
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(theme.panelWidth, 200f);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = Vector2.zero;

            var group = GetOrAdd<CanvasGroup>(panel.gameObject);
            var panelImage = panel.GetComponent<Image>();
            Square(panelImage);

            // Neo-Swiss grid: square-ended full-width header rule, a divider above the buttons, an index numeral column.
            var accent = Child<Image>(panel, "AccentRule", 0);
            accent.sprite = null;
            accent.type = Image.Type.Simple;
            accent.raycastTarget = false;
            var divider = Child<Image>(panel, "Divider", -1);
            divider.sprite = null;
            divider.type = Image.Type.Simple;
            divider.raycastTarget = false;
            var stageIndex = ChildText(panel, "StageIndex", -1);

            // Notice card (hands lost): backing + warning bar; the existing Status text moves inside it.
            var notice = Child<Image>(panel, "Notice", 1);
            Square(notice);
            notice.raycastTarget = false;
            var bar = Child<Image>(notice.rectTransform, "Bar", 0);
            Square(bar);
            bar.raycastTarget = false;
            var status = panel.Find("Status") ?? notice.transform.Find("Status");
            if (status != null) status.SetParent(notice.transform, false);

            var dot = Child<Image>(panel, "EyebrowDot", 2);
            Square(dot);
            dot.raycastTarget = false;
            var eyebrow = ChildText(panel, "Eyebrow", 3);
            var pips = panel.Find("Pips") as RectTransform ?? NewRect(panel, "Pips");
            pips.SetSiblingIndex(4);
            for (int i = 0; i < 8; i++)
            {
                var pip = Child<Image>(pips, "Pip" + i, i);
                Square(pip);   // square ticks; size still encodes done / current / upcoming
                pip.raycastTarget = false;
            }
            var alignmentLabel = ChildText(panel, "AlignmentLabel", -1);

            var alignment = panel.Find("Alignment");
            if (alignment != null)
            {
                var track = alignment.GetComponent<Image>();
                Square(track);
                track.raycastTarget = false;
                var alignmentFill = alignment.Find("Fill")?.GetComponent<Image>();
                if (alignmentFill != null) alignmentFill.raycastTarget = false;
            }

            // Buttons: shared style component, square fill, square outline and a square focus ring (hover never relies
            // on colour alone). Every panel button, including the baked Daily, Assist and How to play.
            BakeExtraButtons(ui);
            var retry = panel.Find("Retry");
            var restart = panel.Find("Restart");
            if (restart == null && retry != null)
            {
                restart = Object.Instantiate(retry.gameObject, panel).transform;
                restart.name = "Restart";
                var b = restart.GetComponent<Button>();
                b.onClick = new Button.ButtonClickedEvent();   // runtime binding only (WorldSpaceUIController)
            }
            if (restart != null && restart.Find("Label") is Transform rl && rl.TryGetComponent<TMP_Text>(out var rlt)) rlt.text = UiCopy.RestartAll;
            foreach (var (name, variant) in new[]
                     {
                         ("Primary", UiButtonStyle.Variant.Primary), ("Retry", UiButtonStyle.Variant.Secondary),
                         ("Pause", UiButtonStyle.Variant.Secondary), ("Move", UiButtonStyle.Variant.Secondary),
                         ("Sound", UiButtonStyle.Variant.Secondary), ("Motion", UiButtonStyle.Variant.Secondary),
                         ("Daily", UiButtonStyle.Variant.Secondary), ("Assist", UiButtonStyle.Variant.Secondary),
                         ("HowToPlay", UiButtonStyle.Variant.Secondary), ("Restart", UiButtonStyle.Variant.Quiet),
                         ("Quit", UiButtonStyle.Variant.Quiet)
                     })
            {
                var t = panel.Find(name);
                if (t == null) continue;
                StyleButton(t, variant, theme, ring);
            }

            var so = new SerializedObject(ui);
            Set(so, "_theme", theme);
            Set(so, "_group", group);
            Set(so, "_panelImage", panelImage);
            Set(so, "_accentRule", accent);
            Set(so, "_divider", divider);
            Set(so, "_stageIndex", stageIndex);
            Set(so, "_notice", notice.rectTransform);
            Set(so, "_noticeBar", bar);
            Set(so, "_eyebrow", eyebrow);
            Set(so, "_eyebrowDot", dot);
            Set(so, "_pips", pips);
            Set(so, "_alignmentLabel", alignmentLabel);
            if (restart != null)
            {
                Set(so, "_restart", restart.GetComponent<Button>());
                Set(so, "_restartLabel", ButtonLabel(restart));
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            ui.ApplyTheme();
            EditorUtility.SetDirty(ui);
            return $"Spatial UI applied: theme {ThemePath}, {panel.GetComponentsInChildren<UiButtonStyle>(true).Length} styled buttons.";
        }

        static void StyleButton(Transform t, UiButtonStyle.Variant variant, UiTheme theme, Sprite ring)
        {
            var button = t.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            var bg = t.GetComponent<Image>();
            Square(bg);

            var focus = Child<Image>((RectTransform)t, "Focus", 0);
            focus.sprite = ring;
            focus.type = Image.Type.Sliced;
            focus.raycastTarget = false;
            var frt = focus.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(-2f, -2f);
            frt.offsetMax = new Vector2(2f, 2f);

            // Square outline (secondary / quiet), sliced from the ring sprite; UiButtonStyle sets thickness and colour.
            // Kept under its earlier name "Rule". The cell index numerals of the previous pass are removed.
            var rule = Child<Image>((RectTransform)t, "Rule", 1);
            rule.sprite = ring;
            rule.type = Image.Type.Sliced;
            rule.fillCenter = false;
            rule.raycastTarget = false;
            Stretch(rule.rectTransform);
            var oldIndex = t.Find("Index");
            if (oldIndex != null) Object.DestroyImmediate(oldIndex.gameObject);

            var label = t.Find("Label")?.GetComponent<TMP_Text>();
            if (label != null)
            {
                Stretch(label.rectTransform);
                label.transform.SetAsLastSibling();
            }
            GetOrAdd<UiButtonStyle>(t.gameObject).Configure(variant, theme, bg, focus, label, rule);
            EditorUtility.SetDirty(t.gameObject);
        }

        // ---- Assets -----------------------------------------------------------------------------------

        static UiTheme EnsureTheme()
        {
            EnsureFolder();
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
            if (theme != null) return theme;
            theme = ScriptableObject.CreateInstance<UiTheme>();
            AssetDatabase.CreateAsset(theme, ThemePath);
            AssetDatabase.SaveAssets();
            return theme;
        }

        /// <summary>Original procedural sprite: an anti-aliased (rounded, when Radius > 0) rectangle, filled or as a ring.</summary>
        static Sprite EnsureSprite(string path, bool ring)
        {
            EnsureFolder();
            if (!File.Exists(path))
            {
                var tex = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false);
                var px = new Color32[SpriteSize * SpriteSize];
                float half = SpriteSize * 0.5f;
                for (int y = 0; y < SpriteSize; y++)
                for (int x = 0; x < SpriteSize; x++)
                {
                    // Signed distance to a rounded box of half-extent (half - 0.5) with corner radius Radius.
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - 0.5f - Radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - 0.5f - Radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - Radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (ring) a *= Mathf.Clamp01(d + Stroke + 0.5f);
                    px[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
                tex.SetPixels32(px);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var border = new Vector4(Border, Border, Border, Border);
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != border || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = border;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>A button's label, by name: a button may hold more than one text, so "first TMP child" is not safe.</summary>
        static TMP_Text ButtonLabel(Transform button)
        {
            var label = button.Find("Label");
            return label != null ? label.GetComponent<TMP_Text>() : button.GetComponentInChildren<TMP_Text>(true);
        }

        /// <summary>Plain square quad: no sprite, so no corner radius at any size.</summary>
        static void Square(Image image)
        {
            image.sprite = null;
            image.type = Image.Type.Simple;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(UiDir)) AssetDatabase.CreateFolder(ShadowBloomSceneBuilder.Root, "UI");
        }

        // ---- Hierarchy helpers ------------------------------------------------------------------------

        // Not '??': in the Editor a missing native component (e.g. CanvasGroup) comes back as a fake-null object.
        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static T Child<T>(Transform parent, string name, int siblingIndex) where T : Component
        {
            var t = parent.Find(name);
            var rt = t as RectTransform ?? NewRect(parent, name);
            if (siblingIndex >= 0) rt.SetSiblingIndex(siblingIndex);
            return GetOrAdd<T>(rt.gameObject);
        }

        static TMP_Text ChildText(Transform parent, string name, int siblingIndex)
        {
            var t = Child<TextMeshProUGUI>(parent, name, siblingIndex);
            t.text = name;
            t.raycastTarget = false;
            return t;
        }

        static void Set(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p == null) throw new System.InvalidOperationException($"WorldSpaceUIController.{field} not found.");
            p.objectReferenceValue = value;
        }
    }
}
