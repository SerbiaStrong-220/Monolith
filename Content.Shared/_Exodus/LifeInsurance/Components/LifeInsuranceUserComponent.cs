namespace Content.Shared._Exodus.LifeInsurance.Components;

/// <summary>
/// Added to a ghost that has been granted the life insurance activation action.
/// Tracks the action entity so its charges can be kept in sync with the registry.
/// </summary>
[RegisterComponent]
public sealed partial class LifeInsuranceUserComponent : Component
{
    [ViewVariables]
    public EntityUid? ActionId;
}
