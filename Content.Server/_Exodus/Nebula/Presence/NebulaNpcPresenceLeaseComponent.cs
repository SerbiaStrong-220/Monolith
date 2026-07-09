namespace Content.Server._Exodus.Nebula.Presence;

/// <summary>
/// Runtime lease that keeps nebula presence on an NPC-controlled grid between periodic
/// proximity scans.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaNpcPresenceLeaseComponent : Component
{
    [ViewVariables]
    public TimeSpan ExpiresAt;

    [ViewVariables]
    public TimeSpan LastRefresh;

    [ViewVariables]
    public EntityUid SourceCore = EntityUid.Invalid;
}
