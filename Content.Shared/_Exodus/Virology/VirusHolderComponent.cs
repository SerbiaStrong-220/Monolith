// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

namespace Content.Shared._Exodus.Virology;

[RegisterComponent]
public sealed partial class VirusHolderComponent : Component
{
    [ViewVariables]
    public HashSet<EntityUid> Viruses = [];

    // Keep the exact instances we own so curing a virus cannot remove an innate or externally replaced component.
    public Dictionary<string, GrantedVirusComponent> GrantedComponents = [];

    /// <summary>Populated only during map serialization; live strains stay in nullspace.</summary>
    [DataField]
    public List<VirusSavedState> SavedViruses = [];

    [DataField]
    public HashSet<string> SavedGrantedComponents = [];
}

public sealed record GrantedVirusComponent(IComponent Instance, IComponent Template);
