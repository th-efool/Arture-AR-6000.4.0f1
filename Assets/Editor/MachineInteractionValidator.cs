using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenAI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MachineInteractionValidator
{
    private const string WaterPumpPrefabPath = "Assets/Prefabs/WaterPump.prefab";
    private const string WorldSpaceUIPrefabPath = "Assets/Prefabs/WorldSpaceUI.prefab";
    private const string MachineToolsJsonPath = "Assets/MachineTools.json";
    private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ReportPath = "Temp/MachineInteractionValidationReport.txt";
    private const string RunOnceSessionKey = "Arture.MachineInteractionValidator.RanV2";

    [InitializeOnLoadMethod]
    private static void RunValidationOnLoadOnce()
    {
        if (SessionState.GetBool(RunOnceSessionKey, false))
            return;

        SessionState.SetBool(RunOnceSessionKey, true);
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RunFullValidation();
            }
        };
    }

    [MenuItem("Tools/Arture/Validate Machine Interaction System")]
    public static void RunFullValidation()
    {
        var logLines = new List<string>();
        int passed = 0;
        int failed = 0;

        void AssertCheck(string testName, bool condition, string details)
        {
            if (condition)
            {
                passed++;
                string msg = $"[PASS] {testName}: {details}";
                logLines.Add(msg);
                Debug.Log($"[MachineInteractionValidator] {msg}");
            }
            else
            {
                failed++;
                string msg = $"[FAIL] {testName}: {details}";
                logLines.Add(msg);
                Debug.LogError($"[MachineInteractionValidator] {msg}");
            }
        }

        logLines.Add($"=== Machine Interaction Validation Report ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ===");

        // 0. Inspect assets & shader
        Shader focusShader = Resources.Load<Shader>("MachineFocus");
        AssertCheck("Shader Asset & Support",
            focusShader != null && focusShader.isSupported,
            focusShader != null ? $"Loaded '{focusShader.name}' (supported={focusShader.isSupported})" : "Resources.Load<Shader>('MachineFocus') returned null");

        GameObject pumpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterPumpPrefabPath);
        WorldSpaceUI uiPrefab = AssetDatabase.LoadAssetAtPath<WorldSpaceUI>(WorldSpaceUIPrefabPath);
        TextAsset toolsJson = AssetDatabase.LoadAssetAtPath<TextAsset>(MachineToolsJsonPath);

        AssertCheck("Prefab & Tool Assets Loaded",
            pumpPrefab != null && uiPrefab != null && toolsJson != null,
            $"WaterPump={pumpPrefab != null}, WorldSpaceUI={uiPrefab != null}, MachineTools.json={toolsJson != null}");

        bool messageToolRegisteredInSchema = false;
        if (toolsJson != null)
        {
            try
            {
                JObject schema = JObject.Parse(toolsJson.text);
                JArray toolDefinitions = schema["tools"] as JArray;
                messageToolRegisteredInSchema = toolDefinitions != null && toolDefinitions.Any(tool =>
                    string.Equals(tool["name"]?.ToString(), "Machine_ShowMessage", StringComparison.Ordinal));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MachineInteractionValidator] Could not parse MachineTools.json: {ex.Message}");
            }
        }
        AssertCheck("Machine_ShowMessage Tool Schema",
            messageToolRegisteredInSchema,
            $"registered={messageToolRegisteredInSchema}");

        if (uiPrefab != null)
        {
            Canvas uiCanvas = uiPrefab.GetComponent<Canvas>();
            TMP_Text uiText = uiPrefab.GetComponentInChildren<TMP_Text>(true);
            Vector3 scale = uiPrefab.transform.localScale;
            RectTransform rt = uiPrefab.GetComponent<RectTransform>();
            float worldWidth = rt != null ? rt.sizeDelta.x * scale.x : 0f;
            float worldHeight = rt != null ? rt.sizeDelta.y * scale.y : 0f;

            AssertCheck("WorldSpaceUI Prefab Configuration",
                uiCanvas != null && uiCanvas.renderMode == RenderMode.WorldSpace && uiText != null && worldWidth > 0.1f && worldWidth < 1.5f,
                $"renderMode={uiCanvas?.renderMode}, hasTMP={uiText != null}, worldSize=({worldWidth:F2}m x {worldHeight:F2}m)");
        }

        // Open isolated preview scene so SampleScene is never dirtied during testing
        Scene previewScene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject camGo = new GameObject("PreviewARCamera");
            SceneManager.MoveGameObjectToScene(camGo, previewScene);
            Camera previewCam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0.17f, 0.5f, 2.2f);
            camGo.transform.LookAt(new Vector3(0.17f, 0.25f, 3.95f));

            GameObject pumpInstance = (GameObject)PrefabUtility.InstantiatePrefab(pumpPrefab, previewScene);
            pumpInstance.transform.position = new Vector3(0.17f, 0f, 3.95f);

            MachineProfile profile = pumpInstance.GetComponent<MachineProfile>();
            MachineInteraction interaction = pumpInstance.GetComponent<MachineInteraction>();
            if (interaction == null)
                interaction = pumpInstance.AddComponent<MachineInteraction>();

            interaction.LabelPrefab = uiPrefab;

            // Snapshot original sharedMaterials for every renderer on the machine
            Renderer[] allRenderers = pumpInstance.GetComponentsInChildren<Renderer>(true);
            var originalMaterialSnapshot = new Dictionary<Renderer, Material[]>();
            foreach (Renderer r in allRenderers)
            {
                originalMaterialSnapshot[r] = (Material[])r.sharedMaterials.Clone();
            }

            // 1. Test Machine_GetComponents
            JObject getCompsResult = interaction.Machine_GetComponents(new JObject());
            JArray compsArray = getCompsResult["components"] as JArray;
            AssertCheck("1. Machine_GetComponents",
                getCompsResult["ok"]?.Value<bool>() == true &&
                getCompsResult["machineType"]?.ToString() == nameof(MachineType.CentrifugalWaterPump) &&
                compsArray != null && compsArray.Count == 14 &&
                compsArray.All(c => c["canHighlight"]?.Value<bool>() == true),
                $"ok={getCompsResult["ok"]}, machineType={getCompsResult["machineType"]}, count={compsArray?.Count}");

            JObject typedMessage = interaction.Machine_ShowMessage(new JObject
            {
                ["type"] = "warning",
                ["text"] = "Check the pump casing before operation.",
                ["component"] = "PumpCasing",
                ["direction"] = "left"
            });
            WorldSpaceUI typedLabel = interaction.ActiveLabel;
            WorldSpaceMessageIcon typedIcon = typedLabel != null
                ? typedLabel.GetComponentInChildren<WorldSpaceMessageIcon>(true)
                : null;
            TMP_Text typeLabel = typedLabel != null
                ? typedLabel.transform.Find("MessageHeader/MessageTypeLabel")?.GetComponent<TMP_Text>()
                : null;
            Image accent = typedLabel != null
                ? typedLabel.transform.Find("MessageAccent")?.GetComponent<Image>()
                : null;
            AssertCheck("Typed Message Card Runtime Layout",
                typedMessage["ok"]?.Value<bool>() == true &&
                typedMessage["uiDisplayed"]?.Value<bool>() == true &&
                typedLabel != null && typedLabel.gameObject.scene == previewScene &&
                typedLabel.TextElement.text == "Check the pump casing before operation." &&
                typeLabel != null && typeLabel.text == "WARNING" &&
                typedIcon != null && typedIcon.color == WorldSpaceUI.GetTypeColor(WorldSpaceMessageType.Warning) &&
                accent != null && accent.color == WorldSpaceUI.GetTypeColor(WorldSpaceMessageType.Warning),
                $"ok={typedMessage["ok"]}, uiDisplayed={typedMessage["uiDisplayed"]}, scene={typedLabel?.gameObject.scene.name}, type={typeLabel?.text}, icon={typedIcon != null}, accent={accent != null}");

            // Find specific component transforms for verification
            Transform electricMotorTr = profile.Components.First(c => c.ComponentValue == (int)MachineDefinitions.CentrifugalWaterPump.ElectricMotor).GameObjectRef.transform;
            Transform terminalBoxTr = profile.Components.First(c => c.ComponentValue == (int)MachineDefinitions.CentrifugalWaterPump.MotorTerminalBox).GameObjectRef.transform;
            Transform pumpCasingTr = profile.Components.First(c => c.ComponentValue == (int)MachineDefinitions.CentrifugalWaterPump.PumpCasing).GameObjectRef.transform;
            Transform impellerTr = profile.Components.First(c => c.ComponentValue == (int)MachineDefinitions.CentrifugalWaterPump.Impeller).GameObjectRef.transform;

            // 2. Test external component (ElectricMotor) and verify child sub-component exclusion
            JObject showMotor = interaction.Machine_ShowAnswer(new JObject
            {
                ["component"] = "ElectricMotor",
                ["answer"] = "Converts electrical energy into rotational mechanical power."
            });

            Renderer motorOwnRenderer = electricMotorTr.GetComponentsInChildren<Renderer>()
                .First(r => !r.transform.IsChildOf(terminalBoxTr) && !r.name.EndsWith("_OutlineHalo"));
            Renderer terminalBoxRenderer = terminalBoxTr.GetComponentsInChildren<Renderer>()
                .First(r => !r.name.EndsWith("_OutlineHalo"));

            bool motorOpaque = motorOwnRenderer.sharedMaterial.renderQueue < 2500;
            bool terminalBoxFaded = terminalBoxRenderer.sharedMaterial.renderQueue >= 3000;

            AssertCheck("2. External Component (ElectricMotor) & Subtree Exclusion",
                showMotor["ok"]?.Value<bool>() == true &&
                showMotor["highlighted"]?.Value<bool>() == true &&
                showMotor["uiDisplayed"]?.Value<bool>() == true &&
                interaction.OutlineObjects.Count > 0 &&
                motorOpaque && terminalBoxFaded,
                $"highlighted={showMotor["highlighted"]}, uiDisplayed={showMotor["uiDisplayed"]}, outlines={interaction.OutlineObjects.Count}, motorOpaque={motorOpaque}, childTerminalBoxFaded={terminalBoxFaded}");

            // 3. Test switching to internal component (Impeller inside PumpCasing)
            JObject showImpeller = interaction.Machine_ShowAnswer(new JObject
            {
                ["component"] = "Impeller",
                ["answer"] = "Rotates to transfer energy to the water, increasing velocity and pressure."
            });

            Renderer impellerRenderer = impellerTr.GetComponentsInChildren<Renderer>()
                .First(r => !r.name.EndsWith("_OutlineHalo"));
            Renderer casingRenderer = pumpCasingTr.GetComponentsInChildren<Renderer>()
                .First(r => !r.transform.IsChildOf(impellerTr) && !r.name.EndsWith("_OutlineHalo"));

            bool impellerOpaque = impellerRenderer.sharedMaterial.renderQueue < 2500 &&
                                  impellerRenderer.sharedMaterial.GetInt("_ZWrite") == 1;
            bool casingTransparentReveal = casingRenderer.sharedMaterial.renderQueue >= 3000 &&
                                           casingRenderer.sharedMaterial.GetInt("_ZWrite") == 0 &&
                                           casingRenderer.sharedMaterial.color.a > 0f &&
                                           casingRenderer.sharedMaterial.color.a < 1f;

            AssertCheck("3. Internal Component Reveal (Impeller inside PumpCasing)",
                showImpeller["ok"]?.Value<bool>() == true &&
                showImpeller["highlighted"]?.Value<bool>() == true &&
                showImpeller["uiDisplayed"]?.Value<bool>() == true &&
                impellerOpaque && casingTransparentReveal &&
                interaction.ActiveLabel != null &&
                interaction.ActiveLabel.Text.Contains("Impeller"),
                $"impellerOpaque={impellerOpaque}, casingTransparentReveal={casingTransparentReveal} (alpha={casingRenderer.sharedMaterial.color.a:F2}, queue={casingRenderer.sharedMaterial.renderQueue})");

            // 4. Test asking a follow-up question about the same component (Impeller)
            int outlineCountBeforeFollowUp = interaction.OutlineObjects.Count;
            int matCountBeforeFollowUp = interaction.ActiveRuntimeMaterialCount;

            JObject followUpImpeller = interaction.Machine_ShowAnswer(new JObject
            {
                ["component"] = "Impeller",
                ["answer"] = "Follow-up: The impeller vanes accelerate water radially outward into the volute."
            });

            int activeLabelsInScene = previewScene.GetRootGameObjects()
                .Sum(go => go.GetComponentsInChildren<WorldSpaceUI>(true).Count(u => u.gameObject.activeInHierarchy));

            AssertCheck("4. Follow-Up Question on Same Component (No Accumulation)",
                followUpImpeller["ok"]?.Value<bool>() == true &&
                interaction.ActiveLabel != null &&
                interaction.ActiveLabel.Text.Contains("Follow-up:") &&
                activeLabelsInScene == 1 &&
                interaction.OutlineObjects.Count == outlineCountBeforeFollowUp &&
                interaction.ActiveRuntimeMaterialCount == matCountBeforeFollowUp,
                $"activeLabels={activeLabelsInScene}, outlines={interaction.OutlineObjects.Count}, runtimeMats={interaction.ActiveRuntimeMaterialCount}");

            // 5. Test Transformation Tools while selection is active
            Vector3 posBefore = interaction.ActiveLabel.transform.position;
            interaction.Machine_Translate(new JObject { ["x"] = 0.25f, ["y"] = 0.15f, ["z"] = -0.1f });
            Vector3 posAfterTranslate = interaction.ActiveLabel.transform.position;

            interaction.Machine_Rotate(new JObject { ["axis"] = "y", ["degrees"] = 45f });
            interaction.Machine_SetScale(new JObject { ["multiplier"] = 1.25f });
            Vector3 posAfterScale = interaction.ActiveLabel.transform.position;

            AssertCheck("5. Transform Tools (Translate, Rotate, Scale) Follow Selection",
                Vector3.Distance(posBefore, posAfterTranslate) > 0.2f &&
                posAfterScale.y > posAfterTranslate.y &&
                interaction.OutlineObjects.All(o => o != null && o.transform.IsChildOf(impellerTr)),
                $"labelMoved={(posAfterTranslate - posBefore)}, scaledLabelY={posAfterScale.y:F3}");

            // 6. Test Missing UI Reference Fallback (labelPrefab == null must still highlight!)
            interaction.LabelPrefab = null;
            JObject showWithoutUI = interaction.Machine_ShowAnswer(new JObject
            {
                ["component"] = "PumpCasing",
                ["answer"] = "Encloses the impeller chamber."
            });

            AssertCheck("6. Missing UI Reference Does Not Block Highlight",
                showWithoutUI["ok"]?.Value<bool>() == true &&
                showWithoutUI["highlighted"]?.Value<bool>() == true &&
                showWithoutUI["highlightStatus"]?.ToString() == "ok" &&
                showWithoutUI["uiDisplayed"]?.Value<bool>() == false &&
                showWithoutUI["uiStatus"]?.ToString() == "error" &&
                !string.IsNullOrEmpty(showWithoutUI["uiError"]?.ToString()) &&
                interaction.OutlineObjects.Count > 0,
                $"ok={showWithoutUI["ok"]}, highlighted={showWithoutUI["highlighted"]}, uiDisplayed={showWithoutUI["uiDisplayed"]}, uiError='{showWithoutUI["uiError"]}'");

            interaction.LabelPrefab = uiPrefab;

            // 7. Test Invalid Component Requests
            JObject invalidComp = interaction.Machine_ShowAnswer(new JObject
            {
                ["component"] = "NonExistentFluxCapacitor",
                ["answer"] = "Invalid part."
            });
            JObject emptyComp = interaction.Machine_ShowAnswer(new JObject
            {
                ["component"] = "",
                ["answer"] = "Empty part."
            });

            AssertCheck("7. Invalid Component Requests Rejected Cleanly",
                invalidComp["ok"]?.Value<bool>() == false &&
                emptyComp["ok"]?.Value<bool>() == false &&
                !string.IsNullOrEmpty(invalidComp["error"]?.ToString()),
                $"invalidError='{invalidComp["error"]}'");

            // 8. Test Clearing Selection & Exact Material Restoration
            interaction.Machine_ShowAnswer(new JObject
            {
                ["component"] = "Impeller",
                ["answer"] = "Testing clear restoration."
            });
            interaction.OnUserSpeaking(true); // Simulates next spoken query starting

            bool allMaterialsRestored = true;
            foreach (var kvp in originalMaterialSnapshot)
            {
                Renderer r = kvp.Key;
                Material[] expected = kvp.Value;
                Material[] actual = r.sharedMaterials;
                if (actual.Length != expected.Length)
                {
                    allMaterialsRestored = false;
                    break;
                }
                for (int i = 0; i < expected.Length; i++)
                {
                    if (actual[i] != expected[i])
                    {
                        allMaterialsRestored = false;
                        break;
                    }
                }
            }

            int remainingOutlines = pumpInstance.GetComponentsInChildren<Transform>(true)
                .Count(t => t.name.EndsWith("_OutlineHalo"));

            AssertCheck("8. Clear on Next Question & Exact Material Restoration",
                allMaterialsRestored &&
                interaction.ActiveLabel == null &&
                interaction.OutlineObjects.Count == 0 &&
                interaction.ActiveRendererStateCount == 0 &&
                interaction.ActiveRuntimeMaterialCount == 0 &&
                remainingOutlines == 0,
                $"allMaterialsRestored={allMaterialsRestored}, activeLabel={interaction.ActiveLabel != null}, remainingOutlines={remainingOutlines}, runtimeMats={interaction.ActiveRuntimeMaterialCount}");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(previewScene);
        }

        // 9. Verify SampleScene Inspector Assignments
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path == SampleScenePath)
        {
            MachineInteraction sceneInteraction = UnityEngine.Object.FindAnyObjectByType<MachineInteraction>();
            ToolBindings sceneToolBindings = UnityEngine.Object.FindAnyObjectByType<ToolBindings>();
            RealtimeConversationManager sceneConvo = UnityEngine.Object.FindAnyObjectByType<RealtimeConversationManager>();

            bool sceneConfigured = sceneInteraction != null &&
                                   sceneInteraction.LabelPrefab != null &&
                                   sceneToolBindings != null;

            AssertCheck("9. SampleScene Inspector Assignments",
                sceneConfigured,
                $"sceneInteraction={sceneInteraction != null}, labelPrefab={sceneInteraction?.LabelPrefab?.name}, toolBindings={sceneToolBindings != null}, convoManager={sceneConvo != null}");
        }

        logLines.Add($"=== SUMMARY: {passed} Passed, {failed} Failed ===");
        Directory.CreateDirectory("Temp");
        File.WriteAllLines(ReportPath, logLines);
        Debug.Log($"[MachineInteractionValidator] Validation complete: {passed} passed, {failed} failed. Report written to {ReportPath}");
    }
}
