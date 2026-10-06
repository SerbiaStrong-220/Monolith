using Content.Server.Power.Components;
using Content.Shared.Interaction.Components;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Whitelist;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.OreMagnet;

public sealed partial class OreMagnetDepositSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMaterialStorageSystem _materialStorage = default!;
    [Dependency] private SharedOreSiloSystem _oreSilo = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    private static readonly TimeSpan ScanDelay = TimeSpan.FromSeconds(2);
    private const int MaxItemsPerPass = 30;

    private TimeSpan _nextScan;
    private readonly List<EntityUid> _toDeposit = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextScan)
            return;
        _nextScan = _timing.CurTime + ScanDelay;

        var query = EntityQueryEnumerator<OreMagnetComponent, StorageComponent, MaterialStorageComponent, OreSiloClientComponent>();
        while (query.MoveNext(out var uid, out _, out var storage, out var matStorage, out var client))
        {
            if (client.Silo is not { } siloUid || Deleted(siloUid))
                continue;

            if (!TryComp<ApcPowerReceiverComponent>(uid, out var power) || !power.Powered)
                continue;

            if (!TryComp<OreSiloComponent>(siloUid, out var siloComp))
                continue;

            if (!_oreSilo.CanTransmitMaterials((siloUid, siloComp), uid))
                continue;

            DepositStoredOre(uid, storage, matStorage, siloUid);
        }
    }

    private void DepositStoredOre(EntityUid uid, StorageComponent storage, MaterialStorageComponent matStorage, EntityUid silo)
    {
        _toDeposit.Clear();
        foreach (var item in storage.Container.ContainedEntities)
        {
            if (_toDeposit.Count >= MaxItemsPerPass)
                break;

            if (Deleted(item))
                continue;

            if (!HasComp<MaterialComponent>(item)
                || !TryComp<PhysicalCompositionComponent>(item, out _)
                || HasComp<UnremoveableComponent>(item))
                continue;

            if (_whitelist.IsWhitelistFail(matStorage.Whitelist, item))
                continue;

            _toDeposit.Add(item);
        }

        foreach (var item in _toDeposit)
        {
            if (Deleted(item))
                continue;

            if (!TryComp<PhysicalCompositionComponent>(item, out var composition))
                continue;

            var multiplier = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
            var fits = true;
            var totalVolume = 0;
            foreach (var (mat, vol) in composition.MaterialComposition)
            {
                if (!_materialStorage.CanChangeMaterialAmount(uid, mat, vol * multiplier, matStorage))
                {
                    fits = false;
                    break;
                }
                totalVolume += vol * multiplier;
            }

            if (!fits || !_materialStorage.CanTakeVolume(uid, totalVolume, matStorage))
                continue;

            foreach (var (mat, vol) in composition.MaterialComposition)
                _materialStorage.TryChangeMaterialAmount(uid, mat, vol * multiplier, matStorage);

            Del(item);
        }
        _toDeposit.Clear();

        // Push everything into the linked silo, mirroring the link-time transfer.
        var stored = _materialStorage.GetStoredMaterials(uid, true);
        if (stored.Count == 0)
            return;

        var inverseMats = new Dictionary<string, int>();
        foreach (var (mat, amount) in stored)
            inverseMats.Add(mat, -amount);

        if (!_materialStorage.TryChangeMaterialAmount(silo, stored))
            return;
        _materialStorage.TryChangeMaterialAmount(uid, inverseMats, localOnly: true);
    }
}
