using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.War;

/// <summary>
/// Data-driven faction escalation codes. Sector alert levels remain in the
/// existing <c>AlertLevelPrototype</c>; this prototype describes the separate
/// military code shown only on faction communication consoles.
/// </summary>
[Prototype("factionAlertLevels")]
public sealed partial class FactionAlertLevelPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public string DefaultLevel { get; private set; } = "hestia";

    /// <summary>Minimum round age before the starting code may be changed.</summary>
    [DataField]
    public TimeSpan InitialMinimumDuration { get; private set; } = TimeSpan.FromMinutes(45);

    [DataField]
    public Dictionary<string, FactionAlertLevelDetail> Levels { get; private set; } = new();
}

[DataDefinition]
public sealed partial class FactionAlertLevelDetail
{
    [DataField(required: true)]
    public LocId Name { get; private set; } = string.Empty;

    [DataField(required: true)]
    public LocId Description { get; private set; } = string.Empty;

    [DataField(required: true)]
    public LocId StartAnnouncement { get; private set; } = string.Empty;

    [DataField(required: true)]
    public LocId EndAnnouncement { get; private set; } = string.Empty;

    [DataField]
    public TimeSpan TransitionDuration { get; private set; } = TimeSpan.FromMinutes(5);

    [DataField]
    public TimeSpan MinimumDuration { get; private set; } = TimeSpan.FromHours(1);

    [DataField]
    public Color Color { get; private set; } = Color.White;

    /// <summary>
    /// Escalation and display order. Only the next higher level may be selected;
    /// values do not need to be consecutive. Each level should have a unique order.
    /// </summary>
    [DataField]
    public int Order { get; private set; }
}
