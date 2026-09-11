using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Virology;

[ByRefEvent]
public readonly record struct GetBloodDataEvent(List<ReagentData> Data);

[ByRefEvent]
public record struct GetMetabolicMultiplierEvent
{
    public float Multiplier = 1f;

    public GetMetabolicMultiplierEvent()
    {
    }
}

[ByRefEvent]
public record struct ReagentMetabolismAttemptEvent(ProtoId<ReagentPrototype> Reagent)
{
    public bool Cancelled;
}

[ByRefEvent]
public record struct VirusInjectionAttemptEvent
{
    public bool Cancelled;
    public string? Message;
}

[ByRefEvent]
public record struct VirusBleedEffectEvent(float Amount);

[ByRefEvent]
public record struct VirusInjectReagentEvent(ProtoId<ReagentPrototype> Reagent, FixedPoint2 Amount);

[ByRefEvent]
public record struct VirusConsumeReagentEvent(ProtoId<ReagentPrototype> Reagent, FixedPoint2 Amount)
{
    public bool Consumed;
}
