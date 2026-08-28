using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.ShipShields;

/// <summary>
/// Configures the emergency reserve cartridges installed in a CDM Bastion shield generator.
/// </summary>
[RegisterComponent]
public sealed partial class CdmShieldReserveComponent : Component
{
    public const int DefaultMaxCartridges = 4;
    public const string SlotPrefix = "cdm-shield-reserve-";

    [DataField]
    public int MaxCartridges = DefaultMaxCartridges;

    /// <summary>
    /// The fraction of shield capacity restored when a cartridge averts an overload.
    /// </summary>
    [DataField]
    public float EmergencyShieldFraction = 0.25f;

    public static string GetSlotId(int index)
    {
        return $"{SlotPrefix}{index}";
    }
}

/// <summary>
/// Marker for a cartridge compatible with the CDM Bastion's emergency reserve slots.
/// </summary>
[RegisterComponent]
public sealed partial class CdmShieldReserveCartridgeComponent : Component
{
}

[Serializable, NetSerializable]
public enum CdmShieldReserveVisuals : byte
{
    CartridgeCount,
}

[Serializable, NetSerializable]
public enum CdmShieldReserveVisualLayers : byte
{
    Cartridges,
}
