// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using System.Linq;
using Content.Server._Exodus.Economy;
using Content.Server.Construction.Components;
using Content.Shared._Exodus.CCVar;
using Content.Shared.Materials;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketCompositeSaleImpactTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusCompositeSaleMaterialStorage
  parent: BaseItem
  components:
  - type: StaticPrice
    price: 100
  - type: MaterialStorage
    storage:
      Steel: 10000
";

    [Test]
    public async Task MaterialStorageSaleAppliesPressureBeforeConsumingItsMaterials()
    {
        await RunTest((entities, source, coordinates, market, inventory) =>
        {
            var uid = entities.SpawnEntity("ExodusCompositeSaleMaterialStorage", coordinates);
            var expected = ExpectedImpact(entities, market, uid);
            Assert.That(expected.Factors["stack:Steel"], Is.LessThan(2));
            var sold = new MarketGoodsSoldEvent([uid], source);
            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

            AssertExpectedImpact(market, expected);
            Assert.That(entities.GetComponent<MaterialStorageComponent>(uid).Storage.GetValueOrDefault("Steel"), Is.Zero,
                "Stock intake must consume the same materials whose pressure was captured beforehand.");
            Assert.That(inventory.TryGetStock("SheetSteel1", out var materials), Is.True);
            Assert.That(materials!.Quantity, Is.EqualTo(100));
            Assert.That(inventory.TryGetStock("ExodusCompositeSaleMaterialStorage", out _), Is.False,
                "The plain item's admission filter must not prevent sale pressure or recovery of its stored materials.");

            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);
            AssertExpectedImpact(market, expected);
            Assert.That(inventory.TryGetStock("SheetSteel1", out materials), Is.True);
            Assert.That(materials!.Quantity, Is.EqualTo(100));
            entities.DeleteEntity(uid);
        });
    }

    [Test]
    public async Task InstalledMachinePartsApplyPressureOnceWithoutBecomingSeparateStock()
    {
        await RunTest((entities, source, coordinates, market, inventory) =>
        {
            var uid = entities.SpawnEntity("ShieldGeneratorCdm", coordinates);
            var machine = entities.GetComponent<MachineComponent>(uid);
            Assert.That(machine.BoardContainer.ContainedEntities, Has.Count.EqualTo(1));
            Assert.That(machine.PartContainer.ContainedEntities, Is.Not.Empty);
            var board = machine.BoardContainer.ContainedEntities[0];
            var expected = ExpectedImpact(entities, market, uid);
            Assert.That(expected.Factors[market.GetMarketKey(board)], Is.LessThan(2));
            var sold = new MarketGoodsSoldEvent([board, uid], source);
            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

            AssertExpectedImpact(market, expected);
            var stock = inventory.GetStock();
            Assert.That(stock, Has.Count.EqualTo(1),
                "Machine parts affect quotes as sold goods, but the machine already recreates them on buyback.");
            Assert.That(stock[0].Prototype.Id, Is.EqualTo("ShieldGeneratorCdm"));
            Assert.That(stock[0].Quantity, Is.EqualTo(1));

            var repeatedBoard = new MarketGoodsSoldEvent([board], source);
            entities.EventBus.RaiseEvent(EventSource.Local, ref repeatedBoard);
            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);
            AssertExpectedImpact(market, expected);
            Assert.That(inventory.GetStock().Single().Quantity, Is.EqualTo(1));
            entities.DeleteEntity(uid);
        });
    }

    private static MarketTransactionState ExpectedImpact(IEntityManager entities, DynamicMarketSystem market, EntityUid uid)
    {
        Assert.That(entities.System<MarketBasketSystem>().TryGetEntityBasket(uid, out var basket, out var failure), Is.True, failure);
        Assert.That(basket.Exact, Is.True);
        foreach (var line in basket.Lines)
            market.SetFactor(line.MarketKey, 2);
        var expected = new MarketTransactionState();
        foreach (var line in basket.Lines)
            market.CalculateSequentialSellValue(line.MarketKey, line.UnitBasePrice, line.Quantity, 1, 1, expected, false);
        return expected;
    }

    private static void AssertExpectedImpact(DynamicMarketSystem market, MarketTransactionState expected)
    {
        foreach (var (key, factor) in expected.Factors)
            Assert.That(market.GetFactor(key), Is.EqualTo(factor).Within(1e-12), key);
    }

    private static async Task RunTest(
        Action<IEntityManager, EntityUid, EntityCoordinates, DynamicMarketSystem, MarketInventorySystem> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var configuration = pair.Server.ResolveDependency<IConfigurationManager>();
            var market = entities.System<DynamicMarketSystem>();
            var inventory = entities.System<MarketInventorySystem>();
            var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = configuration.GetCVar(EXCVars.DynamicMarketPersist);
            var definitions = new[]
            {
                EXCVars.DynamicMarketMinFactor,
                EXCVars.DynamicMarketMaxFactor,
                EXCVars.DynamicMarketSellImpact,
                EXCVars.DynamicMarketBuyImpact,
                EXCVars.DynamicMarketReferenceVolume,
                EXCVars.DynamicMarketDecayRate,
            };
            var previous = new float[definitions.Length];
            for (var i = 0; i < definitions.Length; i++)
                previous[i] = configuration.GetCVar(definitions[i]);
            try
            {
                configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
                for (var i = 0; i < definitions.Length; i++)
                    configuration.SetCVar(definitions[i], definitions[i].DefaultValue);
                market.ResetAll();
                inventory.Clear();
                assertion(entities, map.Grid.Owner, map.GridCoords, market, inventory);
            }
            finally
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                inventory.Clear();
                market.ResetAll();
                for (var i = 0; i < definitions.Length; i++)
                    configuration.SetCVar(definitions[i], previous[i]);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }
}
