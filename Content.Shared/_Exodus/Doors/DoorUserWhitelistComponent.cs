using Content.Shared.Whitelist;

namespace Content.Shared._Exodus.Doors;

/// <summary>Restricts who may open a door, independently of access cards and power.</summary>
[RegisterComponent]
public sealed partial class DoorUserWhitelistComponent : Component
{
    [DataField(required: true)]
    public EntityWhitelist Whitelist = new();

    [DataField]
    public bool AllowWithoutUser;
}
