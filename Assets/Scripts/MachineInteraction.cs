using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

[RequireComponent(typeof(MachineProfile))]
public class MachineInteraction : MonoBehaviour
{
    [Header("Answer UI")]
    [SerializeField] private WorldSpaceUI labelPrefab;
    [SerializeField] private Camera viewerCamera;
    [SerializeField] private float labelHeight = 0.15f;

    [Header("Highlight")]
    [SerializeField] private Color highlightColor = Color.yellow;

    private MachineProfile profile;
    private WorldSpaceUI activeLabel;
    private GameObject selectedObject;
    private Renderer[] selectedRenderers;

    private readonly List<HighlightState> highlights = new();

    private class HighlightState
    {
        public Renderer Renderer;
        public Material[] Originals;
        public Material[] Copies;
    }

    private void Awake()
    {
        profile = GetComponent<MachineProfile>();
    }

    // AI reads the real component data before answering.
    public JObject Machine_GetComponents(JObject args)
    {
        if (!isActiveAndEnabled)
            return Error("Machine interaction is disabled.");

        if (!Enum.IsDefined(typeof(MachineType), profile.MachineType))
            return Error("Machine type is invalid.");

        Type enumType =
            MachineDefinitions.GetComponentEnumType(profile.MachineType);

        var components = new JArray();

        if (profile.Components != null)
        {
            foreach (MachineComponent component in profile.Components)
            {
                if (component == null)
                    continue;

                string name = Enum.GetName(
                    enumType, component.ComponentValue);

                if (name == null)
                    continue;

                components.Add(new JObject
                {
                    ["component"] = name,
                    ["description"] = GetDescription(component, enumType),
                    ["canHighlight"] = component.GameObjectRef != null
                });
            }
        }

        return new JObject
        {
            ["ok"] = true,
            ["machine"] = profile.Name,
            ["components"] = components
        };
    }

    // AI selects one component and supplies the answer to display.
    public JObject Machine_ShowAnswer(JObject args)
    {
        if (!isActiveAndEnabled)
            return Error("Machine interaction is disabled.");

        if (!Enum.IsDefined(typeof(MachineType), profile.MachineType))
            return Error("Machine type is invalid.");

        string requestedName = args?.Value<string>("component");
        string answer = args?.Value<string>("answer");

        if (string.IsNullOrWhiteSpace(requestedName))
            return Error("A component name is required.");

        if (labelPrefab == null)
            return Error("Assign the WorldSpaceUI label prefab.");

        Type enumType =
            MachineDefinitions.GetComponentEnumType(profile.MachineType);

        MachineComponent selected = null;
        string selectedName = null;

        if (profile.Components != null)
        {
            foreach (MachineComponent component in profile.Components)
            {
                if (component == null)
                    continue;

                string name = Enum.GetName(
                    enumType, component.ComponentValue);

                if (string.Equals(
                    name, requestedName, StringComparison.OrdinalIgnoreCase))
                {
                    selected = component;
                    selectedName = name;
                    break;
                }
            }
        }

        if (selected == null)
            return Error("Unknown component. Call Machine_GetComponents.");

        if (selected.GameObjectRef == null)
            return Error("This component has no assigned GameObject.");

        ClearAnswer();

        selectedObject = selected.GameObjectRef;
        selectedRenderers =
            selectedObject.GetComponentsInChildren<Renderer>();

        ApplyHighlight();

        if (string.IsNullOrWhiteSpace(answer))
            answer = GetDescription(selected, enumType);

        activeLabel = WorldSpaceUI.Spawn(
            labelPrefab,
            GetLabelPosition(),
            selectedName + "\n\n" + answer
        );

        UpdateLabel();

        return new JObject
        {
            ["ok"] = true,
            ["component"] = selectedName,
            ["answer"] = answer,
            ["highlighted"] = highlights.Count > 0
        };
    }

    // Connect this to the conversation manager's On User Speaking event.
    public void OnUserSpeaking(bool speaking)
    {
        if (speaking)
            ClearAnswer();
    }

