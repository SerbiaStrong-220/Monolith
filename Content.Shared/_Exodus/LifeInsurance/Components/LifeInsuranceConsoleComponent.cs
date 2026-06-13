using Content.Shared.Access;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.LifeInsurance.Components;

/// <summary>
/// Console that records player DNA from a linked scanner capsule, sells life insurance charges,
/// and drives the cloning capsule when an insured ghost activates their insurance.
/// Part of the three-piece life insurance machine, auto-linked to nearby scanner/cloner on map init.
/// </summary>
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
    /// Access levels permitted to delete recorded DNA. Default: TSF Colonel (Head of Security access)
    /// and the Grand Vizier. Anyone may still record/buy; only these may purge the registry.
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
    /// Display name shown in the console list.
    /// </summary>
    public string Name = string.Empty;

    /// <summary>
    /// Snapshot of the player's character used to rebuild the body on cloning.
    /// </summary>
    public HumanoidCharacterProfile Profile;

    /// <summary>
    /// Number of available insurance charges (0..MaxInsurances).
    /// </summary>
    public int Insurances;

    public LifeInsuranceRecord(string name, HumanoidCharacterProfile profile, int insurances)
    {
        Name = name;
        Profile = profile;
        Insurances = insurances;
    }
}
