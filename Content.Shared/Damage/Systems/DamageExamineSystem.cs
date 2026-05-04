using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Damage.Systems;

public sealed class DamageExamineSystem : EntitySystem
{
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageExaminableComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
    }

    private void OnGetExamineVerbs(EntityUid uid, DamageExaminableComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var ev = new DamageExamineEvent(new FormattedMessage(), args.User);
        RaiseLocalEvent(uid, ref ev);
        if (!ev.Message.IsEmpty)
        {
            _examine.AddDetailedExamineVerb(args, component, ev.Message,
                Loc.GetString("damage-examinable-verb-text"),
                "/Textures/Interface/VerbIcons/smite.svg.192dpi.png",
                Loc.GetString("damage-examinable-verb-message")
            );
        }
    }

    public void AddDamageExamine(FormattedMessage message, DamageSpecifier damageSpecifier, float? armorPenetration = null, string? type = null)    //Exodus ArmorPiercingExamine
    {
        var markup = GetDamageExamine(damageSpecifier, armorPenetration, type); //Exodus ArmorPiercingExamine
        if (!message.IsEmpty)
        {
            message.PushNewline();
        }
        message.AddMessage(markup);
    }

    /// <summary>
    /// Retrieves the damage examine values.
    /// </summary>
    private FormattedMessage GetDamageExamine(DamageSpecifier damageSpecifier, float? armorPenetration = null, string? type = null) //Exodus ArmorPiercingExamine
    {
        var msg = new FormattedMessage();

        if (string.IsNullOrEmpty(type))
        {
            msg.AddMarkupOrThrow(Loc.GetString("damage-examine"));
        }
        else
        {
            if (damageSpecifier.GetTotal() == FixedPoint2.Zero && !damageSpecifier.AnyPositive())
            {
                msg.AddMarkupOrThrow(Loc.GetString("damage-none"));
                return msg;
            }

            msg.AddMarkupOrThrow(Loc.GetString("damage-examine-type", ("type", type)));
        }

        foreach (var damage in damageSpecifier.DamageDict)
        {
            if (damage.Value != FixedPoint2.Zero)
            {
                msg.PushNewline();
                msg.AddMarkupOrThrow(Loc.GetString("damage-value", ("type", _prototype.Index<DamageTypePrototype>(damage.Key).LocalizedName), ("amount", damage.Value)));
            }
        }

        //Exodus AdvancedWeaponExamine Start
        if (armorPenetration is not null)
        {
            msg.PushNewline();

            var ap = Math.Round(armorPenetration.Value, 5);

            if (ap > 0)
                msg.AddMarkupOrThrow(Loc.GetString("damage-positive-armor-penetration", ("value", ap)));
            else if (ap < 0)
                msg.AddMarkupOrThrow(Loc.GetString("damage-negative-armor-penetration", ("value", ap)));
        }
        //Exodus AdvancedWeaponExamine End

        return msg;
    }
}
