using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShadowBloom.EditorTools
{
    /// <summary>Default inspector plus an Edit-mode preview of the panel in a chosen game state.</summary>
    [CustomEditor(typeof(WorldSpaceUIController))]
    public sealed class WorldSpaceUIControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Edit-mode preview", EditorStyles.boldLabel);
            var ui = (WorldSpaceUIController)target;
            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("UiTheme edits apply live while playing.", MessageType.Info);
                return;
            }
            if (PrefabUtility.IsPartOfPrefabInstance(ui) && PrefabStageUtility.GetPrefabStage(ui.gameObject) == null)
            {
                EditorGUILayout.HelpBox("Preview edits the prefab. Open GameUI to preview without creating scene overrides.", MessageType.Info);
                if (GUILayout.Button("Open GameUI Prefab"))
                    AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(ShadowBloomPrefabs.GameUiPath));
                return;
            }
            UiThemePreview.State = (UiPreviewState)EditorGUILayout.EnumPopup("State", UiThemePreview.State);
            if (GUILayout.Button("Preview")) UiThemePreview.Apply(ui, UiThemePreview.State);
            EditorGUILayout.HelpBox("Layout comes from UiTheme. Edit Assets/ShadowBloom/UI/UiTheme.asset; this panel updates while the theme is selected.", MessageType.None);
        }
    }
}
