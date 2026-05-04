// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Provstat
using Content.Shared.Damage.Systems;
using Robust.Shared.Utility;

namespace Content.Shared._Exodus.Examine.Damage;

public sealed class ExplosionExamineSystem : EntitySystem
{
    [Dependency] private readonly DamageExamineSystem _damageExamine = default!;

    public void AddExplosiveInfoToExamineMessage(FormattedMessage examineMessage, ExamineExplosionInfo explosionInfo)
    {
        var msg = new FormattedMessage();

        msg.PushNewline();
        msg.AddMarkupOrThrow(Loc.GetString("explosion-examine"));

        var radius = Math.Cbrt(explosionInfo.TotalIntensity);

        msg.PushNewline();
        msg.AddMarkupOrThrow(Loc.GetString("explosion-radius", ("amount", Math.Round(radius, 1))));
        msg.PushNewline();
        msg.AddMarkupOrThrow(Loc.GetString("explosion-slope", ("amount", Math.Round(explosionInfo.IntensitySlope))));

        if (explosionInfo.Damage is not null)
        {
            var epicenterDamage = explosionInfo.Damage * explosionInfo.MaxIntensity;
            var damage = _damageExamine.AddDamageDictionary(epicenterDamage);

            if (damage is not null)
            {
                msg.PushNewline();
                msg.AddMarkupOrThrow(Loc.GetString("explosion-damage-examine"));
                msg.AddMessage(damage);
            }
        }

        examineMessage.PushNewline();
        examineMessage.AddMessage(msg);
    }
}
