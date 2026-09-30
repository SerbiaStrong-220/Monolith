using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Access;

/// <summary>
/// Grants every registered access level on map initialization and bypasses company access checks.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class UniversalAccessComponent : Component;
