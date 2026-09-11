// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

namespace Content.Shared._Exodus.Virology;

[RegisterComponent]
public sealed partial class VirusProtectionComponent : Component
{
    /// <summary>Vectors this gear protects against.</summary>
    [DataField]
    public VirusTransmissionVector Vectors;

    /// <summary>Contribution (0..1) to protection against a vector. Combined equipment protection is capped below immunity.</summary>
    [DataField]
    public float BlockChance = 1f;
}
