using Content.Shared.Roles;

namespace Content.Shared._Exodus.Roles;

/// <summary>
/// Exodus: shared mind-role marker for nuke operatives so shared systems
/// such as biocode and whitelists can validate the role on both client and server.
/// </summary>
[RegisterComponent]
public sealed partial class NukeopsRoleComponent : BaseMindRoleComponent
{
}
