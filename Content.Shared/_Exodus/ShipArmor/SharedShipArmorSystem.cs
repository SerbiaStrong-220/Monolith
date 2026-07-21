// (c) Space Exodus Team
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Robust.Shared.Timing;

namespace Content.Shared._Exodus.ShipArmor;

/// <summary>
/// Shared helpers and examine for <see cref="ShipArmorComponent"/>.
/// Server system owns registration, absorption and regeneration.
/// </summary>
public abstract class SharedShipArmorSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming Timing = default!;

    // Reused by damage interception to avoid a per-hit allocation.
    private readonly List<(string Type, FixedPoint2 Amount)> _reductions = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipArmorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipArmorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<ShipArmorComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.CurrentCharge <= FixedPoint2.Zero && ent.Comp.MaxCharge > FixedPoint2.Zero)
            ent.Comp.CurrentCharge = ent.Comp.MaxCharge;

        if (ent.Comp.CurrentCharge > ent.Comp.MaxCharge)
            ent.Comp.CurrentCharge = ent.Comp.MaxCharge;

        Dirty(ent);
    }

    private void OnExamined(Entity<ShipArmorComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.ShowExamine || !args.IsInDetailsRange)
            return;

        var max = ent.Comp.MaxCharge;
        var current = ent.Comp.CurrentCharge;
        var percent = max > FixedPoint2.Zero
            ? (int)Math.Clamp((current.Float() / max.Float()) * 100f, 0f, 100f)
            : 0;

        var color = percent switch
        {
            > 66 => "green",
            >= 33 => "yellow",
            _ => "red",
        };

        args.PushMarkup(Loc.GetString("ship-armor-examine",
            ("current", current.Float().ToString("0.#")),
            ("max", max.Float().ToString("0.#")),
            ("percent", percent),
            ("color", color),
            ("radius", ent.Comp.Radius.ToString("0.#"))));
    }

    /// <summary>
    /// Absorption fraction for a damage type. Empty ratio map means full absorb for all types.
    /// </summary>
    protected static float GetAbsorbRatio(ShipArmorComponent armor, string damageType)
    {
        if (armor.AbsorbRatios.Count == 0)
            return 1f;

        var ratio = armor.AbsorbRatios.GetValueOrDefault(damageType, 0f);
        return float.IsFinite(ratio) ? Math.Clamp(ratio, 0f, 1f) : 0f;
    }

    /// <summary>
    /// Reduces <paramref name="damage"/> using the armor pool. Returns total damage absorbed (pre cost mult).
    /// </summary>
    protected FixedPoint2 TryAbsorb(
        Entity<ShipArmorComponent> armor,
        DamageSpecifier damage,
        float armorPenetration)
    {
        if (!armor.Comp.Enabled || armor.Comp.CurrentCharge <= FixedPoint2.Zero || !damage.AnyPositive())
            return FixedPoint2.Zero;

        var penFactor = armorPenetration >= 1f ? 0f : Math.Max(0f, 1f - armorPenetration);
        if (penFactor <= 0f)
            return FixedPoint2.Zero;

        var costMult = Math.Max(0f, armor.Comp.ChargeCostMultiplier);
        if (costMult <= 0f)
            return FixedPoint2.Zero;

        var charge = armor.Comp.CurrentCharge;
        var absorbedTotal = FixedPoint2.Zero;

        _reductions.Clear();

        foreach (var (type, amount) in damage.DamageDict)
        {
            if (amount <= FixedPoint2.Zero || charge <= FixedPoint2.Zero)
                continue;

            var ratio = GetAbsorbRatio(armor.Comp, type) * penFactor;
            if (ratio <= 0f)
                continue;

            var wanted = amount * ratio;
            var cost = wanted * costMult;
            if (cost > charge)
            {
                wanted = charge / costMult;
                cost = charge;
            }

            if (wanted <= FixedPoint2.Zero)
                continue;

            _reductions.Add((type, wanted));
            charge -= cost;
            absorbedTotal += wanted;
        }

        if (absorbedTotal <= FixedPoint2.Zero)
        {
            _reductions.Clear();
            return FixedPoint2.Zero;
        }

        for (var i = 0; i < _reductions.Count; i++)
        {
            var (type, amount) = _reductions[i];
            var remaining = damage.DamageDict[type] - amount;
            if (remaining <= FixedPoint2.Zero)
                damage.DamageDict.Remove(type);
            else
                damage.DamageDict[type] = remaining;
        }

        _reductions.Clear();
        CommitAbsorption(armor, charge);
        return absorbedTotal;
    }

    /// <summary>
    /// Absorbs a single damage type without creating a DamageSpecifier.
    /// Used by tile damage hooks, which are called directly from tile-processing hot paths.
    /// </summary>
    protected FixedPoint2 TryAbsorbAmount(
        Entity<ShipArmorComponent> armor,
        FixedPoint2 amount,
        string damageType,
        float armorPenetration)
    {
        if (!armor.Comp.Enabled || armor.Comp.CurrentCharge <= FixedPoint2.Zero || amount <= FixedPoint2.Zero)
            return FixedPoint2.Zero;

        var penFactor = armorPenetration >= 1f ? 0f : Math.Max(0f, 1f - armorPenetration);
        var ratio = GetAbsorbRatio(armor.Comp, damageType) * penFactor;
        var costMult = Math.Max(0f, armor.Comp.ChargeCostMultiplier);
        if (ratio <= 0f || costMult <= 0f)
            return FixedPoint2.Zero;

        var wanted = amount * ratio;
        var cost = wanted * costMult;
        if (cost > armor.Comp.CurrentCharge)
        {
            wanted = armor.Comp.CurrentCharge / costMult;
            cost = armor.Comp.CurrentCharge;
        }

        if (wanted <= FixedPoint2.Zero)
            return FixedPoint2.Zero;

        CommitAbsorption(armor, armor.Comp.CurrentCharge - cost);
        return wanted;
    }

    private void CommitAbsorption(Entity<ShipArmorComponent> armor, FixedPoint2 charge)
    {
        var oldCharge = armor.Comp.CurrentCharge;
        armor.Comp.CurrentCharge = charge;
        armor.Comp.NextUpdate = Timing.CurTime + armor.Comp.RegenDelay;
        Dirty(armor);

        var changed = new ShipArmorChargeChangedEvent(oldCharge, charge, armor.Comp.MaxCharge);
        RaiseLocalEvent(armor, ref changed);

        if (charge <= FixedPoint2.Zero && oldCharge > FixedPoint2.Zero)
        {
            var depleted = new ShipArmorDepletedEvent();
            RaiseLocalEvent(armor, ref depleted);
        }

        EnsureComp<ActiveShipArmorComponent>(armor);
    }
}
