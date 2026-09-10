using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.War;

/// <summary>
/// A directional declaration of war. The relation itself is symmetric, but the declaring faction is retained.
/// </summary>
[DataDefinition]
public sealed partial class FactionWarDeclaration
{
    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> DeclaringFaction = default!;

    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> TargetFaction = default!;

    [DataField]
    public TimeSpan DeclaredAtRoundTime;

    /// <summary>
    /// Pending offer. The war remains active until the other faction accepts it.
    /// </summary>
    [DataField]
    public FactionPeaceOffer? PeaceOffer;

    /// <summary>
    /// Earliest round time for a new offer after the previous one was withdrawn.
    /// </summary>
    [DataField]
    public TimeSpan NextPeaceOfferAtRoundTime;

    public FactionWarDeclaration()
    {
    }

    public FactionWarDeclaration(
        ProtoId<TerritoryFactionPrototype> declaringFaction,
        ProtoId<TerritoryFactionPrototype> targetFaction,
        TimeSpan declaredAtRoundTime)
    {
        DeclaringFaction = declaringFaction;
        TargetFaction = targetFaction;
        DeclaredAtRoundTime = declaredAtRoundTime;
    }
}

public enum WarDeclarationResult : byte
{
    Success,
    StateUnavailable,
    RoundNotRunning,
    TooEarly,
    InvalidFaction,
    SameFaction,
    AlreadyAtWar,
    NotAtWar,
    PostWarCooldown,
}
