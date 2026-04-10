namespace Content.Server._Lua.Physics;

[RegisterComponent]
public sealed partial class StuckedComponent : Component
{
    [DataField]
    public TimeSpan StuckedAt;
}
