using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC.Abilities;

[RegisterComponent, Access(typeof(NpcThrowSystem))]
public sealed partial class NpcThrownWeaponComponent : Component
{
    /// <summary>Action granted for this ability (HTN uses this id).</summary>
    /// Actions with target only
    [DataField]
    public EntProtoId Action = "ActionThrowNPC";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>Cooldown.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(20);

    /// <summary>Entity prototype spawned and thrown.</summary>
    [DataField]
    public EntProtoId Proto = "ThrowingKnife";

    /// <summary>If true, start item's timer (prime a grenade) before throwing.</summary>
    [DataField]
    public bool Prime;

    /// <summary>Random scatter (tiles) added to the aim. 0 = throw straight at the target, recommended to add some (3-5) for grenades.</summary>
    [DataField]
    public float Spread = 0f;

    /// <summary>Aim is capped at distance (tiles).</summary>
    [DataField]
    public float MaxRange = 12f;

    /// <summary>Proj speed.</summary>
    [DataField]
    public float Speed = 10f;
}
