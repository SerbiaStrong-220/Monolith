using System.Diagnostics.CodeAnalysis;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server._Exodus.Mining.Pipes.NodeGroups;
using Content.Server._Exodus.Mining.Pipes.Nodes;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared._Exodus.Materials;
using Content.Shared.Materials;
using Content.Shared.NodeContainer;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Mining.Pipes;

public sealed partial class MiningPipeNetSystem : EntitySystem
{
    [Dependency] private SharedMaterialStorageSystem _materials = default!;

    private EntityQuery<MiningPipeNetworkMemberComponent> _memberQuery;
    private EntityQuery<MaterialStorageComponent> _storageQuery;

    public override void Initialize()
    {
        base.Initialize();
        _memberQuery = GetEntityQuery<MiningPipeNetworkMemberComponent>();
        _storageQuery = GetEntityQuery<MaterialStorageComponent>();
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, GetStoredMaterialsEvent>(OnGetStoredMaterials);
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, ConsumeStoredMaterialsEvent>(OnConsumeStoredMaterials);
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, MaterialAmountChangedEvent>(OnMaterialsChanged);
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, MaterialStorageCapacityChangedEvent>(OnCapacityChanged);
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, NodeGroupsRebuilt>(OnNodesRebuilt);
    }

    private void OnMaterialsChanged(Entity<MiningPipeNetworkMemberComponent> ent, ref MaterialAmountChangedEvent args)
    {
        InvalidateClientMaterials(ent);
    }

    private void OnCapacityChanged(Entity<MiningPipeNetworkMemberComponent> ent, ref MaterialStorageCapacityChangedEvent args)
    {
        InvalidateClientMaterials(ent);
    }

    private void OnNodesRebuilt(Entity<MiningPipeNetworkMemberComponent> ent, ref NodeGroupsRebuilt args)
    {
        // Every device in each rebuilt group receives this event, including separated buffers.
        ent.Comp.ClientMaterialsDirty = true;
    }

    private void InvalidateClientMaterials(Entity<MiningPipeNetworkMemberComponent> ent)
    {
        ent.Comp.ClientMaterialsDirty = true;
        if (!ent.Comp.SupplyMaterials || GetNetwork(ent) is not { } net)
            return;

        foreach (var node in net.Nodes)
        {
            if (node is MiningPipeDeviceNode && _memberQuery.TryComp(node.Owner, out var member) && member.Node == node.Name)
                member.ClientMaterialsDirty = true;
        }
    }

    private MiningPipeNet? GetNetwork(Entity<MiningPipeNetworkMemberComponent> ent)
    {
        if (!Transform(ent).Anchored ||
            !TryComp<NodeContainerComponent>(ent, out var container) ||
            !container.Nodes.TryGetValue(ent.Comp.Node, out var node) ||
            node.NodeGroup is not MiningPipeNet { Removed: false, Remaking: false } net)
            return null;

        return net;
    }

    private bool TryGetBuffer(MiningPipeDeviceNode node, EntityUid receiver, [NotNullWhen(true)] out MaterialStorageComponent? storage)
    {
        storage = default!;
        return node.Owner != receiver && !TerminatingOrDeleted(node.Owner) &&
               _memberQuery.TryComp(node.Owner, out var member) && member.SupplyMaterials && member.Node == node.Name &&
               Transform(node.Owner).Anchored && _storageQuery.TryComp(node.Owner, out storage);
    }

