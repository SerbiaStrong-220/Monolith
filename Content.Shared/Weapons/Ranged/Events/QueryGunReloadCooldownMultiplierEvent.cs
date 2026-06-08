// Exodus-begin nebula weapon cooldown
namespace Content.Shared.Weapons.Ranged.Events;

/// <summary>
/// Raised before applying non-shot reload cooldowns, such as burst cooldown.
/// Higher values make reloads slower.
/// </summary>
[ByRefEvent]
public record struct QueryGunReloadCooldownMultiplierEvent(float ReloadCooldownMultiplier = 1f);
// Exodus-end
