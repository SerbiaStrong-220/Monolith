using System.Collections.Generic;
using System.Numerics;
using Content.Client._Exodus.StationAi;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Server.Chat.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [TestCase(0, 0, 0)]
    [TestCase(20000, 12000, 0)]
    [TestCase(20000, 12000, 90)]
    [TestCase(20000, 12000, 37)]
    public async Task PrivateVisionStaysAroundTheCameraOnTranslatedRotatedGrids(float x, float y, double rotation)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true }, new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var tileX = -3; tileX <= 3; tileX++)
                for (var tileY = -3; tileY <= 3; tileY++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(tileX, tileY), map.Tile.Tile);

            var transform = em.System<SharedTransformSystem>();
            transform.SetWorldPosition(map.Grid, new Vector2(x, y));
            transform.SetWorldRotation(map.Grid, Angle.FromDegrees(rotation));
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, core);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var state = em.GetComponent<RotColonyStateComponent>(core);
            Assert.That(state.ViewFrame, Is.EqualTo(map.Grid.Owner));
            Assert.That(state.LastView, Does.Contain(Vector2i.Zero));
            Assert.That(state.LastView, Does.Contain(new Vector2i(2, 0)),
                "The view must rotate around the camera, not the world origin.");
            Assert.That(em.System<RotIntelligentSystem>().CanSee(core, new EntityCoordinates(map.Grid, 2.5f, .5f)), Is.True);
        });
        await pair.Client.WaitAssertion(() =>
        {
            var view = pair.Client.EntMan.GetComponent<CameraViewMaskComponent>(pair.ToClientUid(core));
            Assert.That(view.Frame, Is.EqualTo(pair.ToClientUid(map.Grid.Owner)));
            Assert.That(view.Tiles, Does.Contain(Vector2i.Zero));
            Assert.That(view.Tiles, Does.Contain(new Vector2i(2, 0)));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ColonyHearsSpeechOnlyThroughItsVisibleEye()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true }, new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var tileX = -3; tileX <= 3; tileX++)
                for (var tileY = -3; tileY <= 3; tileY++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(tileX, tileY), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, core);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var near = em.SpawnEntity("MobMouse", new EntityCoordinates(map.Grid, 2.5f, .5f));
            var far = em.SpawnEntity("MobMouse", new EntityCoordinates(map.MapUid, 60f, 0f));
            var heard = new Dictionary<ICommonSession, ChatSystem.ICChatRecipientData>();
            em.EventBus.RaiseEvent(EventSource.Local, new ExpandICChatRecipientsEvent(near, 10, heard));
            Assert.That(heard.ContainsKey(pair.Player!), "Speech near the colony eye must reach the core's player.");
            var silent = new Dictionary<ICommonSession, ChatSystem.ICChatRecipientData>();
            em.EventBus.RaiseEvent(EventSource.Local, new ExpandICChatRecipientsEvent(far, 10, silent));
            Assert.That(silent, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }
}
