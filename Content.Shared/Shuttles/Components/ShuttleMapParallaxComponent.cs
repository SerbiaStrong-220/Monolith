using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared.Shuttles.Components;

/// <summary>
/// Shows a parallax background on the shuttle map console.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShuttleMapParallaxComponent : Component
{
    // # Exodus start - custom BSS map background
    // The background sprite for the BSS jump map (ShuttleMapControl / FTL selection tab)
    // is now the custom BSSMapBackground.png provided for this project.
    // This is the single place that controls the default tiled background on the BSS map.
    public static readonly ResPath FallbackTexture = new ResPath("/Textures/Parallaxes/BSSMapBackground.png");
    // # Exodus end - custom BSS map background

    // TODO: This should ideally be shared with parallax stuff to avoid duplication, for now it's just a texture
    [DataField, AutoNetworkedField]
    public ResPath TexturePath;
}
