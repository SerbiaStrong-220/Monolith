// (c) Space Exodus Team - EXDS-RL with CLA
using System.Linq;
using Content.Server._Exodus.Economy;
using Content.Server._NF.Market.Components;
using Content.Server.Cargo.Components;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._Exodus.CCVar;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Events;
using Content.Shared.Maps;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketDepotBuybackTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusDepotBuybackConsole
  components:
  - type: StationTracker
  - type: CargoOrderConsole
    allowedGroups: []
  - type: UserInterface
    interfaces:
      enum.CargoConsoleUiKey.Orders:
        type: CargoOrderConsoleBoundUserInterface

- type: entity
  id: ExodusDepotBuybackItem
  parent: BaseItem
  components:
  - type: StaticPrice
    price: 100
";

    [TestCase("CargoDepot")]
    [TestCase("CargoDepotAlt")]
    public async Task DepotWithoutMarketPolicyListsAndPurchasesSharedStock(string gameMapId)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var configuration = server.ResolveDependency<IConfigurationManager>();
        var inventory = entities.System<MarketInventorySystem>();
        var market = entities.System<DynamicMarketSystem>();
        var map = await pair.CreateTestMap();
        var player = pair.Player!;
        var previousActor = player.AttachedEntity;
        var originalProfile = (HumanoidCharacterProfile)preferences.GetPreferences(player.UserId).SelectedCharacter!;
        var originalSlot = preferences.GetPreferences(player.UserId).SelectedCharacterIndex;
        var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
        var persist = configuration.GetCVar(EXCVars.DynamicMarketPersist);
        EntityUid? station = null;
        var profileUpdate = Task.CompletedTask;
        try
        {
            await server.WaitPost(() => profileUpdate = preferences.SetProfile(player.UserId, originalSlot,
                originalProfile.WithBankBalance(1000000)));
            await profileUpdate;
            await server.WaitAssertion(() =>
            {
                configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
                market.ResetAll();
                inventory.Clear();
                var stations = entities.System<StationSystem>();
                var gameMap = prototypes.Index<GameMapPrototype>(gameMapId);
                station = stations.InitializeNewStation(gameMap.Stations[gameMapId], [map.Grid.Owner]);
                Assert.That(entities.HasComponent<CargoMarketDataComponent>(station.Value), Is.False,
                    "A depot must buy from sector stock without a station-specific market policy.");
                // Install the order database required by this fixture's cargo terminal; real depots do not have one.
                // This terminal infrastructure must not require a station-specific resale inventory or policy.
                var orders = entities.AddComponent<StationCargoOrderDatabaseComponent>(station.Value);
                Assert.That(inventory.TryAddStock("ExodusDepotBuybackItem", 5, 100), Is.True);
                var console = entities.SpawnEntity("ExodusDepotBuybackConsole", map.GridCoords);
                Assert.That(stations.GetOwningStation(console), Is.EqualTo(station));
                var buyer = entities.SpawnEntity(null, map.GridCoords);
                var bank = entities.AddComponent<BankAccountComponent>(buyer);
                server.PlayerMan.SetAttachedEntity(player, buyer);
                Assert.That(bank.Balance, Is.EqualTo(1000000));

                var ui = entities.System<UserInterfaceSystem>();
                Assert.That(ui.TryOpenUi(console, CargoConsoleUiKey.Orders, buyer), Is.True);
                Assert.That(ui.TryGetUiState<CargoConsoleInterfaceState>(console, CargoConsoleUiKey.Orders, out var state), Is.True);
                var listing = state!.MarketListings!.Single(entry => entry.EntityProtoId == "ExodusDepotBuybackItem");
                Assert.Multiple(() =>
                {
                    Assert.That(listing.IsResale, Is.True);
                    Assert.That(listing.Available, Is.True);
                    Assert.That(listing.StockQuantity, Is.EqualTo(5));
                    Assert.That(listing.UnitPrice, Is.GreaterThan(0));
                });

                entities.EventBus.RaiseLocalEvent(console,
                    new CargoConsoleAddOrderMessage("Tester", "", listing.ProductId, 2) { Actor = buyer });
                Assert.That(orders.Orders, Has.Count.EqualTo(1));
                var order = orders.Orders[0];
                Assert.Multiple(() =>
                {
                    Assert.That(order.FromResaleStock, Is.True);
                    Assert.That(order.ProductId, Is.EqualTo("ExodusDepotBuybackItem"));
                    Assert.That(order.OrderQuantity, Is.EqualTo(2));
                    Assert.That(order.TotalPrice, Is.GreaterThan(0));
                    Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(5),
                        "Creating an order must not consume or reserve shared stock.");
                });

                var cost = order.TotalPrice!.Value;
                entities.EventBus.RaiseLocalEvent(console,
                    new CargoConsoleApproveOrderMessage(order.OrderId, cost) { Actor = buyer });
                Assert.Multiple(() =>
                {
                    Assert.That(order.Approved, Is.True);
                    Assert.That(order.TotalPrice, Is.EqualTo(cost));
                    Assert.That(order.NumDispatched, Is.Zero);
                    Assert.That(bank.Balance, Is.EqualTo(1000000 - cost));
                    Assert.That(((HumanoidCharacterProfile)preferences.GetPreferences(player.UserId).SelectedCharacter!).BankBalance,
                        Is.EqualTo(bank.Balance));
                    Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(3));
                });
                Assert.That(ui.TryGetUiState<CargoConsoleInterfaceState>(console, CargoConsoleUiKey.Orders, out var paidState), Is.True);
                Assert.That(paidState!.MarketListings!.Single(entry => entry.EntityProtoId == "ExodusDepotBuybackItem").StockQuantity,
                    Is.EqualTo(3));

                entities.EventBus.RaiseLocalEvent(console,
                    new CargoConsoleApproveOrderMessage(order.OrderId, cost) { Actor = buyer });
                Assert.That(bank.Balance, Is.EqualTo(1000000 - cost), "An approved order cannot be charged twice.");
                Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(3));
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                profileUpdate = preferences.SetProfile(player.UserId, originalSlot, originalProfile);
                server.PlayerMan.SetAttachedEntity(player, previousActor);
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                if (station.HasValue)
                    entities.DeleteEntity(station.Value);
                inventory.Clear();
                market.ResetAll();
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            });
            await profileUpdate;
        }
        await pair.CleanReturnAsync();
    }
}
