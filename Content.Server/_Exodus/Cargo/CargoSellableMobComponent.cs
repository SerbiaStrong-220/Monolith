// (c) Space Exodus Team - EXDS-RL with CLA
namespace Content.Server._Exodus.Cargo;

/// <summary>
/// Allows cargo to sell an operational NPC as equipment, provided it has no player or mind.
/// Does not bypass cargo blacklists or the checks on its contents.
/// </summary>
[RegisterComponent]
public sealed partial class CargoSellableMobComponent : Component;
