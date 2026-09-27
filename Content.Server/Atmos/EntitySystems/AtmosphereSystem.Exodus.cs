// Exodus-begin
using Content.Shared.Atmos;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    private bool HasContactFireFuel(GasMixture mixture)
    {
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            if (GasPrototypes[i].ContactFireMoles is not { } minimum || minimum <= 0)
                continue;

            var fuel = mixture.GetMoles(i);
            if (fuel >= minimum && mixture.TotalMoles - fuel >= minimum)
                return true;
        }

        return false;
    }
}
// Exodus-end
