// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Server.VendingMachines;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Economy;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Stacks;
using Content.Shared.VendingMachines;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class VendingMarketTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusVendingMarketItem
  parent: BaseItem
  components:
  - type: StaticPrice
    price: 100

- type: entity
  id: ExodusVendingMarketExplicitItem
  parent: ExodusVendingMarketItem
  components:
  - type: StaticPrice
    price: 100
    vendPrice: 10

- type: vendingMachineInventory
  id: ExodusVendingMarketInventory
  startingInventory:
    ExodusVendingMarketItem: 2
    ExodusVendingMarketExplicitItem: 2

- type: entity
  id: ExodusVendingMarketMachine
  components:
  - type: VendingMachine
    pack: ExodusVendingMarketInventory
    requiresCash: true
    cashSlot:
      whitelist:
        components:
        - Cash
    cashSlotName: cash_slot
    currencyStackType: Credit
  - type: ItemSlots
  - type: ContainerContainer
    containers:
      cash_slot: !type:ContainerSlot
  - type: UserInterface
    interfaces:
      enum.VendingMachineUiKey.Key:
        type: VendingMachineBoundUserInterface

- type: entity
  id: ExodusVendingMarketDisconnectedBuyer
  components:
  - type: BankAccount
    balance: 1000000
";

    [Test]
    public async Task FailedBankPaymentPreservesCashStockAndMarket()
    {
        await RunVendingTest((entities, machine, cash, coordinates, market) =>
        {
            var buyer = entities.SpawnEntity("ExodusVendingMarketDisconnectedBuyer", coordinates);
            // Reproduce a stale advertised balance without a session able to authorize withdrawal.
#pragma warning disable RA0002
            entities.GetComponent<BankAccountComponent>(buyer).Balance = 1000000;
#pragma warning restore RA0002
            Assert.That(entities.GetComponent<BankAccountComponent>(buyer).Balance, Is.EqualTo(1000000));
            entities.System<StackSystem>().SetCount(cash, 50);
            var component = entities.GetComponent<VendingMachineComponent>(machine);
            var entry = component.Inventory["ExodusVendingMarketItem"];
            var quantity = entry.Amount;
            var key = market.GetMarketKeyFromPrototype("ExodusVendingMarketItem");

            entities.System<VendingMachineSystem>().AuthorizedVend(machine, buyer, InventoryType.Regular, entry.ID, component);

            Assert.Multiple(() =>
            {
                Assert.That(component.Ejecting, Is.False, "A rejected bank withdrawal must not dispense the item.");
                Assert.That(entry.Amount, Is.EqualTo(quantity));
                Assert.That(entities.GetComponent<StackComponent>(cash).Count, Is.EqualTo(50));
                Assert.That(entities.GetComponent<BankAccountComponent>(buyer).Balance, Is.EqualTo(1000000));
                Assert.That(market.GetFactor(key), Is.EqualTo(1));
            });
        });
    }

    [TestCase("ExodusVendingMarketItem")]
    [TestCase("ExodusVendingMarketExplicitItem")]
    public async Task PaidVendingCoversResaleAndCommitsBuyImpact(string prototype)
    {
        await RunVendingTest((entities, machine, cash, coordinates, market) =>
        {
            var buyer = entities.SpawnEntity(null, coordinates);
            var component = entities.GetComponent<VendingMachineComponent>(machine);
            var key = market.GetMarketKeyFromPrototype(prototype);
            market.SetFactor(key, 2);
            var before = entities.GetComponent<StackComponent>(cash).Count;

            entities.System<VendingMachineSystem>().AuthorizedVend(machine, buyer, InventoryType.Regular, prototype, component);

            var paid = before - entities.GetComponent<StackComponent>(cash).Count;
            var item = entities.SpawnEntity(prototype, coordinates);
            var resale = market.CalculateEntitySellValue(item, entities.System<PricingSystem>().GetPrice(item),
                1.5, null, applyImpact: false);
            Assert.Multiple(() =>
            {
                Assert.That(component.Ejecting, Is.True);
                Assert.That(paid, Is.GreaterThanOrEqualTo(DynamicMarketSystem.RoundSellPayout(resale)),
                    "Explicit vendPrice must not bypass the common resale price floor.");
                Assert.That(market.GetFactor(key), Is.GreaterThan(2), "A successful paid vend must commit its buy pressure.");
            });
        });
    }

    [Test]
    public async Task FreeVendingDoesNotChargeOrCommitBuyImpact()
    {
        await RunVendingTest((entities, machine, cash, coordinates, market) =>
        {
            var buyer = entities.SpawnEntity(null, coordinates);
            var component = entities.GetComponent<VendingMachineComponent>(machine);
            component.RequiresCash = false;
            var before = entities.GetComponent<StackComponent>(cash).Count;
            var key = market.GetMarketKeyFromPrototype("ExodusVendingMarketItem");

            entities.System<VendingMachineSystem>().AuthorizedVend(machine, buyer, InventoryType.Regular,
                "ExodusVendingMarketItem", component);

            Assert.Multiple(() =>
            {
                Assert.That(component.Ejecting, Is.True);
                Assert.That(entities.GetComponent<StackComponent>(cash).Count, Is.EqualTo(before));
                Assert.That(market.GetFactor(key), Is.EqualTo(1));
            });
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PaidUiRejectsMissingOrStaleQuote(bool stale)
    {
        await RunVendingTest((entities, machine, cash, coordinates, market) =>
        {
            var buyer = entities.SpawnEntity(null, coordinates);
            var component = entities.GetComponent<VendingMachineComponent>(machine);
            var entry = component.Inventory["ExodusVendingMarketItem"];
            var key = market.GetMarketKeyFromPrototype(entry.ID);
            entities.EventBus.RaiseLocalEvent(machine, new BoundUIOpenedEvent(VendingMachineUiKey.Key, machine, buyer));
            var ui = entities.System<UserInterfaceSystem>();
            Assert.That(ui.TryGetUiState<VendingMachinePriceState>(machine, VendingMachineUiKey.Key, out var state), Is.True);
            Assert.That(state!.Prices[entry.ID], Is.Not.Null);
            var displayedPrice = state.Prices[entry.ID];
            var before = entities.GetComponent<StackComponent>(cash).Count;
            var quantity = entry.Amount;
            market.SetFactor(key, 2);

            entities.EventBus.RaiseLocalEvent(machine,
                new VendingMachineEjectMessage(InventoryType.Regular, entry.ID, stale ? displayedPrice : null)
                {
                    Actor = buyer,
                    UiKey = VendingMachineUiKey.Key,
                });

            Assert.Multiple(() =>
            {
                Assert.That(component.Ejecting, Is.False);
                Assert.That(entry.Amount, Is.EqualTo(quantity));
                Assert.That(entities.GetComponent<StackComponent>(cash).Count, Is.EqualTo(before));
                Assert.That(market.GetFactor(key), Is.EqualTo(2));
            });
            Assert.That(ui.TryGetUiState<VendingMachinePriceState>(machine, VendingMachineUiKey.Key, out var updated), Is.True);
            Assert.That(updated!.Prices[entry.ID], Is.GreaterThan(displayedPrice));
        });
    }

    [Test]
    public async Task PaidUiChargesExactlyDisplayedQuote()
    {
        await RunVendingTest((entities, machine, cash, coordinates, market) =>
        {
            var buyer = entities.SpawnEntity(null, coordinates);
            var entry = entities.GetComponent<VendingMachineComponent>(machine).Inventory["ExodusVendingMarketExplicitItem"];
            entities.EventBus.RaiseLocalEvent(machine, new BoundUIOpenedEvent(VendingMachineUiKey.Key, machine, buyer));
            var ui = entities.System<UserInterfaceSystem>();
            Assert.That(ui.TryGetUiState<VendingMachinePriceState>(machine, VendingMachineUiKey.Key, out var state), Is.True);
            Assert.That(state!.Prices[entry.ID], Is.GreaterThan(10));
            var before = entities.GetComponent<StackComponent>(cash).Count;

            entities.EventBus.RaiseLocalEvent(machine,
                new VendingMachineEjectMessage(InventoryType.Regular, entry.ID, state.Prices[entry.ID])
                {
                    Actor = buyer,
                    UiKey = VendingMachineUiKey.Key,
                });

            Assert.That(entities.GetComponent<VendingMachineComponent>(machine).Ejecting, Is.True);
            Assert.That(before - entities.GetComponent<StackComponent>(cash).Count, Is.EqualTo(state.Prices[entry.ID]));
        });
    }

    private static async Task RunVendingTest(Action<IEntityManager, EntityUid, EntityUid, EntityCoordinates, DynamicMarketSystem> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var market = entities.System<DynamicMarketSystem>();
            var enabled = cfg.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = cfg.GetCVar(EXCVars.DynamicMarketPersist);
            cfg.SetCVar(EXCVars.DynamicMarketPersist, false);
            cfg.SetCVar(EXCVars.DynamicMarketEnabled, true);
            market.ResetAll();
            try
            {
                var coordinates = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
                var machine = entities.SpawnEntity("ExodusVendingMarketMachine", coordinates);
                var cash = entities.SpawnEntity("SpaceCash", coordinates);
                entities.System<StackSystem>().SetCount(cash, 100000);
                Assert.That(entities.System<ItemSlotsSystem>().TryInsert(machine, "cash_slot", cash, null), Is.True);
                assertion(entities, machine, cash, coordinates, market);
            }
            finally
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                market.ResetAll();
                cfg.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                cfg.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }
}
