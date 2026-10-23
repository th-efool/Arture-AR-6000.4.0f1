using UnityEngine;

public class MachineProfile : MonoBehaviour
{
    [Header("Machine Identity")]
    public string MachineID;
    public string Name;

    [Header("Machine Type")]
    public MachineType MachineType;

    [Header("Machine Components")]
    public MachineComponent[] Components;
}