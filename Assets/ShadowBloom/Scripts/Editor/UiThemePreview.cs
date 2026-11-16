using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShadowBloom.EditorTools
{
    /// <summary>
    /// Live UI tuning: while UiTheme.asset is selected, edits re-lay out the panel open in Prefab Mode (Edit mode)
    /// or every panel using that theme (Play mode).
    /// </summary>
    [InitializeOnLoad]
    public static class UiThemePreview
    {
        const string Key = "ShadowBloom.UiPreviewState";

        public static UiPreviewState State
        {
            get => (UiPreviewState)SessionState.GetInt(Key, (int)UiPreviewState.Playing);
            set => SessionState.SetInt(Key, (int)value);
        }

        static UiThemePreview()
        {
            UiTheme.Edited += theme => EditorApplication.delayCall += () => Refresh(theme);
        }

        static void Refresh(UiTheme theme)
        {
            // OnValidate also fires on import and load; only react to edits of the selected theme.
            if (theme == null || Selection.activeObject != theme) return;
            if (EditorApplication.isPlaying)
            {
                foreach (var ui in Object.FindObjectsByType<WorldSpaceUIController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (Uses(ui, theme)) ui.RefreshTheme();
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null) return;
            foreach (var ui in stage.prefabContentsRoot.GetComponentsInChildren<WorldSpaceUIController>(true))
                if (Uses(ui, theme)) Apply(ui, State);
        }

        public static void Apply(WorldSpaceUIController ui, UiPreviewState state)
        {
            Undo.RegisterFullObjectHierarchyUndo(ui.gameObject, "Preview UI");
            ui.Preview(UiPreviewViews.For(state));
            var stage = PrefabStageUtility.GetPrefabStage(ui.gameObject);
            EditorSceneManager.MarkSceneDirty(stage != null ? stage.scene : ui.gameObject.scene);
        }

        static bool Uses(WorldSpaceUIController ui, UiTheme theme) =>
            new SerializedObject(ui).FindProperty("_theme").objectReferenceValue == theme;
    }
}
