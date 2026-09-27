// Exodus-begin
namespace Content.Shared.Atmos.Prototypes;

public sealed partial class GasPrototype
{
    /// <summary>
    /// Minimum moles of this gas and of any other gas needed to sustain a contact fire.
    /// Null keeps the normal oxygen-dependent combustion rules.
    /// </summary>
    [DataField]
    public float? ContactFireMoles;
}
// Exodus-end
