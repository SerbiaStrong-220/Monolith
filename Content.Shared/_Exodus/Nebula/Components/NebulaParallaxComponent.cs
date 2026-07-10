namespace Content.Shared._Exodus.Nebula.Components;

/// <summary>
/// Parallax prototype the client should display while inside this nebula. Uses plain string
/// because the parallax prototype lives in client-only code; the client resolves it to its
/// strong type when consuming this value.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaParallaxComponent : Component
{
    [DataField(required: true)]
    public string Parallax = string.Empty;
}
