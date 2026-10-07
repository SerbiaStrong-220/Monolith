using System.Globalization;
using Content.Shared._Exodus.Economy.Admin;
using Content.Shared.Eui;

namespace Content.Server._Exodus.Economy.Admin;

/// <summary>
/// One EUI's immutable manual-write request. Each acknowledgement consumes a random token;
/// cancellation, expiry, replacement and out-of-order confirmations discard the captured request.
/// </summary>
public sealed class MarketAdminMutationChallenge
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private TimeSpan _expires;
    private EuiMessageBase? _mutation;

    public MarketAdminMutationKind Kind { get; private set; }
    public int Stage { get; private set; }
    public string? Token { get; private set; }
    public string? Target { get; private set; }
    public double? Factor { get; private set; }

    public bool TryBegin(EuiMessageBase mutation, TimeSpan now)
    {
        Cancel();
        switch (mutation)
        {
            case MarketAdminApplySettingsMessage { Globals: not null, GroupOverrides: not null } apply:
                _mutation = new MarketAdminApplySettingsMessage(apply.Revision, apply.Globals with { },
                    new(apply.GroupOverrides));
                Kind = MarketAdminMutationKind.ApplySettings;
                Target = apply.Revision.ToString(CultureInfo.InvariantCulture);
                break;
            case MarketAdminSetQuoteMessage { MarketKey: not null } set when double.IsFinite(set.Factor):
                _mutation = new MarketAdminSetQuoteMessage(set.MarketKey, set.Factor);
                Kind = MarketAdminMutationKind.SetQuote;
                Target = set.MarketKey;
                Factor = set.Factor;
                break;
            case MarketAdminResetQuoteMessage { MarketKey: not null } reset:
                _mutation = new MarketAdminResetQuoteMessage(reset.MarketKey);
                Kind = MarketAdminMutationKind.ResetQuote;
                Target = reset.MarketKey;
                break;
            case MarketAdminResetGroupMessage group:
                _mutation = new MarketAdminResetGroupMessage(group.Group);
                Kind = MarketAdminMutationKind.ResetGroup;
                Target = group.Group.Id;
                break;
            case MarketAdminBeginResetAllMessage:
                _mutation = new MarketAdminBeginResetAllMessage();
                Kind = MarketAdminMutationKind.ResetAllQuotes;
                break;
            default:
                return false;
        }

        Stage = 1;
        Token = Guid.NewGuid().ToString("N");
        _expires = now + Lifetime;
        return true;
    }

    public bool TryConfirm(int stage, string token, TimeSpan now, out EuiMessageBase? mutation)
    {
        mutation = null;
        if (Stage == 0 || stage != Stage || Token != token || now >= _expires)
        {
            Cancel();
            return false;
        }

        if (Stage == 3)
        {
            mutation = _mutation;
            Cancel();
            return true;
        }

        Stage++;
        Token = Guid.NewGuid().ToString("N");
        return true;
    }

    public void Cancel()
    {
        _mutation = null;
        Kind = MarketAdminMutationKind.None;
        Stage = 0;
        Token = null;
        Target = null;
        Factor = null;
        _expires = TimeSpan.Zero;
    }
}
