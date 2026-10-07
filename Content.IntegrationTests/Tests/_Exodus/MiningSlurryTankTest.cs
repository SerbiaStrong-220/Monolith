using System.Numerics;
using Content.Server._Exodus.Mining.AutoMining;
using Content.Server._Exodus.Mining.Pipes;
using Content.Server.Construction.Components;
using Content.Server.Lathe;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Construction.Components;
using Content.Shared.Destructible.Thresholds;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

// Arrange power and mining deadlines directly to keep the storage checks deterministic.
#pragma warning disable RA0002

[TestFixture]
public sealed class MiningSlurryTankTest
{
    [TestCase("MiningSlurryTank", 1)]
    [TestCase("MiningSlurryTankLarge", 3)]
    public async Task ConnectedTanksExtendCapacityWithoutPartialDeposits(string prototype, int footprint)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            AddFloor(em, map.Grid, map.Tile.Tile, -1, 10);
            var emitter = em.SpawnEntity("BulkAutoMiningEmitter", new EntityCoordinates(map.Grid, .5f, .5f));
            var first = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, 4.5f, .5f));
            var second = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, 8.5f, .5f));
            var materials = em.System<SharedMaterialStorageSystem>();
            var pipes = em.System<MiningPipeNetSystem>();
            var storage = em.GetComponent<MaterialStorageComponent>(emitter);
            var member = em.GetComponent<MiningPipeNetworkMemberComponent>(emitter);
            var slurry = em.GetComponent<BulkAutoMiningEmitterComponent>(emitter).SlurryMaterial;
            var localCapacity = storage.StorageLimit!.Value;
            var tankCapacity = em.GetComponent<MaterialStorageComponent>(first).StorageLimit!.Value;
            Assert.That(tankCapacity, Is.Positive);
            Assert.That(em.GetComponent<MiningPipeNetworkMemberComponent>(first).ReceiveMaterials, Is.True);
            Assert.That(em.GetComponent<MiningPipeNetworkMemberComponent>(first).SupplyMaterials, Is.True);

            var bounds = em.System<SharedPhysicsSystem>().GetWorldAABB(first);
            Assert.That(bounds.Width, Is.InRange(footprint - .4f, footprint));
            Assert.That(bounds.Height, Is.InRange(footprint - .4f, footprint));
            var machine = em.GetComponent<MachineComponent>(first);
            Assert.That(machine.BoardContainer.ContainedEntities.Count, Is.EqualTo(1));
            var board = em.GetComponent<MachineBoardComponent>(machine.BoardContainer.ContainedEntities[0]);
            Assert.That(board.Prototype.Id, Is.EqualTo(prototype));
            if (footprint == 3)
            {
                var frame = em.SpawnEntity("MachineFrame3x3Centered", new EntityCoordinates(map.Grid, 4.5f, .5f));
                Assert.That(em.System<SharedPhysicsSystem>().GetWorldAABB(frame), Is.EqualTo(bounds),
                    "The matching frame must occupy the same tiles as the constructed tank.");
                Assert.That(em.GetComponent<MachineFrameComponent>(frame).FrameSize, Is.EqualTo(board.FrameSize));
                Assert.That(em.GetComponent<ConstructionComponent>(frame).Graph,
                    Is.EqualTo(em.GetComponent<ConstructionComponent>(first).Graph));
                em.DeleteEntity(frame);
            }

            Assert.That(materials.TryChangeMaterialAmount(emitter, slurry, localCapacity - 1, localOnly: true), Is.True);
            em.System<NodeGroupSystem>().ForceUpdate();
            Assert.That(pipes.GetStorageCapacity((emitter, member)), Is.EqualTo(localCapacity));
            Assert.That(pipes.CanDepositMaterial((emitter, storage), slurry, 2), Is.False,
                "Nearby tanks must require a connecting liquid-metal pipe.");
            Assert.That(pipes.TryDepositMaterial((emitter, storage), slurry, 2), Is.False);
            Assert.That(materials.GetMaterialAmount(emitter, slurry, localOnly: true), Is.EqualTo(localCapacity - 1));

            AddPipes(em, map.Grid, 0, 8);
            em.System<NodeGroupSystem>().ForceUpdate();
            var totalCapacity = localCapacity + tankCapacity * 2;
            Assert.That(pipes.GetStorageCapacity((emitter, member)), Is.EqualTo(totalCapacity));
            Assert.That(pipes.CanDepositMaterial((emitter, storage), slurry, tankCapacity * 2 + 2), Is.False);
            Assert.That(pipes.TryDepositMaterial((emitter, storage), slurry, tankCapacity * 2 + 2), Is.False);
            Assert.That(materials.GetMaterialAmount(emitter, slurry), Is.EqualTo(localCapacity - 1),
                "A rejected batch must leave all three buffers unchanged.");

            Assert.That(pipes.CanDepositMaterial((emitter, storage), slurry, tankCapacity * 2 + 1), Is.True);
            Assert.That(pipes.TryDepositMaterial((emitter, storage), slurry, tankCapacity * 2 + 1), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(materials.GetMaterialAmount(emitter, slurry, localOnly: true), Is.EqualTo(localCapacity));
                Assert.That(materials.GetMaterialAmount(first, slurry, localOnly: true), Is.EqualTo(tankCapacity));
                Assert.That(materials.GetMaterialAmount(second, slurry, localOnly: true), Is.EqualTo(tankCapacity));
                Assert.That(materials.GetMaterialAmount(emitter, slurry), Is.EqualTo(totalCapacity));
                Assert.That(pipes.TryDepositMaterial((emitter, storage), slurry, 1), Is.False);
            });

            Assert.That(materials.TryChangeMaterialAmount(first, slurry, -5, localOnly: true), Is.True);
            Assert.That(pipes.TryDepositMaterial((emitter, storage), slurry, 6), Is.False);
            Assert.That(materials.GetMaterialAmount(emitter, slurry), Is.EqualTo(totalCapacity - 5));
            Assert.That(pipes.TryDepositMaterial((emitter, storage), slurry, 5), Is.True);
            Assert.That(materials.GetMaterialAmount(emitter, slurry), Is.EqualTo(totalCapacity));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DepositsRespectReceiverAvailabilityWhitelistAndSharedMaterialCapacity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            AddFloor(em, map.Grid, map.Tile.Tile, -1, 5);
            var emitter = em.SpawnEntity("BulkAutoMiningEmitter", new EntityCoordinates(map.Grid, .5f, .5f));
            var tank = em.SpawnEntity("MiningSlurryTank", new EntityCoordinates(map.Grid, 4.5f, .5f));
            AddPipes(em, map.Grid, 0, 4);
            em.System<NodeGroupSystem>().ForceUpdate();
            var materials = em.System<SharedMaterialStorageSystem>();
            var pipes = em.System<MiningPipeNetSystem>();
            var source = em.GetComponent<MaterialStorageComponent>(emitter);
            var storage = em.GetComponent<MaterialStorageComponent>(tank);
            var member = em.GetComponent<MiningPipeNetworkMemberComponent>(tank);
            var slurry = em.GetComponent<BulkAutoMiningEmitterComponent>(emitter).SlurryMaterial;
            Assert.That(materials.TryChangeMaterialAmount(emitter, slurry, source.StorageLimit!.Value, localOnly: true), Is.True);

            member.ReceiveMaterials = false;
            Assert.That(pipes.CanDepositMaterial((emitter, source), slurry, 1), Is.False);
            Assert.That(pipes.TryDepositMaterial((emitter, source), slurry, 1), Is.False);
            member.ReceiveMaterials = true;
            Assert.That(pipes.CanDepositMaterial((emitter, source), slurry, 1), Is.True);

            ProtoId<MaterialPrototype> steel = "Steel";
            Assert.That(pipes.CanDepositMaterial((emitter, source), steel, 1), Is.False);
            Assert.That(pipes.TryDepositMaterial((emitter, source), steel, 1), Is.False);
            Assert.That(materials.GetTotalMaterialAmount(tank, localOnly: true), Is.Zero);
            storage.MaterialWhiteList!.Add(steel);
            var capacity = storage.StorageLimit!.Value;
            Assert.That(materials.TryChangeMaterialAmount(tank, steel, capacity - 5, localOnly: true), Is.True);
            Assert.That(pipes.CanDepositMaterial((emitter, source), slurry, 6), Is.False);
            Assert.That(pipes.TryDepositMaterial((emitter, source), slurry, 6), Is.False);
            Assert.That(materials.GetMaterialAmount(tank, slurry, localOnly: true), Is.Zero);
            Assert.That(pipes.TryDepositMaterial((emitter, source), slurry, 5), Is.True);
            Assert.That(materials.GetMaterialAmount(tank, slurry, localOnly: true), Is.EqualTo(5));
            Assert.That(materials.GetMaterialAmount(tank, steel, localOnly: true), Is.EqualTo(capacity - 5));
            Assert.That(materials.GetTotalMaterialAmount(tank, localOnly: true), Is.EqualTo(capacity));

            Assert.That(materials.TryChangeMaterialAmount(tank, steel, -1, localOnly: true), Is.True);
            em.QueueDeleteEntity(tank);
            Assert.That(pipes.CanDepositMaterial((emitter, source), slurry, 1), Is.False);
            Assert.That(pipes.TryDepositMaterial((emitter, source), slurry, 1), Is.False);
            Assert.That(materials.GetMaterialAmount(tank, slurry, localOnly: true), Is.EqualTo(5));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisconnectingTankExcludesCapacityAndContentsUntilReconnected(bool unanchor)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            AddFloor(em, map.Grid, map.Tile.Tile, -1, 6);
            var emitter = em.SpawnEntity("BulkAutoMiningEmitter", new EntityCoordinates(map.Grid, .5f, .5f));
            var tank = em.SpawnEntity("MiningSlurryTankLarge", new EntityCoordinates(map.Grid, 4.5f, .5f));
            AddPipes(em, map.Grid, 0, 1);
            AddPipes(em, map.Grid, 3, 4);
            var connector = em.SpawnEntity("BulkMiningPipe", new EntityCoordinates(map.Grid, 2.5f, .5f));
            var groups = em.System<NodeGroupSystem>();
            groups.ForceUpdate();
            var materials = em.System<SharedMaterialStorageSystem>();
            var pipes = em.System<MiningPipeNetSystem>();
            var storage = em.GetComponent<MaterialStorageComponent>(emitter);
            var member = em.GetComponent<MiningPipeNetworkMemberComponent>(emitter);
            var slurry = em.GetComponent<BulkAutoMiningEmitterComponent>(emitter).SlurryMaterial;
            var localCapacity = storage.StorageLimit!.Value;
            var tankCapacity = em.GetComponent<MaterialStorageComponent>(tank).StorageLimit!.Value;
            Assert.That(pipes.TryDepositMaterial((emitter, storage), slurry, localCapacity + 100), Is.True);
            Assert.That(materials.GetMaterialAmount(tank, slurry, localOnly: true), Is.EqualTo(100));

            var transforms = em.System<SharedTransformSystem>();
            if (unanchor)
                transforms.Unanchor(tank);
            else
                em.DeleteEntity(connector);
            groups.ForceUpdate();
            Assert.Multiple(() =>
            {
                Assert.That(pipes.GetStorageCapacity((emitter, member)), Is.EqualTo(localCapacity));
                Assert.That(materials.GetMaterialAmount(emitter, slurry), Is.EqualTo(localCapacity));
                Assert.That(materials.GetMaterialAmount(tank, slurry, localOnly: true), Is.EqualTo(100));
                Assert.That(pipes.CanDepositMaterial((emitter, storage), slurry, 1), Is.False);
                Assert.That(pipes.TryDepositMaterial((emitter, storage), slurry, 1), Is.False);
            });

            if (unanchor)
                Assert.That(transforms.AnchorEntity(tank), Is.True);
            else
                em.SpawnEntity("BulkMiningPipe", new EntityCoordinates(map.Grid, 2.5f, .5f));
            groups.ForceUpdate();
            Assert.That(pipes.GetStorageCapacity((emitter, member)), Is.EqualTo(localCapacity + tankCapacity));
            Assert.That(materials.GetMaterialAmount(emitter, slurry), Is.EqualTo(localCapacity + 100));
            Assert.That(pipes.TryDepositMaterial((emitter, storage), slurry, 1), Is.True);
            Assert.That(materials.GetMaterialAmount(tank, slurry, localOnly: true), Is.EqualTo(101));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RefineryConsumesTankContentsAndCanFillItsLocalBuffer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            AddFloor(em, map.Grid, map.Tile.Tile, -1, 6);
            var refinery = em.SpawnEntity("BulkMiningRefinery", new EntityCoordinates(map.Grid, .5f, .5f));
            var tank = em.SpawnEntity("MiningSlurryTank", new EntityCoordinates(map.Grid, 4.5f, .5f));
            AddPipes(em, map.Grid, 0, 4);
            em.System<NodeGroupSystem>().ForceUpdate();
            var materials = em.System<SharedMaterialStorageSystem>();
            var slurry = em.GetComponent<MiningRefineryComponent>(refinery).SlurryMaterial;
            const int stored = 1000;
            Assert.That(materials.TryChangeMaterialAmount(tank, slurry, stored, localOnly: true), Is.True);
            Assert.That(materials.GetMaterialAmount(refinery, slurry, localOnly: true), Is.Zero);
            Assert.That(materials.GetMaterialAmount(refinery, slurry), Is.EqualTo(stored));

            var lathes = em.System<LatheSystem>();
            var lathe = em.GetComponent<LatheComponent>(refinery);
            ProtoId<LatheRecipePrototype> recipeId = "BulkMiningSteelOre";
            var recipe = pair.Server.ProtoMan.Index(recipeId);
            var cost = SharedLatheSystem.AdjustMaterial(recipe.Materials[slurry], recipe.MaterialDiscountScale,
                lathe.FinalMaterialUseMultiplier);
            em.GetComponent<ApcPowerReceiverComponent>(refinery).Powered = true;
            Assert.That(lathes.TryAddToQueue(refinery, recipe, 1), Is.True);
            Assert.That(lathes.TryStartProducing(refinery), Is.True);
            Assert.That(materials.GetMaterialAmount(tank, slurry, localOnly: true), Is.EqualTo(stored - cost));
            Assert.That(materials.GetMaterialAmount(refinery, slurry, localOnly: true), Is.Zero);

            var member = em.GetComponent<MiningPipeNetworkMemberComponent>(refinery);
            Assert.That(em.System<MiningPipeNetSystem>().FillBuffer((refinery, member), slurry), Is.EqualTo(stored - cost));
            Assert.That(materials.GetMaterialAmount(tank, slurry, localOnly: true), Is.Zero);
            Assert.That(materials.GetMaterialAmount(refinery, slurry), Is.EqualTo(stored - cost));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FullEmitterMinesIntoTankAndPausesWithoutDestroyingUnpaidTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        Entity<BulkAutoMiningConsoleComponent> console = default;
        Entity<BulkAutoMiningEmitterComponent> emitter = default;
        Entity<MapGridComponent> target = default;
        EntityUid tank = default;
        await pair.Server.WaitAssertion(() =>
        {
            AddFloor(em, map.Grid, map.Tile.Tile, -9, 1);
            var consoleUid = em.SpawnEntity("BulkAutoMiningConsole", new EntityCoordinates(map.Grid, -3.5f, .5f));
            console = (consoleUid, em.GetComponent<BulkAutoMiningConsoleComponent>(consoleUid));
            console.Comp.MaxRange = 64;
            console.Comp.TilesPerTick = 1;
            console.Comp.ProcessInterval = TimeSpan.FromSeconds(10);
            var emitterUid = em.SpawnEntity("BulkAutoMiningEmitter", map.GridCoords);
            emitter = (emitterUid, em.GetComponent<BulkAutoMiningEmitterComponent>(emitterUid));
            emitter.Comp.SlurryPerTile = new MinMax(10, 10);
            emitter.Comp.MaxWarmupYieldBonus = 0;
            tank = em.SpawnEntity("MiningSlurryTank", new EntityCoordinates(map.Grid, -7.5f, .5f));
            AddPipes(em, map.Grid, -8, 0);
            em.System<NodeGroupSystem>().ForceUpdate();
            var storage = em.GetComponent<MaterialStorageComponent>(emitter);
            Assert.That(em.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(emitter,
                emitter.Comp.SlurryMaterial, storage.StorageLimit!.Value, localOnly: true), Is.True);
            var power = em.System<PowerReceiverSystem>();
            power.SetNeedsPower(console, false);
            power.SetNeedsPower(emitter, false);

            target = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            target.Comp.CanSplit = false;
            em.System<SharedTransformSystem>().SetWorldPosition(target, new Vector2(20, 0));
            var maps = em.System<SharedMapSystem>();
            var lattice = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId);
            for (var x = 0; x < 4; x++)
                maps.SetTile(target, target.Comp, new Vector2i(x, 0), lattice);
            em.AddComponent<BulkMiningDepositComponent>(target);
            var generated = new BulkMiningDepositGeneratedEvent();
            em.EventBus.RaiseLocalEvent(target, ref generated);
        });
        await PoolManager.WaitUntil(pair.Server, () =>
            em.GetComponent<ApcPowerReceiverComponent>(console).Powered && em.GetComponent<ApcPowerReceiverComponent>(emitter).Powered);
        await pair.Server.WaitAssertion(() =>
        {
            var mining = em.System<BulkAutoMiningSystem>();
            var materials = em.System<SharedMaterialStorageSystem>();
            var slurry = emitter.Comp.SlurryMaterial;
            Assert.That(mining.TrySelectGrid(console, target), Is.True);
            Assert.That(mining.TryStartMining(console), Is.True, "A full laser must use its connected tank.");
            Assert.That(console.Comp.ProcessedTiles, Is.EqualTo(1));
            Assert.That(materials.GetMaterialAmount(tank, slurry, localOnly: true), Is.EqualTo(10));

            var tankCapacity = em.GetComponent<MaterialStorageComponent>(tank).StorageLimit!.Value;
            Assert.That(materials.TryChangeMaterialAmount(tank, slurry, tankCapacity - 10, localOnly: true), Is.True);
            var total = materials.GetMaterialAmount(emitter, slurry);
            var job = em.GetComponent<BulkAutoMiningJobComponent>(console);
            emitter.Comp.NextMiningTime = TimeSpan.Zero;
            job.NextProcessTime = TimeSpan.Zero;
            mining.Update(0);
            Assert.That(console.Comp.ProcessedTiles, Is.EqualTo(1));
            Assert.That(job.Statuses[emitter], Is.EqualTo(BulkAutoMiningLaserStatus.Full));
            Assert.That(materials.GetMaterialAmount(emitter, slurry), Is.EqualTo(total));
            var remaining = 0;
            for (var x = 0; x < 4; x++)
            {
                if (!em.System<SharedMapSystem>().GetTileRef(target, target.Comp, new Vector2i(x, 0)).Tile.IsEmpty)
                    remaining++;
            }
            Assert.That(remaining, Is.EqualTo(3), "A full network must leave unpaid terrain intact.");

            Assert.That(materials.TryChangeMaterialAmount(tank, slurry, -10, localOnly: true), Is.True);
            emitter.Comp.NextMiningTime = TimeSpan.Zero;
            emitter.Comp.NextTargetSearchTime = TimeSpan.Zero;
            job.NextProcessTime = TimeSpan.Zero;
            mining.Update(0);
            Assert.That(console.Comp.ProcessedTiles, Is.EqualTo(2));
            Assert.That(materials.GetMaterialAmount(emitter, slurry), Is.EqualTo(total));
            mining.StopMining(console);
        });
        await pair.CleanReturnAsync();
    }

    private static void AddFloor(IEntityManager em, Entity<MapGridComponent> grid, Tile tile, int first, int last)
    {
        var maps = em.System<SharedMapSystem>();
        for (var x = first; x <= last; x++)
        {
            for (var y = -2; y <= 2; y++)
                maps.SetTile(grid, grid.Comp, new Vector2i(x, y), tile);
        }
    }

    private static void AddPipes(IEntityManager em, EntityUid grid, int first, int last)
    {
        for (var x = first; x <= last; x++)
            em.SpawnEntity("BulkMiningPipe", new EntityCoordinates(grid, x + .5f, .5f));
    }
}

#pragma warning restore RA0002
