using Content.Server._NF.Shipyard.Components;
using Content.Server._NF.Shipyard.Systems;
using Content.Shared._NF.Shipyard;
using Content.Shared.Access.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus.Company;

[TestFixture]
public sealed class CorporateConcernShipyardTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task LegacyCardsAndVouchersKeepAccessToConcernShips(bool useVoucher)
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
                voucher.Vessels = ["HorizonSiphon", "regenerator", "BlackhawkKortic"];
            }

            try
            {
                foreach (var company in new[] { "HorizonEnergy", "HarmonyMedicalEnterprises", "HorizonHarmonyHammerMatter" })
                {
                    SetCompany(company);
                    var available = shipyard.GetAvailableShuttles(console, ShipyardConsoleUiKey.Shipyard, targetId: credential).available;
                    Assert.That(available, Does.Contain("HorizonSiphon").And.Contain("regenerator"), company);
                    Assert.That(available, Does.Not.Contain("BlackhawkKortic"), company);
                }

                SetCompany("blackhawkpmc");
                var blackhawkShips = shipyard.GetAvailableShuttles(console, ShipyardConsoleUiKey.Shipyard, targetId: credential).available;
                Assert.That(blackhawkShips, Does.Contain("BlackhawkKortic").And.Not.Contain("HorizonSiphon"));

                SetCompany("Buno");
                var unrelatedShips = shipyard.GetAvailableShuttles(console, ShipyardConsoleUiKey.Shipyard, targetId: credential).available;
                Assert.That(unrelatedShips, Does.Not.Contain("BlackhawkKortic").And.Not.Contain("HorizonSiphon").And.Not.Contain("regenerator"));
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
        });
        await pair.CleanReturnAsync();
    }
}
