using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityMeshSimplifier;

public sealed class OnePassMeshSimplifier : EditorWindow
{
    [SerializeField] private GameObject source;
    [SerializeField] private float reduction = 50f;
    private string result;

    [MenuItem("Tools/Arture/Simplify Meshes")]
    private static void Open()
    {
        var window = GetWindow<OnePassMeshSimplifier>("Simplify Meshes");
        window.source = Selection.activeGameObject;
        window.minSize = new Vector2(360, 180);
    }

    private void OnGUI()
    {
        source = (GameObject)EditorGUILayout.ObjectField("Machine", source, typeof(GameObject), true);
        reduction = EditorGUILayout.Slider("Reduce triangles by (%)", reduction, 1f, 95f);
        EditorGUILayout.HelpBox("Simplifies all child meshes in one operation and saves a new prefab. Parts stay separate; the original is preserved. Reduction is approximate.", MessageType.Info);
        using (new EditorGUI.DisabledScope(source == null || EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Simplify and Save Copy", GUILayout.Height(32)))
                Simplify();
        }
        if (!string.IsNullOrEmpty(result))
            EditorGUILayout.HelpBox(result, MessageType.Info);
    }

    private void Simplify()
    {
        GameObject copy = null;
        string outputFolder = null;
        var meshes = new Dictionary<Mesh, Mesh>();
        try
        {
            // A preview scene avoids adding anything to the user's open scene.
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                copy = (GameObject)Instantiate(source, preview);
                copy.name = source.name + "_Simplified";
                if (PrefabUtility.IsPartOfPrefabInstance(copy))
                    PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                var filters = copy.GetComponentsInChildren<MeshFilter>(true);
                var skins = copy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var filter in filters)
                    if (filter.sharedMesh != null) meshes.TryAdd(filter.sharedMesh, null);
                foreach (var skin in skins)
                    if (skin.sharedMesh != null) meshes.TryAdd(skin.sharedMesh, null);
                if (meshes.Count == 0)
                    throw new InvalidOperationException("The selected object has no meshes.");

                long before = 0;
                long after = 0;
                int index = 0;
                foreach (var original in new List<Mesh>(meshes.Keys))
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Simplifying machine", original.name, (float)index++ / meshes.Count))
                        throw new OperationCanceledException();
                    var options = SimplificationOptions.Default;
                    options.PreserveBorderEdges = true;
                    options.PreserveUVSeamEdges = true;
                    options.PreserveUVFoldoverEdges = true;
                    var simplifier = new MeshSimplifier { SimplificationOptions = options };
                    simplifier.Initialize(original);
                    simplifier.SimplifyMesh(1f - reduction / 100f);
                    var simplified = simplifier.ToMesh();
                    meshes[original] = simplified;
                    simplified.name = original.name + "_Simplified";
                    simplified.RecalculateBounds();
                    before += original.triangles.LongLength / 3;
                    after += simplified.triangles.LongLength / 3;
                    if (original.vertexCount > 0 && simplified.vertexCount == 0)
                        throw new InvalidOperationException("Simplification removed a whole mesh: " + original.name);
                }

                if (!AssetDatabase.IsValidFolder("Assets/Optimized"))
                    AssetDatabase.CreateFolder("Assets", "Optimized");
                var folderName = "Machine_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                outputFolder = "Assets/Optimized/" + folderName;
                AssetDatabase.CreateFolder("Assets/Optimized", folderName);
                index = 0;
                foreach (var mesh in meshes.Values)
                    AssetDatabase.CreateAsset(mesh, outputFolder + "/Mesh_" + index++ + ".asset");
                foreach (var filter in filters)
                    if (filter.sharedMesh != null) filter.sharedMesh = meshes[filter.sharedMesh];
                foreach (var skin in skins)
                    if (skin.sharedMesh != null) skin.sharedMesh = meshes[skin.sharedMesh];
                foreach (var collider in copy.GetComponentsInChildren<MeshCollider>(true))
                    if (collider.sharedMesh != null && meshes.TryGetValue(collider.sharedMesh, out var mesh))
                        collider.sharedMesh = mesh;

                var prefab = PrefabUtility.SaveAsPrefabAsset(copy, outputFolder + "/Machine_Simplified.prefab");
                if (prefab == null) throw new InvalidOperationException("Could not save the simplified prefab.");
                AssetDatabase.SaveAssets();
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                result = $"Saved {meshes.Count} unique meshes: {before:N0} → {after:N0} triangles. Drag the selected prefab into your scene to inspect it.";
                outputFolder = null; // Keep successfully saved assets.
            }
            finally
            {
                if (copy != null) DestroyImmediate(copy);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
        }
        catch (OperationCanceledException) { result = "Cancelled. Original unchanged."; }
        catch (Exception exception)
        {
            result = "Could not simplify: " + exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (outputFolder != null) AssetDatabase.DeleteAsset(outputFolder);
            foreach (var mesh in meshes.Values)
                if (mesh != null && !EditorUtility.IsPersistent(mesh)) DestroyImmediate(mesh);
        }
    }
}
