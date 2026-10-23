
using System;
using System.ComponentModel;
using System.Reflection;

public enum MachineType
{
    CentrifugalWaterPump,
    CNC
}

public static class MachineDefinitions
{
    public enum CentrifugalWaterPump
    {
        [Description("Converts electrical energy into rotational mechanical power to drive the pump.")]
        ElectricMotor,

        [Description("Houses the electrical terminals used to connect the motor power supply.")]
        MotorTerminalBox,

        [Description("Protects the motor cooling fan while allowing airflow.")]
        MotorCoolingFanCover,

        [Description("Transfers rotational torque from the motor to the pump shaft.")]
        ShaftCoupling,

        [Description("Covers the rotating coupling to protect against accidental contact.")]
        CouplingGuard,

        [Description("Encloses the impeller chamber and directs water toward the discharge.")]
        PumpCasing,

        [Description("Rotates to transfer energy to the water, increasing its velocity and pressure.")]
        Impeller,

        [Description("Transfers torque from the coupling to the impeller.")]
        PumpShaft,

        [Description("Supports the shaft bearings and helps maintain shaft alignment.")]
        BearingHousing,

        [Description("Connects the pump inlet to the incoming water pipe.")]
        SuctionFlange,

        [Description("Connects the pump outlet to the outgoing water pipe.")]
        DischargeFlange,

        [Description("Provides a common mounting platform for the motor and pump.")]
        Baseplate,

        [Description("Supports and secures the motor or pump assembly to the baseplate.")]
        MountingFoot,

        [Description("Fastens machine components and mounting supports to the baseplate.")]
        MountingBolt
    }

    public enum CNC
    {
        [Description("Rotates the cutting tool during machining operations.")]
        Spindle,

        [Description("Holds and secures the workpiece or tooling, depending on machine configuration.")]
        Chuck,

        [Description("Circulates coolant to assist cutting and heat removal.")]
        CoolantPump,

        [Description("Holds or indexes cutting tools for machining operations.")]
        ToolTurret
    }

    public static Type GetComponentEnumType(MachineType machineType)
    {
        return machineType switch
        {
            MachineType.CentrifugalWaterPump =>
                typeof(CentrifugalWaterPump),

            MachineType.CNC =>
                typeof(CNC),

            _ => throw new ArgumentOutOfRangeException(
                nameof(machineType), machineType, null)
        };
    }

    public static Array GetComponentValues<TComponent>()
        where TComponent : Enum
    {
        return Enum.GetValues(typeof(TComponent));
    }

    public static Array GetComponentValues(MachineType machineType)
    {
        return Enum.GetValues(GetComponentEnumType(machineType));
    }

    public static string GetComponentDescription(Enum component)
    {
        FieldInfo field = component.GetType().GetField(
            component.ToString());

        DescriptionAttribute attribute =
            Attribute.GetCustomAttribute(
                field, typeof(DescriptionAttribute))
            as DescriptionAttribute;

        return attribute?.Description ?? string.Empty;
    }
}
