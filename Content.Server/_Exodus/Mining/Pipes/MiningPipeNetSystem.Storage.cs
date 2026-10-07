using System.Diagnostics.CodeAnalysis;
using Content.Server._Exodus.Mining.Pipes.Nodes;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Examine;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Mining.Pipes;

public sealed partial class MiningPipeNetSystem
{
    // A batch may span several reservoirs; callbacks can re-enter, so every deposit rents its own plan.
    private readonly Stack<List<MaterialDeposit>> _depositLists = new();

    private void InitializeStorage()
    {
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, ExaminedEvent>(OnStorageExamined);
    }

    private void OnStorageExamined(Entity<MiningPipeNetworkMemberComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !ent.Comp.ReceiveMaterials || !_storageQuery.TryComp(ent, out var storage))
            return;

        long stored = 0;
        foreach (var amount in storage.Storage.Values)
            stored += amount;

        args.PushMarkup(storage.StorageLimit is { } capacity
            ? Loc.GetString("mining-pipe-storage-examine", ("stored", stored), ("capacity", capacity))
            : Loc.GetString("mining-pipe-storage-examine-unlimited", ("stored", stored)));
    }

    /// <summary>Whether a complete production batch fits in the local buffer and connected reservoirs.</summary>
    public bool CanDepositMaterial(Entity<MaterialStorageComponent?> ent, ProtoId<MaterialPrototype> material, int amount)
    {
        if (amount <= 0 || TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent) ||
            !Resolve(ent, ref ent.Comp, false))
            return false;

        var needed = amount - GetAvailableSpace((ent, ent.Comp), material);
        if (needed <= 0)
            return true;

        if (!_memberQuery.TryComp(ent, out var member) || GetNetwork((ent, member)) is not { } start)
            return false;

        var nets = RentJoinedNetworks(start);
        try
        {
            foreach (var net in nets)
            {
                foreach (var node in net.Nodes)
                {
                    if (node is not MiningPipeDeviceNode device || !TryGetReservoir(device, ent, out var storage))
                        continue;

                    needed -= GetAvailableSpace((device.Owner, storage), material);
                    if (needed <= 0)
                        return true;
                }
            }
        }
        finally
        {
            ReturnNets(nets);
        }

        return false;
    }

    /// <summary>Deposits a complete production batch, using the local buffer before connected reservoirs.</summary>
    public bool TryDepositMaterial(Entity<MaterialStorageComponent?> ent, ProtoId<MaterialPrototype> material, int amount)
    {
        if (amount <= 0 || TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent) ||
            !Resolve(ent, ref ent.Comp, false))
            return false;

        var localAmount = Math.Min(amount, GetAvailableSpace((ent, ent.Comp), material));
        if (localAmount == amount)
            return _materials.TryChangeMaterialAmount(ent, material, amount, ent.Comp, localOnly: true);

        if (!_memberQuery.TryComp(ent, out var member) || GetNetwork((ent, member)) is not { } start)
            return false;

        var deposits = _depositLists.Count > 0 ? _depositLists.Pop() : new List<MaterialDeposit>();
        var nets = RentJoinedNetworks(start);
        try
        {
            if (localAmount > 0)
                deposits.Add(new MaterialDeposit((ent, ent.Comp), localAmount));

            var needed = amount - localAmount;
            foreach (var net in nets)
            {
                foreach (var node in net.Nodes)
                {
                    if (needed <= 0)
                        break;

                    if (node is not MiningPipeDeviceNode device || !TryGetReservoir(device, ent, out var storage))
                        continue;

                    var received = Math.Min(needed, GetAvailableSpace((device.Owner, storage), material));
                    if (received <= 0)
                        continue;

                    deposits.Add(new MaterialDeposit((device.Owner, storage), received));
                    needed -= received;
                }

                if (needed <= 0)
                    break;
            }

            // Never pay out part of a mining batch when the rest has nowhere to go.
            if (needed > 0)
                return false;

            for (var applied = 0; applied < deposits.Count; applied++)
            {
                var deposit = deposits[applied];
                if (TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent) ||
                    TerminatingOrDeleted(deposit.Storage) || EntityManager.IsQueuedForDeletion(deposit.Storage) ||
                    !_materials.TryChangeMaterialAmount(deposit.Storage, material, deposit.Amount,
                        deposit.Storage.Comp, localOnly: true))
                {
                    RollbackDeposits(deposits, applied, material);
                    return false;
                }
            }

            return true;
        }
        finally
        {
            ReturnNets(nets);
            deposits.Clear();
            _depositLists.Push(deposits);
        }
    }

    private int GetAvailableSpace(Entity<MaterialStorageComponent> ent, ProtoId<MaterialPrototype> material)
    {
        if (!_materials.IsMaterialWhitelisted(ent.AsNullable(), material))
            return 0;

        long free = ent.Comp.StorageLimit ?? int.MaxValue;
        foreach (var stored in ent.Comp.Storage.Values)
            free -= stored;

        return (int)Math.Clamp(free, 0, int.MaxValue);
    }

    private bool TryGetReservoir(
        MiningPipeDeviceNode node,
        EntityUid source,
        [NotNullWhen(true)] out MaterialStorageComponent? storage)
    {
        storage = null;
        return node.Owner != source && !TerminatingOrDeleted(node.Owner) && !EntityManager.IsQueuedForDeletion(node.Owner) &&
               _memberQuery.TryComp(node.Owner, out var member) && member.SupplyMaterials && member.ReceiveMaterials &&
               member.Node == node.Name && Transform(node.Owner).Anchored && _storageQuery.TryComp(node.Owner, out storage);
    }

    private void RollbackDeposits(List<MaterialDeposit> deposits, int applied, ProtoId<MaterialPrototype> material)
    {
        for (var i = applied - 1; i >= 0; i--)
        {
            var deposit = deposits[i];
            if (TerminatingOrDeleted(deposit.Storage) ||
                !_materials.TryChangeMaterialAmount(deposit.Storage, material, -deposit.Amount,
                    deposit.Storage.Comp, localOnly: true))
                Log.Error($"Unable to roll back {deposit.Amount} units of {material} in {ToPrettyString(deposit.Storage)} after a failed pipe deposit.");
        }
    }

    private readonly record struct MaterialDeposit(Entity<MaterialStorageComponent> Storage, int Amount);
}
