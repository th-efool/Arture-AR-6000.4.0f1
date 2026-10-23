using System.Collections;
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

    private Vector3 _originalScale;
    private Vector3 _originalPosition;
    private Quaternion _originalRotation;
    private Color _originalColor;

    private bool _isFloating;
    private float _floatTimer;
    private float _floatBaseY;

    private bool _isRainbow;
    private float _rainbowHue;

    private bool _isPulsing;
    private float _pulseTimer;
    private Vector3 _pulseBaseScale;

    private Coroutine _activeRoutine;

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

        _originalScale = transform.localScale;
        _originalPosition = transform.position;
        _originalRotation = transform.rotation;
        _floatBaseY = transform.position.y;
        _pulseBaseScale = transform.localScale;

        EnsureMaterialInstance();
        _originalColor = GetCurrentMaterialColor();
    }

    private void Update()
    {
        if (_isSpinning)
        {
            transform.Rotate(0f, _spinSpeed * Time.deltaTime, 0f, Space.Self);
        }

        if (_isFloating)
        {
            _floatTimer += Time.deltaTime * 2.2f;
            Vector3 pos = transform.position;
            pos.y = _floatBaseY + Mathf.Sin(_floatTimer) * 0.35f;
            transform.position = pos;
        }

        if (_isPulsing)
        {
            _pulseTimer += Time.deltaTime * 3.5f;
            float factor = 1f + Mathf.Sin(_pulseTimer) * 0.22f;
            transform.localScale = _pulseBaseScale * factor;
        }

        if (_isRainbow)
        {
            _rainbowHue = (_rainbowHue + Time.deltaTime * 0.45f) % 1f;
            Color rainbowCol = Color.HSVToRGB(_rainbowHue, 0.85f, 1f);
            ApplyMaterialColorAndOpacity(rainbowCol, _currentOpacity);
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

    // ==========================================
    // Core Tools (Spin, Speed, Color, Opacity)
    // ==========================================

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
        var speed = args?.Value<float?>("speed") ?? 90f;
        _spinSpeed = speed;
        _isSpinning = true;

        Debug.Log($"[TestTools] Tool executed: Cube_SetSpeed -> {speed} deg/s");

        await Task.Yield();
        return new JObject { ["success"] = true, ["speed"] = speed };
    }

    public async Task<JObject> Cube_RandomColor(JObject args)
    {
        _isRainbow = false;
        Color randomColor = Random.ColorHSV(0f, 1f, 0.75f, 1f, 0.85f, 1f);
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
        if (step > 1f) step /= 100f;
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
        if (step > 1f) step /= 100f;
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
        if (targetOpacity > 1f) targetOpacity /= 100f;
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

    // ==========================================
    // New Creative AR Tools
    // ==========================================

    public async Task<JObject> Cube_SetScale(JObject args)
    {
        float multiplier = args?.Value<float?>("multiplier") ?? 1.5f;
        multiplier = Mathf.Clamp(multiplier, 0.2f, 4f);

        _pulseBaseScale = _originalScale * multiplier;
        transform.localScale = _pulseBaseScale;
        Debug.Log($"[TestTools] Tool executed: Cube_SetScale (multiplier={multiplier:F2}, scale={transform.localScale})");

        await Task.Yield();
        return new JObject { ["success"] = true, ["multiplier"] = multiplier };
    }

    public async Task<JObject> Cube_Jump(JObject args)
    {
        float jumpHeight = args?.Value<float?>("height") ?? 1.2f;
        jumpHeight = Mathf.Clamp(jumpHeight, 0.3f, 3f);

        if (_activeRoutine != null)
            StopCoroutine(_activeRoutine);

        _activeRoutine = StartCoroutine(JumpRoutine(jumpHeight));
        Debug.Log($"[TestTools] Tool executed: Cube_Jump (height={jumpHeight:F2}m)");

        await Task.Yield();
        return new JObject { ["success"] = true, ["height"] = jumpHeight };
    }

    private IEnumerator JumpRoutine(float height)
    {
        Vector3 startPos = transform.position;
        float duration = 0.65f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float yOffset = Mathf.Sin(t * Mathf.PI) * height;
            transform.position = new Vector3(startPos.x, startPos.y + yOffset, startPos.z);
            yield return null;
        }

        transform.position = startPos;
        _floatBaseY = startPos.y;
        _activeRoutine = null;
    }

    public async Task<JObject> Cube_Float(JObject args)
    {
        bool enable = args?.Value<bool?>("enable") ?? !_isFloating;
        _isFloating = enable;
        if (_isFloating)
        {
            _floatBaseY = transform.position.y;
            _floatTimer = 0f;
        }
        else
        {
            Vector3 pos = transform.position;
            pos.y = _floatBaseY;
            transform.position = pos;
        }

        Debug.Log($"[TestTools] Tool executed: Cube_Float (floating={_isFloating})");
        await Task.Yield();
        return new JObject { ["success"] = true, ["floating"] = _isFloating };
    }

    public async Task<JObject> Cube_Pulse(JObject args)
    {
        bool enable = args?.Value<bool?>("enable") ?? !_isPulsing;
        _isPulsing = enable;
        if (!_isPulsing)
        {
            transform.localScale = _pulseBaseScale;
        }
        else
        {
            _pulseTimer = 0f;
        }

        Debug.Log($"[TestTools] Tool executed: Cube_Pulse (pulsing={_isPulsing})");
        await Task.Yield();
        return new JObject { ["success"] = true, ["pulsing"] = _isPulsing };
    }

    public async Task<JObject> Cube_Rainbow(JObject args)
    {
        bool enable = args?.Value<bool?>("enable") ?? !_isRainbow;
        _isRainbow = enable;
        if (!_isRainbow)
        {
            Color current = GetCurrentMaterialColor();
            ApplyMaterialColorAndOpacity(current, _currentOpacity);
        }

        Debug.Log($"[TestTools] Tool executed: Cube_Rainbow (rainbow={_isRainbow})");
        await Task.Yield();
        return new JObject { ["success"] = true, ["rainbow"] = _isRainbow };
    }

    public async Task<JObject> Cube_Reset(JObject args)
    {
        if (_activeRoutine != null)
        {
            StopCoroutine(_activeRoutine);
            _activeRoutine = null;
        }

        _isSpinning = false;
        _isFloating = false;
        _isPulsing = false;
        _isRainbow = false;
        _spinSpeed = 90f;

        transform.position = _originalPosition;
        transform.rotation = _originalRotation;
        transform.localScale = _originalScale;
        _floatBaseY = _originalPosition.y;
        _pulseBaseScale = _originalScale;

        ApplyMaterialColorAndOpacity(_originalColor, 1f);

        Debug.Log("[TestTools] Tool executed: Cube_Reset (all properties restored)");
        await Task.Yield();
        return new JObject { ["success"] = true, ["reset"] = true };
    }
}
