namespace Content.Client._Exodus.Virology.Intelligent;

[RegisterComponent]
public sealed partial class RotOrganAnimationComponent : Component
{
    public string DesiredState = "dormant";
    public bool Complete;
}
