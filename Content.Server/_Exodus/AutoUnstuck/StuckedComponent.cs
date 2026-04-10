namespace Content.Server._Exodus.AutoUnstuck;

[RegisterComponent]
public sealed partial class StuckedComponent : Component
{
    [DataField]
    public TimeSpan StuckedAt;
}
