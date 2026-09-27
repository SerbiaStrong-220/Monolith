using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server._Exodus.Atmos.Reactions;

/// <summary>
/// A configured fuel reacts with any other gas, consuming both and releasing bounded heat.
/// Pure fuel remains stable, including in a vacuum or an uncontaminated pipe network.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class GasContactReaction : IGasReactionEffect
{
    [DataField(required: true)]
    public Gas Fuel;

    [DataField(required: true)]
    public Gas Product;

    [DataField]
    public float MinimumOtherMoles = 0.1f;

    [DataField]
    public float BurnFraction = 0.25f;

    [DataField]
    public float MaximumBurnMoles = 10f;

    [DataField]
    public float EnergyPerMole = 60000f;

    [DataField]
    public float MaximumTemperature = 3000f;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        if (mixture.Immutable || Fuel == Product)
            return ReactionResult.NoReaction;

        var fuel = mixture.GetMoles(Fuel);
        var otherMoles = mixture.TotalMoles - fuel;
        if (fuel <= 0 || otherMoles <= 0 || otherMoles < MinimumOtherMoles)
            return ReactionResult.NoReaction;

        var burned = MathF.Min(MathF.Min(fuel * Math.Clamp(BurnFraction, 0, 1), otherMoles), MaximumBurnMoles);
        if (burned <= 0)
            return ReactionResult.NoReaction;

        var oldTemperature = mixture.Temperature;
        var oldEnergy = oldTemperature * atmosphereSystem.GetHeatCapacity(mixture, true);
        // Consume one mole of the surrounding mixture for each mole of fuel, preserving its proportions.
        var remainingRatio = Math.Clamp(1 - burned / otherMoles, 0, 1);
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            if (i != (int)Fuel)
                mixture.SetMoles(i, mixture.GetMoles(i) * remainingRatio);
        }

        mixture.AdjustMoles(Fuel, -burned);
        mixture.AdjustMoles(Product, 2 * burned);
        var capacity = atmosphereSystem.GetHeatCapacity(mixture, true);
        if (capacity > Atmospherics.MinimumHeatCapacity)
        {
            var energy = Math.Max(0, EnergyPerMole) * burned / Math.Max(1, heatScale);
            var temperature = (oldEnergy + energy) / capacity;
            // Limit only the additional heating; already-hot gas must not be artificially cooled.
            mixture.Temperature = Math.Clamp(temperature, oldTemperature, Math.Max(oldTemperature, MaximumTemperature));
        }

        mixture.ReactionResults[(byte)GasReaction.Fire] = 2 * burned;
        if (holder is TileAtmosphere tile && mixture.Temperature > Atmospherics.FireMinimumTemperatureToExist)
            atmosphereSystem.HotspotExpose(tile, mixture.Temperature, mixture.Volume);

        return ReactionResult.Reacting;
    }
}
