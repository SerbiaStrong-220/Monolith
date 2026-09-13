namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Combat history belongs to the body and survives dropping its weapon or revival.</summary>
[RegisterComponent]
public sealed partial class RotCombatHistoryComponent : Component
{
    [DataField]
    public bool HasAttacked;

    [DataField]
    public EntityUid? LastAttacker;
}
