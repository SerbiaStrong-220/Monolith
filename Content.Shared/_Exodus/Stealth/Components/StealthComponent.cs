using Content.Shared.Exodus.Stealth;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Exodus.Stealth.Components;
/// <summary>
/// Add this component to an entity that you want to be cloaked.
/// It overlays a shader on the entity to give them an invisibility cloaked effect.
/// It also turns the entity invisible.
/// Use other components (like StealthOnMove) to modify this component's visibility based on certain conditions.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedStealthSystem))]
public sealed partial class StealthComponent : Component
{
    [DataField]
    public Dictionary<string, StealthData> StealthLayers = new();

    /// <summary>
    /// Whether or not the entity previously had an interaction outline prior to cloaking.
    /// </summary>
    [DataField("hadOutline")]
    public bool HadOutline;

    /// <summary>
    /// Time at which <see cref="LastVisibility"/> was set. Null implies the entity is currently paused and not
    /// accumulating any visibility change.
    /// </summary>
    [DataField("lastUpdate", customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan? LastUpdated;
}

[Serializable, NetSerializable]
[DataDefinition]
public sealed partial class StealthData
{
    [DataField]
    public float MinVisibility = -1.5f;

    [DataField]
    public float MaxVisibility = 1.5f;

    [DataField]
    public float LastVisibility = 1;

    [DataField]
    public bool EnabledOnDeath = true;

    [DataField]
    public bool EnabledOnCrit = true;

    [DataField]
    public float ExamineThreshold = 0.5f;

    [DataField]
    public string ExaminedDesc = "stealth-visual-effect";

    [DataField]
    public float PassiveVisibilityRate = -0.15f;

    [DataField]
    public float MovementVisibilityRate = 0f;

    [DataField]
    public float InvisibilityPenalty = 1f;

    [DataField]
    public float MaxInvisibilityPenalty = 0.5f;

    public StealthData(float minVisibility = -1f, float maxVisibility = 1.5f, float lastVisibility = 1,
        bool enabledOnDeath = true, bool enabledOnCrit = true, float examineThreshold = 0.5f, string examinedDesc = "stealth-visual-effect",
        float passiveVisibilityRate = -0.15f, float movementVisibilityRate = 0.2f, float invisibilityPenalty = 1f, float maxInvisibilityPenalty = 0.5f)
    {
        MinVisibility = minVisibility;
        MaxVisibility = maxVisibility;
        LastVisibility = lastVisibility;
        EnabledOnDeath = enabledOnDeath;
        EnabledOnCrit = enabledOnCrit;
        ExamineThreshold = examineThreshold;
        ExaminedDesc = examinedDesc;
        PassiveVisibilityRate = passiveVisibilityRate;
        MovementVisibilityRate = movementVisibilityRate;
        InvisibilityPenalty = invisibilityPenalty;
        MaxInvisibilityPenalty = maxInvisibilityPenalty;
    }
}

[Serializable, NetSerializable]
public sealed class StealthComponentState : ComponentState
{
    public readonly Dictionary<string, StealthData> StealthLayers;
    public readonly TimeSpan? LastUpdated;

    public StealthComponentState(Dictionary<string, StealthData> stealthLayers, TimeSpan? lastUpdated)
    {
        StealthLayers = stealthLayers;
        LastUpdated = lastUpdated;
    }
}
