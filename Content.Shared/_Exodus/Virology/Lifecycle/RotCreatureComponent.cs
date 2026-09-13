using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Virology.Lifecycle;

/// <summary>A native rot creature, protected by its vines and recognized by the colony.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RotCreatureComponent : Component;
