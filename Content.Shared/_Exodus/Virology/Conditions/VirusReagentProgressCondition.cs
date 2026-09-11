// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;

namespace Content.Shared._Exodus.Virology.Conditions;

public sealed partial class VirusReagentProgressCondition : VirusProgressCondition
{
    /// <summary>Dose required to advance.</summary>
    [DataField]
    public FixedPoint2 Amount = 5;

    protected override bool Condition(in VirusProgressArgs args)
    {
        if (args.IsClient)
            return false;

        if (args.Symptom.Accelerant is not { } accelerant)
            return false;

        var consume = new VirusConsumeReagentEvent(accelerant, Amount);
        args.EntityManager.EventBus.RaiseLocalEvent(args.Carrier, ref consume);
        if (!consume.Consumed)
            return false;

        var ev = new VirusDoseAbsorbedEvent(args.Symptom);
        args.EntityManager.EventBus.RaiseLocalEvent(args.Virus, ref ev);
        return true;
    }
}
