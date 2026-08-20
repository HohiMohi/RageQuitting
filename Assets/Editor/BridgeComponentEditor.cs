using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(BridgeComponent))]
[CanEditMultipleObjects]
public sealed class BridgeComponentEditor : Editor
{
    private SerializedProperty startFullyCompleted;

    private void OnEnable()
    {
        startFullyCompleted = serializedObject.FindProperty("startFullyCompleted");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Initial State", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(startFullyCompleted, new GUIContent("Start Fully Completed"));
        bool initialStateChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();
        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "startFullyCompleted",
            "isMounted",
            "canBeMounted",
            "isAssembled");

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime State", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            foreach (Object inspectedTarget in targets)
            {
                BridgeComponent component = inspectedTarget as BridgeComponent;
                if (component == null)
                {
                    continue;
                }

                EditorGUILayout.Toggle("Mounted", component.IsMounted);
                EditorGUILayout.Toggle("Assembled", component.IsAssembled);
                EditorGUILayout.Toggle("Can Be Mounted", component.CanBeMounted);
                if (targets.Length > 1)
                {
                    break;
                }
            }
        }

        if (!initialStateChanged)
        {
            return;
        }

        foreach (Object inspectedTarget in targets)
        {
            BridgeComponent component = inspectedTarget as BridgeComponent;
            if (component == null)
            {
                continue;
            }

            component.RefreshInitialStatePreview();
            EditorUtility.SetDirty(component);
            if (component.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            }
        }

        SceneView.RepaintAll();
    }
}
