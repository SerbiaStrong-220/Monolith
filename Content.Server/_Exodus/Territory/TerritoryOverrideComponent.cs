using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

/// <summary>An anchored source which temporarily takes precedence over ordinary territory banners.</summary>
[RegisterComponent]
public sealed partial class TerritoryOverrideComponent : Component
{
    /// <summary>Faction granted control while this source is active.</summary>
    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> Faction;

    /// <summary>Higher values take precedence over other override sources.</summary>
    [DataField]
    public int Priority = 1;

    /// <summary>Allows the owning system to suspend the claim without removing this component.</summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>Only living mobs may supply this claim when enabled.</summary>
    [DataField]
    public bool RequiresAlive = true;

    /// <summary>Runtime registration, rebuilt when the source or its territory initializes.</summary>
    public EntityUid? RegisteredGrid;
}

/// <summary>Override sources and the interrupted ordinary claim belong to the affected grid.</summary>
[RegisterComponent]
public sealed partial class TerritoryOverrideStateComponent : Component
{
    /// <summary>Known sources retained across map saves; validity is rechecked on initialization.</summary>
    [DataField]
    public HashSet<EntityUid> Sources = new();

    /// <summary>Current override source. Equal-priority additions retain the existing source.</summary>
    [DataField]
    public EntityUid? ActiveSource;

    /// <summary>Whether an ordinary claim has already been saved, including a neutral one.</summary>
    [DataField]
    public bool HasPreviousClaim;

    /// <summary>Completed ownership before the first override, or null for neutral/pending capture.</summary>
    [DataField]
    public ProtoId<TerritoryFactionPrototype>? PreviousFaction;

    /// <summary>Only this still-anchored banner may recover the interrupted claim.</summary>
    [DataField]
    public EntityUid? PreviousBanner;
}
