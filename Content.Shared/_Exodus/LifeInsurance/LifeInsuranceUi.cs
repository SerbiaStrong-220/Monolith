using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.LifeInsurance;

[Serializable, NetSerializable]
public enum LifeInsuranceConsoleUiKey : byte
{
    Key
}

/// <summary>
/// One recorded person shown in the console list.
/// </summary>
[Serializable, NetSerializable]
public struct LifeInsuranceRecordEntry
{
    public Guid UserId;
    public string Name;
    public int Insurances;
}

/// <summary>
/// Power/connection status of one linked machine (scanner or cloner).
/// </summary>
[Serializable, NetSerializable]
public struct LifeInsuranceMachineStatus
{
    public bool Connected;
    public bool Unpowered;
    public bool OnGridPower;
    /// <summary>Internal battery charge percent (0..1), shown when not on grid power.</summary>
    public float BatteryPercent;
}

[Serializable, NetSerializable]
public sealed class LifeInsuranceConsoleState : BoundUserInterfaceState
{
    public readonly List<LifeInsuranceRecordEntry> Records;
    public readonly int MaxInsurances;
    public readonly string? ScannerOccupantName;
    public readonly LifeInsuranceMachineStatus Scanner;
    public readonly LifeInsuranceMachineStatus Cloner;
    public readonly int Price;

    public LifeInsuranceConsoleState(
        List<LifeInsuranceRecordEntry> records,
        int maxInsurances,
        string? scannerOccupantName,
        LifeInsuranceMachineStatus scanner,
        LifeInsuranceMachineStatus cloner,
        int price)
    {
        Records = records;
        MaxInsurances = maxInsurances;
        ScannerOccupantName = scannerOccupantName;
        Scanner = scanner;
        Cloner = cloner;
        Price = price;
    }
}

/// <summary>
/// Records the DNA of whoever currently occupies the linked scanner capsule.
/// </summary>
[Serializable, NetSerializable]
public sealed class LifeInsuranceRecordDnaMessage : BoundUserInterfaceMessage
{
}

/// <summary>
/// Buys one insurance charge for the given recorded person, charging the operator's bank account.
/// </summary>
[Serializable, NetSerializable]
public sealed class LifeInsuranceBuyMessage : BoundUserInterfaceMessage
{
    public readonly Guid UserId;

    public LifeInsuranceBuyMessage(Guid userId)
    {
        UserId = userId;
    }
}

/// <summary>
/// Removes a recorded person from the registry.
/// </summary>
[Serializable, NetSerializable]
public sealed class LifeInsuranceDeleteMessage : BoundUserInterfaceMessage
{
    public readonly Guid UserId;

    public LifeInsuranceDeleteMessage(Guid userId)
    {
        UserId = userId;
    }
}
