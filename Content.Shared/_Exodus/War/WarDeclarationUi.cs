using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.War;

[Serializable, NetSerializable]
public enum WarDeclarationDirection : byte
{
    None,
    Outgoing,
    Incoming,
}

[Serializable, NetSerializable]
public enum PeaceOfferDirection : byte
{
    None,
    Outgoing,
    Incoming,
}

[Serializable, NetSerializable]
public enum AllianceOfferDirection : byte
{
    None,
    Outgoing,
    Incoming,
}

[Serializable, NetSerializable]
public enum FactionRelationKind : byte
{
    Neutral,
    War,
    Alliance,
}

[Serializable, NetSerializable]
public readonly record struct FactionAlertLevelOptionState(
    string Id,
    LocId Name,
    LocId Description,
    Color Color,
    bool Selectable);

[Serializable, NetSerializable]
public sealed class FactionAlertLevelState
{
    public List<FactionAlertLevelOptionState> Levels { get; }
    public string CurrentLevel { get; }
    public string? PendingLevel { get; }
    public TimeSpan AvailableAt { get; }
    public TimeSpan PendingAt { get; }

    public FactionAlertLevelState(
        List<FactionAlertLevelOptionState> levels,
        string currentLevel,
        string? pendingLevel,
        TimeSpan availableAt,
        TimeSpan pendingAt)
    {
        Levels = levels;
        CurrentLevel = currentLevel;
        PendingLevel = pendingLevel;
        AvailableAt = availableAt;
        PendingAt = pendingAt;
    }
}

[Serializable, NetSerializable]
public enum WarLockReason : byte
{
    None,
    PostWar,
    AllianceBreak,
}

[Serializable, NetSerializable]
public readonly record struct FactionRelationState(
    ProtoId<TerritoryFactionPrototype> Faction,
    LocId Name,
    LocId ShortName,
    Color Color);

[Serializable, NetSerializable]
public readonly record struct FactionPairRelationState(
    ProtoId<TerritoryFactionPrototype> First,
    ProtoId<TerritoryFactionPrototype> Second,
    FactionRelationKind Relation);

[Serializable, NetSerializable]
public readonly record struct CorporationTerritoryState(
    ProtoId<Content.Shared._Mono.Company.CompanyPrototype> Company,
    ProtoId<TerritoryFactionPrototype>? Faction);

[Serializable, NetSerializable]
public sealed class WarDeclarationConsoleState
{
    public ProtoId<TerritoryFactionPrototype> SourceFaction { get; }
    public LocId SourceName { get; }
    public bool RoundRunning { get; }
    public bool CodeAllowsWar { get; }
    public List<WarDeclarationTargetState> Targets { get; }
    public List<FactionRelationState> Relations { get; }
    public List<FactionPairRelationState> PairRelations { get; }
    public List<CorporationTerritoryState> Corporations { get; }
    public TimeSpan AllianceBreakCooldown { get; }

    public WarDeclarationConsoleState(
        ProtoId<TerritoryFactionPrototype> sourceFaction,
        LocId sourceName,
        bool roundRunning,
        bool codeAllowsWar,
        List<WarDeclarationTargetState> targets,
        List<FactionRelationState>? relations = null,
        List<FactionPairRelationState>? pairRelations = null,
        List<CorporationTerritoryState>? corporations = null,
        TimeSpan allianceBreakCooldown = default)
    {
        SourceFaction = sourceFaction;
        SourceName = sourceName;
        RoundRunning = roundRunning;
        CodeAllowsWar = codeAllowsWar;
        Targets = targets;
        Relations = relations ?? new List<FactionRelationState>();
        PairRelations = pairRelations ?? new List<FactionPairRelationState>();
        Corporations = corporations ?? new List<CorporationTerritoryState>();
        AllianceBreakCooldown = allianceBreakCooldown;
    }
}

[Serializable, NetSerializable]
public readonly record struct WarDeclarationTargetState(
    ProtoId<TerritoryFactionPrototype> Faction,
    LocId Name,
    WarDeclarationDirection Direction,
    TimeSpan DeclarationAvailableAt,
    PeaceOfferDirection PeaceDirection,
    int PeaceOfferId,
    TimeSpan PeaceOfferAvailableAt,
    FactionRelationKind Relation = FactionRelationKind.Neutral,
    AllianceOfferDirection AllianceDirection = AllianceOfferDirection.None,
    int AllianceOfferId = 0,
    TimeSpan AllianceOfferAvailableAt = default,
    TimeSpan LockUntil = default,
    WarLockReason LockReason = WarLockReason.None);

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleDeclareWarMessage(
    ProtoId<TerritoryFactionPrototype> targetFaction) : BoundUserInterfaceMessage
{
    public ProtoId<TerritoryFactionPrototype> TargetFaction { get; } = targetFaction;
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleOfferPeaceMessage(
    ProtoId<TerritoryFactionPrototype> targetFaction) : BoundUserInterfaceMessage
{
    public ProtoId<TerritoryFactionPrototype> TargetFaction { get; } = targetFaction;
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleAcceptPeaceMessage(
    ProtoId<TerritoryFactionPrototype> targetFaction,
    int offerId) : BoundUserInterfaceMessage
{
    public ProtoId<TerritoryFactionPrototype> TargetFaction { get; } = targetFaction;
    public int OfferId { get; } = offerId;
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleWithdrawPeaceMessage(
    ProtoId<TerritoryFactionPrototype> targetFaction,
    int offerId) : BoundUserInterfaceMessage
{
    public ProtoId<TerritoryFactionPrototype> TargetFaction { get; } = targetFaction;
    public int OfferId { get; } = offerId;
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleOfferAllianceMessage(
    ProtoId<TerritoryFactionPrototype> targetFaction) : BoundUserInterfaceMessage
{
    public ProtoId<TerritoryFactionPrototype> TargetFaction { get; } = targetFaction;
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleAcceptAllianceMessage(
    ProtoId<TerritoryFactionPrototype> targetFaction,
    int offerId) : BoundUserInterfaceMessage
{
    public ProtoId<TerritoryFactionPrototype> TargetFaction { get; } = targetFaction;
    public int OfferId { get; } = offerId;
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleWithdrawAllianceMessage(
    ProtoId<TerritoryFactionPrototype> targetFaction,
    int offerId) : BoundUserInterfaceMessage
{
    public ProtoId<TerritoryFactionPrototype> TargetFaction { get; } = targetFaction;
    public int OfferId { get; } = offerId;
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleBreakAllianceMessage(
    ProtoId<TerritoryFactionPrototype> targetFaction) : BoundUserInterfaceMessage
{
    public ProtoId<TerritoryFactionPrototype> TargetFaction { get; } = targetFaction;
}
