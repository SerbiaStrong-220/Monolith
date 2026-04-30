// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Provstat
using Robust.Shared.Utility;

namespace Content.Shared._Exodus.Examine.Damage;

public sealed class PiercingExamineSystem : EntitySystem
{
    public void AddPenetrationToExamineMessage(FormattedMessage examineMessage, float armorPenetration)
    {
        var msg = new FormattedMessage();

        var ap = Math.Round(armorPenetration, 5);

        if (ap > 0)
            msg.AddMarkupOrThrow(Loc.GetString("damage-positive-armor-penetration", ("value", ap)));
        else if (ap < 0)
            msg.AddMarkupOrThrow(Loc.GetString("damage-negative-armor-penetration", ("value", ap)));

        if (!msg.IsEmpty)
        {
            examineMessage.PushNewline();
            examineMessage.AddMessage(msg);
        }
    }
}
