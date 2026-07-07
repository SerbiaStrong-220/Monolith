using Content.Shared._Mono.Company;
using Content.Shared.Access;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.LifeInsurance.Components;

[RegisterComponent]
public sealed partial class LifeInsuranceConsoleComponent : Component
{
    /// <summary>
    /// Maximum number of insurance charges a single person may hold.
    /// </summary>
    [DataField]
    public int MaxInsurances = 3;

    /// <summary>
    /// Radius (in tiles) used to auto-discover the scanner and cloner capsules.
    /// </summary>
    [DataField]
    public float LinkRange = 4f;

    /// <summary>
    /// Access levels permitted to delete recorded DNA. Default: TSF Colonel and Vizier.
    /// </summary>
    [DataField]
    public List<ProtoId<AccessLevelPrototype>> DeleteAccess = new() { "HeadOfSecurity", "GrandVizier" };

    /// <summary>
    /// Linked scanner capsule entity.
    /// </summary>
    [ViewVariables]
    public EntityUid? Scanner;

    /// <summary>
    /// Linked cloning capsule entity.
    /// </summary>
    [ViewVariables]
    public EntityUid? Cloner;

    /// <summary>
    /// Recorded DNA registry, keyed by the player's user id (stable across death/reconnect).
    /// </summary>
    [ViewVariables]
    public Dictionary<NetUserId, LifeInsuranceRecord> Records = new();
}

/// <summary>
/// A single recorded person in the insurance registry.
/// </summary>
public sealed class LifeInsuranceRecord
{
    /// <summary>
    /// Snapshot of the player's character used to rebuild the body on cloning.
    /// </summary>
    public HumanoidCharacterProfile Profile;

    /// <summary>
    /// Number of available insurance charges (0..MaxInsurances).
    /// </summary>
    public int Insurances;

    /// <summary>
    /// Company/faction the player belonged to when recorded. Restored on the clone so it keeps
    /// company-gated access (e.g. faction uplinks). "None" when the player had no company.
    /// </summary>
    public ProtoId<CompanyPrototype> Company = "None";

    public LifeInsuranceRecord(HumanoidCharacterProfile profile, int insurances)
    {
        Profile = profile;
        Insurances = insurances;
    }
}
