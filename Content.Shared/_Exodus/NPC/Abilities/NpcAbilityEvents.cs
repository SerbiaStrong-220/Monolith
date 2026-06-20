using Content.Shared.Actions;

namespace Content.Shared._Exodus.NPC.Abilities;

// NPC ability action events. These are just triggers. You can configure everything u want in components,
// (in a mob proto to be exact, pls dont change default values without good reason)

/// <summary>Raised when an NPC uses its self-heal ability. Handled by NpcSelfHealSystem.</summary>
public sealed partial class NpcSelfHealActionEvent : InstantActionEvent;

/// <summary>Raised when an NPC injects an ally. Handled by NpcHealAbilitiesSystem.</summary>
public sealed partial class NpcHealInjectionActionEvent : EntityTargetActionEvent;

/// <summary>Raised when an NPC uses its healing-aura ability. Handled by NpcHealAbilitiesSystem.</summary>
public sealed partial class NpcHealAuraActionEvent : InstantActionEvent;

/// <summary>Raised when an NPC throws an item at a target. Handled by NpcThrowSystem.</summary>
public sealed partial class NpcThrowActionEvent : EntityTargetActionEvent;

/// <summary>
/// Raised when NPC medic defibrillates a downed ally. Handled by NpcHealAbilitiesSystem. 
/// The charge sound + wait are done in the HTN tree so the medic holds position.
/// </summary>
public sealed partial class NpcReviveActionEvent : EntityTargetActionEvent;
