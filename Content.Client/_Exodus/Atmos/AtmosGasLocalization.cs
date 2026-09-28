using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;

namespace Content.Client._Exodus.Atmos;

public static class AtmosGasLocalization
{
    public static string GetName(Gas gas, SharedAtmosphereSystem atmosphere, ILocalizationManager localization)
    {
        if (localization.TryGetString($"atmos-gas-{gas}", out var name))
            return name;

        return localization.GetString(atmosphere.GetGas(gas).Name);
    }
}
