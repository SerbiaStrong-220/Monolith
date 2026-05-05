using Content.Server.Storage.Components;
using Content.Shared.Crucible;
using Content.Shared.Storage;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server.Crucible;

public sealed class CrucibleSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float UpdateInterval = 0.5f;
    private float _updateTimer;
    private const float ConnectRange = 2.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrucibleConsoleComponent, CrucibleStartCookMessage>(OnStartCook);
        SubscribeLocalEvent<CrucibleConsoleComponent, MapInitEvent>(OnConsoleInit);
        SubscribeLocalEvent<CrucibleComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<CrucibleComponent, EntRemovedFromContainerMessage>(OnContainerModified);
    }

    private void OnConsoleInit(EntityUid uid, CrucibleConsoleComponent comp, MapInitEvent args)
    {
        var xform = Transform(uid);

        foreach (var ent in _lookup.GetEntitiesInRange(xform.Coordinates, ConnectRange))
        {
            if (!HasComp<CrucibleComponent>(ent))
                continue;

            comp.LinkedCrucible = ent;
            break;
        }
    }
    private void OnStartCook(EntityUid uid, CrucibleConsoleComponent console, CrucibleStartCookMessage args)
    {
        if (console.LinkedCrucible is not { } crucibleUid)
            return;

        if (!TryComp(crucibleUid, out CrucibleComponent? crucible) ||
            !TryComp(crucibleUid, out EntityStorageComponent? storage))
            return;

        if (crucible.IsCooking || storage.Open)
            return;

        var contents = storage.Contents.ContainedEntities;
        if (contents.Count != 1)
            return;

        var item = contents[0];

        if (!TryComp<CrucibleRecipeComponent>(item, out var recipe))
            return;

        crucible.IsCooking = true;
        crucible.CookingTimer = 0f;
        crucible.TargetTime = recipe.ProcessingTime;
        crucible.TargetItem = item;
        crucible.ResultPrototype = recipe.ResultEntity;

        crucible.UiDirty = true;
    }

    private void OnContainerModified(EntityUid uid, CrucibleComponent crucible, ContainerModifiedMessage args)
    {
        if (crucible.IsCooking)
            CancelCooking(uid, crucible);

        crucible.UiDirty = true;
    }

    private void CancelCooking(EntityUid uid, CrucibleComponent crucible)
    {
        crucible.IsCooking = false;
        crucible.TargetItem = null;
        crucible.CookingTimer = 0f;
        crucible.TargetTime = 0f;
        crucible.ResultPrototype = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateTimer += frameTime;
        var refresh = _updateTimer >= UpdateInterval;

        var query = EntityQueryEnumerator<CrucibleComponent, EntityStorageComponent>();

        while (query.MoveNext(out var uid, out var crucible, out var storage))
        {
            if (crucible.IsCooking)
            {
                crucible.CookingTimer = MathF.Min(
                    crucible.CookingTimer + frameTime,
                    crucible.TargetTime);

                if (crucible.CookingTimer >= crucible.TargetTime)
                {
                    FinishCooking(uid, crucible, storage);
                }

                crucible.UiDirty = true;
            }

            if (refresh && crucible.UiDirty)
            {
                UpdateUI(uid, crucible, storage);
                crucible.UiDirty = false;
            }
        }

        if (refresh)
            _updateTimer = 0f;
    }

    private void FinishCooking(EntityUid uid, CrucibleComponent crucible, EntityStorageComponent storage)
    {
        crucible.IsCooking = false;

        if (crucible.TargetItem is { } item && Exists(item))
            QueueDel(item);

        if (crucible.ResultPrototype != null && !storage.Open)
        {
            var result = Spawn(crucible.ResultPrototype, Transform(uid).Coordinates);
            _container.Insert(result, storage.Contents);
        }

        crucible.TargetItem = null;
        crucible.CookingTimer = 0f;
        crucible.TargetTime = 0f;
        crucible.ResultPrototype = null;

        crucible.UiDirty = true;
    }

    private void UpdateUI(EntityUid crucibleUid, CrucibleComponent crucible, EntityStorageComponent storage)
    {
        var state = BuildState(crucibleUid, crucible, storage);

        var consoles = EntityQueryEnumerator<CrucibleConsoleComponent>();
        while (consoles.MoveNext(out var consoleUid, out var console))
        {
            if (console.LinkedCrucible != crucibleUid)
                continue;

            _ui.SetUiState(consoleUid, CrucibleConsoleUiKey.Key, state);
        }
    }

    private CrucibleConsoleState BuildState(EntityUid uid, CrucibleComponent crucible, EntityStorageComponent storage)
    {
        var contents = storage.Contents.ContainedEntities;

        var hasItem = contents.Count > 0;
        var itemName = hasItem ? MetaData(contents[0]).EntityName : string.Empty;

        var canCook = hasItem &&
                      !storage.Open &&
                      !crucible.IsCooking &&
                      HasComp<CrucibleRecipeComponent>(contents[0]);

        var remaining = (int)MathF.Max(0, crucible.TargetTime - crucible.CookingTimer);
        var progress = crucible.TargetTime > 0
            ? crucible.CookingTimer / crucible.TargetTime
            : 0f;

        return new CrucibleConsoleState(
            itemName,
            hasItem,
            canCook,
            crucible.IsCooking,
            progress,
            remaining);
    }
}
