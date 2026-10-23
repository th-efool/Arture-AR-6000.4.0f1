
using TMPro;
using UnityEngine;

public class WorldSpaceUI : MonoBehaviour
{
    [SerializeField] private TMP_Text textElement;

    public string Text
    {
        get => textElement != null ? textElement.text : string.Empty;
        set => SetText(value);
    }

    public void SetText(string text)
    {
        if (textElement == null)
        {
            Debug.LogWarning("WorldSpaceUI: Text element is not assigned.", this);
            return;
        }

        textElement.text = text;
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

        instance.SetText(text);

        return instance;
    }
}
