// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server._NF.Bank;
using Content.Server._NF.SectorServices;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Mono.ItemTax.Components;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Events;
using Content.Shared.Materials;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketItemTaxTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusItemTaxSaleConsole
          components:
          - type: CargoPalletConsole
          - type: UserInterface
            interfaces:
              enum.CargoPalletConsoleUiKey.Sale:
                type: CargoPalletConsoleBoundUserInterface

        - type: entity
          id: ExodusItemTaxSaleWrapper
          parent: BaseItem
          components:
          - type: StaticPrice
            price: 100

        - type: entity
          id: ExodusItemTaxSaleContents
          parent: BaseItem
          components:
          - type: StaticPrice
            price: 1000

        - type: entity
          id: ExodusItemTaxSaleGasTank
          parent: ExodusItemTaxSaleWrapper
          components:
          - type: GasTank
            air:
              volume: 10000
              temperature: 293.15

        - type: entity
          id: ExodusItemTaxSaleMedicalContents
          parent: ExodusItemTaxSaleContents
          components:
          - type: ItemTax
            taxAccounts:
              Medical: 0.5

        - type: entity
          id: ExodusItemTaxSalePackage
          parent: ExodusItemTaxSaleWrapper
          components:
          - type: SpawnItemsOnUse
            items:
            - id: ExodusItemTaxSaleContents

        - type: entity
          id: ExodusItemTaxSaleMedicalPackage
          parent: ExodusItemTaxSalePackage
          components:
          - type: SpawnItemsOnUse
            items:
            - id: ExodusItemTaxSaleMedicalContents
        """;

    [Test]
    public async Task TaxedWrapperDoesNotTaxUnrelatedContents()
    {
        await RunPalletTest((entities, console, coordinates) =>
        {
            var wrapper = entities.SpawnEntity("ExodusItemTaxSaleWrapper", coordinates);
            var item = entities.SpawnEntity("ExodusItemTaxSaleContents", coordinates);
            entities.AddComponent<ItemTaxComponent>(wrapper).TaxAccounts[SectorBankAccount.Medical] = 0.5f;
            Insert(entities, item, wrapper);

            Assert.That(Appraise(entities, console), Is.EqualTo(1100));
            Assert.That(SellAndGetMedicalCredit(entities, console), Is.EqualTo(50),
                "The wrapper's 50% payment applies to its own 100-credit value, not the enclosed 1000-credit item.");
            Assert.That(entities.Deleted(wrapper), Is.True);
            Assert.That(entities.Deleted(item), Is.True);
        });
    }

    [Test]
    public async Task UntaxedWrapperPreservesItsContentsTax()
    {
        await RunPalletTest((entities, console, coordinates) =>
        {
            var wrapper = entities.SpawnEntity("ExodusItemTaxSaleWrapper", coordinates);
            var item = entities.SpawnEntity("ExodusItemTaxSaleContents", coordinates);
            entities.AddComponent<ItemTaxComponent>(item).TaxAccounts[SectorBankAccount.Medical] = 0.5f;
            Insert(entities, item, wrapper);

            Assert.That(Appraise(entities, console), Is.EqualTo(1100));
            Assert.That(SellAndGetMedicalCredit(entities, console), Is.EqualTo(500),
                "Putting a taxed item in an ordinary container must preserve its own sector payment.");
            Assert.That(entities.Deleted(item), Is.True);
        });
    }

    [TestCase("ExodusItemTaxSalePackage", true, 0)]
    [TestCase("ExodusItemTaxSaleMedicalPackage", false, 500)]
    public async Task UnopenedPackagePaysItsPayloadTaxInsteadOfItsWrapperTax(
        string prototype, bool taxedWrapper, int expectedCredit)
    {
        await RunPalletTest((entities, console, coordinates) =>
        {
            var package = entities.SpawnEntity(prototype, coordinates);
            if (taxedWrapper)
                entities.AddComponent<ItemTaxComponent>(package).TaxAccounts[SectorBankAccount.Medical] = 0.5f;

            Assert.That(Appraise(entities, console), Is.EqualTo(1000),
                "An unopened deterministic package is sold for its payload value.");
            Assert.That(SellAndGetMedicalCredit(entities, console), Is.EqualTo(expectedCredit),
                "The displayed source entity is the package, but the tax belongs to the delivered payload.");
            Assert.That(entities.Deleted(package), Is.True);
        });
    }

    [Test]
    public async Task StoredMaterialsDoNotInheritTheMachineTax()
    {
        await RunPalletTest((entities, console, coordinates) =>
        {
            var storage = entities.SpawnEntity("ExodusItemTaxSaleWrapper", coordinates);
            entities.AddComponent<MaterialStorageComponent>(storage);
            entities.AddComponent<ItemTaxComponent>(storage).TaxAccounts[SectorBankAccount.Medical] = 0.5f;
            Assert.That(entities.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(storage, "Steel", 1000),
                Is.True);

            Assert.That(Appraise(entities, console), Is.GreaterThan(100));
            Assert.That(SellAndGetMedicalCredit(entities, console), Is.EqualTo(50),
                "The stored steel is a separate untaxed commodity, not part of the taxed machine shell.");
            Assert.That(entities.Deleted(storage), Is.True);
        });
    }

    [TestCase(100)]
    [TestCase(1000)]
    public async Task TankTaxAppliesToTheShellWithoutTaxingTransferableGas(int moles)
    {
        await RunPalletTest((entities, console, coordinates) =>
        {
            var tank = entities.SpawnEntity("ExodusItemTaxSaleGasTank", coordinates);
            entities.GetComponent<GasTankComponent>(tank).Air.SetMoles(Gas.Oxygen, moles);
            entities.AddComponent<ItemTaxComponent>(tank).TaxAccounts[SectorBankAccount.Medical] = 0.5f;

            Assert.That(Appraise(entities, console), Is.GreaterThan(100),
                "The seller must still receive the gas value as well as the shell value.");
            Assert.That(SellAndGetMedicalCredit(entities, console), Is.EqualTo(50),
                "Changing the gas amount must not change the shell's 50-credit sector payment.");
            Assert.That(entities.Deleted(tank), Is.True);
        });
    }

    [Test]
    public async Task SaleUsesAnOwnTaxEditedAfterAppraisal()
    {
        await RunPalletTest((entities, console, coordinates) =>
        {
            var item = entities.SpawnEntity("ExodusItemTaxSaleWrapper", coordinates);
            var tax = entities.AddComponent<ItemTaxComponent>(item);
            tax.TaxAccounts[SectorBankAccount.Medical] = 0.5f;
            Assert.That(Appraise(entities, console), Is.EqualTo(100));

            tax.TaxAccounts[SectorBankAccount.Medical] = 0.25f;
            Assert.That(SellAndGetMedicalCredit(entities, console), Is.EqualTo(25),
                "A VV edit must affect the next sale without retaining an earlier live basket's tax snapshot.");
        });
    }

    private static void Insert(IEntityManager entities, EntityUid item, EntityUid wrapper)
    {
        var containers = entities.System<SharedContainerSystem>();
        Assert.That(containers.Insert(item, containers.EnsureContainer<Container>(wrapper, "test-contents")), Is.True);
    }

    private static int Appraise(IEntityManager entities, EntityUid console)
    {
        entities.EventBus.RaiseLocalEvent(console, new CargoPalletAppraiseMessage());
        Assert.That(entities.System<UserInterfaceSystem>().TryGetUiState<CargoPalletConsoleInterfaceState>(
            console, CargoPalletConsoleUiKey.Sale, out var state), Is.True);
        return state!.Appraisal;
    }

    private static int SellAndGetMedicalCredit(IEntityManager entities, EntityUid console)
    {
        var bank = entities.System<BankSystem>();
        Assert.That(bank.TryGetBalance(SectorBankAccount.Medical, out var before), Is.True);
        entities.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());
        Assert.That(bank.TryGetBalance(SectorBankAccount.Medical, out var after), Is.True);
        return after - before;
    }

    private static async Task RunPalletTest(Action<IEntityManager, EntityUid, EntityCoordinates> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var configuration = server.ResolveDependency<IConfigurationManager>();
            var market = entities.System<DynamicMarketSystem>();
            var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = configuration.GetCVar(EXCVars.DynamicMarketPersist);
            EntityUid? serviceHost = null;
            EntityUid? createdService = null;
            SectorBankComponent sectorBank = null;
            SectorBankAccountInfo medical = null;
            var previousBalance = 0;
            int? previousLedgerEntry = null;
            var ledgerKey = (SectorBankAccount.Medical, LedgerEntryType.MedicalSales);
            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
            market.ResetAll();
            try
            {
                var services = entities.System<SectorServiceSystem>();
                if (!entities.EntityExists(services.GetServiceEntity()))
                {
                    serviceHost = entities.SpawnEntity(null, MapCoordinates.Nullspace);
                    entities.AddComponent<StationSectorServiceHostComponent>(serviceHost.Value);
                    createdService = services.GetServiceEntity();
                }

                sectorBank = entities.GetComponent<SectorBankComponent>(services.GetServiceEntity());
#pragma warning disable RA0002 // Save the fixture's sector balance and ledger for cleanup.
                medical = sectorBank.Accounts[SectorBankAccount.Medical];
                previousBalance = medical.Balance;
                if (sectorBank.AccountLedgerEntries.TryGetValue(ledgerKey, out var previous))
                    previousLedgerEntry = previous;
#pragma warning restore RA0002

                var maps = entities.System<SharedMapSystem>();
                for (var x = 0; x <= 2; x++)
                {
                    for (var y = -2; y <= 0; y++)
                        maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                }

                var coordinates = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
                var pallet = entities.SpawnEntity("CargoPalletSell", coordinates);
                Assert.That(entities.GetComponent<TransformComponent>(pallet).Anchored, Is.True);
                var console = entities.SpawnEntity("ExodusItemTaxSaleConsole", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
                assertion(entities, console, coordinates);
            }
            finally
            {
#pragma warning disable RA0002 // Restore shared bank state after the real sale side effects.
                if (medical != null)
                    medical.Balance = previousBalance;
                if (sectorBank != null)
                {
                    if (previousLedgerEntry is { } entry)
                        sectorBank.AccountLedgerEntries[ledgerKey] = entry;
                    else
                        sectorBank.AccountLedgerEntries.Remove(ledgerKey);
                }
#pragma warning restore RA0002
                if (serviceHost is { } host)
                    entities.DeleteEntity(host);
                if (createdService is { } service && entities.EntityExists(service))
                    entities.DeleteEntity(service);
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                market.ResetAll();
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }
}
