using Content.Shared._ES.Weapons.Ranged.Attachments.Components;
using Content.Shared._Exodus.Weapons.Events;

namespace Content.Shared._ES.Weapons.Ranged.Attachments;

public abstract partial class ESSharedGunAttachmentsSystem
{
    private void InitializeWieldModifiers()
    {
        SubscribeLocalEvent<ESAttachableGunComponent, GunWieldBonusRefreshEvent>(OnRefreshWieldBonus);
        SubscribeLocalEvent<ESGunRecoilAttachmentComponent, GunWieldBonusRefreshEvent>(OnAttachmentWieldBonus);
    }

    private void OnRefreshWieldBonus(Entity<ESAttachableGunComponent> ent, ref GunWieldBonusRefreshEvent args)
    {
        foreach (var slot in ent.Comp.Slots)
        {
            if (TryGetAttachment(ent, slot, out var attachment))
                RaiseLocalEvent(attachment.Value.Owner, ref args);
        }
    }

    private void OnAttachmentWieldBonus(Entity<ESGunRecoilAttachmentComponent> ent, ref GunWieldBonusRefreshEvent args)
    {
        args.MinAngle *= ent.Comp.WieldMinSpreadModifier;
        args.MaxAngle *= ent.Comp.WieldMaxSpreadModifier;
        args.AngleDecay *= ent.Comp.WieldRecoilRecoveryModifier;
        args.AngleIncrease *= ent.Comp.WieldRecoilIncreaseModifier;
    }
}
