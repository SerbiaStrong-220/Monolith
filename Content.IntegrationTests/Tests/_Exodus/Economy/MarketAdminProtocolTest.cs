using System.Collections.Generic;
using System.IO;
using Content.Shared._Exodus.Economy.Admin;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketAdminProtocolTest
{
    [Test]
    public async Task ConfirmationScopeAndPayloadSurviveTheServerClientSerializer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        object[] messages =
        [
            new MarketAdminState
            {
                ConfirmationKind = MarketAdminMutationKind.SetQuote,
                ConfirmationStage = 2,
                ConfirmationToken = "operation-token",
                ConfirmationTarget = "stack:Steel",
                ConfirmationFactor = 1.75,
            },
            new MarketAdminApplySettingsMessage(17, new MarketGlobalSettingsOverride(PurchaseMargin: 0.2),
                new() { ["General"] = 0.125 }),
            new MarketAdminConfirmMutationMessage(2, "operation-token"),
            new MarketAdminCancelMutationMessage(),
        ];
        var serialized = new List<byte[]>();
        await pair.Server.WaitAssertion(() =>
        {
            var serializer = pair.Server.ResolveDependency<IRobustSerializer>();
            foreach (var message in messages)
            {
                using var stream = new MemoryStream();
                serializer.Serialize(stream, message);
                serialized.Add(stream.ToArray());
            }
        });
        await pair.Client.WaitAssertion(() =>
        {
            var serializer = pair.Client.ResolveDependency<IRobustSerializer>();
            using var stateStream = new MemoryStream(serialized[0]);
            var state = serializer.Deserialize<MarketAdminState>(stateStream);
            Assert.Multiple(() =>
            {
                Assert.That(state.ConfirmationKind, Is.EqualTo(MarketAdminMutationKind.SetQuote));
                Assert.That(state.ConfirmationStage, Is.EqualTo(2));
                Assert.That(state.ConfirmationToken, Is.EqualTo("operation-token"));
                Assert.That(state.ConfirmationTarget, Is.EqualTo("stack:Steel"));
                Assert.That(state.ConfirmationFactor, Is.EqualTo(1.75));
            });
            using var applyStream = new MemoryStream(serialized[1]);
            var apply = (MarketAdminApplySettingsMessage) serializer.Deserialize<EuiMessageBase>(applyStream);
            Assert.That(apply.Revision, Is.EqualTo(17));
            Assert.That(apply.Globals.PurchaseMargin, Is.EqualTo(0.2));
            Assert.That(apply.GroupOverrides["General"], Is.EqualTo(0.125));
            using var confirmStream = new MemoryStream(serialized[2]);
            var confirm = (MarketAdminConfirmMutationMessage) serializer.Deserialize<EuiMessageBase>(confirmStream);
            Assert.That(confirm.Stage, Is.EqualTo(2));
            Assert.That(confirm.Token, Is.EqualTo("operation-token"));
            using var cancelStream = new MemoryStream(serialized[3]);
            Assert.That(serializer.Deserialize<EuiMessageBase>(cancelStream), Is.TypeOf<MarketAdminCancelMutationMessage>());
        });
        await pair.CleanReturnAsync();
    }
}
