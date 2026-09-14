using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>Pending faction claim. Kept idle after completion so a new claim can safely reuse it in the same tick.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class TerritoryCaptureComponent : Component
{
    /// <summary>The claimant, or null when no capture is in progress.</summary>
    [AutoNetworkedField]
    public ProtoId<TerritoryFactionPrototype>? Faction;

    /// <summary>Physical source that must remain anchored to this grid throughout the capture.</summary>
    [ViewVariables]
    public EntityUid? Banner;

    /// <summary>Player who started the claim, retained for the completion log.</summary>
    [ViewVariables]
    public EntityUid? Actor;

    /// <summary>Server deadline, replicated once rather than updating countdown text every second.</summary>
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan EndsAt;

    /// <summary>Configured color of the contested territory.</summary>
    [AutoNetworkedField]
    public Color Color;

    /// <summary>Last deadline published to the distant radar palette; used to detect pause compensation.</summary>
    public TimeSpan PublishedEndsAt;
}
