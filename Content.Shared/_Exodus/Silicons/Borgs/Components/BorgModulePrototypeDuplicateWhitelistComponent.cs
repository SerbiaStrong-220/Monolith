using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Silicons.Borgs.Components;

[RegisterComponent]
public sealed partial class BorgModulePrototypeDuplicateWhitelistComponent : Component
{
    [DataField(required: true)]
    public List<EntProtoId> ModulePrototypes = new();
}
