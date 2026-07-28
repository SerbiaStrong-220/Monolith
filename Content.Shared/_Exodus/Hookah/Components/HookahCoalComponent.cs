namespace Content.Shared._Exodus.Hookah.Components;

[RegisterComponent]
public sealed partial class HookahCoalComponent : Component
{
    [DataField]
    public float FuelLeft = 120f;

    [DataField]
    public float FuelDrainIdle = 0.5f;
}

