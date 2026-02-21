using Robust.Shared.Configuration;

namespace Content.Shared._Goobstation.CCVars;

[CVarDefs]
public sealed partial class GoobCVars
{
    #region Mechs

    /// <summary>
    ///     Whether or not players can use mech guns outside of mechs.
    /// </summary>
    public static readonly CVarDef<bool> MechGunOutsideMech =
        CVarDef.Create("mech.gun_outside_mech", true, CVar.SERVER | CVar.REPLICATED);

    #endregion

    #region Space Whale

    /// <summary>
    /// Whether or not to spawn space whales if the entity is too far away from the station
    /// </summary>
    public static readonly CVarDef<bool> SpaceWhaleSpawn =
        CVarDef.Create("misc.space_whale_spawn", true, CVar.SERVER);

    /// <summary>
    /// The distance to spawn a space whale from the station
    /// </summary>
    public static readonly CVarDef<int> SpaceWhaleSpawnDistance =
        CVarDef.Create("misc.space_whale_spawn_distance", 1000, CVar.SERVER);

    public static readonly CVarDef<string> SpaceWhalePrototype =
        CVarDef.Create("misc.space_whale_prototype", "SpaceLeviathanDespawn", CVar.SERVER);

    #endregion
}
