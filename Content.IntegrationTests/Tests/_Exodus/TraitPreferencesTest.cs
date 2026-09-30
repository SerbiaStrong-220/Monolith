using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(HumanoidCharacterProfile))]
public sealed class TraitPreferencesTest
{
    [Test]
    public async Task DrawbacksFundAdvantagesRegardlessOfTraitOrder()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var profile = new HumanoidCharacterProfile();
            ProtoId<TraitPrototype>[] traits =
            [
                "Feeble",
                "BionicLegs",
                "PrybarProsthetics",
                "PlateletFactories",
            ];

            CheckOrder(0);

            void CheckOrder(int index)
            {
                if (index == traits.Length)
                {
                    var valid = profile.GetValidTraits(traits, prototypes);
                    Assert.That(valid, Is.EquivalentTo(traits),
                        $"Valid traits were lost for order: {string.Join(", ", traits)}");
                    Assert.That(profile.GetValidTraits(valid, prototypes), Is.EqualTo(valid),
                        "Repeated validation must preserve the accepted traits.");
                    return;
                }

                for (var next = index; next < traits.Length; next++)
                {
                    (traits[index], traits[next]) = (traits[next], traits[index]);
                    CheckOrder(index + 1);
                    (traits[index], traits[next]) = (traits[next], traits[index]);
                }
            }

            ProtoId<TraitPrototype>[] withoutDrawback =
            [
                "BionicLegs",
                "PrybarProsthetics",
                "PlateletFactories",
            ];
            ProtoId<TraitPrototype>[] expected = ["BionicLegs", "PrybarProsthetics"];
            Assert.That(profile.GetValidTraits(withoutDrawback, prototypes), Is.EqualTo(expected),
                "Removing the drawback must still enforce the point limit in selection order.");
        });

        await pair.CleanReturnAsync();
    }
}