    public void ClearAnswer()
    {
        if (activeLabel != null)
        {
            activeLabel.gameObject.SetActive(false);
            Destroy(activeLabel.gameObject);
            activeLabel = null;
        }

        foreach (HighlightState state in highlights)
        {
            if (state.Renderer != null)
                state.Renderer.sharedMaterials = state.Originals;

            foreach (Material copy in state.Copies)
            {
                if (copy != null)
                    Destroy(copy);
            }
        }

        highlights.Clear();
        selectedObject = null;
        selectedRenderers = null;
    }

    public JObject Machine_SetScale(JObject args)
    {
        float multiplier = args?.Value<float?>("multiplier") ?? 1f;
        multiplier = Mathf.Clamp(multiplier, 0.5f, 2f);
        transform.localScale *= multiplier;
        return new JObject { ["ok"] = true, ["multiplier"] = multiplier, ["scale"] = transform.localScale.ToString() };
    }

    public JObject Machine_Rotate(JObject args)
    {
        float degrees = Mathf.Clamp(args?.Value<float?>("degrees") ?? 15f, -180f, 180f);
        string axis = (args?.Value<string>("axis") ?? "y").ToLowerInvariant();
        Vector3 rotationAxis = axis == "x" ? Vector3.right : axis == "z" ? Vector3.forward : Vector3.up;
        transform.Rotate(rotationAxis, degrees, Space.World);
        return new JObject { ["ok"] = true, ["axis"] = axis, ["degrees"] = degrees };
    }

    public JObject Machine_Translate(JObject args)
    {
        float x = Mathf.Clamp(args?.Value<float?>("x") ?? 0f, -2f, 2f);
        float y = Mathf.Clamp(args?.Value<float?>("y") ?? 0f, -2f, 2f);
        float z = Mathf.Clamp(args?.Value<float?>("z") ?? 0f, -2f, 2f);
        Vector3 offset = new Vector3(x, y, z);
        transform.position += offset;
        return new JObject { ["ok"] = true, ["offset"] = offset.ToString(), ["position"] = transform.position.ToString() };
    }

    private void ApplyHighlight()
    {
        foreach (Renderer renderer in selectedRenderers)
        {
            Material[] originals = renderer.sharedMaterials;
            var copies = new Material[originals.Length];

            for (int i = 0; i < originals.Length; i++)
            {
                if (originals[i] == null)
                    continue;

                Material copy = new Material(originals[i]);
                copies[i] = copy;

                if (copy.HasProperty("_BaseColor"))
                    copy.SetColor("_BaseColor", highlightColor);

                if (copy.HasProperty("_Color"))
                    copy.SetColor("_Color", highlightColor);

                if (copy.HasProperty("_EmissionColor"))
                {
                    copy.EnableKeyword("_EMISSION");
                    copy.SetColor("_EmissionColor", highlightColor);
                }
            }

            highlights.Add(new HighlightState
            {
                Renderer = renderer,
                Originals = originals,
                Copies = copies
            });

            renderer.sharedMaterials = copies;
        }
    }

    private Vector3 GetLabelPosition()
    {
        Bounds bounds = new Bounds(
            selectedObject.transform.position, Vector3.zero);

        bool hasBounds = false;

        foreach (Renderer renderer in selectedRenderers)
        {
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return new Vector3(
            bounds.center.x,
            bounds.max.y + labelHeight,
            bounds.center.z
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

        Camera camera = viewerCamera != null
            ? viewerCamera
            : Camera.main;

        if (camera != null)
            activeLabel.transform.rotation = camera.transform.rotation;
    }

    private static string GetDescription(
        MachineComponent component, Type enumType)
    {
        if (!string.IsNullOrWhiteSpace(component.ComponentDescription))
            return component.ComponentDescription;

        var value = (Enum)Enum.ToObject(
            enumType, component.ComponentValue);

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
    }
}
