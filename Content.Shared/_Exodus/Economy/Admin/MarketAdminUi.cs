using Content.Shared.Eui;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Economy.Admin;

[Serializable, NetSerializable]
public enum MarketAdminMutationKind : byte
{
    None,
    ApplySettings,
    SetQuote,
    ResetQuote,
    ResetGroup,
    ResetAllQuotes,
}

[Serializable, NetSerializable]
public sealed class MarketAdminState : EuiStateBase
{
    public MarketSettingsSnapshot? Settings;
    public List<MarketAdminGroup> Groups = new();
    public List<MarketAdminQuote> Quotes = new();
    public bool Ready;
    public bool Saving;
    public bool QuotesPersistenceEnabled;
    public string Search = string.Empty;
    public ProtoId<MarketCommodityGroupPrototype>? GroupFilter;
    public int Page;
    public int PageSize = 50;
    public int TotalQuotes;
    public string? Status;
    public bool StatusSuccess;
    public MarketAdminMutationKind ConfirmationKind;
    public int ConfirmationStage;
    public string? ConfirmationToken;
    public string? ConfirmationTarget;
    public double? ConfirmationFactor;
    public int ResetQuoteCount;
}

[Serializable, NetSerializable]
public sealed record MarketAdminGroup(
    ProtoId<MarketCommodityGroupPrototype> Id,
    string Name,
    bool Gases,
    double ImpactMultiplier,
    double DefaultImpactStrength,
    double ImpactStrength,
    double? OverrideImpactStrength);

[Serializable, NetSerializable]
public sealed record MarketAdminQuote(
    string MarketKey,
    string Name,
    ProtoId<MarketCommodityGroupPrototype> Group,
    ProtoId<MarketCommodityRulePrototype>? Rule,
    double Factor,
    float Trend);

[Serializable, NetSerializable]
public sealed class MarketAdminRefreshMessage : EuiMessageBase;

[Serializable, NetSerializable]
public sealed class MarketAdminQueryMessage(
    string search,
    ProtoId<MarketCommodityGroupPrototype>? group,
    int page,
    int pageSize = 50) : EuiMessageBase
{
    public readonly string Search = search;
    public readonly ProtoId<MarketCommodityGroupPrototype>? Group = group;
    public readonly int Page = page;
    public readonly int PageSize = pageSize;
}

[Serializable, NetSerializable]
public sealed class MarketAdminApplySettingsMessage(
    long revision,
    MarketGlobalSettingsOverride globals,
    Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> groupOverrides) : EuiMessageBase
{
    public readonly long Revision = revision;
    public readonly MarketGlobalSettingsOverride Globals = globals;
    public readonly Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> GroupOverrides = groupOverrides;
}

[Serializable, NetSerializable]
public sealed class MarketAdminSetQuoteMessage(string marketKey, double factor) : EuiMessageBase
{
    public readonly string MarketKey = marketKey;
    public readonly double Factor = factor;
}

[Serializable, NetSerializable]
public sealed class MarketAdminResetQuoteMessage(string marketKey) : EuiMessageBase
{
    public readonly string MarketKey = marketKey;
}

[Serializable, NetSerializable]
public sealed class MarketAdminResetGroupMessage(ProtoId<MarketCommodityGroupPrototype> group) : EuiMessageBase
{
    public readonly ProtoId<MarketCommodityGroupPrototype> Group = group;
}

[Serializable, NetSerializable]
public sealed class MarketAdminBeginResetAllMessage : EuiMessageBase;

[Serializable, NetSerializable]
public sealed class MarketAdminConfirmMutationMessage(int stage, string token) : EuiMessageBase
{
    public readonly int Stage = stage;
    public readonly string Token = token;
}

[Serializable, NetSerializable]
public sealed class MarketAdminCancelMutationMessage : EuiMessageBase;
