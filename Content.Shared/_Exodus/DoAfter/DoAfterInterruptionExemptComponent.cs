using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.DoAfter;

[Flags]
public enum DoAfterInterruptionExemptions : byte
{
    None = 0,
    Movement = 1 << 0,
    HandChange = 1 << 1,
    DropItem = 1 << 2,
}

/// <summary>
/// Configures which do-after interruption conditions an entity ignores.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DoAfterInterruptionExemptComponent : Component
{
    [DataField]
    public DoAfterInterruptionExemptions Exemptions = DoAfterInterruptionExemptions.Movement;
}