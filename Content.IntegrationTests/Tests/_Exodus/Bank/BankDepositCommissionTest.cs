using Content.Server._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus.Bank;

[TestFixture]
[TestOf(typeof(BankSystem))]
public sealed class BankDepositCommissionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusTestCorporateDepositor
  components:
  - type: Company
    companyName: SteelHammerManufacturing

- type: company
  id: ExodusTestFullCommissionCompany
  name: test full commission company
  atmDepositCommission: 1

- type: entity
  id: ExodusTestFullCommissionDepositor
  components:
  - type: Company
    companyName: ExodusTestFullCommissionCompany
";

    [TestCase("ComputerBankATM", 5, 80)]
    [TestCase("ComputerBankATMFree", 0, 85)]
    public async Task CompanyCommissionIsIncludedInDepositBreakdown(
        string atmPrototype,
        int expectedAtmFee,
        int expectedDeposit)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var player = entMan.Spawn("ExodusTestCorporateDepositor");
            var atm = entMan.Spawn(atmPrototype);
            var atmComponent = entMan.GetComponent<BankATMComponent>(atm);
            var bankSystem = entMan.System<BankSystem>();

            var deposit = bankSystem.GetDepositAfterFees(player,
                atmComponent,
                100,
                out var companyCommission,
                out var atmFee);

            Assert.Multiple(() =>
            {
                Assert.That(companyCommission, Is.EqualTo(15));
                Assert.That(atmFee, Is.EqualTo(expectedAtmFee));
                Assert.That(deposit, Is.EqualTo(expectedDeposit));
            });

            entMan.DeleteEntity(atm);
            entMan.DeleteEntity(player);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CompanyCommissionIsClampedForMaximumDeposit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var player = entMan.Spawn("ExodusTestFullCommissionDepositor");
            var atm = entMan.Spawn("ComputerBankATMFree");
            var atmComponent = entMan.GetComponent<BankATMComponent>(atm);
            var bankSystem = entMan.System<BankSystem>();

            var deposit = bankSystem.GetDepositAfterFees(player,
                atmComponent,
                int.MaxValue,
                out var companyCommission,
                out var atmFee);

            Assert.Multiple(() =>
            {
                Assert.That(companyCommission, Is.EqualTo(int.MaxValue));
                Assert.That(atmFee, Is.Zero);
                Assert.That(deposit, Is.Zero);
            });

            entMan.DeleteEntity(atm);
            entMan.DeleteEntity(player);
        });

        await pair.CleanReturnAsync();
    }
}