    /// <summary>Fill the local buffer from connected suppliers, without exceeding its total capacity.</summary>
    public int FillBuffer(Entity<MiningPipeNetworkMemberComponent> ent, ProtoId<MaterialPrototype> material)
    {
        if (!_storageQuery.TryComp(ent, out var receiver) ||
            !_materials.IsMaterialWhitelisted((ent, receiver), material))
            return 0;

        long free = receiver.StorageLimit ?? int.MaxValue;
        foreach (var amount in receiver.Storage.Values)
            free -= amount;

        if (free <= 0 || GetNetwork(ent) is not { } net)
            return 0;

        var transferred = 0;
        foreach (var node in net.Nodes)
        {
            if (free <= 0 || TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent))
                break;

            if (node is not MiningPipeDeviceNode device || !TryGetBuffer(device, ent, out var source))
                continue;

            var amount = (int)Math.Min(free, source.Storage.GetValueOrDefault(material));
            if (amount <= 0 || !_materials.CanChangeMaterialAmount(ent, material, amount, receiver, localOnly: true) ||
                !_materials.TryChangeMaterialAmount(device.Owner, material, -amount, source, localOnly: true))
                continue;

            // Debit first so material-change callbacks never observe the same contents in both buffers.
            if (!_materials.TryChangeMaterialAmount(ent, material, amount, receiver, localOnly: true))
            {
                if (!_materials.TryChangeMaterialAmount(device.Owner, material, amount, source, localOnly: true))
                    Log.Error($"Unable to return {amount} units of {material} to {ToPrettyString(device.Owner)} after a failed pipe transfer.");
                break;
            }

            free -= amount;
            transferred += amount;
        }

        return transferred;
    }

    private void OnGetStoredMaterials(Entity<MiningPipeNetworkMemberComponent> ent, ref GetStoredMaterialsEvent args)
    {
        if (args.LocalOnly || GetNetwork(ent) is not { } net)
            return;

        foreach (var node in net.Nodes)
        {
            if (node is not MiningPipeDeviceNode device || !TryGetBuffer(device, ent, out var storage))
                continue;

            foreach (var (material, amount) in storage.Storage)
                args.Materials[material] = (int)Math.Min(int.MaxValue, (long)args.Materials.GetValueOrDefault(material) + amount);
        }
    }

    public bool UpdateClientMaterials(Entity<MiningPipeNetworkMemberComponent> ent)
    {
        ent.Comp.ClientMaterialsDirty = false;
        if (!_storageQuery.TryComp(ent, out var storage))
            return false;

        var snapshot = new Dictionary<ProtoId<MaterialPrototype>, int>();
        var ev = new GetStoredMaterialsEvent((ent, storage), snapshot, false);
        OnGetStoredMaterials(ent, ref ev);
        var changed = snapshot.Count != ent.Comp.RemoteMaterials.Count;
        foreach (var (material, amount) in snapshot)
            changed |= ent.Comp.RemoteMaterials.GetValueOrDefault(material) != amount;

        if (!changed)
            return false;

        ent.Comp.RemoteMaterials = snapshot;
        Dirty(ent);
        return true;
    }

    /// <summary>The capacity of the same local and remote buffers exposed through material storage.</summary>
    public int? GetStorageCapacity(Entity<MiningPipeNetworkMemberComponent> ent)
    {
        if (!_storageQuery.TryComp(ent, out var storage))
            return 0;

        if (storage.StorageLimit is not { } localLimit)
            return null;

        long capacity = localLimit;
        if (GetNetwork(ent) is not { } net)
            return localLimit;

        foreach (var node in net.Nodes)
        {
            if (node is not MiningPipeDeviceNode device || !TryGetBuffer(device, ent, out var remote))
                continue;

            if (remote.StorageLimit is not { } limit)
                return null;

            capacity += limit;
        }

        return (int)Math.Clamp(capacity, 0, int.MaxValue);
    }

    private void OnConsumeStoredMaterials(Entity<MiningPipeNetworkMemberComponent> ent, ref ConsumeStoredMaterialsEvent args)
    {
        if (args.LocalOnly || GetNetwork(ent) is not { } net)
            return;

        // Deltas are signed. Deposits stay local; withdrawals use the receiver's own buffer first.
        foreach (var (material, change) in args.Materials)
        {
            if (change >= 0)
                continue;

            var needed = -(long)change - args.Entity.Comp.Storage.GetValueOrDefault(material);
            foreach (var node in net.Nodes)
            {
                if (needed <= 0)
                    break;

                if (node is not MiningPipeDeviceNode device || !TryGetBuffer(device, ent, out var storage))
                    continue;

                var taken = (int)Math.Min(needed, storage.Storage.GetValueOrDefault(material));
                if (taken <= 0 || !_materials.TryChangeMaterialAmount(device.Owner, material, -taken, storage, localOnly: true))
                    continue;

                needed -= taken;
                args.Materials[material] += taken;
            }
        }
    }
}
