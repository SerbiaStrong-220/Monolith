using Content.Server._Exodus.Mining.Pipes.Components;
using Content.Server.Administration.Logs;
using Content.Server.Stack;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Stacks;
using Content.Shared.Tools.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using CableCuttingFinishedEvent = Content.Shared.Tools.Systems.CableCuttingFinishedEvent;

namespace Content.Server._Exodus.Mining.Pipes;

public sealed partial class MiningPipeSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly ITileDefinitionManager _tileManager = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly SharedToolSystem _toolSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MiningPipeComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<MiningPipeComponent, CableCuttingFinishedEvent>(OnPipeCut);
        SubscribeLocalEvent<MiningPipeComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<MiningPipePlacerComponent, AfterInteractEvent>(OnPipePlacerAfterInteract);
    }

    private void OnInteractUsing(Entity<MiningPipeComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.CuttingQuality == null)
            return;

        args.Handled = _toolSystem.UseTool(
            args.Used,
            args.User,
            ent,
            ent.Comp.CuttingDelay,
            ent.Comp.CuttingQuality,
            new CableCuttingFinishedEvent());
    }

    private void OnPipeCut(Entity<MiningPipeComponent> ent, ref CableCuttingFinishedEvent args)
    {
        if (args.Cancelled || args.Handled || TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent))
            return;

        args.Handled = true;

        _adminLogger.Add(
            LogType.CableCut,
            LogImpact.Medium,
            $"The {ToPrettyString(ent)} at {Transform(ent).Coordinates} was cut by {ToPrettyString(args.User)}.");

        Spawn(ent.Comp.PipeDroppedOnCutPrototype, Transform(ent).Coordinates);
        QueueDel(ent);
    }

    private void OnAnchorChanged(Entity<MiningPipeComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored || TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent))
            return;

        Spawn(ent.Comp.PipeDroppedOnCutPrototype, Transform(ent).Coordinates);
        QueueDel(ent);
    }

    private void OnPipePlacerAfterInteract(Entity<MiningPipePlacerComponent> placer, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || placer.Comp.PipePrototypeId == null)
            return;

        var gridUid = _transform.GetGrid(args.ClickLocation);
        if (gridUid is not { } gridEnt || !TryComp<MapGridComponent>(gridEnt, out var grid))
            return;

        var snapPos = _map.TileIndicesFor((gridEnt, grid), args.ClickLocation);
        var tileDef = (ContentTileDefinition)_tileManager[_map.GetTileRef(gridEnt, grid, snapPos).Tile.TypeId];

        if (!tileDef.IsSubFloor || !tileDef.Sturdy)
            return;

        foreach (var anchored in _map.GetAnchoredEntities((gridEnt, grid), snapPos))
        {
            if (TryComp<MiningPipeComponent>(anchored, out var existing) &&
                existing.PipeType == placer.Comp.BlockingPipeType)
            {
                return;
            }
        }

        if (!TryComp<StackComponent>(placer, out var stack) || !_stack.Use(placer, 1, stack))
            return;

        var newPipe = Spawn(placer.Comp.PipePrototypeId, _map.GridTileToLocal(gridEnt, grid, snapPos));
        _adminLogger.Add(
            LogType.Construction,
            LogImpact.Low,
            $"{ToPrettyString(args.User):player} placed {ToPrettyString(newPipe):pipe} at {Transform(newPipe).Coordinates}");
        args.Handled = true;
    }
}
