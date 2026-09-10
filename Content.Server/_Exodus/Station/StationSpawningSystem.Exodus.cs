using Content.Shared._Exodus.NameModifier;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Random.Helpers;
using Content.Shared.Roles;

namespace Content.Server.Station.Systems;

public sealed partial class StationSpawningSystem
{
    private string GetRoleCharacterName(
        HumanoidCharacterProfile profile,
        RoleLoadout? loadout,
        RoleLoadoutPrototype? role)
    {
        if (role?.CanCustomizeName == true && loadout?.EntityName is { } customName && !string.IsNullOrWhiteSpace(customName))
            return customName;

        if (role != null && _prototypeManager.TryIndex(role.NameDataset, out var dataset))
            return _random.Pick(dataset);

        return profile.Name;
    }

    private void UpdatePrefixedJobIdentity(EntityUid uid, JobPrototype? job, EntityUid? station)
    {
        if (job != null && HasComp<EntityNamePrefixComponent>(uid))
            SetPdaAndIdCardData(uid, Name(uid), job, station);
    }
}
