namespace Content.Server.Crucible;

[RegisterComponent]
public sealed partial class CrucibleComponent : Component
{
    public bool IsCooking = false;
    public bool UiDirty = true;
    public EntityUid? TargetItem;
    public float CookingTimer;
    public float TargetTime;
    public string? ResultPrototype;

}
