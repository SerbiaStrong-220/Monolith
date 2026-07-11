using System.Numerics;
using Content.Shared.Hands.Components;

namespace Content.Shared.Hands.EntitySystems;

public abstract partial class SharedHandsSystem
{
    /// <summary>
    /// Adds a hand with an offset for its held-item visuals.
    /// </summary>
    public void AddHand(
        EntityUid uid,
        string handName,
        HandLocation handLocation,
        Vector2 visualOffset,
        HandsComponent? handsComp = null)
    {
        if (!Resolve(uid, ref handsComp, false))
            return;

        AddHand(uid, handName, handLocation, handsComp);

        if (!handsComp.Hands.TryGetValue(handName, out var hand))
            return;

        hand.VisualOffset = visualOffset;
        Dirty(uid, handsComp);
    }
}
