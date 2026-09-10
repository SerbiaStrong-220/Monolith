// Exodus-begin relative POI placement integration
using System.Numerics;
using Content.Server._Exodus.Worldgen;
using Content.Server._NF.Trade;
using Content.Shared._NF.CCVar;
using Robust.Shared.Map;

namespace Content.Server._NF.GameRule;

public sealed partial class PointOfInterestSystem
{
    [Dependency] private readonly RelativePoiSpawnSystem _relativePoi = default!;
    [Dependency] private readonly SharedTransformSystem _relativeTransform = default!;

    private void InitializeRelativePlacement()
    {
        SubscribeLocalEvent<RelativePoiSpawnEvent>(OnSpawnRelativePoi);
        SubscribeLocalEvent<RelativePoiPositionAttemptEvent>(OnRelativePositionAttempt);
    }

    private bool QueueRelativePoi(MapId map, PointOfInterestPrototype poi, List<EntityUid> output,
        string? overrideName = null, int? depotIndex = null)
    {
        return _relativePoi.QueueIfRelative(map, new(false, poi.ID), output, overrideName, depotIndex);
    }

    public void BeginRelativeGeneration(MapId map) => _relativePoi.Begin(map);

    public void ProcessRelativePois(MapId map, bool final = false) => _relativePoi.ProcessPending(map, final);

    private void OnRelativePositionAttempt(ref RelativePoiPositionAttemptEvent args)
    {
        if (args.Map != _ticker.DefaultMap)
            return;

        var separation = MathF.Max(0, _cfg.GetCVar(NFCCVars.MinPOIDistance)) *
                         MathF.Max(0.1f, _cfg.GetCVar(NFCCVars.POIDistanceModifier));
        args.Cancelled |= !IsPlacementValid(args.Position, args.Clearance, separation, args.AnchorOrigin);
    }

    private void OnSpawnRelativePoi(ref RelativePoiSpawnEvent args)
    {
        if (args.Rule.Poi is not { } id || !_proto.TryIndex(id, out var poi))
            return;

        if (!TrySpawnPoiGrid(args.Map, poi, Vector2.Zero, out var grid, args.Request.OverrideName, args.Rule) || grid is not { } uid)
            return;

        args.Request.Output?.Add(uid);
        if (args.Map == _ticker.DefaultMap)
            AddStationPlacement(_relativeTransform.GetWorldPosition(uid), poi);
        if (args.Request.DepotIndex is { } index &&
            TryComp<TradeCrateDestinationComponent>(_station.GetOwningStation(uid), out var destination))
        {
            destination.DestinationProto = index < 26 ? $"Cargo{(char) ('A' + index)}" : "CargoOther";
        }
    }
}
// Exodus-end
