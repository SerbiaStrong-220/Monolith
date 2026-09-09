using System.Numerics;
using Content.Shared._Exodus.Tailed;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics.Joints;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(TailedEntitySystem))]
public sealed class TailedEntityTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: TestTailedHead
  components:
  - type: Physics
    bodyType: Kinematic
    canCollide: false
  - type: TailedEntity
    prototype: TestTailedSegment
    amount: 3
    maxSegmentSpeed: 0
    enableRotationControl: false

- type: entity
  id: TestTailedSegment
  components:
  - type: Physics
    bodyType: Kinematic
    canCollide: false
""";

    [TestCase(MapTransfer.Head)]
    [TestCase(MapTransfer.LastSegment)]
    [TestCase(MapTransfer.Grid)]
    [TestCase(MapTransfer.GridRoundTrip)]
    public async Task MapChangesRestoreExistingChain(MapTransfer transfer)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();
        var transform = entities.System<SharedTransformSystem>();
        EntityUid sourceMap = default;
        EntityUid destinationMap = default;
        EntityUid head = default;
        EntityUid[] segments = [];
        Joint healthyJoint = default!;

        await server.WaitAssertion(() =>
        {
            sourceMap = maps.CreateMap(out var sourceMapId);
            destinationMap = maps.CreateMap();
            head = entities.SpawnEntity("TestTailedHead", new EntityCoordinates(sourceMap, new Vector2(0.5f)));
            segments = entities.GetComponent<TailedEntityComponent>(head).TailSegments.ToArray();
            AssertChain(entities, head, sourceMap, segments);
            healthyJoint = FindJoint(entities, head, segments[0]);

            switch (transfer)
            {
                case MapTransfer.Head:
                    transform.SetCoordinates(head, new EntityCoordinates(destinationMap, new Vector2(20f)));
                    Assert.That(entities.GetComponent<JointComponent>(head).JointCount, Is.Zero);
                    break;
                case MapTransfer.LastSegment:
                    transform.SetCoordinates(segments[^1], new EntityCoordinates(destinationMap, new Vector2(20f)));
                    Assert.That(entities.GetComponent<JointComponent>(segments[^1]).JointCount, Is.Zero);
                    break;
                case MapTransfer.Grid:
                case MapTransfer.GridRoundTrip:
                    var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(sourceMapId);
                    for (var x = -3; x <= 0; x++)
                        maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, 0), new Tile(1));

                    transform.SetParent(head, grid.Owner);
                    foreach (var segment in segments)
                        transform.SetParent(segment, grid.Owner);

                    transform.SetCoordinates(grid.Owner, new EntityCoordinates(destinationMap, Vector2.Zero));
                    if (transfer == MapTransfer.GridRoundTrip)
                    {
                        // NoFTL can return entities before the next update, leaving their final map unchanged.
                        transform.SetCoordinates(grid.Owner, new EntityCoordinates(sourceMap, Vector2.Zero));
                    }

                    Assert.That(entities.GetComponent<JointComponent>(head).JointCount, Is.Zero);
                    foreach (var segment in segments)
                        Assert.That(entities.GetComponent<JointComponent>(segment).JointCount, Is.Zero);
                    break;
            }
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var expectedMap = transfer is MapTransfer.Head or MapTransfer.Grid ? destinationMap : sourceMap;
            AssertChain(entities, head, expectedMap, segments);
            if (transfer == MapTransfer.LastSegment)
                Assert.That(FindJoint(entities, head, segments[0]), Is.SameAs(healthyJoint));

            entities.DeleteEntity(sourceMap);
            entities.DeleteEntity(destinationMap);
        });

        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SegmentDeletionStillDeletesEntireChain(bool pendingMapRepair)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();
        var transform = entities.System<SharedTransformSystem>();
        EntityUid sourceMap = default;
        EntityUid destinationMap = default;
        EntityUid head = default;
        EntityUid[] segments = [];

        await server.WaitAssertion(() =>
        {
            sourceMap = maps.CreateMap();
            destinationMap = maps.CreateMap();
            head = entities.SpawnEntity("TestTailedHead", new EntityCoordinates(sourceMap, Vector2.Zero));
            segments = entities.GetComponent<TailedEntityComponent>(head).TailSegments.ToArray();
            AssertChain(entities, head, sourceMap, segments);

            if (pendingMapRepair)
                transform.SetCoordinates(head, new EntityCoordinates(destinationMap, Vector2.Zero));

            entities.QueueDeleteEntity(segments[1]);
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(head), Is.False);
            foreach (var segment in segments)
                Assert.That(entities.EntityExists(segment), Is.False);

            entities.DeleteEntity(sourceMap);
            entities.DeleteEntity(destinationMap);
        });

        await pair.CleanReturnAsync();
    }

    private static void AssertChain(IEntityManager entities, EntityUid head, EntityUid map, EntityUid[] segments)
    {
        Assert.That(segments, Has.Length.EqualTo(3));
        Assert.That(entities.GetComponent<TailedEntityComponent>(head).TailSegments, Is.EqualTo(segments));
        Assert.That(entities.GetComponent<TransformComponent>(head).MapUid, Is.EqualTo(map));
        Assert.That(entities.GetComponent<JointComponent>(head).JointCount, Is.EqualTo(1));

        var previous = head;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var tail = entities.GetComponent<TailedEntitySegmentComponent>(segment);
            var joints = entities.GetComponent<JointComponent>(segment);
            Assert.That(tail.HeadEntity, Is.EqualTo(head));
            Assert.That(tail.Index, Is.EqualTo(i));
            Assert.That(entities.GetComponent<TransformComponent>(segment).MapUid, Is.EqualTo(map));
            Assert.That(joints.JointCount, Is.EqualTo(i == segments.Length - 1 ? 1 : 2));
            var joint = FindJoint(entities, previous, segment);
            Assert.That(joint, Is.TypeOf<DistanceJoint>());
            Assert.That(joints.GetJoints[joint.ID], Is.SameAs(joint));
            previous = segment;
        }
    }

    private static Joint FindJoint(IEntityManager entities, EntityUid bodyA, EntityUid bodyB)
    {
        foreach (var joint in entities.GetComponent<JointComponent>(bodyA).GetJoints.Values)
        {
            if (joint.BodyAUid == bodyA && joint.BodyBUid == bodyB)
                return joint;
        }

        throw new AssertionException($"Missing joint between {bodyA} and {bodyB}.");
    }

    public enum MapTransfer : byte
    {
        Head,
        LastSegment,
        Grid,
        GridRoundTrip,
    }
}
