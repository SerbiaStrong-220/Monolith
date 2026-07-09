namespace Content.Shared._Exodus.Nebula.Components;

/// <summary>
/// Marker on a nebula prototype that suppresses worldgen debris (asteroids and similar)
/// inside this nebula's volume. Picked up by NebulaDebrisExclusionSystem, which intercepts
/// <c>PrePlaceDebrisFeatureEvent</c> before the debris placer spawns anything.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaBlocksDebrisComponent : Component
{
}
