using System.Numerics;
using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

public sealed partial class GridTerritorySystem
{
    /// <summary>Checks whether an entity is inside a territory using the requested profile.</summary>
    public bool IsInTerritory(EntityUid entity, ProtoId<TerritoryProfilePrototype> profile)
    {
        if (TerminatingOrDeleted(entity) || !TryComp<TransformComponent>(entity, out var source))
            return false;

        var position = _transform.GetWorldPosition(source);
        var query = EntityQueryEnumerator<GridTerritoryComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var territory, out var transform))
        {
            if (transform.MapID != source.MapID || territory.Radius <= 0f ||
                Vector2.DistanceSquared(position, _transform.GetWorldPosition(transform)) > territory.Radius * territory.Radius)
                continue;

            if (ResolveProfile((uid, territory))?.ID == profile.Id)
                return true;
        }

        return false;
    }
}
