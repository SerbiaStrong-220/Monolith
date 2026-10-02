using Content.Server.Materials;
using Content.Server.Spawners.Components;
using Content.Shared._Goobstation.ItemMiner;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Goobstation.ItemMiner;

public sealed partial class ItemMinerSystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private MaterialStorageSystem _materialStorage = default!;

    private readonly Dictionary<string, int> _minedMatsBuffer = new();

    private bool TryStoreMinedMaterials(EntityUid uid, ItemMinerComponent miner, EntProtoId proto)
    {
        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return false;

        var targetProto = ResolveMinedPrototype(proto);
        if (targetProto == null)
            return true;

        if (!_prototype.TryIndex<EntityPrototype>(targetProto.Value, out var entProto))
            return false;

        if (!entProto.TryGetComponent<PhysicalCompositionComponent>(out var composition, EntityManager.ComponentFactory))
            return false;

        if (composition.MaterialComposition.Count == 0)
            return false;

        var count = entProto.TryGetComponent<StackComponent>(out var stack, EntityManager.ComponentFactory) ? stack.Count : 1;

        _minedMatsBuffer.Clear();
        foreach (var (mat, vol) in composition.MaterialComposition)
        {
            _minedMatsBuffer[mat] = vol * count;
        }

        if (!_materialStorage.CanChangeMaterialAmount((uid, storage), _minedMatsBuffer))
            return true;

        _materialStorage.TryChangeMaterialAmount((uid, storage), _minedMatsBuffer);

        if (miner.MinedSound != null)
            _audio.PlayPvs(miner.MinedSound, uid);

        return true;
    }

    private EntProtoId? ResolveMinedPrototype(EntProtoId proto)
    {
        var current = proto;
        for (var i = 0; i < 5; i++)
        {
            if (!_prototype.TryIndex<EntityPrototype>(current, out var entProto))
                return null;

            if (entProto.TryGetComponent<RandomSpawnerComponent>(out var randSpawner, EntityManager.ComponentFactory))
            {
                if (randSpawner.RarePrototypes.Count > 0 && (randSpawner.RareChance == 1.0f || _gambling.Prob(randSpawner.RareChance)))
                {
                    current = _gambling.Pick(randSpawner.RarePrototypes);
                    continue;
                }

                if (randSpawner.Chance != 1.0f && !_gambling.Prob(randSpawner.Chance))
                    return null;

                if (randSpawner.Prototypes.Count == 0)
                    return null;

                current = _gambling.Pick(randSpawner.Prototypes);
                continue;
            }

            if (entProto.TryGetComponent<ConditionalSpawnerComponent>(out var condSpawner, EntityManager.ComponentFactory))
            {
                if (condSpawner.Chance != 1.0f && !_gambling.Prob(condSpawner.Chance))
                    return null;

                if (condSpawner.Prototypes.Count == 0)
                    return null;

                current = _gambling.Pick(condSpawner.Prototypes);
                continue;
            }

            return current;
        }

        return current;
    }
}
