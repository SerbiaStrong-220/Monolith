using System.IO;
using System.Threading.Tasks;
using Content.Server._Exodus.SafetyDepositBox;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.Database._Exodus.SafetyDepositBox;
using Content.Shared._Exodus.SafetyDepositBox;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Item;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SafetyDepositBox;

public sealed partial class SafetyDepositBoxSystem
{
    [Dependency] private IAdminManager _adminManager = default!;

    /// <summary>
    /// Resolves physical boxes only on demand. Ambiguous identities and staged nullspace copies are excluded.
    /// </summary>
    public Dictionary<Guid, EntityUid> GetAdminPhysicalBoxes()
    {
        var result = new Dictionary<Guid, EntityUid>();
        var duplicates = new HashSet<Guid>();
        var query = AllEntityQuery<SafetyDepositBoxComponent, StorageComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var box, out _, out var transform))
        {
            if (box.BoxId is not { } id || TerminatingOrDeleted(uid) ||
                transform.MapID == MapId.Nullspace || duplicates.Contains(id))
                continue;

            if (!result.TryAdd(id, uid))
            {
                result.Remove(id);
                duplicates.Add(id);
            }
        }

        return result;
    }

    public bool IsAdminBoxBusy(Guid boxId) => _activeBoxOperations.Contains(boxId);

    public List<AdminSafetyDepositItem> GetAdminBoxItems(WayfarerSafetyDepositBox box, EntityUid? physicalBox)
    {
        var result = new List<AdminSafetyDepositItem>();
        foreach (var item in box.Items)
        {
            var name = Loc.GetString("admin-safety-deposit-item-unknown");
            var protoId = string.Empty;
            var count = 1;
            if (SafetyDepositItemMetadata.TryRead(item.EntityData, out protoId, out var savedName, out var savedCount))
            {
                if (_prototypeManager.TryIndex<EntityPrototype>(protoId, out var prototype))
                {
                    name = prototype.Name;
                    if (prototype.TryGetComponent<StackComponent>(out var stack, _componentFactory))
                        count = stack.Count;
                }

                name = savedName ?? name;
                count = savedCount ?? count;
            }

            result.Add(new AdminSafetyDepositItem(item.Id, null, name, protoId, count));
        }

        if (box.LastWithdrawn == null || physicalBox is not { } physical || !IsAdminPhysicalBoxValid(physical, box) ||
            !TryComp<StorageComponent>(physical, out var storage))
            return result;

        foreach (var item in storage.Container.ContainedEntities)
        {
            if (TerminatingOrDeleted(item))
                continue;

            result.Add(new AdminSafetyDepositItem(0, GetNetEntity(item), Name(item),
                Prototype(item)?.ID ?? string.Empty, TryComp<StackComponent>(item, out var stack) ? stack.Count : 1));
        }

        return result;
    }

    /// <summary>
    /// Administrative changes share the player's persistence lock. The callback also invalidates closed EUIs.
    /// Returns a localization ID, never a client-provided message.
    /// </summary>
    public async Task<string> AdminModifyBoxAsync(
        ICommonSession admin,
        Guid ownerId,
        AdminSafetyDepositModifyMessage request,
        Func<bool> canContinue)
    {
        if (!CanAdminModify(admin, request.Action, canContinue))
            return "admin-safety-deposit-error-permission";

        if (!Enum.IsDefined(request.Action) || request.Reason is null or { Length: > 500 } ||
            request.PrototypeId is null or { Length: > 200 } ||
            request.RecordId < 0 || (request.RecordId > 0 && request.Entity != null))
            return "admin-safety-deposit-error-invalid";

        AdminHandItem? handItem = null;
        if (request.Action == AdminSafetyDepositAction.AddFromHand)
        {
            if (request.RecordId != 0 || request.Entity != null)
                return "admin-safety-deposit-error-invalid";

            if (!TryGetAdminHandItem(admin, out var held))
                return "admin-safety-deposit-error-hand";

            handItem = held;
        }

        if (!_activeBoxOperations.Add(request.BoxId))
            return "admin-safety-deposit-error-busy";

        var staging = new AdminBoxStaging();
        _adminBoxOperations.Add(request.BoxId, staging);
        var roundId = _gameTicker.RoundId;
        bool CanContinue() => IsAdminOperationCurrent(request.BoxId, staging) &&
                              roundId == _gameTicker.RoundId && CanAdminModify(admin, request.Action, canContinue);
        try
        {
            if (handItem is { } requestedSource)
            {
                _adminLogger.Add(LogType.Action, LogImpact.High,
                    $"{admin:actor} requested safety deposit hand transfer of {DescribeAdminItem(requestedSource.Item)}, owner {new NetUserId(ownerId):subject}, box {request.BoxId}");
            }

            var box = await _dbManager.GetSafetyDepositBox(request.BoxId);
            if (!CanContinue())
                return "admin-safety-deposit-error-permission";

            if (box == null || box.OwnerUserId != ownerId)
                return "admin-safety-deposit-error-box";

            if (handItem is { } source && !IsAdminHandItemValid(admin, source))
                return "admin-safety-deposit-error-hand";

            if (request.Action == AdminSafetyDepositAction.Withdraw && !TryGetAdminRecipient(admin, out _))
                return "admin-safety-deposit-error-recipient";

            var physicalBoxes = GetAdminPhysicalBoxes();
            physicalBoxes.TryGetValue(box.BoxId, out var physical);
            if (box.LastWithdrawn == null && physical.Valid)
                return "admin-safety-deposit-error-unavailable";

            if (box.LastWithdrawn == null && handItem is { } storedSource)
            {
                return await AdminStoreHandItemAsync(admin, box, request, storedSource, staging,
                    CanContinue);
            }

            if (request.RecordId > 0 || (request.Action == AdminSafetyDepositAction.Add && box.LastWithdrawn == null))
            {
                return await AdminModifyStoredItemAsync(admin, box, request, staging,
                    CanContinue);
            }

            if (box.LastWithdrawn == null || !physical.Valid || !IsAdminPhysicalBoxValid(physical, box) ||
                box.LastWithdrawnRoundId != roundId)
                return "admin-safety-deposit-error-unavailable";

            return await AdminModifyPhysicalItemAsync(admin, box, physical, request, handItem, staging,
                CanContinue);
        }
        catch (Exception ex)
        {
            Log.Error($"Admin safety deposit operation for {request.BoxId} by {admin.UserId} failed: {ex}");
            _adminLogger.Add(LogType.Action, LogImpact.High,
                $"{admin:actor} failed safety deposit action {request.Action} for owner {new NetUserId(ownerId):subject}, box {request.BoxId}");
            return "admin-safety-deposit-error-database";
        }
        finally
        {
            FinishAdminBoxOperation(request.BoxId, staging);
        }
    }

    private bool CanAdminModify(ICommonSession admin, AdminSafetyDepositAction action, Func<bool> canContinue)
    {
        var requiredFlags = AdminFlags.Admin;
        if (action == AdminSafetyDepositAction.Add)
            requiredFlags |= AdminFlags.Spawn;

        return canContinue() && _adminManager.HasAdminFlag(admin, requiredFlags) &&
               _playerManager.TryGetSessionById(admin.UserId, out var session) && session == admin;
    }

    private bool IsAdminPhysicalBoxValid(EntityUid physical, WayfarerSafetyDepositBox box)
    {
        return !TerminatingOrDeleted(physical) &&
               TryComp<SafetyDepositBoxComponent>(physical, out var component) &&
               component.BoxId == box.BoxId && component.OwnerId == box.OwnerUserId &&
               component.CharacterIndex == box.CharacterIndex && Prototype(physical)?.ID == box.ProtoId;
    }

    private bool TryGetAdminRecipient(ICommonSession admin, out EntityUid recipient)
    {
        recipient = default;
        if (admin.AttachedEntity is not { } entity || TerminatingOrDeleted(entity) ||
            Transform(entity).MapID == MapId.Nullspace)
            return false;

        recipient = entity;
        return true;
    }

    private bool TryDeliverAdminItem(ICommonSession admin, EntityUid item)
    {
        if (!TryGetAdminRecipient(admin, out var recipient) || TerminatingOrDeleted(item))
            return false;

        _transform.SetCoordinates(item, Transform(recipient).Coordinates);
        _hands.TryPickupAnyHand(recipient, item);
        return !TerminatingOrDeleted(item);
    }

    private EntityUid LoadAdminItem(string data, AdminBoxStaging staging)
    {
        using var reader = new StringReader(data);
        // Keep the complete load result: referenced nullspace entities are not necessarily children of the item.
        if (!_loader.TryLoadGeneric(reader, "admin safety deposit", out var loaded,
                new MapLoadOptions { ExpectedCategory = FileCategory.Entity }))
            throw new InvalidOperationException("Could not deserialize safety deposit item.");

        foreach (var entity in loaded.Entities)
            staging.Entities.Add(entity);

        if (loaded.Orphans.Count != 1)
            throw new InvalidOperationException("A stored item must have exactly one root.");

        var root = default(EntityUid);
        foreach (var entity in loaded.Orphans)
            root = entity;

        if (TerminatingOrDeleted(root))
            throw new InvalidOperationException("The stored item was deleted during restoration.");

        staging.Groups.Add(root, loaded.Entities);
        EnsureComp<SafetyDepositStoredComponent>(root);
        ResetStoredUseDelays(root);
        return root;
    }

    private static void ReleaseAdminStagedItem(EntityUid item, AdminBoxStaging staging)
    {
        staging.Entities.Remove(item);
        if (!staging.Groups.Remove(item, out var group))
            return;

        foreach (var entity in group)
            staging.Entities.Remove(entity);
    }

    private string SaveAdminItem(EntityUid item)
    {
        using var writer = new StringWriter();
        if (TerminatingOrDeleted(item) || !_loader.TrySaveEntity(item, writer))
            throw new InvalidOperationException("Could not serialize safety deposit item.");

        return writer.ToString();
    }

    private EntityUid? SpawnAdminItem(EntProtoId prototypeId, AdminBoxStaging staging)
    {
        if (!_prototypeManager.TryIndex(prototypeId, out var prototype) ||
            prototype.Abstract || !prototype.TryGetComponent<ItemComponent>(out _, _componentFactory))
            return null;

        var entity = Spawn(prototype.ID, MapCoordinates.Nullspace);
        staging.Entities.Add(entity);
        // A saved entity needs an external parent to be serialized as a single orphan.
        var anchor = Spawn(null, MapCoordinates.Nullspace);
        staging.Entities.Add(anchor);
        _transform.SetParent(entity, anchor);
        return entity;
    }

    private SafetyDepositAdminAudit CreateAdminAudit(
        ICommonSession admin,
        WayfarerSafetyDepositBox box,
        string action,
        string result,
        string details,
        string? itemData = null)
    {
        return new SafetyDepositAdminAudit
        {
            Id = Guid.NewGuid(),
            AdminUserId = admin.UserId.UserId,
            AdminName = admin.Name,
            OwnerUserId = box.OwnerUserId,
            CharacterIndex = box.CharacterIndex,
            BoxId = box.BoxId,
            CreatedAt = DateTime.UtcNow,
            Action = action,
            Result = result,
            Details = details,
            ItemData = itemData,
            RoundId = _gameTicker.RoundId,
        };
    }

    private void LogAdminBoxAction(ICommonSession admin, SafetyDepositAdminAudit audit)
    {
        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{admin:actor} safety deposit {audit.Action}: owner {new NetUserId(audit.OwnerUserId):subject}, slot {audit.CharacterIndex}, box {audit.BoxId}, operation {audit.Id}, result {audit.Result}: {audit.Details}");
    }

    private void RefreshPlayerSafetyDepositUis()
    {
        var query = EntityQueryEnumerator<SafetyDepositConsoleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            foreach (var actor in _uiSystem.GetActors(uid, Content.Shared._WF.SafetyDepositBox.BUI.SafetyDepositConsoleUiKey.Key))
                UpdateUI(uid, actor);
        }
    }

    // Operation-local staging, not persistent gameplay state.
    private sealed class AdminBoxStaging
    {
        public readonly HashSet<EntityUid> Entities = [];
        public readonly Dictionary<EntityUid, HashSet<EntityUid>> Groups = [];
        public bool Retain;
    }
}
