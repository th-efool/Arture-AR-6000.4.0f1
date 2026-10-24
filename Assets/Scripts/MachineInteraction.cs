using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenAI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

[DefaultExecutionOrder(-120)]
[RequireComponent(typeof(MachineProfile))]
public class MachineInteraction : MonoBehaviour
{
    private const string FocusShaderResourcePath = "MachineFocus";
    private const string FocusShaderName = "Arture/MachineFocus";

    [Header("Answer UI")]
    [SerializeField] private WorldSpaceUI labelPrefab;
    [SerializeField] private Camera viewerCamera;
    [SerializeField] private float labelHeight = 0.08f;

    [Header("Highlight & Outline")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.1f, 1f);
    [SerializeField] [Range(1f, 16f)] private float outlineWidthPixels = 4.5f;
    [SerializeField] [Range(0f, 3f)] private float bloomGlowIntensity = 1.35f;
    [SerializeField] private Shader focusShader;

    [Header("Reveal Internal Components")]
    [SerializeField] [Range(0.02f, 0.5f)] private float unselectedOpacity = 0.27f;

    [Header("Conversation Integration")]
    [SerializeField] private RealtimeConversationManager conversationManager;

    private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProp = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");
    private static readonly int OutlineColorProp = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthProp = Shader.PropertyToID("_OutlineWidth");
    private static readonly int GlowIntensityProp = Shader.PropertyToID("_GlowIntensity");
    private static readonly int FadeOpacityProp = Shader.PropertyToID("_FadeOpacity");
    private static readonly int SurfaceProp = Shader.PropertyToID("_Surface");
    private static readonly int BlendProp = Shader.PropertyToID("_Blend");
    private static readonly int SrcBlendProp = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendProp = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteProp = Shader.PropertyToID("_ZWrite");

    private MachineProfile profile;
    private WorldSpaceUI activeLabel;
    private GameObject selectedObject;
    private Renderer[] selectedRenderers;

    private readonly List<RendererState> rendererStates = new();
    private readonly List<GameObject> outlineObjects = new();
    private readonly List<Material> runtimeMaterials = new();

    private bool conversationEventSubscribed;
    private float lastAnswerShownUnscaledTime = -999f;

    private class RendererState
    {
        public Renderer Renderer;
        public Material[] Originals;
        public ShadowCastingMode OriginalShadowCastingMode;
        public bool OriginalReceiveShadows;
        public int OriginalRendererPriority;
    }

    public WorldSpaceUI LabelPrefab
    {
        get => labelPrefab;
        set => labelPrefab = value;
    }

    public WorldSpaceUI ActiveLabel => activeLabel;
    public GameObject SelectedObject => selectedObject;
    public IReadOnlyList<GameObject> OutlineObjects => outlineObjects;
    public int ActiveRendererStateCount => rendererStates.Count;
    public int ActiveRuntimeMaterialCount => runtimeMaterials.Count;

    private void Awake()
    {
        profile = GetComponent<MachineProfile>();
        EnsureFocusShaderLoaded();
        BindConversationEvents();
    }

    private void OnEnable()
    {
        if (profile == null)
            profile = GetComponent<MachineProfile>();

        EnsureFocusShaderLoaded();
        BindConversationEvents();
    }

    private void Start()
    {
        BindConversationEvents();
        EnsureToolsRegistered();
    }

    private void BindConversationEvents()
    {
        if (conversationManager == null)
            conversationManager = FindAnyObjectByType<RealtimeConversationManager>();

        if (conversationManager != null && !conversationEventSubscribed)
        {
            if (!HasPersistentUserSpeakingListener(conversationManager))
            {
                conversationManager.onUserSpeaking.RemoveListener(OnUserSpeaking);
                conversationManager.onUserSpeaking.AddListener(OnUserSpeaking);
                conversationEventSubscribed = true;
            }
        }
    }

