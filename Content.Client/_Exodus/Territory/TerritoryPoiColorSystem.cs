using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Territory;

public sealed partial class TerritoryPoiColorSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;

    private EntityQuery<GridTerritoryComponent> _territoryQuery;

    public override void Initialize()
    {
        base.Initialize();
        _territoryQuery = GetEntityQuery<GridTerritoryComponent>();
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
