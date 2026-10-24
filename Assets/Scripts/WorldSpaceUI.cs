using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum WorldSpaceMessageType
{
    Info,
    PointArrow,
    TurnArrow,
    Alert,
    Warning
}

public class WorldSpaceUI : MonoBehaviour
{
    private static readonly Color PanelBase = new Color(0.035f, 0.055f, 0.075f, 0.97f);

    [SerializeField] private TMP_Text textElement;

    private RectTransform rootRect;
    private Image panelImage;
    private Image accentImage;
    private Image badgeImage;
    private Image separatorImage;
    private TMP_Text typeLabel;
    private WorldSpaceMessageIcon messageIcon;

    public TMP_Text TextElement => textElement;
    public bool HasTextElement => textElement != null;

    public string Text
    {
        get => textElement != null ? textElement.text : string.Empty;
        set => SetText(value);
    }

    private void Awake()
    {
        EnsureValidSetup();
    }

    public bool EnsureValidSetup()
    {
        if (rootRect == null)
            rootRect = transform as RectTransform;
        if (textElement == null)
            textElement = GetComponentInChildren<TMP_Text>(true);

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = Mathf.Max(100, canvas.sortingOrder);
        }

        if (textElement == null || rootRect == null)
            return false;

        EnsureMessageLayout();
        ApplyMessageStyle(WorldSpaceMessageType.Info, "right");
        return true;
    }

    public void ShowMessage(WorldSpaceMessageType type, string message, string direction = "right")
    {
        if (!EnsureValidSetup())
        {
            Debug.LogWarning("WorldSpaceUI: Root RectTransform or TMP text is missing.", this);
            return;
        }

        ApplyMessageStyle(type, direction);
        textElement.text = message ?? string.Empty;
    }

    private void EnsureMessageLayout()
    {
        panelImage = GetOrCreateImage("LabelBackground", transform, true);
        panelImage.raycastTarget = false;
        SetStretch(panelImage.rectTransform, Vector2.zero, Vector2.zero);

        accentImage = GetOrCreateImage("MessageAccent", transform, false);
        accentImage.raycastTarget = false;
        accentImage.rectTransform.anchorMin = new Vector2(0f, 0f);
        accentImage.rectTransform.anchorMax = new Vector2(0f, 1f);
        accentImage.rectTransform.pivot = new Vector2(0f, 0.5f);
        accentImage.rectTransform.sizeDelta = new Vector2(8f, 0f);

        GameObject header = GetOrCreateRect("MessageHeader", transform, false);
        headerRect(header).anchorMin = new Vector2(0f, 1f);
        headerRect(header).anchorMax = new Vector2(1f, 1f);
        headerRect(header).pivot = new Vector2(0.5f, 1f);
        headerRect(header).anchoredPosition = new Vector2(0f, -10f);
        headerRect(header).sizeDelta = new Vector2(-40f, 58f);

        badgeImage = GetOrCreateImage("MessageIconBadge", header.transform, false);
        badgeImage.raycastTarget = false;
        RectTransform badgeRect = badgeImage.rectTransform;
        badgeRect.anchorMin = new Vector2(0f, 0.5f);
        badgeRect.anchorMax = new Vector2(0f, 0.5f);
        badgeRect.pivot = new Vector2(0f, 0.5f);
        badgeRect.anchoredPosition = Vector2.zero;
        badgeRect.sizeDelta = new Vector2(46f, 46f);

        GameObject glyph = GetOrCreateRect("MessageTypeIcon", badgeImage.transform, false);
        RectTransform glyphRect = headerRect(glyph);
        glyphRect.anchorMin = Vector2.zero;
        glyphRect.anchorMax = Vector2.one;
        glyphRect.offsetMin = new Vector2(9f, 9f);
        glyphRect.offsetMax = new Vector2(-9f, -9f);
        messageIcon = glyph.GetComponent<WorldSpaceMessageIcon>();
        if (messageIcon == null)
            messageIcon = glyph.AddComponent<WorldSpaceMessageIcon>();
        messageIcon.raycastTarget = false;

        GameObject typeTextObject = GetOrCreateRect("MessageTypeLabel", header.transform, false);
        RectTransform typeRect = headerRect(typeTextObject);
        typeRect.anchorMin = new Vector2(0f, 0f);
        typeRect.anchorMax = new Vector2(1f, 1f);
        typeRect.offsetMin = new Vector2(60f, 0f);
        typeRect.offsetMax = Vector2.zero;
        typeLabel = typeTextObject.GetComponent<TMP_Text>();
        if (typeLabel == null)
            typeLabel = typeTextObject.AddComponent<TextMeshProUGUI>();
        CopyTypography(textElement, typeLabel);
        typeLabel.raycastTarget = false;
        typeLabel.enableAutoSizing = true;
        typeLabel.fontSizeMin = 18f;
        typeLabel.fontSizeMax = 26f;
        typeLabel.fontSize = 24f;
        typeLabel.fontStyle = FontStyles.Bold;
        typeLabel.alignment = TextAlignmentOptions.MidlineLeft;
        typeLabel.textWrappingMode = TextWrappingModes.NoWrap;

        separatorImage = GetOrCreateImage("MessageHeaderSeparator", transform, false);
        separatorImage.raycastTarget = false;
        RectTransform separatorRect = separatorImage.rectTransform;
        separatorRect.anchorMin = new Vector2(0f, 1f);
        separatorRect.anchorMax = new Vector2(1f, 1f);
        separatorRect.pivot = new Vector2(0.5f, 1f);
        separatorRect.anchoredPosition = new Vector2(0f, -72f);
        separatorRect.sizeDelta = new Vector2(-44f, 2f);

        RectTransform textRect = textElement.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(28f, 20f);
        textRect.offsetMax = new Vector2(-28f, -86f);
        textElement.raycastTarget = false;
        textElement.enableAutoSizing = true;
        textElement.fontSizeMin = 18f;
        textElement.fontSizeMax = 30f;
        textElement.fontSize = 26f;
        textElement.fontStyle = FontStyles.Normal;
        textElement.alignment = TextAlignmentOptions.TopLeft;
        textElement.textWrappingMode = TextWrappingModes.Normal;
        textElement.margin = new Vector4(0f, 2f, 0f, 0f);
        textElement.transform.SetAsLastSibling();
    }

    private void ApplyMessageStyle(WorldSpaceMessageType type, string direction)
    {
        Color typeColor = GetTypeColor(type);
        if (panelImage != null)
            panelImage.color = Color.Lerp(PanelBase, typeColor, 0.095f);
        if (accentImage != null)
            accentImage.color = typeColor;
        if (badgeImage != null)
            badgeImage.color = new Color(typeColor.r, typeColor.g, typeColor.b, 0.22f);
        if (separatorImage != null)
            separatorImage.color = new Color(typeColor.r, typeColor.g, typeColor.b, 0.7f);
        if (typeLabel != null)
        {
            typeLabel.color = typeColor;
            typeLabel.text = GetTypeTitle(type);
        }
        if (messageIcon != null)
        {
            messageIcon.color = typeColor;
            messageIcon.SetMessageType(type, direction);
        }
        if (textElement != null)
            textElement.color = new Color(0.94f, 0.97f, 1f, 1f);
    }

    public static Color GetTypeColor(WorldSpaceMessageType type)
    {
        switch (type)
        {
            case WorldSpaceMessageType.PointArrow: return new Color32(40, 184, 212, 255);
            case WorldSpaceMessageType.TurnArrow: return new Color32(139, 124, 246, 255);
            case WorldSpaceMessageType.Alert: return new Color32(255, 89, 100, 255);
            case WorldSpaceMessageType.Warning: return new Color32(242, 181, 68, 255);
            default: return new Color32(74, 157, 255, 255);
        }
    }

    public static string GetTypeTitle(WorldSpaceMessageType type)
    {
        switch (type)
        {
            case WorldSpaceMessageType.PointArrow: return "POINT ARROW";
            case WorldSpaceMessageType.TurnArrow: return "TURN ARROW";
            case WorldSpaceMessageType.Alert: return "ALERT";
            case WorldSpaceMessageType.Warning: return "WARNING";
            default: return "INFO";
        }
    }

    public static bool TryParseMessageType(string value, out WorldSpaceMessageType type)
    {
        string normalized = (value ?? string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty).Trim();
        return System.Enum.TryParse(normalized, true, out type) &&
               System.Enum.IsDefined(typeof(WorldSpaceMessageType), type);
    }

    private static Image GetOrCreateImage(string objectName, Transform parent, bool firstSibling)
    {
        Transform existing = parent.Find(objectName);
        GameObject child = existing != null ? existing.gameObject : new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        if (existing == null)
        {
            child.layer = parent.gameObject.layer;
            child.transform.SetParent(parent, false);
        }
        if (firstSibling) child.transform.SetAsFirstSibling();
        Image image = child.GetComponent<Image>();
        if (image == null)
            image = child.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static GameObject GetOrCreateRect(string objectName, Transform parent, bool firstSibling)
    {
        Transform existing = parent.Find(objectName);
        GameObject child = existing != null ? existing.gameObject : new GameObject(objectName, typeof(RectTransform));
        if (existing == null)
        {
            child.layer = parent.gameObject.layer;
            child.transform.SetParent(parent, false);
        }
        if (firstSibling) child.transform.SetAsFirstSibling();
        return child;
    }

    private static RectTransform headerRect(GameObject value) => value.GetComponent<RectTransform>();

    private static void SetStretch(RectTransform rect, Vector2 minInset, Vector2 maxInset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = minInset;
        rect.offsetMax = maxInset;
    }

    private static void CopyTypography(TMP_Text source, TMP_Text destination)
    {
        destination.font = source.font;
        destination.fontSharedMaterial = source.fontSharedMaterial;
        destination.fontSize = source.fontSize;
        destination.extraPadding = source.extraPadding;
        destination.richText = false;
    }

    public void SetText(string text)
    {
        if (!EnsureValidSetup())
        {
            Debug.LogWarning("WorldSpaceUI: Text element is not assigned.", this);
            return;
        }
        textElement.text = text ?? string.Empty;
    }

    public static WorldSpaceUI Spawn(WorldSpaceUI prefab, Vector3 position, string text, Transform parent = null)
    {
        return Spawn(prefab, position, text, WorldSpaceMessageType.Info, "right", parent);
    }

    public static WorldSpaceUI Spawn(
        WorldSpaceUI prefab,
        Vector3 position,
        string text,
        WorldSpaceMessageType type,
        string direction = "right",
        Transform parent = null,
        Scene targetScene = default)
    {
        if (prefab == null)
        {
            Debug.LogError("WorldSpaceUI: Prefab is not assigned.");
            return null;
        }

        WorldSpaceUI instance = Instantiate(prefab, position, Quaternion.identity, parent);
        if (instance != null)
        {
            Vector3 s = instance.transform.localScale;
            if (s.x < 0.0019f && s.x > 0f)
            {
                instance.transform.localScale = s * 2f;
            }
        }
        if (parent == null && targetScene.IsValid() && targetScene.isLoaded && instance.gameObject.scene != targetScene)
            SceneManager.MoveGameObjectToScene(instance.gameObject, targetScene);
        if (!instance.EnsureValidSetup())
        {
            Destroy(instance.gameObject);
            return null;
        }
        instance.ShowMessage(type, text, direction);
        return instance;
    }
}
