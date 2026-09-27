namespace Content.Server._Exodus.MedicalTracking;

[RegisterComponent]
public sealed partial class MedicalTrackingBrainComponent : Component
{
    /// <summary>Implant that registered this brain; used to revoke tracking on deliberate extraction.</summary>
    [DataField]
    public EntityUid? Implant;

    /// <summary>Remains true after body destruction, until the brain is destroyed or coverage is revoked.</summary>
    [DataField]
    public bool Registered;

    /// <summary>Last visible identity of the original client, retained after extraction of the brain.</summary>
    [DataField]
    public string ClientName = string.Empty;

    /// <summary>Localized service tier retained with the brain after destruction of the implant.</summary>
    [DataField]
    public LocId TierName = "medical-tracking-tier-platinum";

    /// <summary>Service badge color retained after destruction of the implant.</summary>
    [DataField]
    public Color TierColor = Color.LightGray;
}
