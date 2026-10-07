using System.Collections.Generic;
using Content.Server._NF.Shipyard.Components;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Cargo.Systems;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Access.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class OrcaShipyardTest
{
    [Test]
    public async Task OrcaMapLoadsAndCannotBeBoughtBelowItsAppraisal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            ProtoId<VesselPrototype> vesselId = "DrakeOrca";
            var vessel = server.ProtoMan.Index(vesselId);
            var maps = entities.System<MapSystem>();
            maps.CreateMap(out var mapId);
            try
            {
                Assert.That(entities.System<MapLoaderSystem>().TryLoadGrid(mapId, vessel.ShuttlePath, out var grid), Is.True);
                var appraisal = entities.System<PricingSystem>().AppraiseGrid(grid!.Value.Owner);
                var minimumPrice = appraisal * vessel.MinPriceMarkup;
                TestContext.Progress.WriteLine($"DrakeOrca appraisal: {appraisal:R}; minimum price: {minimumPrice:R}; configured price: {vessel.Price}");
                Assert.That(appraisal, Is.GreaterThan(0));
                Assert.That(vessel.Price, Is.AtLeast(minimumPrice), "Buying and immediately selling the Orca must not yield a profit.");
            }
            finally
            {
                maps.DeleteMap(mapId);
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OnlyAquilaCredentialsListOrca(bool useVoucher)
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var shipyard = entities.System<ShipyardSystem>();
            var console = entities.Spawn();
            var credential = entities.Spawn();
            var card = entities.AddComponent<IdCardComponent>(credential);
            ShipyardVoucherComponent voucher = null;
            if (useVoucher)
            {
                voucher = entities.AddComponent<ShipyardVoucherComponent>(credential);
                voucher.ConsoleType = ShipyardConsoleUiKey.Shipyard;
                voucher.Vessels = ["DrakeOrca"];
            }

            try
            {
                SetCompany("DrakeBlackArmsUSA");
                Assert.That(AvailableShips(), Does.Contain("DrakeOrca"));
                SetCompany("Buno");
                Assert.That(AvailableShips(), Does.Not.Contain("DrakeOrca"));
                SetCompany("None");
                Assert.That(AvailableShips(), Does.Not.Contain("DrakeOrca"));
            }
            finally
            {
                entities.DeleteEntity(credential);
                entities.DeleteEntity(console);
            }

            void SetCompany(string company)
            {
                card.CompanyName = useVoucher ? "None" : company;
                if (voucher != null)
                    voucher.CompanyName = company;
            }

            List<string> AvailableShips() => shipyard.GetAvailableShuttles(console, ShipyardConsoleUiKey.Shipyard, targetId: credential).available;
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActiveOrcaImmediatelyBlocksAnotherBuyerUntilItIsRemovedOrInactive(bool removeShip)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            ProtoId<VesselPrototype> vesselId = "DrakeOrca";
            var vessel = server.ProtoMan.Index(vesselId);
            var firstShip = entities.SpawnEntity(null, map.GridCoords);
            var secondShip = entities.SpawnEntity(null, map.GridCoords);
            var firstBuyer = entities.SpawnEntity(null, map.GridCoords);
            var secondBuyer = entities.SpawnEntity(null, map.GridCoords);
            try
            {
                var firstAttempt = new AttemptShipyardShuttlePurchaseEvent(firstShip, firstBuyer, vessel);
                entities.EventBus.RaiseEvent(EventSource.Local, ref firstAttempt);
                Assert.That(firstAttempt.Cancelled, Is.False);

                // Match the successful purchase path without letting the periodic activity update run.
                entities.AddComponents(firstShip, vessel.AddComponents);
                entities.AddComponent<VesselComponent>(firstShip).VesselId = vessel.ID;

                var secondAttempt = new AttemptShipyardShuttlePurchaseEvent(secondShip, secondBuyer, vessel);
                entities.EventBus.RaiseEvent(EventSource.Local, ref secondAttempt);
                Assert.That(secondAttempt.Cancelled, Is.True, "Another player must be blocked immediately after the first purchase.");
                Assert.That(secondAttempt.CancelReason.ToString(), Is.EqualTo("shipyard-console-limited"));

                if (removeShip)
                    entities.DeleteEntity(firstShip);
                else
                    entities.GetComponent<ShipActivityComponent>(firstShip).InactivePastThreshold = true;

                var replacementAttempt = new AttemptShipyardShuttlePurchaseEvent(secondShip, secondBuyer, vessel);
                entities.EventBus.RaiseEvent(EventSource.Local, ref replacementAttempt);
                Assert.That(replacementAttempt.Cancelled, Is.False, "An active-ship limit must allow a replacement when the previous ship no longer counts.");
            }
            finally
            {
                if (entities.EntityExists(firstShip))
                    entities.DeleteEntity(firstShip);
                entities.DeleteEntity(secondShip);
                entities.DeleteEntity(firstBuyer);
                entities.DeleteEntity(secondBuyer);
            }
        });
        await pair.CleanReturnAsync();
    }
}
