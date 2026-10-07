using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using Content.Server.Atmos.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Atmos;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class MiningRefineryExhaustTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SavedOutletPreservesRemainingCycleWhenLoadedAtAnotherTime(bool legacyTimestamps)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid outletUid = default;
        string saved = null;
        TimeSpan remaining = default;
        TimeSpan pulseAge = default;
        PipeNode pipe = null;
        await server.WaitAssertion(() =>
        {
            outletUid = em.SpawnEntity("BulkMiningExhaust", new EntityCoordinates(map.Grid, .5f, .5f));
            em.GetComponent<PulsedGasOutletComponent>(outletUid).LastPulseTime = server.Timing.CurTime;
        });
        await pair.RunSeconds(6);
        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<PulsedGasOutletComponent>(outletUid);
            remaining = comp.NextPulseTime - server.Timing.CurTime;
            pulseAge = server.Timing.CurTime - comp.LastPulseTime!.Value;
            using var writer = new StringWriter();
            Assert.That(em.System<MapLoaderSystem>().TrySaveGrid(map.Grid, writer), Is.True);
            saved = writer.ToString();
            if (legacyTimestamps)
            {
                var legacy = Regex.Replace(saved, @"(?m)^(\s*)nextPulseTime:.*$", "$1nextPulse: 7200");
                Assert.That(legacy, Is.Not.EqualTo(saved));
                saved = legacy.Replace("lastPulseTime:", "lastPulse:");
                remaining = comp.CycleInterval;
            }
            em.DeleteEntity(map.Grid);
        });
        await pair.RunSeconds(60);
        await server.WaitAssertion(() =>
        {
            using var reader = new StringReader(saved);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(reader, "mining-exhaust-cycle", out _, out var loadedGrid,
                DeserializationOptions.Default with { InitializeMaps = true }), Is.True);
            var found = false;
            var query = em.AllEntityQueryEnumerator<PulsedGasOutletComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var comp, out var xform))
            {
                if (xform.GridUid != loadedGrid!.Value.Owner)
                    continue;

                found = true;
                outletUid = uid;
                Assert.That(comp.NextPulseTime - server.Timing.CurTime, Is.EqualTo(remaining),
                    "Saved cycles must be relative to load time, not the old server's uptime.");
                if (legacyTimestamps)
                    Assert.That(comp.LastPulseTime, Is.Null);
                else
                    Assert.That(server.Timing.CurTime - comp.LastPulseTime, Is.EqualTo(pulseAge));
            }
            Assert.That(found, Is.True);
            em.System<NodeGroupSystem>().ForceUpdate();
            var outlet = em.GetComponent<PulsedGasOutletComponent>(outletUid);
            Assert.That(em.System<NodeContainerSystem>().TryGetNode(outletUid, outlet.Inlet, out pipe), Is.True);
            pipe.Air.AdjustMoles(Gas.ChlorineTrifluoride, 70);
        });
        await pair.RunSeconds((float)remaining.TotalSeconds + 2);
        await server.WaitAssertion(() => Assert.That(pipe.Air.TotalMoles, Is.EqualTo(35).Within(0.001f),
            "A loaded outlet must discharge on its next cycle, including legacy saved grids."));
        await pair.CleanReturnAsync();
    }

    [TestCase(false, 0)]
    [TestCase(true, 0)]
    [TestCase(true, 90)]
    [TestCase(true, 180)]
    [TestCase(true, 270)]
    public async Task RefineryTransfersExhaustThroughArmoredPipesAndDischargesIntoSpace(bool simulatedAtmosphere, int degrees)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid refineryUid = default;
        EntityUid exhaustUid = default;
        MiningRefineryComponent refinery = null;
        PulsedGasOutletComponent exhaust = null;
        PipeNode outlet = null;
        PipeNode inlet = null;
        var rotation = Angle.FromDegrees(degrees);

        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var y = -3; y <= 0; y++)
            {
                var offset = rotation.RotateVec(new Vector2(0, y));
                maps.SetTile(map.Grid.Owner, map.Grid.Comp,
                    new Vector2i((int)MathF.Round(offset.X), (int)MathF.Round(offset.Y)), map.Tile.Tile);
            }

            if (simulatedAtmosphere)
                em.EnsureComponent<GridAtmosphereComponent>(map.Grid);

            Assert.That(em.HasComponent<GridAtmosphereComponent>(map.Grid), Is.EqualTo(simulatedAtmosphere));
            refineryUid = em.SpawnEntity("BulkMiningRefinery", new EntityCoordinates(map.Grid, .5f, .5f));
            var pipeUid = em.SpawnEntity("GasPipeArmoredStraight",
                new EntityCoordinates(map.Grid, new Vector2(.5f, .5f) + rotation.RotateVec(new Vector2(0, -2))));
            exhaustUid = em.SpawnEntity("BulkMiningExhaust",
                new EntityCoordinates(map.Grid, new Vector2(.5f, .5f) + rotation.RotateVec(new Vector2(0, -3))));
            var transform = em.System<SharedTransformSystem>();
            transform.SetLocalRotation(refineryUid, rotation);
            transform.SetLocalRotation(pipeUid, rotation);
            transform.SetLocalRotation(exhaustUid, rotation + Angle.FromDegrees(180));
            em.System<NodeGroupSystem>().ForceUpdate();

            var nodes = em.System<NodeContainerSystem>();
            refinery = em.GetComponent<MiningRefineryComponent>(refineryUid);
            exhaust = em.GetComponent<PulsedGasOutletComponent>(exhaustUid);
            Assert.That(nodes.TryGetNode(refineryUid, refinery.ExhaustNode, out outlet), Is.True);
            Assert.That(nodes.TryGetNode(exhaustUid, exhaust.Inlet, out inlet), Is.True);
            Assert.That(outlet.NodeGroup, Is.SameAs(inlet.NodeGroup), "Correctly connected fittings must share a pipe network.");

            ProtoId<LatheRecipePrototype> recipeId = "BulkMiningSteelOre";
            var recipe = server.ProtoMan.Index(recipeId);
            var printing = new LatheStartPrintingEvent(recipe);
            for (var i = 0; i < 7; i++)
                em.EventBus.RaiseLocalEvent(refineryUid, ref printing);
            Assert.That(refinery.Exhaust.TotalMoles, Is.EqualTo(70));
        });

        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(outlet.Air.GetMoles(refinery.ExhaustGas), Is.GreaterThan(0), "Produced gas must leave the refinery's buffer.");
            Assert.That(refinery.Exhaust.TotalMoles + outlet.Air.TotalMoles,
                Is.EqualTo(70).Within(0.001f));
        });

        await pair.RunSeconds(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(exhaust.LastPulseTime, Is.Not.Null, "The connected injector must discharge into space.");
            // Small pipe buffers may hold less than one full pulse between pressure equalizations.
            Assert.That(refinery.Exhaust.TotalMoles + inlet.Air.TotalMoles, Is.LessThan(70),
                "The injector must remove gas from the refinery and pipe network.");
        });
        await pair.CleanReturnAsync();
    }
}