    private bool HasPersistentUserSpeakingListener(RealtimeConversationManager manager)
    {
        if (manager?.onUserSpeaking == null)
            return false;

        int count = manager.onUserSpeaking.GetPersistentEventCount();
        for (int i = 0; i < count; i++)
        {
            if (manager.onUserSpeaking.GetPersistentTarget(i) == this &&
                string.Equals(manager.onUserSpeaking.GetPersistentMethodName(i), nameof(OnUserSpeaking), StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private void UnbindConversationEvents()
    {
        if (conversationManager != null && conversationEventSubscribed)
        {
            conversationManager.onUserSpeaking.RemoveListener(OnUserSpeaking);
        }
        conversationEventSubscribed = false;
    }

    private void EnsureToolsRegistered()
    {
        var toolBindings = FindAnyObjectByType<ToolBindings>();
        if (toolBindings != null)
        {
            // Verify tools are registered before session sends tools spec
            JArray specs = AgentToolRegistry.GetToolsSpec();
            if (specs == null || specs.Count == 0)
            {
                toolBindings.RegisterAll();
            }
        }
    }

    private Shader EnsureFocusShaderLoaded()
    {
        if (focusShader != null && focusShader.isSupported)
            return focusShader;

        focusShader = Resources.Load<Shader>(FocusShaderResourcePath);
        if (focusShader == null)
            focusShader = Shader.Find(FocusShaderName);

        return focusShader;
    }

    // AI reads the real component data before answering.
    [Preserve]
    public JObject Machine_GetComponents(JObject args)
    {
        if (!isActiveAndEnabled)
            return Error("Machine interaction is disabled.");

        if (profile == null)
            profile = GetComponent<MachineProfile>();

        if (profile == null)
        {
            Debug.LogError($"[MachineInteraction] Missing MachineProfile on '{name}'.", this);
            return Error("MachineProfile component is missing.");
        }

        if (!Enum.IsDefined(typeof(MachineType), profile.MachineType))
        {
            Debug.LogError($"[MachineInteraction] Invalid MachineType '{profile.MachineType}' on '{name}'.", this);
            return Error($"Machine type '{profile.MachineType}' is invalid.");
        }

        Type enumType;
        try
        {
            enumType = MachineDefinitions.GetComponentEnumType(profile.MachineType);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MachineInteraction] Failed to resolve component enum for '{profile.MachineType}': {ex.Message}", this);
            return Error($"Unsupported machine type '{profile.MachineType}'.");
        }

        var components = new JArray();

        if (profile.Components != null)
        {
            foreach (MachineComponent component in profile.Components)
            {
                if (component == null)
                    continue;

                string componentName = Enum.GetName(enumType, component.ComponentValue);
                if (string.IsNullOrEmpty(componentName))
                {
                    Debug.LogWarning($"[MachineInteraction] Component entry on '{name}' has invalid enum value {component.ComponentValue} for {enumType.Name}.", this);
                    continue;
                }

                Renderer[] ownRenderers = component.GameObjectRef != null
                    ? ResolveComponentRenderers(component.GameObjectRef)
                    : Array.Empty<Renderer>();

                bool canHighlight = component.GameObjectRef != null && ownRenderers.Length > 0;

                components.Add(new JObject
                {
                    ["component"] = componentName,
                    ["description"] = GetDescription(component, enumType),
                    ["canHighlight"] = canHighlight
                });
            }
        }

        return new JObject
        {
            ["ok"] = true,
            ["machineId"] = profile.MachineID ?? string.Empty,
            ["machine"] = !string.IsNullOrWhiteSpace(profile.Name) ? profile.Name : name,
            ["machineType"] = profile.MachineType.ToString(),
            ["components"] = components
        };
    }

    // AI selects one component and supplies the answer to display.
    [Preserve]
    public JObject Machine_ShowAnswer(JObject args)
    {
        if (!isActiveAndEnabled)
            return Error("Machine interaction is disabled.");

        if (profile == null)
            profile = GetComponent<MachineProfile>();

        if (profile == null)
        {
            Debug.LogError($"[MachineInteraction] Missing MachineProfile on '{name}'.", this);
            return Error("MachineProfile component is missing.");
        }

        if (!Enum.IsDefined(typeof(MachineType), profile.MachineType))
        {
            Debug.LogError($"[MachineInteraction] Invalid MachineType '{profile.MachineType}' on '{name}'.", this);
            return Error($"Machine type '{profile.MachineType}' is invalid.");
        }

        string requestedName = args?.Value<string>("component")?.Trim();
        string answer = args?.Value<string>("answer")?.Trim();

        if (string.IsNullOrWhiteSpace(requestedName))
        {
            Debug.LogWarning($"[MachineInteraction] Machine_ShowAnswer called without a component name on '{name}'.", this);
            return Error("A component name is required.");
        }

        Type enumType;
        try
        {
            enumType = MachineDefinitions.GetComponentEnumType(profile.MachineType);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MachineInteraction] Failed to resolve component enum for '{profile.MachineType}': {ex.Message}", this);
            return Error($"Unsupported machine type '{profile.MachineType}'.");
        }

        MachineComponent selected = FindMatchingComponent(requestedName, enumType, out string selectedName);

        if (selected == null)
        {
            string validNames = string.Join(", ", Enum.GetNames(enumType));
            Debug.LogWarning($"[MachineInteraction] Unknown component '{requestedName}' requested on '{name}'. Valid components: {validNames}", this);
            return Error($"Unknown component '{requestedName}'. Call Machine_GetComponents and use one of: {validNames}.");
        }

        if (selected.GameObjectRef == null)
        {
            Debug.LogError($"[MachineInteraction] Component '{selectedName}' on '{name}' has no assigned GameObjectRef in MachineProfile.", this);
            return new JObject
            {
                ["ok"] = false,
                ["component"] = selectedName,
                ["highlighted"] = false,
                ["highlightStatus"] = "error",
                ["highlightError"] = $"Component '{selectedName}' has no assigned GameObject reference in MachineProfile on '{name}'.",
                ["uiDisplayed"] = false,
                ["uiStatus"] = "skipped",
                ["uiError"] = "Cannot position answer label because component GameObject reference is missing.",
                ["error"] = $"Component '{selectedName}' has no assigned GameObject reference."
            };
        }

        if (!selected.GameObjectRef.activeInHierarchy)
        {
            Debug.LogError($"[MachineInteraction] Assigned GameObject '{selected.GameObjectRef.name}' for component '{selectedName}' is inactive in hierarchy.", selected.GameObjectRef);
            return new JObject
            {
                ["ok"] = false,
                ["component"] = selectedName,
                ["highlighted"] = false,
                ["highlightStatus"] = "error",
                ["highlightError"] = $"Assigned GameObject '{selected.GameObjectRef.name}' for component '{selectedName}' is inactive.",
                ["uiDisplayed"] = false,
                ["uiStatus"] = "skipped",
                ["uiError"] = $"Assigned GameObject '{selected.GameObjectRef.name}' is inactive.",
                ["error"] = $"Assigned GameObject '{selected.GameObjectRef.name}' for component '{selectedName}' is inactive."
            };
        }

        // Clear any previous highlight and label before applying the new selection
        ClearAnswer();

        selectedObject = selected.GameObjectRef;
        selectedRenderers = ResolveComponentRenderers(selectedObject);

        bool highlightApplied = false;
        string highlightError = null;

        if (selectedRenderers.Length == 0)
        {
            highlightError = $"Assigned GameObject '{selectedObject.name}' for component '{selectedName}' has no active MeshRenderer or SkinnedMeshRenderer with a valid mesh.";
            Debug.LogError($"[MachineInteraction] {highlightError}", selectedObject);
        }
        else
        {
            highlightApplied = ApplySelectionAndRevealEffect(out highlightError);
        }

        if (string.IsNullOrWhiteSpace(answer))
            answer = GetDescription(selected, enumType);

        bool uiDisplayed = false;
        string uiError = null;

        if (labelPrefab == null)
        {
            uiError = $"WorldSpaceUI 'labelPrefab' is not assigned on MachineInteraction ('{name}').";
            Debug.LogWarning($"[MachineInteraction] {uiError} Highlight was still applied: {highlightApplied}.", this);
        }
        else
        {
            string formattedLabel = $"<b>{FormatDisplayName(selectedName)}</b>\n{answer}";
            activeLabel = WorldSpaceUI.Spawn(
                labelPrefab,
                GetLabelPosition(),
                formattedLabel
            );

            if (activeLabel == null)
            {
                uiError = "Failed to instantiate WorldSpaceUI label prefab.";
                Debug.LogError($"[MachineInteraction] {uiError}", this);
            }
            else if (activeLabel.TextElement == null)
            {
                uiError = $"WorldSpaceUI prefab '{labelPrefab.name}' is missing a valid TMP_Text reference.";
                Debug.LogError($"[MachineInteraction] {uiError}", activeLabel);
                DestroyRuntimeObject(activeLabel.gameObject);
                activeLabel = null;
            }
            else
            {
                UpdateLabel();
                uiDisplayed = true;
            }
        }

        lastAnswerShownUnscaledTime = Time.unscaledTime;

        bool overallOk = highlightApplied || uiDisplayed;
        var response = new JObject
        {
            ["ok"] = overallOk,
            ["component"] = selectedName,
            ["answer"] = answer ?? string.Empty,
            ["highlighted"] = highlightApplied,
            ["highlightStatus"] = highlightApplied ? "ok" : "error",
            ["uiDisplayed"] = uiDisplayed,
            ["uiStatus"] = uiDisplayed ? "ok" : "error"
        };

        if (!string.IsNullOrEmpty(highlightError))
            response["highlightError"] = highlightError;

        if (!string.IsNullOrEmpty(uiError))
            response["uiError"] = uiError;

        if (!overallOk)
            response["error"] = highlightError ?? uiError ?? "Both component highlighting and answer UI failed.";

        return response;
    }

    private MachineComponent FindMatchingComponent(string requestedName, Type enumType, out string canonicalName)
    {
        canonicalName = null;
        if (profile.Components == null)
            return null;

        string normalizedRequest = NormalizeIdentifier(requestedName);

        foreach (MachineComponent component in profile.Components)
        {
            if (component == null)
                continue;

            string name = Enum.GetName(enumType, component.ComponentValue);
            if (string.IsNullOrEmpty(name))
                continue;

            if (string.Equals(name, requestedName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeIdentifier(name), normalizedRequest, StringComparison.OrdinalIgnoreCase))
            {
                canonicalName = name;
                return component;
            }
        }

        return null;
    }

    private static string NormalizeIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        char[] buffer = new char[value.Length];
        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsLetterOrDigit(c))
                buffer[count++] = char.ToLowerInvariant(c);
        }
        return new string(buffer, 0, count);
    }

    private static string FormatDisplayName(string pascalName)
    {
        if (string.IsNullOrWhiteSpace(pascalName))
            return string.Empty;

        var chars = new List<char>(pascalName.Length + 4);
        for (int i = 0; i < pascalName.Length; i++)
        {
            char c = pascalName[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(pascalName[i - 1]))
                chars.Add(' ');
            chars.Add(c);
        }
        return new string(chars.ToArray());
    }

    /// <summary>
    /// Collects renderers belonging to the selected component's hierarchy while excluding subtrees
    /// that are explicitly assigned as separate MachineComponents (e.g., excluding Impeller when PumpCasing is selected).
    /// Falls back to all child renderers if excluding sub-components would leave zero renderers.
    /// </summary>
    private Renderer[] ResolveComponentRenderers(GameObject targetRoot)
    {
        if (targetRoot == null)
            return Array.Empty<Renderer>();

        var otherAssignedRoots = new HashSet<Transform>();
        if (profile != null && profile.Components != null)
        {
            foreach (MachineComponent comp in profile.Components)
            {
                if (comp?.GameObjectRef != null &&
                    comp.GameObjectRef != targetRoot &&
                    comp.GameObjectRef.transform.IsChildOf(targetRoot.transform))
                {
                    otherAssignedRoots.Add(comp.GameObjectRef.transform);
                }
            }
        }

        var collected = new List<Renderer>();
        CollectRenderersRecursive(targetRoot.transform, targetRoot.transform, otherAssignedRoots, collected);

        if (collected.Count == 0 && otherAssignedRoots.Count > 0)
        {
            CollectRenderersRecursive(targetRoot.transform, targetRoot.transform, null, collected);
        }

        return collected.ToArray();
    }

    private static void CollectRenderersRecursive(
        Transform current,
        Transform root,
        HashSet<Transform> excludedSubtrees,
        List<Renderer> results)
    {
        if (current == null || !current.gameObject.activeInHierarchy)
            return;

        if (current != root && excludedSubtrees != null && excludedSubtrees.Contains(current))
            return;

        if (current.TryGetComponent<Renderer>(out var renderer) && IsSupportedValidRenderer(renderer))
        {
            results.Add(renderer);
        }

        for (int i = 0; i < current.childCount; i++)
        {
            CollectRenderersRecursive(current.GetChild(i), root, excludedSubtrees, results);
        }
    }

    private static bool IsSupportedValidRenderer(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            return false;

        if (renderer is MeshRenderer)
        {
            var mf = renderer.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0;
        }

        if (renderer is SkinnedMeshRenderer smr)
        {
            return smr.sharedMesh != null && smr.sharedMesh.vertexCount > 0;
        }

        return false;
    }

    // Connect this to the conversation manager's On User Speaking event.
    public void OnUserSpeaking(bool speaking)
    {
        if (!speaking)
            return;

        // Guard against clearing a newly displayed answer if a late VAD event fires in the same frame/turn
        if (Application.isPlaying && (Time.unscaledTime - lastAnswerShownUnscaledTime) < 0.25f)
            return;

        ClearAnswer();
    }

    public void ClearAnswer()
    {
        if (activeLabel != null)
        {
            activeLabel.gameObject.SetActive(false);
            DestroyRuntimeObject(activeLabel.gameObject);
            activeLabel = null;
        }

        for (int i = outlineObjects.Count - 1; i >= 0; i--)
        {
            GameObject outlineGo = outlineObjects[i];
            if (outlineGo != null)
            {
                outlineGo.SetActive(false);
                DestroyRuntimeObject(outlineGo);
            }
        }
        outlineObjects.Clear();

        foreach (RendererState state in rendererStates)
        {
            if (state.Renderer != null)
            {
                state.Renderer.sharedMaterials = state.Originals;
                state.Renderer.shadowCastingMode = state.OriginalShadowCastingMode;
                state.Renderer.receiveShadows = state.OriginalReceiveShadows;
                state.Renderer.rendererPriority = state.OriginalRendererPriority;
            }
        }
        rendererStates.Clear();

        for (int i = runtimeMaterials.Count - 1; i >= 0; i--)
        {
            Material mat = runtimeMaterials[i];
            if (mat != null)
                DestroyRuntimeObject(mat);
        }
        runtimeMaterials.Clear();

        selectedObject = null;
        selectedRenderers = null;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    [Preserve]
    public JObject Machine_SetScale(JObject args)
    {
        float multiplier = args?.Value<float?>("multiplier") ?? 1f;
        multiplier = Mathf.Clamp(multiplier, 0.5f, 2f);
        transform.localScale *= multiplier;
        UpdateLabel();
        return new JObject { ["ok"] = true, ["multiplier"] = multiplier, ["scale"] = transform.localScale.ToString() };
    }

    [Preserve]
    public JObject Machine_Rotate(JObject args)
    {
        float degrees = Mathf.Clamp(args?.Value<float?>("degrees") ?? 15f, -180f, 180f);
        string axis = (args?.Value<string>("axis") ?? "y").ToLowerInvariant();
        Vector3 rotationAxis = axis == "x" ? Vector3.right : axis == "z" ? Vector3.forward : Vector3.up;
        transform.Rotate(rotationAxis, degrees, Space.World);
        UpdateLabel();
        return new JObject { ["ok"] = true, ["axis"] = axis, ["degrees"] = degrees };
    }

    [Preserve]
    public JObject Machine_Translate(JObject args)
    {
        float x = Mathf.Clamp(args?.Value<float?>("x") ?? 0f, -2f, 2f);
        float y = Mathf.Clamp(args?.Value<float?>("y") ?? 0f, -2f, 2f);
        float z = Mathf.Clamp(args?.Value<float?>("z") ?? 0f, -2f, 2f);
        Vector3 offset = new Vector3(x, y, z);
        transform.position += offset;
        UpdateLabel();
        return new JObject { ["ok"] = true, ["offset"] = offset.ToString(), ["position"] = transform.position.ToString() };
    }

    private bool ApplySelectionAndRevealEffect(out string errorMessage)
    {
        errorMessage = null;
        Shader shader = EnsureFocusShaderLoaded();
        if (shader == null || !shader.isSupported)
        {
            errorMessage = $"Focus shader '{FocusShaderName}' is missing or unsupported on this graphics device.";
            Debug.LogError($"[MachineInteraction] {errorMessage}", this);
        }

        var selectedSet = new HashSet<Renderer>(selectedRenderers);
        Renderer[] allMachineRenderers = GetComponentsInChildren<Renderer>(false);

        int highlightedCount = 0;

        foreach (Renderer renderer in allMachineRenderers)
        {
            if (!IsSupportedValidRenderer(renderer))
                continue;

            // Never fade or alter WorldSpaceUI canvases if parented under the machine
            if (renderer.GetComponentInParent<WorldSpaceUI>() != null)
                continue;

            Material[] originals = renderer.sharedMaterials;
            if (originals == null || originals.Length == 0)
                continue;

            rendererStates.Add(new RendererState
            {
                Renderer = renderer,
                Originals = (Material[])originals.Clone(),
                OriginalShadowCastingMode = renderer.shadowCastingMode,
                OriginalReceiveShadows = renderer.receiveShadows,
                OriginalRendererPriority = renderer.rendererPriority
            });

            if (selectedSet.Contains(renderer))
            {
                ApplySelectedComponentMaterials(renderer, originals, shader);
                CreateSilhouetteOutlineRenderer(renderer, originals.Length, shader);
                highlightedCount++;
            }
            else
            {
                ApplyFadedMachineMaterials(renderer, originals);
            }
        }

        if (highlightedCount == 0)
        {
            errorMessage = $"No valid renderers could be highlighted for '{selectedObject?.name}'.";
            Debug.LogError($"[MachineInteraction] {errorMessage}", this);
            return false;
        }

        return true;
    }

    private void ApplySelectedComponentMaterials(Renderer renderer, Material[] originals, Shader shader)
    {
        var updatedMaterials = new Material[originals.Length];
        Color emissionTint = highlightColor * Mathf.Max(0.35f, bloomGlowIntensity * 0.45f);

        for (int i = 0; i < originals.Length; i++)
        {
            Material orig = originals[i];
            if (orig == null)
                continue;

            Material copy = new Material(orig)
            {
                name = orig.name + "_SelectedRuntime"
            };
            runtimeMaterials.Add(copy);

            // Ensure selected component stays 100% opaque and writes to depth buffer
            ConfigureMaterialOpaque(copy);

            // Subtle warm lift on base color while preserving original texture & shading identity
            Color baseCol = GetMaterialColor(orig);
            Color lifted = Color.Lerp(baseCol, highlightColor, 0.28f);
            lifted.a = 1f;
            SetMaterialColor(copy, lifted);

            if (copy.HasProperty(EmissionColorProp))
            {
                copy.EnableKeyword("_EMISSION");
                copy.SetColor(EmissionColorProp, emissionTint);
            }

            copy.renderQueue = (int)RenderQueue.Geometry + 10;
            updatedMaterials[i] = copy;
        }

        renderer.rendererPriority = 10;
        renderer.sharedMaterials = updatedMaterials;
    }

    private void CreateSilhouetteOutlineRenderer(Renderer sourceRenderer, int subMeshSlotCount, Shader shader)
    {
        if (shader == null || !shader.isSupported)
            return;

        var outlineGo = new GameObject(sourceRenderer.gameObject.name + "_OutlineHalo");
        outlineGo.layer = sourceRenderer.gameObject.layer;
        outlineGo.transform.SetParent(sourceRenderer.transform, false);
        outlineGo.transform.localPosition = Vector3.zero;
        outlineGo.transform.localRotation = Quaternion.identity;
        outlineGo.transform.localScale = Vector3.one;
        outlineObjects.Add(outlineGo);

        Material outlineMat = new Material(shader)
        {
            name = "MachineFocus_OutlineRuntime"
        };
        runtimeMaterials.Add(outlineMat);

        outlineMat.SetColor(OutlineColorProp, new Color(highlightColor.r, highlightColor.g, highlightColor.b, 1f));
        outlineMat.SetFloat(OutlineWidthProp, outlineWidthPixels);
        outlineMat.SetFloat(GlowIntensityProp, Mathf.Max(1f, bloomGlowIntensity));
        // Render after opaque selected part (2010) and before faded transparent casing (3020),
        // or right at 3005 so the outline is crisp and visible through the faded casing.
        outlineMat.renderQueue = (int)RenderQueue.Transparent + 5;

        int slotCount = Mathf.Max(1, subMeshSlotCount);
        var outlineMats = new Material[slotCount];
        for (int i = 0; i < slotCount; i++)
            outlineMats[i] = outlineMat;

        if (sourceRenderer is MeshRenderer)
        {
            var srcFilter = sourceRenderer.GetComponent<MeshFilter>();
            if (srcFilter != null && srcFilter.sharedMesh != null)
            {
                var dstFilter = outlineGo.AddComponent<MeshFilter>();
                dstFilter.sharedMesh = srcFilter.sharedMesh;

                var dstRenderer = outlineGo.AddComponent<MeshRenderer>();
                dstRenderer.shadowCastingMode = ShadowCastingMode.Off;
                dstRenderer.receiveShadows = false;
                dstRenderer.lightProbeUsage = LightProbeUsage.Off;
                dstRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                dstRenderer.rendererPriority = 15;
                dstRenderer.sharedMaterials = outlineMats;
            }
        }
        else if (sourceRenderer is SkinnedMeshRenderer srcSkinned)
        {
            var dstSkinned = outlineGo.AddComponent<SkinnedMeshRenderer>();
            dstSkinned.sharedMesh = srcSkinned.sharedMesh;
            dstSkinned.bones = srcSkinned.bones;
            dstSkinned.rootBone = srcSkinned.rootBone;
            dstSkinned.shadowCastingMode = ShadowCastingMode.Off;
            dstSkinned.receiveShadows = false;
            dstSkinned.lightProbeUsage = LightProbeUsage.Off;
            dstSkinned.reflectionProbeUsage = ReflectionProbeUsage.Off;
            dstSkinned.rendererPriority = 15;
            dstSkinned.sharedMaterials = outlineMats;
        }
    }

    private void ApplyFadedMachineMaterials(Renderer renderer, Material[] originals)
    {
        var fadedMaterials = new Material[originals.Length];
        float targetAlpha = Mathf.Clamp(unselectedOpacity, 0.02f, 0.85f);

        for (int i = 0; i < originals.Length; i++)
        {
            Material orig = originals[i];
            if (orig == null)
                continue;

            Material copy = new Material(orig)
            {
                name = orig.name + "_FadedRuntime"
            };
            runtimeMaterials.Add(copy);

            ConfigureMaterialTransparent(copy);

            Color baseCol = GetMaterialColor(orig);
            baseCol.a = targetAlpha;
            SetMaterialColor(copy, baseCol);

            if (copy.HasProperty(FadeOpacityProp))
                copy.SetFloat(FadeOpacityProp, targetAlpha);

            // Render after selected component and its outline, with ZWrite Off so casing never occludes internal components
            copy.renderQueue = (int)RenderQueue.Transparent + 20;
            fadedMaterials[i] = copy;
        }

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.rendererPriority = -10;
        renderer.sharedMaterials = fadedMaterials;
    }

    private static Color GetMaterialColor(Material mat)
    {
        if (mat == null)
            return Color.white;
        if (mat.HasProperty(BaseColorProp))
            return mat.GetColor(BaseColorProp);
        if (mat.HasProperty(ColorProp))
            return mat.GetColor(ColorProp);
        return Color.white;
    }

    private static void SetMaterialColor(Material mat, Color color)
    {
        if (mat == null)
            return;
        if (mat.HasProperty(BaseColorProp))
            mat.SetColor(BaseColorProp, color);
        if (mat.HasProperty(ColorProp))
            mat.SetColor(ColorProp, color);
    }

    private static void ConfigureMaterialTransparent(Material mat)
    {
        if (mat.HasProperty(SurfaceProp))
            mat.SetFloat(SurfaceProp, 1f);
        if (mat.HasProperty(BlendProp))
            mat.SetFloat(BlendProp, 0f);

        mat.SetOverrideTag("RenderType", "Transparent");
        if (mat.HasProperty(SrcBlendProp))
            mat.SetInt(SrcBlendProp, (int)BlendMode.SrcAlpha);
        if (mat.HasProperty(DstBlendProp))
            mat.SetInt(DstBlendProp, (int)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty(ZWriteProp))
            mat.SetInt(ZWriteProp, 0);

        mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    private static void ConfigureMaterialOpaque(Material mat)
    {
        if (mat.HasProperty(SurfaceProp))
            mat.SetFloat(SurfaceProp, 0f);

        mat.SetOverrideTag("RenderType", "Opaque");
        if (mat.HasProperty(SrcBlendProp))
            mat.SetInt(SrcBlendProp, (int)BlendMode.One);
        if (mat.HasProperty(DstBlendProp))
            mat.SetInt(DstBlendProp, (int)BlendMode.Zero);
        if (mat.HasProperty(ZWriteProp))
            mat.SetInt(ZWriteProp, 1);

        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_SURFACE_TYPE_OPAQUE");
    }

    private Vector3 GetLabelPosition()
    {
        Bounds componentBounds = new Bounds(
            selectedObject != null ? selectedObject.transform.position : transform.position,
            Vector3.zero);

        bool hasComponentBounds = false;

        if (selectedRenderers != null)
        {
            foreach (Renderer renderer in selectedRenderers)
            {
                if (renderer == null)
                    continue;

                if (!hasComponentBounds)
                {
                    componentBounds = renderer.bounds;
                    hasComponentBounds = true;
                }
                else
                {
                    componentBounds.Encapsulate(renderer.bounds);
                }
            }
        }

        // Also compute overall machine top Y so labels for internal components (like Impeller)
        // are placed above the machine silhouette and never buried inside the machine mesh.
        float machineTopY = componentBounds.max.y;
        foreach (RendererState state in rendererStates)
        {
            if (state.Renderer != null)
            {
                machineTopY = Mathf.Max(machineTopY, state.Renderer.bounds.max.y);
            }
        }

        float scaleFactor = Mathf.Max(0.25f, transform.lossyScale.y);
        float effectiveOffset = labelHeight * scaleFactor;
        float targetY = Mathf.Max(componentBounds.max.y + effectiveOffset, machineTopY + effectiveOffset * 0.5f);

        return new Vector3(
            componentBounds.center.x,
            targetY,
            componentBounds.center.z
        );
    }

    private void LateUpdate()
    {
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        if (activeLabel == null)
            return;

        if (selectedObject == null)
        {
            ClearAnswer();
            return;
        }

        activeLabel.transform.position = GetLabelPosition();

        Camera cam = ResolveViewerCamera();
        if (cam != null)
        {
            Vector3 toLabel = activeLabel.transform.position - cam.transform.position;
            if (toLabel.sqrMagnitude > 0.0001f)
            {
                activeLabel.transform.rotation = Quaternion.LookRotation(toLabel.normalized, Vector3.up);
            }
            else
            {
                activeLabel.transform.rotation = cam.transform.rotation;
            }
        }
    }

    private Camera ResolveViewerCamera()
    {
        if (viewerCamera != null && viewerCamera.isActiveAndEnabled)
            return viewerCamera;

        if (Camera.main != null)
            return Camera.main;

        return FindAnyObjectByType<Camera>();
    }

    private static string GetDescription(MachineComponent component, Type enumType)
    {
        if (!string.IsNullOrWhiteSpace(component.ComponentDescription))
            return component.ComponentDescription;

        var value = (Enum)Enum.ToObject(enumType, component.ComponentValue);
        return MachineDefinitions.GetComponentDescription(value);
    }

    private static JObject Error(string message)
    {
        return new JObject
        {
            ["ok"] = false,
            ["error"] = message
        };
    }

    private void OnDisable()
    {
        ClearAnswer();
        UnbindConversationEvents();
    }

    private void OnDestroy()
    {
        ClearAnswer();
        UnbindConversationEvents();
    }
}
