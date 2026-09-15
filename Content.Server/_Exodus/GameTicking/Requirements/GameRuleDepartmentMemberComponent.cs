using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.GameTicking.Requirements;

/// <summary>
/// Adds a player-controlled entity to department population checks without assigning it a job.
/// </summary>
[RegisterComponent]
public sealed partial class GameRuleDepartmentMemberComponent : Component
{
    /// <summary>
    /// Additional departments this entity counts towards when checking game rule requirements.
    /// </summary>
    [DataField(required: true)]
    public HashSet<ProtoId<DepartmentPrototype>> Departments = new();
}
