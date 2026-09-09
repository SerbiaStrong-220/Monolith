using System.Numerics;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleMapControl
{
    private void DrawStarSystem(DrawingHandleScreen handle, Matrix3x2 matty)
    {
        if (!EntManager.TryGetComponent<TransformComponent>(_shuttleEntity, out var shuttleTransform) ||
            shuttleTransform.MapUid == null ||
            !EntManager.TryGetComponent<StarSystemMapComponent>(shuttleTransform.MapUid.Value, out var starSystem) ||
            starSystem.StarSystem == null)
            return;
        
        // Exodus: omit the stellar disc on navigation displays so it does not cover central stations.

        foreach (var planet in starSystem.StarSystem.Planets)
        {
            var planetPos = Vector2.Transform(planet.Position, matty);
            planetPos = planetPos with { Y = -planetPos.Y };
            planetPos = ScalePosition(planetPos);
            var planetRadius = Planet.MAP_PIXEL_SIZE * planet.Radius * MinimapScale;
            handle.DrawCircle(planetPos, planetRadius, Color.Gray);
        }
    }
}
