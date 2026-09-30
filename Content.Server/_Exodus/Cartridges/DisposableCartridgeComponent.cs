using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Cartridges;

/// <summary>A single-use cartridge that is replaced by a spent casing when consumed.</summary>
[RegisterComponent]
public sealed partial class DisposableCartridgeComponent : Component
{
    /// <summary>The spent casing left in the device after the cartridge is consumed.</summary>
    [DataField(required: true)]
    public EntProtoId SpentPrototype;
}
