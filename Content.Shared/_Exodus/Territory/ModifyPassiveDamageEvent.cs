using Content.Shared.Damage;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Allows opt-in modifiers to replace a passive damage pulse without mutating its source component.
/// </summary>
[ByRefEvent]
public record struct ModifyPassiveDamageEvent(DamageSpecifier Damage);
