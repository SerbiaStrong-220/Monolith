using Content.Shared._Exodus.NPC.Command;
using Content.Shared.Dataset;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC.Command;

[RegisterComponent, Access(typeof(NpcCommandSystem))]
public sealed partial class NpcCommanderComponent : Component
{
    /// <summary>Recruiting range in tiles.</summary>
    [DataField]
    public float Range = 12f;

    /// <summary>Recruited NPCs will try thier best to stay within this.</summary>
    [DataField]
    public float KeepRange = 25f;

    /// <summary>This is max available slots for recruited NPCs simultaniously .</summary>
    /// What is medic - defined inside mob proto   
    /// - type: NpcMinion
    ///         medic: true
    [DataField]
    public int MaxMedics = 1;

    /// Grunt is - type: NpcMinion
    [DataField]
    public int MaxGrunts = 3;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long the commander engages a target before ordering an assault.</summary>
    [DataField]
    public TimeSpan EngageDelay = TimeSpan.FromSeconds(4);

    /// <summary>Grace period of engagement timer so a target flickering in and out of combat doesn't keep restarting delay.</summary>
    [DataField]
    public TimeSpan EnemyGrace = TimeSpan.FromSeconds(2);

    /// <summary>How long an assault order lasts.</summary>
    [DataField]
    public TimeSpan AssaultDuration = TimeSpan.FromSeconds(10);

    /// <summary>Assault order cooldown.</summary>
    [DataField]
    public TimeSpan AssaultCooldown = TimeSpan.FromSeconds(20);

    /// <summary>How long a regroup lasts (probably enough time to reach the chosen point) before returning to patrol.</summary>
    [DataField]
    public TimeSpan RetreatDuration = TimeSpan.FromSeconds(15);

    /// <summary>How long a hold-position order lasts (basically this exist to stop mobs from chasing surviving players).</summary>
    [DataField]
    public TimeSpan HoldDuration = TimeSpan.FromSeconds(60);

    /// <summary>Commander health breakpoint at which he gives order to retreat.</summary>
    [DataField]
    public float RetreatHealth = 0.4f;

    /// <summary>This is Blackboard key the commander's current enemy is defined by.</summary>
    [DataField]
    public string TargetKey = "Target";

    /// <summary>Random voice lines on issuing each order.</summary>
    [DataField]
    public ProtoId<LocalizedDatasetPrototype> AttackLines = "NpcOrderAttackLines";

    [DataField]
    public ProtoId<LocalizedDatasetPrototype> HoldLines = "NpcOrderHoldLines";

    [DataField]
    public ProtoId<LocalizedDatasetPrototype> RetreatLines = "NpcOrderRetreatLines";

    /// <summary>
    /// If set, the commander PLAYS sound on an order instead of speaking a line — for
    /// non-speaking commanders like fauna. Should be null for speaking commanders.
    /// </summary>
    [DataField]
    public SoundSpecifier? OrderSound;

    [ViewVariables]
    public TimeSpan NextUpdate;

    /// <summary>The squad order currently in effect.</summary>
    [ViewVariables]
    public NpcOrder Order = NpcOrder.Follow;

    /// <summary>Current order time left.</summary>
    [ViewVariables]
    public TimeSpan? OrderUntil;

    /// <summary>Time till next assault.</summary>
    [ViewVariables]
    public TimeSpan NextAssault;

    /// <summary>When the current engagement started (for engage delay).</summary>
    [ViewVariables]
    public TimeSpan? EngageSince;

    /// <summary>Last time enemy was seen by commander (for enemy grace).</summary>
    [ViewVariables]
    public TimeSpan? LastSeenEnemyTime;

    /// <summary>The target grunts are ordered to attack on assault.</summary>
    [ViewVariables]
    public EntityUid? AssaultTarget;

    /// <summary>The assault target's last-known position so grunts can push to where it was last seen.</summary>
    [ViewVariables]
    public EntityCoordinates? AssaultTargetCoordinates;

    /// <summary>Grunts ever recruited.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Members = new();

    /// <summary>Currently recruited NPCs.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Retinue = new();
}
