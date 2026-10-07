namespace Content.Shared._Exodus.Weapons.Melee;

/// <summary>
/// Allows a melee weapon to opt into damage-tool attribution for effects that require it.
/// Leaving the tool unset preserves the attacker's destruction and mining attribution.
/// </summary>
[ByRefEvent]
public record struct GetMeleeDamageToolEvent(EntityUid? Tool = null);
