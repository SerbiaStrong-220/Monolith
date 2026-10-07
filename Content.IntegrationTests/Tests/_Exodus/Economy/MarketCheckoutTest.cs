// (c) Space Exodus Team - EXDS-RL with CLA
using System.Linq;
using Content.Server._Exodus.Economy;
using Content.Server.Construction.Components;
using Content.Server._NF.CrateMachine;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Systems;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Exodus.CCVar;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.CrateMachine.Components;
using Content.Shared._NF.Market;
using Content.Shared._NF.Market.BUI;
using Content.Shared._NF.Market.Components;
using Content.Shared._NF.Market.Events;
using Content.Shared.Preferences;
using Content.Shared.Storage.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketCheckoutTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusCheckoutItem
  parent: BaseItem
  components:
  - type: StaticPrice
    price: 100

- type: entity
  id: ExodusCheckoutCrate
  parent: CrateGenericSteel
  components:
  - type: StaticPrice
    price: 1000

- type: entity
  id: ExodusCheckoutMachine
  parent: CrateMachine
  components:
  - type: CrateMachine
    cratePrototype: ExodusCheckoutCrate

- type: entity
  id: ExodusCheckoutConsole
  components:
  - type: StationTracker
  - type: MarketConsole
    transactionCost: 600
  - type: UserInterface
    interfaces:
      enum.MarketConsoleUiKey.Default:
        type: MarketConsoleBoundUserInterface
