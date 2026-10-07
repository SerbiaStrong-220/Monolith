using System.Collections.Generic;
using Content.Server._Exodus.Economy;
using Content.Server._NF.Atmos.Components;
using Content.Server.Cargo.Components;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketSellCeilingTest
{
    [Test]
    public async Task UnspawnedPrototypesProtectFutureSaleEndpoints()
    {
        await using var pair = await PoolManager.GetServerClient();
        var systems = pair.Server.ResolveDependency<IEntitySystemManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var ceiling = Snapshot(systems);
            Assert.Multiple(() =>
            {
                Assert.That(ceiling.Pallet, Is.GreaterThanOrEqualTo(1.5), "The Hokkaido prototype must be covered before its map loads.");
                Assert.That(ceiling.Gas, Is.GreaterThanOrEqualTo(ceiling.Pallet));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LivePalletOverridesAndDirectEditsAreVisibleImmediately()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var systems = pair.Server.ResolveDependency<IEntitySystemManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var baseline = Snapshot(systems);
            var console = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var pallet = entities.AddComponent<CargoPalletConsoleComponent>(console);
            var modifier = entities.AddComponent<MarketModifierComponent>(console);
            modifier.Buy = false;
            modifier.Mod = (float) baseline.Pallet + 10;
            Assert.That(Snapshot(systems).Pallet, Is.EqualTo(modifier.Mod));

            // VV edits these properties directly. A second quote in the same tick must see the change.
            modifier.Mod += 5;
            var changed = Snapshot(systems);
            Assert.That(changed.Pallet, Is.EqualTo(modifier.Mod));
            Assert.That(changed.Gas, Is.GreaterThanOrEqualTo(changed.Pallet));

            modifier.Buy = true;
            Assert.That(Snapshot(systems).Pallet, Is.EqualTo(baseline.Pallet));
            modifier.Buy = false;
#pragma warning disable RA0002 // Configure the payout currency of this test endpoint.
            pallet.CashType = "Doubloon";
#pragma warning restore RA0002
            Assert.That(Snapshot(systems).Pallet, Is.EqualTo(baseline.Pallet), "Separate currencies must not set the credit payout ceiling.");
#pragma warning disable RA0002
            pallet.CashType = "Credit";
#pragma warning restore RA0002
            modifier.Mod = float.NaN;
            Assert.That(Snapshot(systems).Pallet, Is.EqualTo(baseline.Pallet));
            modifier.Mod = 100;
            entities.DeleteEntity(console);
            Assert.That(Snapshot(systems).Pallet, Is.EqualTo(baseline.Pallet));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LiveGasOverridesUseSaleSemanticsAndCurrency()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var systems = pair.Server.ResolveDependency<IEntitySystemManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var baseline = Snapshot(systems);
            var console = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var gas = entities.AddComponent<GasSaleConsoleComponent>(console);
            var modifier = entities.AddComponent<MarketModifierComponent>(console);
            modifier.Mod = (float) baseline.Gas + 10;
            // Existing gas consoles inherit Buy=true, but their actual operation is a sale.
            Assert.That(Snapshot(systems).Gas, Is.EqualTo(modifier.Mod));
            modifier.Mod += 5;
            Assert.That(Snapshot(systems).Gas, Is.EqualTo(modifier.Mod));
#pragma warning disable RA0002 // Configure the payout currency of this test endpoint.
            gas.CashType = "Doubloon";
#pragma warning restore RA0002
            Assert.That(Snapshot(systems).Gas, Is.EqualTo(baseline.Gas));
            entities.DeleteEntity(console);
            Assert.That(Snapshot(systems).Gas, Is.EqualTo(baseline.Gas));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NonCashPalletsKeepASeparateBoundForTheirCreditTaxPayouts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var systems = pair.Server.ResolveDependency<IEntitySystemManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var baseline = Snapshot(systems);
            var console = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var pallet = entities.AddComponent<CargoPalletConsoleComponent>(console);
#pragma warning disable RA0002 // Configure the payout currency of this test endpoint.
            pallet.CashType = "Doubloon";
#pragma warning restore RA0002
            var modifier = entities.AddComponent<MarketModifierComponent>(console);
            modifier.Buy = false;
            modifier.Mod = (float) Math.Max(100, baseline.NonCash + 10);
            Assert.That(Snapshot(systems).NonCash, Is.EqualTo(modifier.Mod));
            Assert.That(Snapshot(systems).Pallet, Is.EqualTo(baseline.Pallet));
            modifier.Mod += 5;
            Assert.That(Snapshot(systems).NonCash, Is.EqualTo(modifier.Mod), "VV edits must update the tax-only endpoint bound too.");
            modifier.Buy = true;
            Assert.That(Snapshot(systems).NonCash, Is.EqualTo(Math.Max(1, baseline.NonCash)));
            entities.DeleteEntity(console);
            Assert.That(Snapshot(systems).NonCash, Is.EqualTo(baseline.NonCash));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PrototypeReloadUpdatesTheCeilingWithoutSpawningEntities()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var systems = pair.Server.ResolveDependency<IEntitySystemManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var baseline = Snapshot(systems);
            const string id = "ExodusMarketCeilingReloadTest";
            var modified = new Dictionary<Type, HashSet<string>>();
            prototypes.LoadString($"""
                - type: entity
                  id: {id}
                  components:
                  - type: CargoPalletConsole
                  - type: MarketModifier
                    buy: false
                    mod: 100
                """, changed: modified);
            prototypes.ReloadPrototypes(modified);
            Assert.That(Snapshot(systems).Pallet, Is.EqualTo(Math.Max(100, baseline.Pallet)));

            modified.Clear();
            prototypes.LoadString($"""
                - type: entity
                  id: {id}
                  components:
                  - type: CargoPalletConsole
                    cashType: Doubloon
                  - type: MarketModifier
                    buy: false
                    mod: 100
                """, overwrite: true, changed: modified);
            prototypes.ReloadPrototypes(modified);
            Assert.That(Snapshot(systems).NonCash, Is.EqualTo(Math.Max(100, baseline.NonCash)));
            Assert.That(Snapshot(systems).Pallet, Is.EqualTo(baseline.Pallet));

            modified.Clear();
            prototypes.LoadString($"""
                - type: entity
                  id: {id}
                  components:
                  - type: CargoPalletConsole
                  - type: MarketModifier
                    buy: false
                    mod: 1
                """, overwrite: true, changed: modified);
            prototypes.ReloadPrototypes(modified);
            Assert.That(Snapshot(systems).Pallet, Is.EqualTo(baseline.Pallet));
            Assert.That(Snapshot(systems).NonCash, Is.EqualTo(baseline.NonCash));
        });

        await pair.CleanReturnAsync();
    }

    private static (double Pallet, double Gas, double NonCash) Snapshot(IEntitySystemManager systems)
    {
        var snapshot = systems.GetEntitySystem<MarketSellCeilingSystem>().GetSnapshot();
        return (snapshot.PalletMultiplier, snapshot.GasMultiplier, snapshot.NonCashPalletMultiplier);
    }
}
