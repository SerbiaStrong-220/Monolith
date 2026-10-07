using System;
using System.Collections.Generic;
using Content.Server._Exodus.Economy.Admin;
using Content.Shared._Exodus.Economy;
using Content.Shared._Exodus.Economy.Admin;
using Content.Shared.Eui;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.Tests.Server._Exodus.Economy;

[TestFixture]
public sealed class MarketAdminMutationChallengeTest
{
    [TestCase(MarketAdminMutationKind.ApplySettings)]
    [TestCase(MarketAdminMutationKind.SetQuote)]
    [TestCase(MarketAdminMutationKind.ResetQuote)]
    [TestCase(MarketAdminMutationKind.ResetGroup)]
    [TestCase(MarketAdminMutationKind.ResetAllQuotes)]
    public void EveryMutationNeedsThreeFreshTokensAndReplaysNeverReturnPayload(MarketAdminMutationKind kind)
    {
        var challenge = new MarketAdminMutationChallenge();
        Assert.That(challenge.TryBegin(Message(kind), TimeSpan.Zero), Is.True);
        Assert.That(challenge.Kind, Is.EqualTo(kind));
        for (var stage = 1; stage <= 3; stage++)
        {
            var token = challenge.Token;
            Assert.That(challenge.Stage, Is.EqualTo(stage));
            Assert.That(challenge.TryConfirm(stage, token, TimeSpan.Zero, out var mutation), Is.True);
            if (stage < 3)
            {
                Assert.That(mutation, Is.Null);
                Assert.That(challenge.Token, Is.Not.EqualTo(token));
            }
            else
            {
                Assert.That(mutation, Is.TypeOf(Message(kind).GetType()));
                Assert.That(challenge.Kind, Is.EqualTo(MarketAdminMutationKind.None));
                Assert.That(challenge.TryConfirm(stage, token, TimeSpan.Zero, out mutation), Is.False);
                Assert.That(mutation, Is.Null);
            }
        }
    }

    [Test]
    public void SettingsPayloadIsDetachedAndRevisionCannotChangeDuringConfirmation()
    {
        var groups = new Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> { ["General"] = 0.125 };
        var challenge = new MarketAdminMutationChallenge();
        Assert.That(challenge.TryBegin(new MarketAdminApplySettingsMessage(7,
            new MarketGlobalSettingsOverride(PurchaseMargin: 0.2), groups), TimeSpan.Zero), Is.True);
        groups["General"] = 900;
        groups["Other"] = 1;
        challenge.TryConfirm(1, challenge.Token, TimeSpan.Zero, out _);
        challenge.TryConfirm(2, challenge.Token, TimeSpan.Zero, out _);
        Assert.That(challenge.TryConfirm(3, challenge.Token, TimeSpan.Zero, out var payload), Is.True);
        var saved = (MarketAdminApplySettingsMessage) payload;
        Assert.Multiple(() =>
        {
            Assert.That(saved.Revision, Is.EqualTo(7));
            Assert.That(saved.Globals.PurchaseMargin, Is.EqualTo(0.2));
            Assert.That(saved.GroupOverrides, Has.Count.EqualTo(1));
            Assert.That(saved.GroupOverrides[(ProtoId<MarketCommodityGroupPrototype>) "General"], Is.EqualTo(0.125));
        });
    }

    [Test]
    public void ForgedSkippedExpiredCancelledAndReplacedConfirmationsLoseAuthorization()
    {
        var challenge = new MarketAdminMutationChallenge();
        var other = new MarketAdminMutationChallenge();
        challenge.TryBegin(Message(MarketAdminMutationKind.SetQuote), TimeSpan.Zero);
        other.TryBegin(Message(MarketAdminMutationKind.SetQuote), TimeSpan.Zero);
        Assert.That(challenge.TryConfirm(1, other.Token, TimeSpan.Zero, out _), Is.False);

        challenge.TryBegin(Message(MarketAdminMutationKind.SetQuote), TimeSpan.Zero);
        Assert.That(challenge.TryConfirm(3, challenge.Token, TimeSpan.Zero, out _), Is.False);
        challenge.TryBegin(Message(MarketAdminMutationKind.SetQuote), TimeSpan.Zero);
        var original = challenge.Token;
        challenge.TryBegin(Message(MarketAdminMutationKind.ResetQuote), TimeSpan.Zero);
        Assert.That(challenge.TryConfirm(1, original, TimeSpan.Zero, out _), Is.False);

        challenge.TryBegin(Message(MarketAdminMutationKind.SetQuote), TimeSpan.Zero);
        original = challenge.Token;
        challenge.Cancel();
        Assert.That(challenge.TryConfirm(1, original, TimeSpan.Zero, out _), Is.False);
        challenge.TryBegin(Message(MarketAdminMutationKind.SetQuote), TimeSpan.Zero);
        Assert.That(challenge.TryConfirm(1, challenge.Token, TimeSpan.FromMinutes(2), out _), Is.False);
        Assert.That(challenge.Stage, Is.Zero);
        Assert.That(challenge.Kind, Is.EqualTo(MarketAdminMutationKind.None));
    }

    private static EuiMessageBase Message(MarketAdminMutationKind kind) => kind switch
    {
        MarketAdminMutationKind.ApplySettings => new MarketAdminApplySettingsMessage(3, new(), new()),
        MarketAdminMutationKind.SetQuote => new MarketAdminSetQuoteMessage("stack:Steel", 1.5),
        MarketAdminMutationKind.ResetQuote => new MarketAdminResetQuoteMessage("stack:Steel"),
        MarketAdminMutationKind.ResetGroup => new MarketAdminResetGroupMessage("General"),
        MarketAdminMutationKind.ResetAllQuotes => new MarketAdminBeginResetAllMessage(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
