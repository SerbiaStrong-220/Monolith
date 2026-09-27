using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Server.Power.Components;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class GeneticDiskTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PrinterCopiesDiskConsumesReagentAndRejectsChangedSources(bool fullGenome)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var user = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, new Vector2(0.5f, 0)));
            var machine = entities.SpawnEntity("GeneticInjectorPrinter", new EntityCoordinates(map, Vector2.Zero));
            var disk = entities.SpawnEntity("GeneticDisk", new EntityCoordinates(map, Vector2.One));
            entities.RemoveComponent<ApcPowerReceiverComponent>(machine);
            var printer = entities.GetComponent<GeneticPrinterComponent>(machine);
            printer.PrintDuration = TimeSpan.Zero;
            var diskData = entities.GetComponent<GeneticDiskComponent>(disk);
            var containers = entities.System<SharedContainerSystem>();
            var slot = containers.EnsureContainer<ContainerSlot>(machine, printer.DiskSlot);
            Assert.That(containers.Insert(disk, slot), Is.True);
            var genetics = entities.System<GeneticsSystem>();
            var disks = entities.System<GeneticDiskSystem>();
            var printers = entities.System<GeneticPrinterSystem>();
            Assert.That(genetics.TryGetLivingGenome(user, out var genome), Is.True);
            var round = genetics.GetRound();
            var block = round.Mutations.IndexOf("GeneticNoBreathing");
            Assert.That(genetics.TrySetBlock((user, genome!), block, 0xFFF, user), Is.True);
            var source = genetics.Capture((user, genome!));
            disks.SetSample((disk, diskData), source);
            source.Blocks[block] = 0;
            Assert.That(diskData.Sample!.Blocks[block], Is.EqualTo(0xFFF));

            void Send(GeneticPrinterOperation operation, int? revision = null)
            {
                var message = new GeneticPrinterMessage(operation, entities.GetNetEntity(disk), revision ?? diskData.Revision, block)
                {
                    Actor = user,
                    UiKey = GeneticsUiKey.Printer,
                };
                entities.EventBus.RaiseLocalEvent(machine, message);
            }

            var operation = fullGenome ? GeneticPrinterOperation.PrintGenome : GeneticPrinterOperation.PrintBlock;
            Send(operation);
            Assert.That(printer.Pending, Is.Null, "Printing requires reagent.");
            var solutions = entities.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(machine, printer.Solution, out var buffer, out var solution), Is.True);
            Assert.That(solutions.TryAddReagent(buffer!.Value, "UnstableMutagen", FixedPoint2.New(100)), Is.True);
            Send(operation);
            var job = printer.Pending;
            Assert.That(job, Is.Not.Null, "A printer does not need a patient in a scanner.");
            Assert.That(job!.Sample, Is.Not.SameAs(diskData.Sample));
            printers.Update(0);
            Assert.That(printer.Pending, Is.Null);
            var remaining = FixedPoint2.New(100) - (fullGenome ? printer.GenomeCost : printer.BlockCost);
            Assert.That(solution!.GetTotalPrototypeQuantity(printer.Reagent), Is.EqualTo(remaining));

            GeneticInjectorComponent printed = null;
            var injectors = entities.AllEntityQueryEnumerator<GeneticInjectorComponent>();
            while (injectors.MoveNext(out _, out var injector))
            {
                if (injector.Context != round.Context)
                    continue;
                Assert.That(printed, Is.Null, "Only one injector should be printed.");
                printed = injector;
            }
            Assert.That(printed, Is.Not.Null);
            if (fullGenome)
                Assert.That(printed!.Sample!.Blocks, Is.EqualTo(genome!.Blocks));
            else
            {
                Assert.That(printed!.Sample, Is.Null);
                Assert.That(printed.Block, Is.EqualTo(block));
                Assert.That(printed.Value, Is.EqualTo(0xFFF));
            }
            Assert.That(entities.System<UserInterfaceSystem>().TryGetUiState<GeneticPrinterUiState>(machine,
                GeneticsUiKey.Printer, out var state), Is.True);
            Assert.That(state!.Disk.Blocks, Is.EqualTo(diskData.Sample.Blocks));

            var oldRevision = diskData.Revision;
            Send(GeneticPrinterOperation.ResetBlock);
            Assert.That(diskData.Sample.Blocks[block], Is.Zero);
            Assert.That(genome!.Blocks[block], Is.EqualTo(0xFFF), "Editing a disk must not change its donor.");
            Assert.That(fullGenome ? printed!.Sample!.Blocks[block] : printed!.Value, Is.EqualTo(0xFFF));
            Send(GeneticPrinterOperation.ClearDisk, oldRevision);
            Assert.That(diskData.Sample, Is.Not.Null, "Stale UI commands cannot erase a newer recording.");

            Send(operation);
            Assert.That(printer.Pending, Is.Not.Null);
            disks.SetSample((disk, diskData), source);
            Assert.That(printer.Pending, Is.Null, "Replacing a recording cancels printing.");
            printers.Update(0);
            Assert.That(solution.GetTotalPrototypeQuantity(printer.Reagent), Is.EqualTo(remaining));
            Send(operation);
            Assert.That(printer.Pending, Is.Not.Null);
            Assert.That(containers.Remove(disk, slot), Is.True);
            Assert.That(printer.Pending, Is.Null, "Ejecting the disk cancels printing.");
            Assert.That(containers.Insert(disk, slot), Is.True);
            source.Context = "expired-cipher";
            disks.SetSample((disk, diskData), source);
            Send(operation);
            Assert.That(printer.Pending, Is.Null, "Old round recordings cannot be printed.");
            Assert.That(disks.GetData(disk).Status, Is.EqualTo(GeneticDiskStatus.Incompatible));
            Send(GeneticPrinterOperation.ClearDisk);
            Assert.That(diskData.Sample, Is.Null, "An incompatible recording can still be erased.");
            Assert.That(disks.GetData(disk).Status, Is.EqualTo(GeneticDiskStatus.Empty));
            Assert.That(solution.GetTotalPrototypeQuantity(printer.Reagent), Is.EqualTo(remaining));
            entities.DeleteEntity(map);
            var rounds = entities.AllEntityQueryEnumerator<GeneticsRoundComponent>();
            while (rounds.MoveNext(out var uid, out var data))
            {
                if (data.Context == round.Context)
                    entities.QueueDeleteEntity(uid);
            }
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AdministrativeGeneControlsEnforcePermissionsAndCurrentBlockMapping()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Fresh = true,
            Destructive = true,
        });
        var server = pair.Server;
        var entities = server.EntMan;
        var admins = server.ResolveDependency<IAdminManager>();
        var player = pair.Player;
        await server.WaitPost(() => admins.PromoteHost(player));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var target = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            server.PlayerMan.SetAttachedEntity(player, target);
            var genetics = entities.System<GeneticsSystem>();
            var controls = entities.System<GeneticsAdminSystem>();
            Assert.That(controls.CanAdminister(player), Is.True);
            Assert.That(genetics.TryGetGenome(target, out var genome), Is.True);
            var round = genetics.GetRound();
            var block = round.Mutations.IndexOf("GeneticNoBreathing");
            var empty = round.Mutations.IndexOf(null);
            var state = controls.GetState(player, target);
            Assert.That(state.Blocks.Count, Is.EqualTo(round.Mutations.Count));
            Assert.That(state.Blocks[block].Name, Is.Not.Null.And.Not.Empty);
            Assert.That(state.Blocks[block].Active, Is.False);
            Assert.That(state.Blocks[empty].Active, Is.Null);
            var damage = entities.GetComponent<DamageableComponent>(target);
            var poison = damage.Damage.DamageDict["Poison"];
            var enable = new GeneticsAdminSetBlockMessage(state.Context, state.Revision, block, true);
            Assert.That(controls.TrySetBlock(player, target, enable), Is.True);
            Assert.That(genome!.Blocks[block], Is.EqualTo(0xFFF));
            Assert.That(controls.GetState(player, target).Blocks[block].Active, Is.True);
            Assert.That(controls.TrySetBlock(player, target, enable), Is.False, "Stale revisions must be rejected.");
            Assert.That(controls.TrySetBlock(player, target,
                new GeneticsAdminSetBlockMessage("expired-cipher", genome.Revision, block, false)), Is.False);
            Assert.That(controls.TrySetBlock(player, target,
                new GeneticsAdminSetBlockMessage(genome.Context, genome.Revision, empty, true)), Is.False);
            Assert.That(controls.TrySetBlock(player, target,
                new GeneticsAdminSetBlockMessage(genome.Context, genome.Revision, block, false)), Is.True);
            Assert.That(genome.Blocks[block], Is.Zero);
            Assert.That(damage.Damage.DamageDict["Poison"], Is.EqualTo(poison));

            var panel = new GeneticsAdminEui(controls, genetics, admins, target);
            server.ResolveDependency<EuiManager>().OpenEui(panel, player);
            admins.DeAdmin(player);
            Assert.That(panel.IsShutDown, Is.True, "Losing administrative access closes the editor.");
            Assert.That(controls.GetState(player, target).Blocks, Is.Empty);
            Assert.That(controls.TrySetBlock(player, target,
                new GeneticsAdminSetBlockMessage(genome.Context, genome.Revision, block, true)), Is.False);
            server.PlayerMan.SetAttachedEntity(player, null);
            entities.DeleteEntity(map);
        });
        await pair.RunTicksSync(10);
        await pair.CleanReturnAsync();
    }
}
