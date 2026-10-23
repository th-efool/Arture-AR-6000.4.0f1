using System;
using UnityEngine;

[Serializable]
public class MachineComponent
{
    [SerializeField, HideInInspector]
    private int componentValue;

    public GameObject GameObjectRef;

    [TextArea(2, 5)]
    public string ComponentDescription;

    public int ComponentValue => componentValue;

    public MachineComponent(int componentValue)
    {
        this.componentValue = componentValue;
        GameObjectRef = null;
        ComponentDescription = string.Empty;
    }
}