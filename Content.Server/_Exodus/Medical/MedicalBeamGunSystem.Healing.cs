using Content.Shared._Exodus.Medical;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Server._Exodus.Medical;

public sealed partial class MedicalBeamGunSystem
{
    internal static FixedPoint2 GetHealingRate(MedicalBeamGunComponent gun, FixedPoint2 rate)
    {
        return gun.Mode == MedicalBeamMode.Automatic
            ? FixedPoint2.New(Math.Round(rate.Double() / Math.Max(1f, gun.AutomaticRateDivisor), MidpointRounding.AwayFromZero))
            : rate;
    }

    internal static float GetChargeRate(MedicalBeamGunComponent gun)
    {
        return Math.Max(0f, gun.ChargePerSecond);
    }

    /// <summary>
    /// Distributes a capped group budget proportionally across injuries still present.
    /// Carrying the remaining budget forward preserves hundredths without healing undamaged types.
    /// </summary>
    internal static void AddGroupHealing(DamageSpecifier injuries, IReadOnlyList<string> types,
        FixedPoint2 budget, DamageSpecifier healing)
    {
        if (budget <= FixedPoint2.Zero)
            return;

        var remainingDamage = FixedPoint2.Zero;
        for (var i = 0; i < types.Count; i++)
            remainingDamage += AvailableDamage(injuries, healing, types[i]);

        var remainingHealing = FixedPoint2.Min(budget, remainingDamage);
        for (var i = 0; i < types.Count; i++)
        {
            if (remainingHealing <= FixedPoint2.Zero)
                break;
            var type = types[i];
            var available = AvailableDamage(injuries, healing, type);
            if (available <= FixedPoint2.Zero)
                continue;

            var amount = FixedPoint2.FromHundredths((int) ((long) remainingHealing.Value * available.Value / remainingDamage.Value));
            AddTypeHealing(injuries, type, amount, healing);
            remainingHealing -= amount;
            remainingDamage -= available;
        }
    }

    internal static void AddTypeHealing(DamageSpecifier injuries, string type, FixedPoint2 budget, DamageSpecifier healing)
    {
        var amount = FixedPoint2.Min(budget, AvailableDamage(injuries, healing, type));
        if (amount > FixedPoint2.Zero)
            healing.DamageDict[type] = healing.DamageDict.GetValueOrDefault(type) - amount;
    }

    private static FixedPoint2 AvailableDamage(DamageSpecifier injuries, DamageSpecifier healing, string type)
    {
        return FixedPoint2.Max(FixedPoint2.Zero,
            injuries.DamageDict.GetValueOrDefault(type) + healing.DamageDict.GetValueOrDefault(type));
    }
}
