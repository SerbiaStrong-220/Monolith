namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    private void RaiseShieldStateChanged(EntityUid? grid)
    {
        if (grid is not { } uid || TerminatingOrDeleted(uid))
            return;

        var ev = new ShipShieldStateChangedEvent(uid);
        RaiseLocalEvent(ref ev);
    }
}

[ByRefEvent]
public readonly record struct ShipShieldStateChangedEvent(EntityUid Grid);
