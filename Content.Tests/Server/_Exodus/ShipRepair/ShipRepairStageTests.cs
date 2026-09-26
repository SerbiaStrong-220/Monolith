using Content.Server._Exodus.ShipRepair;
using Content.Shared._Exodus.ShipRepair;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Tests.Server._Exodus.ShipRepair;

[TestFixture]
[TestOf(typeof(ShipRepairDroneSystem))]
public sealed class ShipRepairStageTests
{
    [Test]
    public void GeometryChangesDoNotStarveUnexaminedHullWork()
    {
        var (drone, queue) = CreateQueue();
        ExhaustEarlierStages(drone, queue);

        // Other drones keep rebuilding walls. The rejected floors must not take priority
        // again before this drone has examined the outstanding hull work.
        for (var repair = 0; repair < 100; repair++)
        {
            queue.StageRetryRevision++;
            queue.Stages[ShipRepairStage.Enclosure].Revision++;
            Assert.That(ShipRepairDroneSystem.GetCurrentRepairStage(drone, queue), Is.EqualTo(ShipRepairStage.Enclosure));
            Assert.That(drone.Comp.StageProbes[ShipRepairStage.Floor].Remaining, Is.Zero);
        }
    }

    [Test]
    public void DeferredFloorsAreRetriedAfterOtherStagesAreExhausted()
    {
        var (drone, queue) = CreateQueue();
        ExhaustEarlierStages(drone, queue);
        queue.StageRetryRevision++;
        Assert.That(ShipRepairDroneSystem.GetCurrentRepairStage(drone, queue), Is.EqualTo(ShipRepairStage.Enclosure));
        drone.Comp.StageProbes[ShipRepairStage.Enclosure].Remaining = 0;

        Assert.That(ShipRepairDroneSystem.GetCurrentRepairStage(drone, queue), Is.EqualTo(ShipRepairStage.Floor));
        Assert.That(drone.Comp.StageProbes[ShipRepairStage.Floor].Remaining, Is.EqualTo(34));
    }

    [Test]
    public void NewFloorDamageStillTakesPriority()
    {
        var (drone, queue) = CreateQueue();
        ExhaustEarlierStages(drone, queue);
        queue.Stages[ShipRepairStage.Floor].Targets.Add(new ShipRepairTarget(new Vector2i(100, 0)));
        queue.Stages[ShipRepairStage.Floor].Revision++;

        Assert.That(ShipRepairDroneSystem.GetCurrentRepairStage(drone, queue), Is.EqualTo(ShipRepairStage.Floor));
        Assert.That(drone.Comp.StageProbes[ShipRepairStage.Floor].Remaining, Is.EqualTo(35));
    }

    [Test]
    public void GeometryChangesPreserveAnUnfinishedPass()
    {
        var (drone, queue) = CreateQueue();
        Assert.That(ShipRepairDroneSystem.GetCurrentRepairStage(drone, queue), Is.EqualTo(ShipRepairStage.Floor));
        drone.Comp.StageProbes[ShipRepairStage.Floor].Remaining = 10;
        queue.StageRetryRevision++;

        Assert.That(ShipRepairDroneSystem.GetCurrentRepairStage(drone, queue), Is.EqualTo(ShipRepairStage.Floor));
        Assert.That(drone.Comp.StageProbes[ShipRepairStage.Floor].Remaining, Is.EqualTo(10));
    }

    private static void ExhaustEarlierStages(Entity<ShipRepairDroneComponent> drone, ShipRepairWorkQueueComponent queue)
    {
        Assert.That(ShipRepairDroneSystem.GetCurrentRepairStage(drone, queue), Is.EqualTo(ShipRepairStage.Floor));
        drone.Comp.StageProbes[ShipRepairStage.Floor].Remaining = 0;
        Assert.That(ShipRepairDroneSystem.GetCurrentRepairStage(drone, queue), Is.EqualTo(ShipRepairStage.Power));
        drone.Comp.StageProbes[ShipRepairStage.Power].Remaining = 0;
    }

    private static (Entity<ShipRepairDroneComponent>, ShipRepairWorkQueueComponent) CreateQueue()
    {
        // Match the remaining categories in the reported late-repair queue.
        var queue = new ShipRepairWorkQueueComponent { Indexed = true, Revision = 1 };
        AddStage(ShipRepairStage.Floor, 34);
        AddStage(ShipRepairStage.Power, 1);
        AddStage(ShipRepairStage.Enclosure, 113);
        return (new Entity<ShipRepairDroneComponent>(default, new ShipRepairDroneComponent()), queue);

        void AddStage(ShipRepairStage stage, int count)
        {
            var pending = new ShipRepairStageQueue();
            for (var i = 0; i < count; i++)
                pending.Targets.Add(new ShipRepairTarget(new Vector2i(i, (int) stage)));
            queue.Stages.Add(stage, pending);
        }
    }
}