";

    [TestCase("ExodusCheckoutItem")]
    [TestCase("ShieldGeneratorCdm")]
    [TestCase("MobShipRepairDrone")]
    public async Task BankBackedCheckoutChargesQuoteAndDeliversExactContentsOnce(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var market = entities.System<DynamicMarketSystem>();
        var inventory = entities.System<MarketInventorySystem>();
        var map = await pair.CreateTestMap();
        var player = pair.Player!;
        var previousActor = player.AttachedEntity;
        var originalProfile = (HumanoidCharacterProfile)preferences.GetPreferences(player.UserId).SelectedCharacter!;
        var originalSlot = preferences.GetPreferences(player.UserId).SelectedCharacterIndex;
        var enabled = cfg.GetCVar(EXCVars.DynamicMarketEnabled);
        var persist = cfg.GetCVar(EXCVars.DynamicMarketPersist);
        EntityUid? station = null;
        var profileUpdate = Task.CompletedTask;
        try
        {
            await server.WaitPost(() => profileUpdate = preferences.SetProfile(player.UserId, originalSlot,
                originalProfile.WithBankBalance(1000000)));
            await profileUpdate;
            await server.WaitAssertion(() =>
            {
                cfg.SetCVar(EXCVars.DynamicMarketPersist, false);
                cfg.SetCVar(EXCVars.DynamicMarketEnabled, true);
                market.ResetAll();
                inventory.Clear();
                entities.System<SharedMapSystem>().SetTile(map.Grid, new Vector2i(1, 0), map.Tile.Tile);
                station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
                var console = entities.SpawnEntity("ExodusCheckoutConsole", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                var machine = entities.SpawnEntity("ExodusCheckoutMachine", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
                entities.System<StationSystem>().SetStation(console, station.Value);
                entities.AddComponent<CargoMarketDataComponent>(station.Value);
                Assert.That(inventory.TryAddStock(prototype, 5, 100), Is.True);
                var buyer = entities.SpawnEntity(null, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                var bank = entities.AddComponent<BankAccountComponent>(buyer);
                server.PlayerMan.SetAttachedEntity(player, buyer);
                Assert.That(player.AttachedEntity, Is.EqualTo(buyer));
                Assert.That(bank.Balance, Is.EqualTo(1000000), "PlayerAttached must load the actual profile balance.");

                entities.EventBus.RaiseLocalEvent(console,
                    new MarketConsoleCartMessage(2, prototype) { Actor = buyer });
                var ui = entities.System<UserInterfaceSystem>();
                Assert.That(ui.TryGetUiState<MarketConsoleInterfaceState>(console, MarketConsoleUiKey.Default, out var state), Is.True);
                Assert.That(state!.CanPurchase, Is.True);
                var total = checked(state.CartBalance + state.TransactionCost);
                entities.EventBus.RaiseLocalEvent(console, new MarketPurchaseMessage(total) { Actor = buyer });

                var cart = entities.GetComponent<MarketConsoleComponent>(console);
                var spawner = entities.GetComponent<MarketItemSpawnerComponent>(machine);
                var itemKey = market.GetMarketKeyFromPrototype(prototype);
                var crateKey = market.GetMarketKeyFromPrototype("ExodusCheckoutCrate");
                var itemFactor = market.GetFactor(itemKey);
                var crateFactor = market.GetFactor(crateKey);
                Assert.Multiple(() =>
                {
                    Assert.That(bank.Balance, Is.EqualTo(1000000 - total));
                    Assert.That(((HumanoidCharacterProfile)preferences.GetPreferences(player.UserId).SelectedCharacter!).BankBalance,
                        Is.EqualTo(bank.Balance), "Checkout must update the real profile, not only the component.");
                    Assert.That(cart.CartDataList, Is.Empty);
                    Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(3));
                    Assert.That(spawner.ItemsToSpawn, Has.Count.EqualTo(1));
                    Assert.That(spawner.ItemsToSpawn[0].Quantity, Is.EqualTo(2));
                    Assert.That(entities.GetComponent<CrateMachineComponent>(machine).OpeningTimeRemaining, Is.GreaterThan(0));
                    Assert.That(itemFactor, Is.GreaterThan(1));
                    Assert.That(crateFactor, Is.GreaterThan(1));
                });
                Assert.That(ui.TryGetUiState<MarketConsoleInterfaceState>(console, MarketConsoleUiKey.Default, out var paidState), Is.True);
                Assert.That(paidState!.CanPurchase, Is.False);
                Assert.That(paidState.CartDataList, Is.Empty);
                Assert.That(paidState.Balance, Is.EqualTo(bank.Balance));

                // A repeated checkout during delivery must not debit or apply demand a second time.
                entities.EventBus.RaiseLocalEvent(console, new MarketPurchaseMessage(total) { Actor = buyer });
                Assert.That(bank.Balance, Is.EqualTo(1000000 - total));
                Assert.That(market.GetFactor(itemKey), Is.EqualTo(itemFactor));
                Assert.That(market.GetFactor(crateKey), Is.EqualTo(crateFactor));

                // Exercise the actual animation-completion delivery handler without requiring a power network.
                entities.EventBus.RaiseLocalEvent(machine, new CrateMachineOpenedEvent(machine));
                EntityUid? crate = null;
                var children = entities.GetComponent<TransformComponent>(map.Grid).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    if (entities.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID != "ExodusCheckoutCrate")
                        continue;

                    Assert.That(crate, Is.Null, "Only one delivery crate may be spawned.");
                    crate = child;
                }

                Assert.That(crate, Is.Not.Null);
                var storage = entities.GetComponent<EntityStorageComponent>(crate!.Value);
                Assert.That(storage.Contents.ContainedEntities, Has.Count.EqualTo(2));
                var contents = storage.Contents.ContainedEntities.ToArray();
                foreach (var item in contents)
                {
                    Assert.That(entities.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID, Is.EqualTo(prototype));
                    Assert.That(entities.GetComponent<TransformComponent>(item).Anchored, Is.False);
                    Assert.That(entities.GetComponent<InsideEntityStorageComponent>(item).Storage, Is.EqualTo(crate.Value));
                    if (entities.TryGetComponent<MachineComponent>(item, out var machineParts))
                    {
                        Assert.That(machineParts.BoardContainer.ContainedEntities, Has.Count.EqualTo(1));
                        Assert.That(machineParts.PartContainer.ContainedEntities, Is.Not.Empty);
                    }
                }

                entities.System<EntityStorageSystem>().OpenStorage(crate.Value, storage);
                foreach (var item in contents)
                {
                    Assert.That(entities.GetComponent<TransformComponent>(item).Anchored, Is.False,
                        "Unpacking a bought structure must not bolt it to the delivery tile.");
                    Assert.That(entities.GetComponent<TransformComponent>(item).ParentUid, Is.EqualTo(map.Grid.Owner));
                }
                Assert.That(spawner.ItemsToSpawn, Is.Empty);
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                profileUpdate = preferences.SetProfile(player.UserId, originalSlot, originalProfile);
                server.PlayerMan.SetAttachedEntity(player, previousActor);
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                if (station != null)
                    entities.DeleteEntity(station.Value);
                market.ResetAll();
                inventory.Clear();
                cfg.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                cfg.SetCVar(EXCVars.DynamicMarketPersist, persist);
            });
            await profileUpdate;
        }
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TwoStationsCompetingForLastItemChargeAndDeliverOnlyOnce()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var configuration = server.ResolveDependency<IConfigurationManager>();
        var inventory = server.System<MarketInventorySystem>();
        var map = await pair.CreateTestMap();
        var player = pair.Player!;
        var previousActor = player.AttachedEntity;
        var originalProfile = (HumanoidCharacterProfile)preferences.GetPreferences(player.UserId).SelectedCharacter!;
        var originalSlot = preferences.GetPreferences(player.UserId).SelectedCharacterIndex;
        var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
        var persist = configuration.GetCVar(EXCVars.DynamicMarketPersist);
        EntityUid? firstStation = null;
        EntityUid? secondStation = null;
        var profileUpdate = Task.CompletedTask;
        try
        {
            await server.WaitPost(() => profileUpdate = preferences.SetProfile(player.UserId, originalSlot,
                originalProfile.WithBankBalance(1000000)));
            await profileUpdate;
            await server.WaitAssertion(() =>
            {
                configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
                inventory.Clear();
                Assert.That(inventory.TryAddStock("ExodusCheckoutItem", 1, 100), Is.True);
                var maps = entities.System<SharedMapSystem>();
                for (var x = 1; x <= 11; x++)
                    maps.SetTile(map.Grid, new Vector2i(x, 0), map.Tile.Tile);
                firstStation = entities.SpawnEntity(null, MapCoordinates.Nullspace);
                secondStation = entities.SpawnEntity(null, MapCoordinates.Nullspace);
                entities.AddComponent<CargoMarketDataComponent>(firstStation.Value);
                entities.AddComponent<CargoMarketDataComponent>(secondStation.Value);
                var firstConsole = entities.SpawnEntity("ExodusCheckoutConsole", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                var secondConsole = entities.SpawnEntity("ExodusCheckoutConsole", new EntityCoordinates(map.Grid, 10.5f, 0.5f));
                var firstMachine = entities.SpawnEntity("ExodusCheckoutMachine", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
                var secondMachine = entities.SpawnEntity("ExodusCheckoutMachine", new EntityCoordinates(map.Grid, 11.5f, 0.5f));
                var stations = entities.System<StationSystem>();
                stations.SetStation(firstConsole, firstStation.Value);
                stations.SetStation(secondConsole, secondStation.Value);
                var buyer = entities.SpawnEntity(null, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                var bank = entities.AddComponent<BankAccountComponent>(buyer);
                server.PlayerMan.SetAttachedEntity(player, buyer);
                Assert.That(bank.Balance, Is.EqualTo(1000000));
                var machines = entities.System<CrateMachineSystem>();
                Assert.That(machines.FindNearestUnoccupied(firstConsole, 8, out var firstNearest), Is.True);
                Assert.That(firstNearest, Is.EqualTo(firstMachine));
                Assert.That(machines.FindNearestUnoccupied(secondConsole, 8, out var secondNearest), Is.True);
                Assert.That(secondNearest, Is.EqualTo(secondMachine));

                entities.EventBus.RaiseLocalEvent(firstConsole,
                    new MarketConsoleCartMessage(1, "ExodusCheckoutItem") { Actor = buyer });
                entities.EventBus.RaiseLocalEvent(secondConsole,
                    new MarketConsoleCartMessage(1, "ExodusCheckoutItem") { Actor = buyer });
                var firstCart = entities.GetComponent<MarketConsoleComponent>(firstConsole);
                var secondCart = entities.GetComponent<MarketConsoleComponent>(secondConsole);
                Assert.Multiple(() =>
                {
                    Assert.That(firstCart.CartDataList, Has.Count.EqualTo(1));
                    Assert.That(secondCart.CartDataList, Has.Count.EqualTo(1));
                    Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(1), "Neither cart may reserve the last item.");
                });

                entities.EventBus.RaiseLocalEvent(firstConsole,
                    new MarketConsoleCartMessage(0, "ExodusCheckoutItem", true) { Actor = buyer });
                Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(1), "Cancelling a cart must not duplicate stock.");
                entities.EventBus.RaiseLocalEvent(firstConsole,
                    new MarketConsoleCartMessage(1, "ExodusCheckoutItem") { Actor = buyer });
                var ui = entities.System<UserInterfaceSystem>();
                Assert.That(ui.TryGetUiState<MarketConsoleInterfaceState>(firstConsole, MarketConsoleUiKey.Default, out var firstState), Is.True);
                Assert.That(ui.TryGetUiState<MarketConsoleInterfaceState>(secondConsole, MarketConsoleUiKey.Default, out var secondState), Is.True);
                Assert.That(firstState!.CanPurchase, Is.True);
                Assert.That(secondState!.CanPurchase, Is.True);
                var total = checked(firstState.CartBalance + firstState.TransactionCost);
                Assert.That(secondState.CartBalance + secondState.TransactionCost, Is.EqualTo(total));

                entities.EventBus.RaiseLocalEvent(firstConsole, new MarketPurchaseMessage(total) { Actor = buyer });
                entities.System<SharedTransformSystem>().SetCoordinates(buyer, new EntityCoordinates(map.Grid, 10.5f, 0.5f));
                entities.EventBus.RaiseLocalEvent(secondConsole, new MarketPurchaseMessage(total) { Actor = buyer });

                var delivery = entities.GetComponent<MarketItemSpawnerComponent>(firstMachine);
                Assert.Multiple(() =>
                {
                    Assert.That(bank.Balance, Is.EqualTo(1000000 - total));
                    Assert.That(((HumanoidCharacterProfile)preferences.GetPreferences(player.UserId).SelectedCharacter!).BankBalance,
                        Is.EqualTo(bank.Balance));
                    Assert.That(inventory.GetStock(), Is.Empty);
                    Assert.That(firstCart.CartDataList, Is.Empty);
                    Assert.That(secondCart.CartDataList, Has.Count.EqualTo(1));
                    Assert.That(delivery.ItemsToSpawn, Has.Count.EqualTo(1));
                    Assert.That(delivery.ItemsToSpawn[0].Quantity, Is.EqualTo(1));
                    Assert.That(entities.GetComponent<MarketItemSpawnerComponent>(secondMachine).ItemsToSpawn, Is.Null.Or.Empty);
                    Assert.That(entities.GetComponent<CrateMachineComponent>(secondMachine).OpeningTimeRemaining, Is.Zero);
                });
                Assert.That(ui.TryGetUiState<MarketConsoleInterfaceState>(secondConsole, MarketConsoleUiKey.Default, out var rejected), Is.True);
                Assert.That(rejected!.CanPurchase, Is.False);
                entities.EventBus.RaiseLocalEvent(secondConsole,
                    new MarketConsoleCartMessage(0, "ExodusCheckoutItem", true) { Actor = buyer });
                Assert.That(inventory.GetStock(), Is.Empty, "Cancelling the losing cart must not recreate the bought item.");

                entities.EventBus.RaiseLocalEvent(firstMachine, new CrateMachineOpenedEvent(firstMachine));
                var deliveredItems = 0;
                var deliveredCrates = 0;
                var query = entities.EntityQueryEnumerator<EntityStorageComponent, MetaDataComponent>();
                while (query.MoveNext(out _, out var storage, out var metadata))
                {
                    if (metadata.EntityPrototype?.ID != "ExodusCheckoutCrate")
                        continue;

                    deliveredCrates++;
                    foreach (var item in storage.Contents.ContainedEntities)
                    {
                        Assert.That(entities.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID, Is.EqualTo("ExodusCheckoutItem"));
                        deliveredItems++;
                    }
                }
                Assert.That(deliveredCrates, Is.EqualTo(1));
                Assert.That(deliveredItems, Is.EqualTo(1));
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                profileUpdate = preferences.SetProfile(player.UserId, originalSlot, originalProfile);
                server.PlayerMan.SetAttachedEntity(player, previousActor);
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                if (firstStation != null)
                    entities.DeleteEntity(firstStation.Value);
                if (secondStation != null)
                    entities.DeleteEntity(secondStation.Value);
                inventory.Clear();
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            });
            await profileUpdate;
        }
        await pair.CleanReturnAsync();
    }

    [TestCase("missing")]
    [TestCase("stale")]
    [TestCase("bank")]
    public async Task FailedCheckoutPreservesStockAndIncludesActualCrate(string failure)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var market = entities.System<DynamicMarketSystem>();
            var inventory = entities.System<MarketInventorySystem>();
            var enabled = cfg.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = cfg.GetCVar(EXCVars.DynamicMarketPersist);
            cfg.SetCVar(EXCVars.DynamicMarketPersist, false);
            cfg.SetCVar(EXCVars.DynamicMarketEnabled, true);
            market.ResetAll();
            inventory.Clear();
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            try
            {
                var maps = entities.System<SharedMapSystem>();
                maps.SetTile(map.Grid, new Vector2i(1, 0), map.Tile.Tile);
                var console = entities.SpawnEntity("ExodusCheckoutConsole", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                var machine = entities.SpawnEntity("ExodusCheckoutMachine", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
                entities.System<StationSystem>().SetStation(console, station);
                entities.AddComponent<CargoMarketDataComponent>(station);
                Assert.That(inventory.TryAddStock("ExodusCheckoutItem", 5, 100), Is.True);
                var buyer = entities.SpawnEntity(null, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                var bank = entities.AddComponent<BankAccountComponent>(buyer);
#pragma warning disable RA0002 // Fixture state: bank initialization ignores YAML balances, and no session can authorize payment.
                bank.Balance = 1000000;
#pragma warning restore RA0002
                Assert.That(bank.Balance, Is.EqualTo(1000000));
                Assert.That(entities.GetComponent<TransformComponent>(console).GridUid, Is.EqualTo(map.Grid.Owner));
                Assert.That(entities.GetComponent<TransformComponent>(machine).Anchored, Is.True);
                Assert.That(entities.System<CrateMachineSystem>().FindNearestUnoccupied(console, 8, out var nearest), Is.True);
                Assert.That(nearest, Is.EqualTo(machine));

                entities.EventBus.RaiseLocalEvent(console,
                    new MarketConsoleCartMessage(2, "ExodusCheckoutItem")
                    {
                        Actor = buyer,
                        UiKey = MarketConsoleUiKey.Default,
                    });

                var ui = entities.System<UserInterfaceSystem>();
                Assert.That(ui.TryGetUiState<MarketConsoleInterfaceState>(console, MarketConsoleUiKey.Default, out var state), Is.True);
                var cart = entities.GetComponent<MarketConsoleComponent>(console);
                var spawner = entities.GetComponent<MarketItemSpawnerComponent>(machine);
                var crateMachine = entities.GetComponent<CrateMachineComponent>(machine);
                Assert.Multiple(() =>
                {
                    Assert.That(state!.CanPurchase, Is.True);
                    Assert.That(inventory.GetStock(), Has.Count.EqualTo(1));
                    Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(5));
                    Assert.That(cart.CartDataList, Has.Count.EqualTo(1));
                    Assert.That(cart.CartDataList[0].Quantity, Is.EqualTo(2));
                    Assert.That(state.TransactionCost, Is.GreaterThan(600), "The actual expensive delivery crate must contribute to its price floor.");
                    Assert.That(state.CartDataList[0].LineTotal, Is.EqualTo(state.CartBalance));
                });
                var total = checked(state!.CartBalance + state.TransactionCost);
                Assert.That(total, Is.GreaterThanOrEqualTo(1890), "The quote must cover the resale of the 1000-credit crate and both 100-credit items.");
                var itemKey = market.GetMarketKeyFromPrototype("ExodusCheckoutItem");
                var crateKey = market.GetMarketKeyFromPrototype("ExodusCheckoutCrate");
                Assert.That(market.GetFactor(itemKey), Is.EqualTo(1));
                Assert.That(market.GetFactor(crateKey), Is.EqualTo(1));
                if (failure == "stale")
                    market.SetFactor(itemKey, 2);

                entities.EventBus.RaiseLocalEvent(console,
                    new MarketPurchaseMessage(failure == "missing" ? null : total)
                    {
                        Actor = buyer,
                        UiKey = MarketConsoleUiKey.Default,
                    });

                Assert.Multiple(() =>
                {
                    Assert.That(bank.Balance, Is.EqualTo(1000000));
                    Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(5));
                    Assert.That(cart.CartDataList, Has.Count.EqualTo(1));
                    Assert.That(cart.CartDataList[0].Quantity, Is.EqualTo(2));
                    Assert.That(spawner.ItemsToSpawn, Is.Null.Or.Empty);
                    Assert.That(crateMachine.OpeningTimeRemaining, Is.Zero);
                    Assert.That(market.GetFactor(itemKey), Is.EqualTo(failure == "stale" ? 2 : 1));
                    Assert.That(market.GetFactor(crateKey), Is.EqualTo(1));
                });

                var children = entities.GetComponent<TransformComponent>(map.Grid).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    var prototype = entities.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID;
                    Assert.That(prototype, Is.Not.EqualTo("ExodusCheckoutCrate").And.Not.EqualTo("ExodusCheckoutItem"));
                }

                if (failure == "stale")
                {
                    Assert.That(ui.TryGetUiState<MarketConsoleInterfaceState>(console, MarketConsoleUiKey.Default, out var updated), Is.True);
                    Assert.That(updated!.CartBalance + updated.TransactionCost, Is.GreaterThan(total));
                }

                // Cancelling an unpurchased cart must not add its intended items to stock.
                entities.EventBus.RaiseLocalEvent(console,
                    new MarketConsoleCartMessage(0, "ExodusCheckoutItem", true) { Actor = buyer });
                Assert.That(cart.CartDataList, Is.Empty);
                Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(5));
            }
            finally
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                entities.DeleteEntity(station);
                market.ResetAll();
                inventory.Clear();
                cfg.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                cfg.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OpenConsoleOnAnotherStationReceivesSharedStockChanges()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var inventory = server.System<MarketInventorySystem>();
        var ui = server.System<UserInterfaceSystem>();
        EntityUid firstStation = default;
        EntityUid secondStation = default;
        EntityUid console = default;
        try
        {
            await server.WaitAssertion(() =>
            {
                inventory.Clear();
                Assert.That(inventory.TryAddStock("ExodusCheckoutItem", 2, 100), Is.True);
                firstStation = entities.SpawnEntity(null, MapCoordinates.Nullspace);
                secondStation = entities.SpawnEntity(null, MapCoordinates.Nullspace);
                entities.AddComponent<CargoMarketDataComponent>(firstStation);
                entities.AddComponent<CargoMarketDataComponent>(secondStation);
                entities.System<SharedMapSystem>().SetTile(map.Grid, new Vector2i(1, 0), map.Tile.Tile);
                console = entities.SpawnEntity("ExodusCheckoutConsole", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                entities.SpawnEntity("ExodusCheckoutMachine", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
                entities.System<StationSystem>().SetStation(console, secondStation);
                var buyer = entities.SpawnEntity(null, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                entities.AddComponent<BankAccountComponent>(buyer);
                Assert.That(ui.TryOpenUi(console, MarketConsoleUiKey.Default, buyer), Is.True);
                entities.EventBus.RaiseLocalEvent(console,
                    new MarketConsoleCartMessage(2, "ExodusCheckoutItem") { Actor = buyer });
                Assert.That(ui.TryGetUiState<MarketConsoleInterfaceState>(console, MarketConsoleUiKey.Default, out var state), Is.True);
                Assert.That(state!.MarketDataList[0].Quantity, Is.EqualTo(2));
                Assert.That(state.CanPurchase, Is.True);

                Assert.That(entities.System<MarketSystem>().TryTakeStock(firstStation, "ExodusCheckoutItem", 1, out _), Is.True);
            });
            await pair.RunTicksSync(20);
            await server.WaitAssertion(() =>
            {
                Assert.That(ui.IsUiOpen(console, MarketConsoleUiKey.Default), Is.True);
                Assert.That(ui.TryGetUiState<MarketConsoleInterfaceState>(console, MarketConsoleUiKey.Default, out var state), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(state!.MarketDataList, Has.Count.EqualTo(1));
                    Assert.That(state.MarketDataList[0].Quantity, Is.EqualTo(1));
                    Assert.That(state.CartDataList[0].Quantity, Is.EqualTo(2));
                    Assert.That(state.CanPurchase, Is.False, "Another station consumed stock required by this open cart.");
                });
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                if (entities.EntityExists(firstStation))
                    entities.DeleteEntity(firstStation);
                if (entities.EntityExists(secondStation))
                    entities.DeleteEntity(secondStation);
                inventory.Clear();
            });
        }
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RepeatedNonStackCartAdditionCannotExceedThirtyItems()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            try
            {
                var console = entities.SpawnEntity("ExodusCheckoutConsole", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                entities.System<StationSystem>().SetStation(console, station);
                entities.AddComponent<CargoMarketDataComponent>(station);
                Assert.That(inventory.TryAddStock("ExodusCheckoutItem", 100, 100), Is.True);
                var buyer = entities.SpawnEntity(null, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                entities.AddComponent<BankAccountComponent>(buyer);
                var cart = entities.GetComponent<MarketConsoleComponent>(console);
                entities.EventBus.RaiseLocalEvent(console,
                    new MarketConsoleCartMessage(30, "ExodusCheckoutItem") { Actor = buyer });
                Assert.That(cart.CartDataList[0].Quantity, Is.EqualTo(30));

                entities.EventBus.RaiseLocalEvent(console,
                    new MarketConsoleCartMessage(1, "ExodusCheckoutItem") { Actor = buyer });
                Assert.That(cart.CartDataList[0].Quantity, Is.EqualTo(30));
                Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(100));
                var ui = entities.System<UserInterfaceSystem>();
                Assert.That(ui.TryGetUiState<MarketConsoleInterfaceState>(console, MarketConsoleUiKey.Default, out var state), Is.True);
                Assert.That(state!.CartEntities, Is.EqualTo(30));
            }
            finally
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                entities.DeleteEntity(station);
                inventory.Clear();
            }
        });
        await pair.CleanReturnAsync();
    }
}
