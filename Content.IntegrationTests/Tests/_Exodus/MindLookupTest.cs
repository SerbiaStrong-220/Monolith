using System;
using Content.Server.Mind;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class MindLookupTest
{
    [Test]
    public async Task MissingPlayerDataReturnsNoMindWithoutThrowing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var player = new NetUserId(Guid.NewGuid());
            var players = server.ResolveDependency<IPlayerManager>();
            Assert.That(players.TryGetPlayerData(player, out _), Is.False);

            var minds = server.EntMan.System<MindSystem>();
            Assert.That(minds.TryGetMind(player, out var mindId, out var mind), Is.False);
            Assert.That(mindId, Is.Null);
            Assert.That(mind, Is.Null);
        });

        await pair.CleanReturnAsync();
    }
}
