using Content.Shared._Mono.Company;
using Content.Shared.Access.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Storage.Components;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus.Company;

[TestFixture]
[TestOf(typeof(CompanyAccessReaderSystem))]
public sealed class CompanyAccessReaderTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusTestCompanyAccessReader
  components:
  - type: CompanyAccessReader
    requireCompanyCard: true
    requiredCompanies:
    - HorizonHarmonyHammerMatter
    popupMessage: null

- type: entity
  id: ExodusTestCompanyCard
  components:
  - type: Item
  - type: IdCard

- type: entity
  id: ExodusTestLegacyCardReader
  parent: ExodusTestCompanyAccessReader
  components:
  - type: CompanyAccessReader
    requiredCompanies: [SteelHammerManufacturing]

- type: entity
  id: ExodusTestLegacyInvertedCardReader
  parent: ExodusTestLegacyCardReader
  components:
  - type: CompanyAccessReader
    inverted: true

- type: entity
  id: ExodusTestLegacyCompanyReader
  parent: ExodusTestLegacyCardReader
  components:
  - type: CompanyAccessReader
    requireCompanyCard: false

- type: entity
  id: ExodusTestLegacyInvertedCompanyReader
  parent: ExodusTestLegacyCompanyReader
  components:
  - type: CompanyAccessReader
    inverted: true
";

    [Test]
    public async Task DumpRequiresMatchingCompanyCard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var readerUid = entMan.Spawn("ExodusTestCompanyAccessReader");
            var user = entMan.Spawn();

            var denied = CreateDumpEvent(entMan, readerUid, user);
            entMan.EventBus.RaiseLocalEvent(readerUid, denied);
            Assert.That(denied.Handled, Is.True);

            var idCard = entMan.AddComponent<IdCardComponent>(user);
            idCard.CompanyName = "HorizonHarmonyHammerMatter";

            var allowed = CreateDumpEvent(entMan, readerUid, user);
            entMan.EventBus.RaiseLocalEvent(readerUid, allowed);
            Assert.That(allowed.Handled, Is.False);

            idCard.CompanyName = "SteelHammerManufacturing";
            var legacyCardAllowed = CreateDumpEvent(entMan, readerUid, user);
            entMan.EventBus.RaiseLocalEvent(readerUid, legacyCardAllowed);
            Assert.That(legacyCardAllowed.Handled, Is.False);

            entMan.DeleteEntity(readerUid);
            entMan.DeleteEntity(user);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnyAccessibleMatchingCompanyCardAllowsAccess()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var handsSystem = entMan.System<SharedHandsSystem>();

        await server.WaitAssertion(() =>
        {
            var readerUid = entMan.Spawn("ExodusTestCompanyAccessReader");
            var user = entMan.Spawn();
            var hands = entMan.AddComponent<HandsComponent>(user);
            handsSystem.AddHand(user, "left", HandLocation.Left, hands);
            handsSystem.AddHand(user, "right", HandLocation.Right, hands);

            var wrongCard = entMan.Spawn("ExodusTestCompanyCard");
            entMan.GetComponent<IdCardComponent>(wrongCard).CompanyName = "DrakeBlackArmsUSA";
            Assert.That(handsSystem.TryPickup(user, wrongCard, "left", checkActionBlocker: false, animate: false));

            var matchingCard = entMan.Spawn("ExodusTestCompanyCard");
            entMan.GetComponent<IdCardComponent>(matchingCard).CompanyName = "HorizonHarmonyHammerMatter";
            Assert.That(handsSystem.TryPickup(user, matchingCard, "right", checkActionBlocker: false, animate: false));

            var allowed = CreateDumpEvent(entMan, readerUid, user);
            entMan.EventBus.RaiseLocalEvent(readerUid, allowed);
            Assert.That(allowed.Handled, Is.False);

            entMan.DeleteEntity(readerUid);
            entMan.DeleteEntity(user);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("ExodusTestLegacyCardReader", false)]
    [TestCase("ExodusTestLegacyInvertedCardReader", true)]
    [TestCase("ExodusTestLegacyCompanyReader", false)]
    [TestCase("ExodusTestLegacyInvertedCompanyReader", true)]
    public async Task SavedLegacyReaderRecognizesConcernAndSubsidiaries(string readerId, bool inverted)
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var reader = entities.Spawn(readerId);
            var user = entities.Spawn();
            var card = entities.AddComponent<IdCardComponent>(user);
            var company = entities.AddComponent<CompanyComponent>(user);
            try
            {
                foreach (var id in new[] { "HorizonHarmonyHammerMatter", "SteelHammerManufacturing", "DarkMatterEnterprises" })
                {
                    card.CompanyName = company.CompanyName = id;
                    var attempt = new ActivatableUIOpenAttemptEvent(user);
                    entities.EventBus.RaiseLocalEvent(reader, attempt);
                    Assert.That(attempt.Cancelled, Is.EqualTo(inverted), id);
                }

                card.CompanyName = company.CompanyName = "Buno";
                var wrongConcern = new ActivatableUIOpenAttemptEvent(user);
                entities.EventBus.RaiseLocalEvent(reader, wrongConcern);
                Assert.That(wrongConcern.Cancelled, Is.EqualTo(!inverted));
            }
            finally
            {
                entities.DeleteEntity(reader);
                entities.DeleteEntity(user);
            }
        });
        await pair.CleanReturnAsync();
    }

    private static DumpableDoAfterEvent CreateDumpEvent(
        IEntityManager entMan,
        EntityUid reader,
        EntityUid user)
    {
        var dumpEvent = new DumpableDoAfterEvent();
        var args = new DoAfterArgs(entMan,
            user,
            TimeSpan.Zero,
            dumpEvent,
            reader,
            target: reader,
            used: reader);
        dumpEvent.DoAfter = new Content.Shared.DoAfter.DoAfter(0, args, TimeSpan.Zero);
        return dumpEvent;
    }
}
