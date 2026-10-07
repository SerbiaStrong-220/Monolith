using System.Diagnostics.CodeAnalysis;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server._Exodus.Mining.Pipes.Components;
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
    private EntityQuery<MiningPipeBridgeComponent> _bridgeQuery;

    // Material callbacks re-enter this system while a transfer walks its networks; each walk rents its own list.
    private readonly Stack<List<MiningPipeNet>> _netLists = new();
    private readonly HashSet<EntityUid> _gridScratch = new();

    public override void Initialize()
    {
        base.Initialize();
        _memberQuery = GetEntityQuery<MiningPipeNetworkMemberComponent>();
        _storageQuery = GetEntityQuery<MaterialStorageComponent>();
        _bridgeQuery = GetEntityQuery<MiningPipeBridgeComponent>();
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, GetStoredMaterialsEvent>(OnGetStoredMaterials);
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, ConsumeStoredMaterialsEvent>(OnConsumeStoredMaterials);
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, MaterialAmountChangedEvent>(OnMaterialsChanged);
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, MaterialStorageCapacityChangedEvent>(OnCapacityChanged);
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, NodeGroupsRebuilt>(OnNodesRebuilt);
        InitializeStorage();
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
        // A rebuilt group may change what a bridged partner network can reach.
        if (_bridgeQuery.HasComp(ent))
            InvalidateJoinedNetworks(ent);
    }

    private void InvalidateClientMaterials(Entity<MiningPipeNetworkMemberComponent> ent)
    {
        ent.Comp.ClientMaterialsDirty = true;
        if (ent.Comp.SupplyMaterials)
            InvalidateJoinedNetworks(ent);
    }

    /// <summary>Marks every member of the networks joined with this device for a new client snapshot.</summary>
    public void InvalidateJoinedNetworks(EntityUid device)
    {
        if (!_memberQuery.TryComp(device, out var member) || GetNetwork((device, member)) is not { } start)
            return;

        var nets = RentJoinedNetworks(start);
        foreach (var net in nets)
        {
            foreach (var node in net.Nodes)
            {
                if (node is MiningPipeDeviceNode && _memberQuery.TryComp(node.Owner, out var other) && other.Node == node.Name)
                    other.ClientMaterialsDirty = true;
            }
        }

        ReturnNets(nets);
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

    /// <summary>
    /// The network of a device plus every network joined to it through bridges, each exactly once, so
    /// suppliers are never counted twice. Return the list with <see cref="ReturnNets"/>.
    /// </summary>
    private List<MiningPipeNet> RentJoinedNetworks(MiningPipeNet start)
    {
        var nets = _netLists.Count > 0 ? _netLists.Pop() : new List<MiningPipeNet>();
        nets.Add(start);
        for (var i = 0; i < nets.Count; i++)
        {
            foreach (var node in nets[i].Nodes)
            {
                if (node is not MiningPipeDeviceNode || !_bridgeQuery.TryComp(node.Owner, out var bridge) ||
                    bridge.Partner is not { } partner || TerminatingOrDeleted(partner) ||
                    !_memberQuery.TryComp(partner, out var partnerMember) ||
                    GetNetwork((partner, partnerMember)) is not { } joined || nets.Contains(joined))
                    continue;

                nets.Add(joined);
            }
        }

        return nets;
    }

    private void ReturnNets(List<MiningPipeNet> nets)
    {
        nets.Clear();
        _netLists.Push(nets);
    }

    private bool TryGetBuffer(MiningPipeDeviceNode node, EntityUid receiver, [NotNullWhen(true)] out MaterialStorageComponent? storage)
    {
        storage = default!;
        return node.Owner != receiver && !TerminatingOrDeleted(node.Owner) &&
               _memberQuery.TryComp(node.Owner, out var member) && member.SupplyMaterials && member.Node == node.Name &&
               Transform(node.Owner).Anchored && _storageQuery.TryComp(node.Owner, out storage);
    }

    /// <summary>Number of distinct ships whose networks are joined with this device's network, including its own.</summary>
    public int CountJoinedShips(Entity<MiningPipeNetworkMemberComponent> ent)
    {
        if (GetNetwork(ent) is not { } start)
            return 1;

        var nets = RentJoinedNetworks(start);
        _gridScratch.Clear();
        foreach (var net in nets)
        {
            // Mining ducts never cross grids, so any node locates the whole network.
            foreach (var node in net.Nodes)
            {
                if (Transform(node.Owner).GridUid is { } grid)
                    _gridScratch.Add(grid);

                break;
            }
        }

        ReturnNets(nets);
        return Math.Max(1, _gridScratch.Count);
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

        if (free <= 0 || GetNetwork(ent) is not { } start)
            return 0;

        var transferred = 0;
        var nets = RentJoinedNetworks(start);
        foreach (var net in nets)
        {
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
                    free = 0;
                    break;
                }

                free -= amount;
                transferred += amount;
            }
        }

        ReturnNets(nets);
        return transferred;
    }

    private void OnGetStoredMaterials(Entity<MiningPipeNetworkMemberComponent> ent, ref GetStoredMaterialsEvent args)
    {
        if (args.LocalOnly || GetNetwork(ent) is not { } start)
            return;

        var nets = RentJoinedNetworks(start);
        foreach (var net in nets)
        {
            foreach (var node in net.Nodes)
            {
                if (node is not MiningPipeDeviceNode device || !TryGetBuffer(device, ent, out var storage))
                    continue;

                foreach (var (material, amount) in storage.Storage)
                    args.Materials[material] = (int)Math.Min(int.MaxValue, (long)args.Materials.GetValueOrDefault(material) + amount);
            }
        }

        ReturnNets(nets);
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

        if (GetNetwork(ent) is not { } start)
            return localLimit;

        long capacity = localLimit;
        var unlimited = false;
        var nets = RentJoinedNetworks(start);
        foreach (var net in nets)
        {
            foreach (var node in net.Nodes)
            {
                if (node is not MiningPipeDeviceNode device || !TryGetBuffer(device, ent, out var remote))
                    continue;

                if (remote.StorageLimit is not { } limit)
                {
                    unlimited = true;
                    break;
                }

                capacity += limit;
            }

            if (unlimited)
                break;
        }

        ReturnNets(nets);
        return unlimited ? null : (int)Math.Clamp(capacity, 0, int.MaxValue);
    }

    private void OnConsumeStoredMaterials(Entity<MiningPipeNetworkMemberComponent> ent, ref ConsumeStoredMaterialsEvent args)
    {
        if (args.LocalOnly || GetNetwork(ent) is not { } start)
            return;

        var nets = RentJoinedNetworks(start);
        // Deltas are signed. Deposits stay local; withdrawals use the receiver's own buffer first.
        foreach (var (material, change) in args.Materials)
        {
            if (change >= 0)
                continue;

            var needed = -(long)change - args.Entity.Comp.Storage.GetValueOrDefault(material);
            foreach (var net in nets)
            {
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

        ReturnNets(nets);
    }
}
