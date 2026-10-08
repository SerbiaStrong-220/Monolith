using Content.Server._Exodus.SafetyDepositBox;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class SafetyDepositItemMetadataTest
{
    [Test]
    public void ReadsTheOrphanInsteadOfTheFirstPrototypeOrNestedEntity()
    {
        const string data = """
            meta: {format: 7, category: Entity}
            maps: []
            grids: []
            orphans: [7]
            nullspace: [9]
            entities:
            - proto: Ammo
              entities:
              - uid: 2
                components:
                - type: MetaData
                  name: nested ammo
                - type: Stack
                  count: 99
            - proto: Bag
              entities:
              - uid: 3
                components:
                - type: MetaData
                  name: nested bag
              - uid: 7
                components:
                - type: MetaData
                  name: "My stored bag"
                - type: Stack
                  count: 12
            - proto: ReferencedEntity
              entities:
              - uid: 9
            """;

        Assert.That(SafetyDepositItemMetadata.TryRead(data, out var prototype, out var name, out var count), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(prototype, Is.EqualTo("Bag"));
            Assert.That(name, Is.EqualTo("My stored bag"));
            Assert.That(count, Is.EqualTo(12));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2147483647)]
    public void ReadsAnExplicitCountWithoutInventingAName(int expectedCount)
    {
        var data = "orphans: [1]\nentities: [{proto: SheetSteel, entities: [{uid: 1, components: [{type: Stack, count: " + expectedCount + "}]}]}]";
        Assert.That(SafetyDepositItemMetadata.TryRead(data, out _, out var name, out var count), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(name, Is.Null);
            Assert.That(count, Is.EqualTo(expectedCount));
        });
    }

    [TestCase("", null)]
    [TestCase("components: []", null)]
    [TestCase("components: [{type: MetaData}, {type: Stack}]", null)]
    [TestCase("components: [{type: MetaData, name: ''}]", "")]
    public void LeavesMissingOverridesForTheCallerToResolve(string components, string expectedName)
    {
        var data = "orphans: [1]\nentities: [{proto: SheetSteel, entities: [{uid: 1, " + components + "}]}]";
        Assert.That(SafetyDepositItemMetadata.TryRead(data, out var prototype, out var name, out var count), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(prototype, Is.EqualTo("SheetSteel"));
            Assert.That(name, Is.EqualTo(expectedName));
            Assert.That(count, Is.Null);
        });
    }

    [TestCase("")]
    [TestCase("[")]
    [TestCase("orphans: [1]\nentities: [")]
    [TestCase("orphans: [1]\norphans: [2]\nentities: []")]
    [TestCase("orphans: [1]\nentities: []\n---\norphans: [2]\nentities: []")]
    [TestCase("entities: [{proto: Bag, entities: [{uid: 1}]}]")]
    [TestCase("orphans: []\nnullspace: [1]\nentities: [{proto: Bag, entities: [{uid: 1}]}]")]
    [TestCase("orphans: [1, 2]\nentities: [{proto: Bag, entities: [{uid: 1}, {uid: 2}]}]")]
    [TestCase("orphans: [1, 1]\nentities: [{proto: Bag, entities: [{uid: 1}]}]")]
    [TestCase("orphans: [1]\nentities: [{proto: Bag, entities: [{uid: 2}]}]")]
    [TestCase("orphans: [1]\nentities: [{proto: Bag, entities: [{uid: 1}, {uid: 1}]}]")]
    [TestCase("orphans: [1]\nentities: [{proto: Bag, entities: [{uid: 1}]}, {proto: Ammo, entities: [{uid: 1}]}]")]
    [TestCase("orphans: [invalid]\nentities: [{proto: Bag, entities: [{uid: 1}]}]")]
    [TestCase("orphans: [1]\nentities: [{entities: [{uid: 1}]}]")]
    [TestCase("orphans: [1]\nentities: [{proto: '', entities: [{uid: 1}]}]")]
    [TestCase("orphans: [1]\nentities: [{proto: null, entities: [{uid: 1}]}]")]
    [TestCase("orphans: [1]\nentities: [{proto: Bag, entities: [{uid: 1, components: [{type: Stack, count: nope}]}]}]")]
    [TestCase("orphans: [1]\nentities: [{proto: Bag, entities: [{uid: 1, components: [{type: Stack, count: -2}]}]}]")]
    [TestCase("orphans: [1]\nentities: [{proto: Bag, entities: [{uid: 1, components: [{type: MetaData, name: []}]}]}]")]
    [TestCase("orphans: [1]\nentities: [{proto: Bag, entities: [{uid: 1, components: [{type: MetaData, name: null}]}]}]")]
    [TestCase("orphans: [1]\nentities: [{proto: Bag, entities: [{uid: 1, components: [{type: Stack}, {type: Stack, count: 3}]}]}]")]
    [TestCase("orphans: [1]\nmaps: [2]\nentities: [{proto: Bag, entities: [{uid: 1}]}]")]
    [TestCase("orphans: [1]\ngrids: [2]\nentities: [{proto: Bag, entities: [{uid: 1}]}]")]
    public void RejectsMalformedOrAmbiguousDataWithoutReturningPartialMetadata(string data)
    {
        Assert.That(SafetyDepositItemMetadata.TryRead(data, out var prototype, out var name, out var count), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(prototype, Is.Empty);
            Assert.That(name, Is.Null);
            Assert.That(count, Is.Null);
        });
    }
}
