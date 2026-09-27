using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Genetics;

// These physiological hooks allow independent providers to coexist without removing each other's components.
[ByRefEvent]
public record struct RespirationAttemptEvent(bool Cancelled = false);

[ByRefEvent]
public record struct PressureImmunityEvent(bool HighPressure, bool Immune = false);

[ByRefEvent]
public record struct TemperatureDamageAttemptEvent(bool Hot, bool Cancelled = false);

[ByRefEvent]
public record struct BleedAmountChangeEvent(float Amount);

[ByRefEvent]
public record struct FlashDurationModifyEvent(TimeSpan Duration);

[ByRefEvent]
public readonly record struct GenomeChangedEvent;

/// <summary>Raised after genetic effects stop contributing, so server-owned abilities can release their resources.</summary>
[ByRefEvent]
public readonly record struct GeneticEffectsShutdownEvent;

public sealed partial class GeneticTelekinesisEvent : EntityTargetActionEvent;
public sealed partial class GeneticRemoteViewingEvent : InstantActionEvent;
public sealed partial class GeneticCloakEvent : InstantActionEvent;
public sealed partial class GeneticMimicEvent : EntityTargetActionEvent;
public sealed partial class GeneticRestoreAppearanceEvent : InstantActionEvent;
public sealed partial class GeneticPryEvent : EntityTargetActionEvent;
public sealed partial class GeneticNightVisionEvent : InstantActionEvent;
public sealed partial class GeneticGlowEvent : InstantActionEvent;
public sealed partial class GeneticHearingEvent : InstantActionEvent;
public sealed partial class GeneticWebEvent : InstantActionEvent;
public sealed partial class GeneticFireBreathEvent : WorldTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class GeneticInjectionDoAfterEvent : SimpleDoAfterEvent
{
    [DataField] public int Revision;
}

[Serializable, NetSerializable]
public sealed partial class GeneticPryDoAfterEvent : SimpleDoAfterEvent;
