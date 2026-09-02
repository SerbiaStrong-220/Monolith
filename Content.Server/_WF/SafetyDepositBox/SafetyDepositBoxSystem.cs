using System.IO;
using System.Threading.Tasks; // Exodus: observed asynchronous persistence operations.
using Content.Server._Mono.MonoCoins;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Server._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.SafetyDepositBox.BUI;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared._WF.SafetyDepositBox.Events;
using Content.Shared.Database;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Labels.Components;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map; // Exodus: stage persistent entities safely in nullspace.
using Robust.Shared.Network; // Exodus: retain the authenticated account ID across awaits.

namespace Content.Server._WF.SafetyDepositBox;

public sealed partial class SafetyDepositBoxSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private BankSystem _bankSystem = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private IServerDbManager _dbManager = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedLabelSystem _label = default!; // Wicce: LabelSystem -> SharedLabelSystem
    [Dependency] private IServerPreferencesManager _prefsManager = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MonoCoinsManager _coinBase = default!; // I had to.
    [Dependency] private ISharedPlayerManager _playerManager = default!;

    // Exodus-begin: serialize persistence operations and discard stale per-player UI queries.
    private readonly HashSet<Guid> _activePurchaseUsers = [];
    private readonly HashSet<Guid> _activeBoxOperations = [];
    private readonly HashSet<Guid> _allowedBoxMutations = [];
    private readonly Dictionary<EntityUid, int> _uiUpdateVersions = [];
    // Exodus-end

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SafetyDepositConsoleComponent, ComponentInit>(OnConsoleInit);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, ComponentRemove>(OnConsoleRemove); // Exodus: clean up async UI state.
        SubscribeLocalEvent<SafetyDepositConsoleComponent, BoundUIOpenedEvent>(OnUIOpen);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, SafetyDepositPurchaseMessage>(OnPurchase);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, SafetyDepositDepositMessage>(OnDeposit);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, SafetyDepositWithdrawMessage>(OnWithdraw);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, SafetyDepositReclaimMessage>(OnReclaim);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, EntInsertedIntoContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, EntRemovedFromContainerMessage>(OnSlotChanged);

        // Exodus-begin: a box being persisted must be an immutable snapshot until its DB operation completes.
        SubscribeLocalEvent<SafetyDepositBoxComponent, StorageInteractAttemptEvent>(OnStorageInteractAttempt);
        SubscribeLocalEvent<SafetyDepositBoxComponent, StorageInteractUsingAttemptEvent>(OnStorageInteractUsingAttempt);
        SubscribeLocalEvent<SafetyDepositBoxComponent, ContainerIsInsertingAttemptEvent>(OnContainerInsertAttempt);
        SubscribeLocalEvent<SafetyDepositBoxComponent, ContainerIsRemovingAttemptEvent>(OnContainerRemoveAttempt);
        // Exodus-end
    }

    private void OnConsoleInit(EntityUid uid, SafetyDepositConsoleComponent component, ComponentInit args)
    {
        _itemSlots.AddItemSlot(uid, SafetyDepositConsoleComponent.BoxSlotId, component.BoxSlot);
    }

    // Exodus-begin: async UI queries are invalid once their console is removed.
    private void OnConsoleRemove(Entity<SafetyDepositConsoleComponent> ent, ref ComponentRemove args)
    {
        _uiUpdateVersions.Remove(ent.Owner);
    }
    // Exodus-end

    private void OnUIOpen(EntityUid uid, SafetyDepositConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        // Exodus-begin: BUI component state is shared, so only one actor may view character-private box data.
        var actors = new List<EntityUid>(_uiSystem.GetActors(uid, SafetyDepositConsoleUiKey.Key));
        foreach (var actor in actors)
        {
            if (actor != player)
                _uiSystem.CloseUi(uid, SafetyDepositConsoleUiKey.Key, actor);
        }

        SetEmptyUiState(uid, component);
        UpdateUI(uid, player);
        // Exodus-end
    }

    // Exodus-begin: replace async-void and prevent older DB results from overwriting a newer user's state.
    private void UpdateUI(EntityUid consoleUid, EntityUid player)
    {
        var version = _uiUpdateVersions.GetValueOrDefault(consoleUid) + 1;
        _uiUpdateVersions[consoleUid] = version;
        _ = UpdateUIAsync(consoleUid, player, version);
    }

    private async Task UpdateUIAsync(EntityUid consoleUid, EntityUid player, int version)
    {
        try
        {
            if (!TryGetCharacter(player, out var userId, out var characterIndex, out _))
                return;

            var ownedBoxes = await _dbManager.GetPlayerSafetyDepositBoxes(userId.UserId, characterIndex);

            if (!_uiUpdateVersions.TryGetValue(consoleUid, out var currentVersion) || currentVersion != version ||
                Deleted(consoleUid) || Deleted(player) ||
                !_uiSystem.IsUiOpen(consoleUid, SafetyDepositConsoleUiKey.Key, player) ||
                !TryComp<SafetyDepositConsoleComponent>(consoleUid, out var component) ||
                !TryGetCharacter(player, out var currentUserId, out var currentCharacterIndex, out _) ||
                currentUserId != userId || currentCharacterIndex != characterIndex)
            {
                return;
            }

            var boxInfoList = new List<SafetyDepositBoxInfo>(ownedBoxes.Count);
            foreach (var box in ownedBoxes)
            {
                bool isDeposited;
                if (!box.LastWithdrawn.HasValue)
                    isDeposited = true;
                else if (box.LastWithdrawnRoundId.HasValue && box.LastWithdrawnRoundId.Value != _gameTicker.RoundId)
                    isDeposited = false;
                else
                    isDeposited = box.Items.Count > 0;

                boxInfoList.Add(new SafetyDepositBoxInfo(
                    box.BoxId,
                    box.OwnerName,
                    isDeposited,
                    box.Nickname,
                    box.ProtoId,
                    box.LastWithdrawn,
                    box.LastWithdrawnRoundId));
            }

            var boxInSlot = component.BoxSlot.Item;
            SafetyDepositBoxInfo? boxInSlotInfo = null;

            if (boxInSlot != null &&
                TryComp<SafetyDepositBoxComponent>(boxInSlot, out var boxComp) &&
                boxComp.BoxId.HasValue &&
                TryPrototype(boxInSlot.Value, out var boxProto))
            {
                string? nickname = null;
                if (TryComp<LabelComponent>(boxInSlot.Value, out var labelComp))
                    nickname = labelComp.CurrentLabel;

                boxInSlotInfo = new SafetyDepositBoxInfo(
                    boxComp.BoxId.Value,
                    boxComp.OwnerName ?? Loc.GetString("safety-deposit-owner-unknown"),
                    false,
                    nickname,
                    boxProto.ToString(),
                    null,
                    null);
            }

            var state = new SafetyDepositConsoleState(
                boxInfoList,
                0,
                boxInSlot != null,
                boxInSlotInfo,
                GetBoxCost(component.SmallBoxProto),
                GetBoxCost(component.MediumBoxProto),
                GetBoxCost(component.LargeBoxProto),
                _gameTicker.RoundId);

            _uiSystem.SetUiState(consoleUid, SafetyDepositConsoleUiKey.Key, state);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to update safety deposit UI for {ToPrettyString(player)}: {ex}");
        }
    }

    private void SetEmptyUiState(EntityUid consoleUid, SafetyDepositConsoleComponent component)
    {
        var state = new SafetyDepositConsoleState(
            [],
            0,
            component.BoxSlot.Item != null,
            null,
            GetBoxCost(component.SmallBoxProto),
            GetBoxCost(component.MediumBoxProto),
            GetBoxCost(component.LargeBoxProto),
            _gameTicker.RoundId);

        _uiSystem.SetUiState(consoleUid, SafetyDepositConsoleUiKey.Key, state);
    }
    // Exodus-end

    private void OnPurchase(EntityUid uid, SafetyDepositConsoleComponent component, SafetyDepositPurchaseMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!TryGetCharacter(player, out var userId, out var characterIndex, out var profile))
        {
            Reject(uid, player, "safety-deposit-error-character");
            return;
        }

        // Exodus-begin: never trust a client-provided prototype outside this console's configured choices.
        if (!IsConfiguredBoxPrototype(component, args.BoxProto) ||
            !_prototypeManager.TryIndex(args.BoxProto, out var prototype) ||
            !prototype.TryGetComponent<SafetyDepositBoxComponent>(out var boxComponent, _componentFactory) ||
            boxComponent.Cost <= 0)
        {
            Reject(uid, player, "safety-deposit-error-invalid-size");
            return;
        }

        if (!TryComp<BankAccountComponent>(player, out _))
        {
            Reject(uid, player, "safety-deposit-error-bank-account");
            return;
        }

        if (!_activePurchaseUsers.Add(userId.UserId))
        {
            Reject(uid, player, "safety-deposit-error-operation-in-progress");
            return;
        }

        _ = PurchaseBoxAsync(uid, player, userId, characterIndex, profile.Name, prototype, boxComponent.Cost);
        // Exodus-end
    }

    // got tired of doing this
    public int GetBoxCost(EntProtoId boxProto)
    {
        if (_prototypeManager.TryIndex(boxProto, out var proto) &&
            proto.TryGetComponent<SafetyDepositBoxComponent>(out var boxComponent, _componentFactory))
            return boxComponent.Cost;

        return 0;
    }

    // Exodus-begin: payment and DB creation are compensated on every failure path.
    private async Task PurchaseBoxAsync(
        EntityUid consoleUid,
        EntityUid player,
        NetUserId userId,
        int characterIndex,
        string characterName,
        EntityPrototype prototype,
        int cost)
    {
        PaymentReceipt? payment = null;
        Guid? boxId = null;
        EntityUid? boxEntity = null;
        var succeeded = false;

        try
        {
            var paymentResult = await TryTakePaymentAsync(player, userId, characterIndex, cost);
            payment = paymentResult.Receipt;
            if (!paymentResult.Success)
            {
                ShowPaymentFailure(consoleUid, player, cost, paymentResult);
                return;
            }

            var box = await _dbManager.PurchaseSafetyDepositBox(
                userId.UserId,
                characterIndex,
                characterName,
                prototype.ID);
            boxId = box.BoxId;

            boxEntity = Spawn(prototype.ID, MapCoordinates.Nullspace);
            ConfigurePhysicalBox(boxEntity.Value, box.BoxId, userId.UserId, characterIndex, characterName);

            await _dbManager.ClearSafetyDepositBoxItems(box.BoxId, _gameTicker.RoundId);
            if (!await IsBoxWithdrawnAsync(box.BoxId, userId.UserId, characterIndex))
                throw new InvalidOperationException($"Purchased safety deposit box {box.BoxId} was not marked withdrawn.");

            if (!IsActorForCharacter(player, userId, characterIndex) ||
                !TryDeliverBox(boxEntity.Value, player, consoleUid))
            {
                throw new InvalidOperationException($"Could not deliver purchased safety deposit box {box.BoxId}.");
            }

            succeeded = true;
            Popup(player, "safety-deposit-purchase-success", ("id", ShortId(box.BoxId)));
            Confirm(consoleUid);

            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(player):actor} purchased safety deposit box {box.BoxId} for {cost} credits");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to purchase safety deposit box for {userId}: {ex}");
            if (!succeeded)
                Reject(consoleUid, player, "safety-deposit-error-transaction");
        }
        finally
        {
            if (!succeeded)
            {
                if (boxEntity is { } spawned && !Deleted(spawned))
                    QueueDel(spawned);

                var mayRefund = boxId == null;
                if (boxId is { } createdBoxId)
                    mayRefund = await TryDeletePurchaseRecordAsync(createdBoxId);

                if (payment is { } receipt && mayRefund && !await RefundPaymentAsync(receipt))
                {
                    Log.Error($"Failed to refund safety deposit purchase for {receipt.UserId}.");
                    _adminLogger.Add(LogType.Action, LogImpact.High,
                        $"Safety deposit payment refund failed for {receipt.UserId}, character slot {receipt.CharacterIndex}.");
                }
            }

            _activePurchaseUsers.Remove(userId.UserId);
            UpdateUIIfOpen(consoleUid, player);
        }
    }

    private async Task<PaymentResult> TryTakePaymentAsync(
        EntityUid player,
        NetUserId userId,
        int characterIndex,
        int cost)
    {
        var savingsBalance = await _coinBase.GetMonoCoinsBalanceAsync(userId);

        if (!TryGetPaymentContext(player, userId, characterIndex, out _, out _, out var profile))
            return new PaymentResult(PaymentFailure.Character, savingsBalance, 0, null);

        var bankBalance = profile.BankBalance;
        if (savingsBalance + bankBalance < cost)
            return new PaymentResult(PaymentFailure.InsufficientFunds, savingsBalance, bankBalance, null);

        var savingsSpent = (int) Math.Min(savingsBalance, cost);
        var sectorSpent = cost - savingsSpent;

        if (savingsSpent > 0)
            await _coinBase.AddMonoCoinsAsync(userId, -savingsSpent);

        if (!TryGetPaymentContext(player, userId, characterIndex, out _, out _, out _))
        {
            PaymentReceipt? partialReceipt = savingsSpent > 0
                ? new PaymentReceipt(userId, characterIndex, savingsSpent, 0)
                : null;
            return new PaymentResult(PaymentFailure.Character, savingsBalance, bankBalance, partialReceipt);
        }

        if (sectorSpent > 0)
        {
            if (!_bankSystem.TryBankWithdraw(player, sectorSpent))
            {
                PaymentReceipt? partialReceipt = savingsSpent > 0
                    ? new PaymentReceipt(userId, characterIndex, savingsSpent, 0)
                    : null;
                return new PaymentResult(PaymentFailure.Transaction, savingsBalance, bankBalance, partialReceipt);
            }
        }

        var receipt = new PaymentReceipt(userId, characterIndex, savingsSpent, sectorSpent);
        return new PaymentResult(PaymentFailure.None, savingsBalance, bankBalance, receipt);
    }

    private async Task<bool> RefundPaymentAsync(PaymentReceipt receipt)
    {
        var refunded = true;

        if (receipt.SavingsAmount > 0)
        {
            try
            {
                await _coinBase.AddMonoCoinsAsync(receipt.UserId, receipt.SavingsAmount);
            }
            catch (Exception ex)
            {
                refunded = false;
                Log.Error($"Failed to refund {receipt.SavingsAmount} savings credits to {receipt.UserId}: {ex}");
            }
        }

        if (receipt.SectorAmount <= 0)
            return refunded;

        try
        {
            PlayerPreferences? prefs;
            if (!_prefsManager.TryGetCachedPreferences(receipt.UserId, out prefs))
                prefs = await _dbManager.GetPlayerPreferencesAsync(receipt.UserId, default);

            if (prefs == null ||
                !prefs.Characters.TryGetValue(receipt.CharacterIndex, out var character) ||
                character is not HumanoidCharacterProfile profile)
            {
                return false;
            }

            if (_playerManager.TryGetSessionById(receipt.UserId, out var session) && session != null)
            {
                var refundedThroughEntity = session.AttachedEntity is { } attached &&
                                            prefs.SelectedCharacterIndex == receipt.CharacterIndex &&
                                            _bankSystem.TryBankDeposit(attached, receipt.SectorAmount, tax: false);

                if (!refundedThroughEntity)
                {
                    if (!_bankSystem.TryBankDeposit(session, prefs, profile, receipt.SectorAmount, out _))
                        return false;

                    if (prefs.SelectedCharacterIndex == receipt.CharacterIndex &&
                        session.AttachedEntity is { } currentEntity &&
                        TryComp<BankAccountComponent>(currentEntity, out var bank))
                    {
                        _bankSystem.OnPreferencesLoaded(
                            currentEntity,
                            bank,
                            new PreferencesLoadedEvent(session, prefs));
                    }
                }
            }
            else if (!await _bankSystem.TryBankDepositOffline(receipt.UserId, prefs, profile, receipt.SectorAmount))
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to refund {receipt.SectorAmount} sector credits to {receipt.UserId}: {ex}");
            return false;
        }

        return refunded;
    }

    private async Task<bool> TryDeletePurchaseRecordAsync(Guid boxId)
    {
        try
        {
            await _dbManager.DeleteSafetyDepositBox(boxId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to delete rolled-back safety deposit box {boxId}: {ex}");

            try
            {
                var remainingBox = await _dbManager.GetSafetyDepositBox(boxId);
                if (remainingBox == null)
                    return true;

                await _dbManager.DepositSafetyDepositBoxItems(boxId, []);
                var restoredBox = await _dbManager.GetSafetyDepositBox(boxId);
                if (restoredBox == null || restoredBox.LastWithdrawn.HasValue)
                    throw new InvalidOperationException($"Safety deposit box {boxId} did not reach a stored rollback state.");
            }
            catch (Exception restoreEx)
            {
                Log.Error($"Failed to restore rolled-back safety deposit box {boxId} to stored state: {restoreEx}");

                try
                {
                    var probedBox = await _dbManager.GetSafetyDepositBox(boxId);
                    if (probedBox == null)
                        return true;

                    if (!probedBox.LastWithdrawn.HasValue)
                        return false;
                }
                catch (Exception probeEx)
                {
                    Log.Error($"Failed to probe rolled-back safety deposit box {boxId}: {probeEx}");
                }

                _adminLogger.Add(LogType.Action, LogImpact.High,
                    $"Safety deposit purchase rollback failed for box {boxId}.");
            }

            return false;
        }
    }

    private void ShowPaymentFailure(EntityUid consoleUid, EntityUid player, int cost, PaymentResult result)
    {
        switch (result.Failure)
        {
            case PaymentFailure.InsufficientFunds:
                Reject(consoleUid, player, "safety-deposit-error-insufficient-funds",
                    ("cost", cost),
                    ("bank", result.BankBalance),
                    ("savings", result.SavingsBalance));
                break;
            case PaymentFailure.Character:
                Reject(consoleUid, player, "safety-deposit-error-character");
                break;
            default:
                Reject(consoleUid, player, "safety-deposit-error-transaction");
                break;
        }
    }
    // Exodus-end

    private void OnDeposit(EntityUid uid, SafetyDepositConsoleComponent component, SafetyDepositDepositMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!TryGetCharacter(player, out var userId, out var characterIndex, out _))
        {
            Reject(uid, player, "safety-deposit-error-character");
            return;
        }

        var boxEntity = component.BoxSlot.Item;
        if (boxEntity == null)
        {
            Reject(uid, player, "safety-deposit-error-insert-box");
            return;
        }

        if (!TryComp<SafetyDepositBoxComponent>(boxEntity.Value, out var boxComp) || boxComp.BoxId is not { } boxId)
        {
            Reject(uid, player, "safety-deposit-error-invalid-box");
            return;
        }

        if (boxComp.OwnerId != userId.UserId || boxComp.CharacterIndex != characterIndex)
        {
            Reject(uid, player, "safety-deposit-error-not-owner");
            return;
        }

        if (!TryComp<StorageComponent>(boxEntity.Value, out _))
        {
            Reject(uid, player, "safety-deposit-error-no-storage");
            return;
        }

        // Exodus-begin: claim both the DB identity and the physical slot before the first await.
        if (!_activeBoxOperations.Add(boxId))
        {
            Reject(uid, player, "safety-deposit-error-operation-in-progress");
            return;
        }

        _itemSlots.SetLock(uid, component.BoxSlot, true);
        _uiSystem.CloseUi(boxEntity.Value, StorageComponent.StorageUiKey.Key);
        _ = DepositBoxAsync(uid, player, boxEntity.Value, userId, characterIndex, boxId);
        // Exodus-end
    }

    // Exodus-begin: all-or-nothing serialization keeps an unpersisted physical box on every DB failure.
    private async Task DepositBoxAsync(
        EntityUid consoleUid,
        EntityUid player,
        EntityUid boxEntity,
        NetUserId userId,
        int characterIndex,
        Guid boxId)
    {
        var succeeded = false;
        var persistenceAttempted = false;
        var itemCount = 0;

        try
        {
            var databaseBox = await _dbManager.GetSafetyDepositBox(boxId);
            if (databaseBox == null)
            {
                Reject(consoleUid, player, "safety-deposit-error-box-not-found");
                return;
            }

            if (databaseBox.OwnerUserId != userId.UserId || databaseBox.CharacterIndex != characterIndex)
            {
                Reject(consoleUid, player, "safety-deposit-error-not-owner");
                return;
            }

            if (!databaseBox.LastWithdrawn.HasValue)
            {
                Reject(consoleUid, player, "safety-deposit-error-already-stored");
                return;
            }

            if (!ValidatePhysicalBox(consoleUid, boxEntity, userId, characterIndex, boxId, out var boxComp, out var storageComp) ||
                !IsActorForCharacter(player, userId, characterIndex))
            {
                Reject(consoleUid, player, "safety-deposit-error-invalid-box");
                return;
            }

            if (!TryPrototype(boxEntity, out var physicalProto) || physicalProto.ToString() != databaseBox.ProtoId)
            {
                Reject(consoleUid, player, "safety-deposit-error-invalid-box");
                return;
            }

            var items = new List<EntityUid>(storageComp.Container.ContainedEntities);
            var entityDataList = new List<string>(items.Count);
            foreach (var item in items)
            {
                try
                {
                    using var writer = new StringWriter();
                    if (!_loader.TrySaveEntity(item, writer))
                        throw new InvalidOperationException($"Map loader rejected {ToPrettyString(item)}.");

                    entityDataList.Add(writer.ToString());
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to serialize {ToPrettyString(item)} for safety deposit box {boxId}: {ex}");
                    Reject(consoleUid, player, "safety-deposit-error-serialize");
                    return;
                }
            }

            itemCount = entityDataList.Count;
            string? nickname = null;
            if (TryComp<LabelComponent>(boxEntity, out var boxLabel) && !string.IsNullOrEmpty(boxLabel.CurrentLabel))
                nickname = boxLabel.CurrentLabel;

            await _dbManager.UpdateSafetyDepositBoxNickname(boxId, nickname);
            persistenceAttempted = true;
            await _dbManager.DepositSafetyDepositBoxItems(boxId, entityDataList);

            var storedBox = await _dbManager.GetSafetyDepositBox(boxId);
            if (storedBox == null ||
                storedBox.OwnerUserId != userId.UserId ||
                storedBox.CharacterIndex != characterIndex ||
                storedBox.LastWithdrawn.HasValue ||
                storedBox.Items.Count != entityDataList.Count)
            {
                throw new InvalidOperationException($"Safety deposit box {boxId} did not reach the expected stored state.");
            }

            ReleaseAndDeletePhysicalBox(consoleUid, boxEntity, boxId);
            succeeded = true;

            Popup(player, "safety-deposit-deposit-success");
            Confirm(consoleUid);

            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(player):actor} deposited safety deposit box {boxComp.BoxId} with {itemCount} items");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to deposit safety deposit box {boxId}: {ex}");

            if (!succeeded)
            {
                if (persistenceAttempted &&
                    !await RestoreWithdrawnBoxAsync(boxId, userId.UserId, characterIndex))
                {
                    _adminLogger.Add(LogType.Action, LogImpact.High,
                        $"Safety deposit rollback could not confirm withdrawn state for box {boxId}; its physical copy was retained.");
                }

                Reject(consoleUid, player, "safety-deposit-error-transaction");
            }
        }
        finally
        {
            if (!succeeded)
                UnlockConsoleSlot(consoleUid);

            _activeBoxOperations.Remove(boxId);
            UpdateUIIfOpen(consoleUid, player);
        }
    }
    // Exodus-end

    private void OnWithdraw(EntityUid uid, SafetyDepositConsoleComponent component, SafetyDepositWithdrawMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!TryGetCharacter(player, out var userId, out var characterIndex, out _))
        {
            Reject(uid, player, "safety-deposit-error-character");
            return;
        }

        // Exodus-begin: one in-process operation owns a persistent box ID at a time.
        if (!_activeBoxOperations.Add(args.BoxId))
        {
            Reject(uid, player, "safety-deposit-error-operation-in-progress");
            return;
        }

        _ = WithdrawBoxAsync(uid, player, userId, characterIndex, args.BoxId);
        // Exodus-end
    }

    private void OnReclaim(EntityUid uid, SafetyDepositConsoleComponent component, SafetyDepositReclaimMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!TryGetCharacter(player, out var userId, out var characterIndex, out _))
        {
            Reject(uid, player, "safety-deposit-error-character");
            return;
        }

        // Exodus-begin: reclaim preserves the same DB record and is serialized with deposit/withdraw.
        if (!_activeBoxOperations.Add(args.BoxId))
        {
            Reject(uid, player, "safety-deposit-error-operation-in-progress");
            return;
        }

        _ = ReclaimBoxAsync(uid, player, userId, characterIndex, args.BoxId);
        // Exodus-end
    }

    // Exodus-begin: reclaim keeps the stable ID and compensates a failed delivery back to stored state.
    private async Task ReclaimBoxAsync(
        EntityUid consoleUid,
        EntityUid player,
        NetUserId userId,
        int characterIndex,
        Guid boxId)
    {
        EntityUid? boxEntity = null;
        var databaseMutationAttempted = false;
        var succeeded = false;

        try
        {
            var box = await _dbManager.GetSafetyDepositBox(boxId);
            if (box == null)
            {
                Reject(consoleUid, player, "safety-deposit-error-box-not-found");
                return;
            }

            if (box.OwnerUserId != userId.UserId || box.CharacterIndex != characterIndex)
            {
                Reject(consoleUid, player, "safety-deposit-error-not-owner");
                return;
            }

            var isLost = box.LastWithdrawn.HasValue &&
                         box.LastWithdrawnRoundId.HasValue &&
                         box.LastWithdrawnRoundId.Value != _gameTicker.RoundId &&
                         box.Items.Count == 0;
            if (!isLost)
            {
                Reject(consoleUid, player, "safety-deposit-error-not-lost");
                return;
            }

            if (!IsActorForCharacter(player, userId, characterIndex) ||
                !TryGetBoxPrototype(box.ProtoId, out var prototype))
            {
                Reject(consoleUid, player, "safety-deposit-error-invalid-box");
                return;
            }

            boxEntity = Spawn(prototype.ID, MapCoordinates.Nullspace);
            ConfigurePhysicalBox(boxEntity.Value, boxId, userId.UserId, characterIndex, MetaData(player).EntityName);

            if (!string.IsNullOrEmpty(box.Nickname))
                _label.Label(boxEntity.Value, box.Nickname);

            databaseMutationAttempted = true;
            await _dbManager.ClearSafetyDepositBoxItems(boxId, _gameTicker.RoundId);
            if (!await IsBoxWithdrawnAsync(boxId, userId.UserId, characterIndex))
                throw new InvalidOperationException($"Reclaimed safety deposit box {boxId} was not marked withdrawn.");

            if (!IsActorForCharacter(player, userId, characterIndex) ||
                !TryDeliverBox(boxEntity.Value, player, consoleUid))
            {
                throw new InvalidOperationException($"Could not deliver reclaimed safety deposit box {boxId}.");
            }

            succeeded = true;
            Popup(player, "safety-deposit-reclaim-success");
            Confirm(consoleUid);

            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(player):actor} reclaimed lost safety deposit box {boxId}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to reclaim safety deposit box {boxId}: {ex}");

            if (!succeeded)
            {
                if (databaseMutationAttempted && !await RestoreStoredBoxAsync(boxId, []))
                {
                    if (boxEntity is { } retained && !Deleted(retained))
                    {
                        TryDeliverBox(retained, player, consoleUid);
                        boxEntity = null;
                    }

                    _adminLogger.Add(LogType.Action, LogImpact.High,
                        $"Safety deposit reclaim rollback failed for box {boxId}; its physical copy was retained.");
                }

                Reject(consoleUid, player, "safety-deposit-error-transaction");
            }
        }
        finally
        {
            if (!succeeded && boxEntity is { } spawned && !Deleted(spawned))
                QueueDel(spawned);

            _activeBoxOperations.Remove(boxId);
            UpdateUIIfOpen(consoleUid, player);
        }
    }
    // Exodus-end

    // Exodus-begin: build in nullspace, load every item, then clear DB; any failure deletes the temporary copy.
    private async Task WithdrawBoxAsync(
        EntityUid consoleUid,
        EntityUid player,
        NetUserId userId,
        int characterIndex,
        Guid boxId)
    {
        EntityUid? boxEntity = null;
        var databaseMutationAttempted = false;
        var succeeded = false;
        List<string>? storedData = null;

        try
        {
            var box = await _dbManager.GetSafetyDepositBox(boxId);
            if (box == null)
            {
                Reject(consoleUid, player, "safety-deposit-error-box-not-found");
                return;
            }

            if (box.OwnerUserId != userId.UserId || box.CharacterIndex != characterIndex)
            {
                Reject(consoleUid, player, "safety-deposit-error-not-owner");
                return;
            }

            if (box.LastWithdrawn != null)
            {
                Reject(consoleUid, player, "safety-deposit-error-already-withdrawn");
                return;
            }

            if (!IsActorForCharacter(player, userId, characterIndex) ||
                !TryGetBoxPrototype(box.ProtoId, out var prototype))
            {
                Reject(consoleUid, player, "safety-deposit-error-invalid-box");
                return;
            }

            storedData = new List<string>(box.Items.Count);
            foreach (var item in box.Items)
                storedData.Add(item.EntityData);

            boxEntity = Spawn(prototype.ID, MapCoordinates.Nullspace);
            ConfigurePhysicalBox(boxEntity.Value, box.BoxId, userId.UserId, characterIndex, MetaData(player).EntityName);

            if (!TryComp<StorageComponent>(boxEntity.Value, out var storageComp))
                throw new InvalidOperationException($"Box prototype {prototype.ID} has no StorageComponent.");

            if (!string.IsNullOrEmpty(box.Nickname))
                _label.Label(boxEntity.Value, box.Nickname);

            _allowedBoxMutations.Add(boxId);
            try
            {
                foreach (var itemData in storedData)
                {
                    using var reader = new StringReader(itemData);
                    if (!_loader.TryLoadEntity(reader, "safety deposit box", out var entity))
                        throw new InvalidOperationException($"Could not deserialize an item from safety deposit box {boxId}.");

                    var itemEntity = entity.Value.Owner;
                    EnsureComp<SafetyDepositStoredComponent>(itemEntity);

                    if (!_storage.Insert(boxEntity.Value, itemEntity, out _, storageComp: storageComp, playSound: false))
                    {
                        QueueDel(itemEntity);
                        throw new InvalidOperationException($"Could not insert a restored item into safety deposit box {boxId}.");
                    }
                }
            }
            finally
            {
                _allowedBoxMutations.Remove(boxId);
            }

            databaseMutationAttempted = true;
            await _dbManager.ClearSafetyDepositBoxItems(boxId, _gameTicker.RoundId);
            if (!await IsBoxWithdrawnAsync(boxId, userId.UserId, characterIndex))
                throw new InvalidOperationException($"Safety deposit box {boxId} was not marked withdrawn.");

            if (!IsActorForCharacter(player, userId, characterIndex) ||
                !TryDeliverBox(boxEntity.Value, player, consoleUid))
            {
                throw new InvalidOperationException($"Could not deliver safety deposit box {boxId}.");
            }

            succeeded = true;
            Popup(player, "safety-deposit-withdraw-success");
            Confirm(consoleUid);

            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(player):actor} withdrew safety deposit box {boxId} with {storedData.Count} items");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to withdraw safety deposit box {boxId}: {ex}");

            if (!succeeded)
            {
                if (databaseMutationAttempted && storedData != null && !await RestoreStoredBoxAsync(boxId, storedData))
                {
                    if (boxEntity is { } retained && !Deleted(retained))
                    {
                        TryDeliverBox(retained, player, consoleUid);
                        boxEntity = null;
                    }

                    _adminLogger.Add(LogType.Action, LogImpact.High,
                        $"Safety deposit withdrawal rollback failed for box {boxId}; its physical copy was retained.");
                }

                Reject(consoleUid, player, "safety-deposit-error-transaction");
            }
        }
        finally
        {
            if (!succeeded && boxEntity is { } spawned && !Deleted(spawned))
                QueueDel(spawned);

            _activeBoxOperations.Remove(boxId);
            UpdateUIIfOpen(consoleUid, player);
        }
    }
    // Exodus-end

    private void OnSlotChanged(EntityUid uid, SafetyDepositConsoleComponent component, ContainerModifiedMessage args)
    {
        foreach (var actor in _uiSystem.GetActors(uid, SafetyDepositConsoleUiKey.Key))
            UpdateUI(uid, actor); // Exodus: versioned task wrapper.
    }

    // Exodus-begin: reject all user-driven storage mutations while a persistence snapshot is in flight.
    private void OnStorageInteractAttempt(Entity<SafetyDepositBoxComponent> ent, ref StorageInteractAttemptEvent args)
    {
        if (IsBoxMutationBlocked(ent.Comp))
            args.Cancelled = true;
    }

    private void OnStorageInteractUsingAttempt(Entity<SafetyDepositBoxComponent> ent, ref StorageInteractUsingAttemptEvent args)
    {
        if (IsBoxMutationBlocked(ent.Comp))
            args.Cancelled = true;
    }

    private void OnContainerInsertAttempt(Entity<SafetyDepositBoxComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID == StorageComponent.ContainerId && IsBoxMutationBlocked(ent.Comp))
            args.Cancel();
    }

    private void OnContainerRemoveAttempt(Entity<SafetyDepositBoxComponent> ent, ref ContainerIsRemovingAttemptEvent args)
    {
        if (args.Container.ID == StorageComponent.ContainerId && IsBoxMutationBlocked(ent.Comp))
            args.Cancel();
    }

    private bool IsBoxMutationBlocked(SafetyDepositBoxComponent component)
    {
        return component.BoxId is { } boxId &&
               _activeBoxOperations.Contains(boxId) &&
               !_allowedBoxMutations.Contains(boxId);
    }
    // Exodus-end

    // Exodus-begin: validation and compensation helpers shared by every persistence operation.
    private bool TryGetCharacter(
        EntityUid player,
        out NetUserId userId,
        out int characterIndex,
        out HumanoidCharacterProfile profile)
    {
        userId = default;
        characterIndex = default;
        profile = default!;

        if (!TryComp<ActorComponent>(player, out var actor))
            return false;

        userId = actor.PlayerSession.UserId;
        if (!_prefsManager.TryGetCachedPreferences(userId, out var prefs) ||
            prefs.SelectedCharacter is not HumanoidCharacterProfile selectedProfile)
        {
            return false;
        }

        characterIndex = prefs.SelectedCharacterIndex;
        profile = selectedProfile;
        return true;
    }

    private bool IsActorForCharacter(EntityUid player, NetUserId userId, int characterIndex)
    {
        return !Deleted(player) &&
               TryGetCharacter(player, out var currentUserId, out var currentCharacterIndex, out _) &&
               currentUserId == userId &&
               currentCharacterIndex == characterIndex;
    }

    private bool TryGetPaymentContext(
        EntityUid player,
        NetUserId userId,
        int characterIndex,
        out ICommonSession session,
        out PlayerPreferences prefs,
        out HumanoidCharacterProfile profile)
    {
        session = default!;
        prefs = default!;
        profile = default!;

        if (Deleted(player) || !HasComp<BankAccountComponent>(player))
            return false;

        if (!_playerManager.TryGetSessionByEntity(player, out var currentSession) ||
            currentSession == null ||
            currentSession.UserId != userId)
        {
            return false;
        }

        if (!_prefsManager.TryGetCachedPreferences(userId, out var currentPrefs) ||
            currentPrefs == null ||
            currentPrefs.SelectedCharacterIndex != characterIndex ||
            currentPrefs.SelectedCharacter is not HumanoidCharacterProfile selectedProfile)
        {
            return false;
        }

        session = currentSession;
        prefs = currentPrefs;
        profile = selectedProfile;
        return true;
    }

    private bool ValidatePhysicalBox(
        EntityUid consoleUid,
        EntityUid boxEntity,
        NetUserId userId,
        int characterIndex,
        Guid boxId,
        out SafetyDepositBoxComponent boxComp,
        out StorageComponent storageComp)
    {
        boxComp = default!;
        storageComp = default!;

        if (Deleted(consoleUid) ||
            Deleted(boxEntity) ||
            !TryComp<SafetyDepositConsoleComponent>(consoleUid, out var consoleComp) ||
            consoleComp.BoxSlot.Item != boxEntity ||
            !consoleComp.BoxSlot.Locked ||
            !TryComp<SafetyDepositBoxComponent>(boxEntity, out var currentBoxComp) ||
            currentBoxComp == null ||
            currentBoxComp.BoxId != boxId ||
            currentBoxComp.OwnerId != userId.UserId ||
            currentBoxComp.CharacterIndex != characterIndex ||
            !TryComp<StorageComponent>(boxEntity, out var currentStorageComp) ||
            currentStorageComp == null)
        {
            return false;
        }

        boxComp = currentBoxComp;
        storageComp = currentStorageComp;
        return true;
    }

    private static bool IsConfiguredBoxPrototype(SafetyDepositConsoleComponent component, EntProtoId prototype)
    {
        return prototype == component.SmallBoxProto ||
               prototype == component.MediumBoxProto ||
               prototype == component.LargeBoxProto;
    }

    private bool TryGetBoxPrototype(string prototypeId, out EntityPrototype prototype)
    {
        if (_prototypeManager.TryIndex<EntityPrototype>(prototypeId, out var currentPrototype) &&
            currentPrototype != null &&
            currentPrototype.TryGetComponent<SafetyDepositBoxComponent>(out _, _componentFactory))
        {
            prototype = currentPrototype;
            return true;
        }

        prototype = default!;
        return false;
    }

    private void ConfigurePhysicalBox(
        EntityUid boxEntity,
        Guid boxId,
        Guid ownerId,
        int characterIndex,
        string ownerName)
    {
        var boxComp = EnsureComp<SafetyDepositBoxComponent>(boxEntity);
        boxComp.BoxId = boxId;
        boxComp.OwnerId = ownerId;
        boxComp.CharacterIndex = characterIndex;
        boxComp.OwnerName = ownerName;
        Dirty(boxEntity, boxComp);
    }

    private bool TryDeliverBox(EntityUid boxEntity, EntityUid player, EntityUid consoleUid)
    {
        if (Deleted(boxEntity))
            return false;

        if (!Deleted(player))
        {
            _transform.SetCoordinates(boxEntity, Transform(player).Coordinates);
            if (!_hands.TryPickupAnyHand(player, boxEntity))
                _transform.SetLocalRotation(boxEntity, Angle.Zero);
            return true;
        }

        if (Deleted(consoleUid))
            return false;

        _transform.SetCoordinates(boxEntity, Transform(consoleUid).Coordinates);
        _transform.SetLocalRotation(boxEntity, Angle.Zero);
        return true;
    }

    private void ReleaseAndDeletePhysicalBox(EntityUid consoleUid, EntityUid boxEntity, Guid boxId)
    {
        if (!TryComp<SafetyDepositConsoleComponent>(consoleUid, out var consoleComp))
        {
            if (!Deleted(boxEntity))
                QueueDel(boxEntity);
            return;
        }

        _allowedBoxMutations.Add(boxId);
        try
        {
            _itemSlots.SetLock(consoleUid, consoleComp.BoxSlot, false);
            if (consoleComp.BoxSlot.Item == boxEntity)
                _itemSlots.TryEject(consoleUid, consoleComp.BoxSlot, null, out _);
        }
        finally
        {
            _allowedBoxMutations.Remove(boxId);
        }

        if (!Deleted(boxEntity))
            QueueDel(boxEntity);
    }

    private void UnlockConsoleSlot(EntityUid consoleUid)
    {
        if (TryComp<SafetyDepositConsoleComponent>(consoleUid, out var consoleComp))
            _itemSlots.SetLock(consoleUid, consoleComp.BoxSlot, false);
    }

    private async Task<bool> IsBoxWithdrawnAsync(Guid boxId, Guid ownerId, int characterIndex)
    {
        var box = await _dbManager.GetSafetyDepositBox(boxId);
        return box != null &&
               box.OwnerUserId == ownerId &&
               box.CharacterIndex == characterIndex &&
               box.LastWithdrawn.HasValue &&
               box.LastWithdrawnRoundId == _gameTicker.RoundId &&
               box.Items.Count == 0;
    }

    private async Task<bool> RestoreWithdrawnBoxAsync(Guid boxId, Guid ownerId, int characterIndex)
    {
        try
        {
            await _dbManager.ClearSafetyDepositBoxItems(boxId, _gameTicker.RoundId);
            return await IsBoxWithdrawnAsync(boxId, ownerId, characterIndex);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to restore safety deposit box {boxId} to withdrawn state: {ex}");
            return false;
        }
    }

    private async Task<bool> RestoreStoredBoxAsync(Guid boxId, List<string> storedData)
    {
        try
        {
            await _dbManager.DepositSafetyDepositBoxItems(boxId, storedData);
            return await IsBoxStoredAsync(boxId, storedData.Count);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to restore safety deposit box {boxId} to stored state: {ex}");

            try
            {
                return await IsBoxStoredAsync(boxId, storedData.Count);
            }
            catch (Exception probeEx)
            {
                Log.Error($"Failed to probe restored safety deposit box {boxId}: {probeEx}");
                return false;
            }
        }
    }

    private async Task<bool> IsBoxStoredAsync(Guid boxId, int expectedItemCount)
    {
        var box = await _dbManager.GetSafetyDepositBox(boxId);
        return box != null && !box.LastWithdrawn.HasValue && box.Items.Count == expectedItemCount;
    }

    private void UpdateUIIfOpen(EntityUid consoleUid, EntityUid player)
    {
        if (!Deleted(consoleUid) &&
            !Deleted(player) &&
            _uiSystem.IsUiOpen(consoleUid, SafetyDepositConsoleUiKey.Key, player))
        {
            UpdateUI(consoleUid, player);
        }
    }
    // Exodus-end

    private void PlayDenySound(EntityUid uid, SafetyDepositConsoleComponent component)
    {
        _audio.PlayPvs(component.ErrorSound, uid);
    }

    private void PlayConfirmSound(EntityUid uid, SafetyDepositConsoleComponent component)
    {
        _audio.PlayPvs(component.ConfirmSound, uid);
    }

    // Exodus-begin: all player-facing responses are localized and tolerate disconnects/destruction.
    private void Reject(EntityUid consoleUid, EntityUid actor, string messageId, params (string, object)[] args)
    {
        Popup(actor, messageId, args);
        if (!Deleted(consoleUid) && TryComp<SafetyDepositConsoleComponent>(consoleUid, out var component))
            PlayDenySound(consoleUid, component);
    }

    private void Confirm(EntityUid consoleUid)
    {
        if (!Deleted(consoleUid) && TryComp<SafetyDepositConsoleComponent>(consoleUid, out var component))
            PlayConfirmSound(consoleUid, component);
    }

    private void Popup(EntityUid actor, string messageId, params (string, object)[] args)
    {
        if (!Deleted(actor))
            _popup.PopupEntity(Loc.GetString(messageId, args), actor, actor);
    }

    private static string ShortId(Guid boxId)
    {
        return boxId.ToString()[..8];
    }
    // Exodus-end

    // Exodus-begin: explicit receipts preserve the original payment split during compensation.
    private readonly record struct PaymentReceipt(
        NetUserId UserId,
        int CharacterIndex,
        int SavingsAmount,
        int SectorAmount);

    private readonly record struct PaymentResult(
        PaymentFailure Failure,
        long SavingsBalance,
        int BankBalance,
        PaymentReceipt? Receipt)
    {
        public bool Success => Failure == PaymentFailure.None && Receipt.HasValue;
    }

    private enum PaymentFailure : byte
    {
        None,
        Character,
        InsufficientFunds,
        Transaction,
    }
    // Exodus-end
}
