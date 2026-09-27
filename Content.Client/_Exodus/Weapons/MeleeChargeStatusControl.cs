using Content.Client.Items.UI;
using Content.Client.Stylesheets;
using Content.Shared._Exodus.Weapons.Melee;
using Content.Shared.Charges.Components;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Weapons;

public sealed class MeleeChargeStatusControl : PollingItemStatusControl<MeleeChargeStatusControl.Data>
{
    private readonly Entity<MeleeChargeComponent> _weapon;
    private readonly IEntityManager _entities;
    private readonly Label _label;

    public MeleeChargeStatusControl(Entity<MeleeChargeComponent> weapon, IEntityManager entities)
    {
        _weapon = weapon;
        _entities = entities;
        _label = new Label { StyleClasses = { StyleNano.StyleClassItemStatus } };
        AddChild(_label);
    }

    protected override Data PollData()
    {
        if (!_entities.TryGetComponent<LimitedChargesComponent>(_weapon, out var charges))
            return default;

        return new Data(charges.Charges, charges.MaxCharges);
    }

    protected override void Update(in Data data)
    {
        _label.Text = Loc.GetString("melee-charge-status",
            ("effect", Loc.GetString(_weapon.Comp.EffectName)),
            ("charges", data.Charges), ("max", data.MaxCharges));
    }

    public readonly record struct Data(int Charges, int MaxCharges);
}
