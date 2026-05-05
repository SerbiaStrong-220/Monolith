namespace Content.Server.Crucible;

[RegisterComponent]
public sealed partial class CrucibleConsoleComponent : Component
{
    [DataField]
    public EntityUid? LinkedCrucible;
}

