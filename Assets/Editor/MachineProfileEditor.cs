
using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MachineProfile))]
public class MachineProfileEditor : Editor
{
    private SerializedProperty machineID;
    private SerializedProperty machineName;
    private SerializedProperty machineType;
    private SerializedProperty components;

    private void OnEnable()
    {
        machineID = serializedObject.FindProperty("MachineID");
        machineName = serializedObject.FindProperty("Name");
        machineType = serializedObject.FindProperty("MachineType");
        components = serializedObject.FindProperty("Components");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(machineID);
        EditorGUILayout.PropertyField(machineName);
        EditorGUILayout.PropertyField(machineType);

        if (machineType.enumValueIndex < 0 ||
            machineType.enumValueIndex >= machineType.enumNames.Length)
        {
            EditorGUILayout.HelpBox(
                "Invalid machine type. Re-select a valid machine type.",
                MessageType.Error);

            serializedObject.ApplyModifiedProperties();
            return;
        }

        if (machineType.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox(
                "Select a single machine profile to edit its components.",
                MessageType.Info);

            serializedObject.ApplyModifiedProperties();
            return;
        }

        if (serializedObject.hasModifiedProperties)
            serializedObject.ApplyModifiedProperties();

        serializedObject.Update();

        Type enumType = MachineDefinitions.GetComponentEnumType(
            (MachineType)machineType.enumValueIndex);

        Array enumValues = Enum.GetValues(enumType);

        if (components.arraySize != enumValues.Length)
        {
            EditorGUILayout.HelpBox(
                "Component list needs initialization for this machine type.",
                MessageType.Warning);

            if (GUILayout.Button("Generate Components"))
            {
                PopulateComponents();
                serializedObject.Update();
            }

            serializedObject.ApplyModifiedProperties();
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Machine Components",
            EditorStyles.boldLabel);

        for (int i = 0; i < components.arraySize; i++)
        {
            SerializedProperty component =
                components.GetArrayElementAtIndex(i);

            SerializedProperty gameObjectRef =
                component.FindPropertyRelative("GameObjectRef");

            SerializedProperty description =
                component.FindPropertyRelative("ComponentDescription");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                Enum.GetName(enumType, i),
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                gameObjectRef,
                new GUIContent("GameObject Reference"));

            EditorGUILayout.PropertyField(
                description,
                new GUIContent("Description"));

            EditorGUILayout.EndVertical();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void PopulateComponents()
    {
        Type enumType = MachineDefinitions.GetComponentEnumType(
            (MachineType)machineType.enumValueIndex);

        Array enumValues = Enum.GetValues(enumType);

        components.arraySize = enumValues.Length;

        for (int i = 0; i < enumValues.Length; i++)
        {
            SerializedProperty component =
                components.GetArrayElementAtIndex(i);

            component.FindPropertyRelative("componentValue")
                .intValue = Convert.ToInt32(enumValues.GetValue(i));

            component.FindPropertyRelative("GameObjectRef")
                .objectReferenceValue = null;

            component.FindPropertyRelative("ComponentDescription")
                .stringValue = MachineDefinitions.GetComponentDescription(
                    (Enum)enumValues.GetValue(i));
        }

        serializedObject.ApplyModifiedProperties();
    }
}
