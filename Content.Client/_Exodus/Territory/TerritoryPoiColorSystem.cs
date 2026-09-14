using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Territory;

public sealed partial class TerritoryPoiColorSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;

    private EntityQuery<GridTerritoryComponent> _territoryQuery;
    private EntityQuery<TerritoryCaptureComponent> _captureQuery;

    public override void Initialize()
    {
        base.Initialize();
        _territoryQuery = GetEntityQuery<GridTerritoryComponent>();
        _captureQuery = GetEntityQuery<TerritoryCaptureComponent>();
    }

    public bool TryGetColor(EntityUid grid, out Color color)
    {
        color = default;

        if (!_territoryQuery.TryGetComponent(grid, out var territory) ||
            territory.Radius <= 0f ||
            !territory.ColorPoiByFaction)
        {
            return false;
        }

        if (_captureQuery.TryGetComponent(grid, out var capture) && capture.Faction != null)
        {
            color = capture.Color;
            return true;
        }

        if (territory.ControllingFaction is { } factionId &&
            _prototype.TryIndex(factionId, out var faction))
        {
            color = faction.Color;
            return true;
        }

        color = territory.NeutralPoiColor;
        return true;
    }
}
