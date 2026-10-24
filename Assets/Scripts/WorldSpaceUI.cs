using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WorldSpaceUI : MonoBehaviour
{
    [SerializeField] private TMP_Text textElement;

    public TMP_Text TextElement => textElement;

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
        if (textElement == null)
        {
            textElement = GetComponentInChildren<TMP_Text>(true);
        }

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            if (canvas.renderMode != RenderMode.WorldSpace)
                canvas.renderMode = RenderMode.WorldSpace;

            canvas.overrideSorting = true;
            if (canvas.sortingOrder < 100)
                canvas.sortingOrder = 100;
        }

        EnsureBackgroundPanel();

        return textElement != null;
    }

    private void EnsureBackgroundPanel()
    {
        if (textElement == null)
            return;

        Transform existingBg = transform.Find("LabelBackground");
        if (existingBg == null)
        {
            var bgGo = new GameObject("LabelBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgGo.layer = gameObject.layer;
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.SetParent(transform, false);
            bgRect.SetAsFirstSibling();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            var img = bgGo.GetComponent<Image>();
            img.color = new Color(0.06f, 0.08f, 0.12f, 0.88f);
            img.raycastTarget = false;
        }

        textElement.raycastTarget = false;
        textElement.enableAutoSizing = true;
        if (textElement.fontSizeMin < 14f) textElement.fontSizeMin = 16f;
        if (textElement.fontSizeMax > 48f) textElement.fontSizeMax = 42f;
    }

    public void SetText(string text)
    {
        if (textElement == null)
        {
            textElement = GetComponentInChildren<TMP_Text>(true);
            if (textElement == null)
            {
                Debug.LogWarning("WorldSpaceUI: Text element is not assigned.", this);
                return;
            }
        }

        EnsureBackgroundPanel();
        textElement.text = text ?? string.Empty;
    }

    public static WorldSpaceUI Spawn(
        WorldSpaceUI prefab,
        Vector3 position,
        string text,
        Transform parent = null)
    {
        if (prefab == null)
        {
            Debug.LogError("WorldSpaceUI: Prefab is not assigned.");
            return null;
        }

        WorldSpaceUI instance = Instantiate(
            prefab, position, Quaternion.identity, parent);

        instance.EnsureValidSetup();
        instance.SetText(text);

        return instance;
    }
}
