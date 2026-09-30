using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Medical;

/// <summary>A single-patient organic shelter with configurable treatment and admission rules.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HealingCocoonComponent : Component
{
    /// <summary>Container slot holding the patient.</summary>
    [DataField] public string ContainerId = "patient";
    /// <summary>Required patient components; evaluated on both client and server.</summary>
    [DataField(required: true)] public EntityWhitelist Whitelist = new();
    /// <summary>Patient types which cannot enter.</summary>
    [DataField] public EntityWhitelist? Blacklist;
    /// <summary>Damage changes applied per treatment interval.</summary>
    [DataField] public DamageSpecifier Healing = new();
    /// <summary>Time between treatment pulses.</summary>
    [DataField] public TimeSpan Interval = TimeSpan.FromSeconds(1);
    /// <summary>Temporarily supports respiration while the living patient remains inside.</summary>
    [DataField] public bool SupportsBreathing = true;
    /// <summary>Optional nonfunctional remains left when the shelter is destroyed.</summary>
    [DataField] public EntProtoId? RemainsPrototype;
    /// <summary>Whether rupture has already been handled, preventing duplicate remains in the same tick.</summary>
    [DataField, AutoNetworkedField] public bool Ruptured;
}

[Serializable, NetSerializable]
public enum HealingCocoonVisuals : byte
{
    Occupied,
}
