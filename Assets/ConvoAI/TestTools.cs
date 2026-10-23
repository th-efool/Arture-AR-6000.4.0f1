using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public class TestTools : MonoBehaviour
{
    private bool _isSpinning;
    private float _spinSpeed = 90f;
    private float _currentOpacity = 1f;
    private MeshRenderer _meshRenderer;
    private Light _attachedLight;
    private Material _instanceMaterial;

    private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProp = Shader.PropertyToID("_Color");
    private static readonly int SurfaceProp = Shader.PropertyToID("_Surface");
    private static readonly int BlendProp = Shader.PropertyToID("_Blend");
    private static readonly int SrcBlendProp = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendProp = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteProp = Shader.PropertyToID("_ZWrite");

    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        _attachedLight = GetComponent<Light>();
        EnsureMaterialInstance();
    }

    private void Update()
    {
        if (_isSpinning)
        {
            transform.Rotate(0f, _spinSpeed * Time.deltaTime, 0f, Space.Self);
        }
    }

    private Material EnsureMaterialInstance()
    {
        if (_instanceMaterial != null)
            return _instanceMaterial;

        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        if (_meshRenderer != null)
        {
            _instanceMaterial = _meshRenderer.material;
            Color current = GetCurrentMaterialColor();
            _currentOpacity = current.a;
        }

        return _instanceMaterial;
    }

    private Color GetCurrentMaterialColor()
    {
        var mat = EnsureMaterialInstance();
        if (mat == null)
            return Color.white;

        if (mat.HasProperty(BaseColorProp))
            return mat.GetColor(BaseColorProp);
        if (mat.HasProperty(ColorProp))
            return mat.GetColor(ColorProp);

        return mat.color;
    }

    private void ApplyMaterialColorAndOpacity(Color rgb, float alpha)
    {
        _currentOpacity = Mathf.Clamp01(alpha);
        Color finalColor = new Color(rgb.r, rgb.g, rgb.b, _currentOpacity);

        var mat = EnsureMaterialInstance();
        if (mat != null)
        {
            ConfigureMaterialTransparency(mat, _currentOpacity < 0.999f);

            if (mat.HasProperty(BaseColorProp))
                mat.SetColor(BaseColorProp, finalColor);
            if (mat.HasProperty(ColorProp))
                mat.SetColor(ColorProp, finalColor);

            mat.color = finalColor;
        }

        if (_attachedLight != null)
        {
            _attachedLight.color = new Color(rgb.r, rgb.g, rgb.b, 1f);
        }
    }

    private static void ConfigureMaterialTransparency(Material mat, bool transparent)
    {
        if (transparent)
        {
            // URP Lit surface type: 1 = Transparent, 0 = Opaque
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
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else
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
            mat.renderQueue = (int)RenderQueue.Geometry;
        }
    }

    // Method names MUST match your JSON schema
    public async Task<JObject> Cube_StartSpin(JObject args)
    {
        _isSpinning = true;
        transform.Rotate(0f, 90f, 0f);
        Debug.Log($"[TestTools] Tool executed: Cube_StartSpin (speed={_spinSpeed} deg/s)");

        await Task.Yield();
        return new JObject { ["success"] = true, ["spinning"] = true, ["speed"] = _spinSpeed };
    }

    public async Task<JObject> Cube_StopSpin(JObject args)
    {
        _isSpinning = false;
        transform.rotation = Quaternion.identity;
        Debug.Log("[TestTools] Tool executed: Cube_StopSpin");

        await Task.Yield();
        return new JObject { ["success"] = true, ["spinning"] = false };
    }

    public async Task<JObject> Cube_SetSpeed(JObject args)
    {
        // Extract parameters from args
        var speed = args?.Value<float?>("speed") ?? 90f;
        _spinSpeed = speed;
        _isSpinning = true;

        Debug.Log($"[TestTools] Tool executed: Cube_SetSpeed -> {speed} deg/s");

        await Task.Yield();
        return new JObject { ["success"] = true, ["speed"] = speed };
    }

    public async Task<JObject> Cube_RandomColor(JObject args)
    {
        Color randomColor = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.75f, 1f);
        ApplyMaterialColorAndOpacity(randomColor, _currentOpacity);

        string hex = $"#{ColorUtility.ToHtmlStringRGB(randomColor)}";
        Debug.Log($"[TestTools] Tool executed: Cube_RandomColor -> {hex}");

        await Task.Yield();
        return new JObject
        {
            ["success"] = true,
            ["colorHex"] = hex,
            ["opacity"] = _currentOpacity
        };
    }

    public async Task<JObject> Cube_DecreaseOpacity(JObject args)
    {
        float step = args?.Value<float?>("amount") ?? 0.25f;
        if (step > 1f) step /= 100f; // Support percentage inputs like 25 -> 0.25
        step = Mathf.Clamp(step, 0.05f, 1f);

        Color current = GetCurrentMaterialColor();
        float newOpacity = Mathf.Clamp(_currentOpacity - step, 0.05f, 1f);
        ApplyMaterialColorAndOpacity(current, newOpacity);

        Debug.Log($"[TestTools] Tool executed: Cube_DecreaseOpacity -> {_currentOpacity:F2}");

        await Task.Yield();
        return new JObject
        {
            ["success"] = true,
            ["opacity"] = _currentOpacity
        };
    }

    public async Task<JObject> Cube_IncreaseOpacity(JObject args)
    {
        float step = args?.Value<float?>("amount") ?? 0.25f;
        if (step > 1f) step /= 100f; // Support percentage inputs like 25 -> 0.25
        step = Mathf.Clamp(step, 0.05f, 1f);

        Color current = GetCurrentMaterialColor();
        float newOpacity = Mathf.Clamp(_currentOpacity + step, 0.05f, 1f);
        ApplyMaterialColorAndOpacity(current, newOpacity);

        Debug.Log($"[TestTools] Tool executed: Cube_IncreaseOpacity -> {_currentOpacity:F2}");

        await Task.Yield();
        return new JObject
        {
            ["success"] = true,
            ["opacity"] = _currentOpacity
        };
    }

    public async Task<JObject> Cube_SetOpacity(JObject args)
    {
        float targetOpacity = args?.Value<float?>("opacity") ?? 0.5f;
        if (targetOpacity > 1f) targetOpacity /= 100f; // Support 0-100% or 0.0-1.0
        targetOpacity = Mathf.Clamp(targetOpacity, 0.05f, 1f);

        Color current = GetCurrentMaterialColor();
        ApplyMaterialColorAndOpacity(current, targetOpacity);

        Debug.Log($"[TestTools] Tool executed: Cube_SetOpacity -> {_currentOpacity:F2}");

        await Task.Yield();
        return new JObject
        {
            ["success"] = true,
            ["opacity"] = _currentOpacity
        };
    }
}
