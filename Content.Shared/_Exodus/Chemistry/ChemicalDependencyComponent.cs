using Content.Shared.Alert;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Chemistry;

/// <summary>A configurable physiological need. Negative reserve records an unpaid withdrawal deficit.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class ChemicalDependencyComponent : Component
{
    /// <summary>The only reagent that satisfies this dependency.</summary>
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    /// <summary>Reserve replenished per unit actually metabolized.</summary>
    [DataField]
    public TimeSpan ReservePerUnit = TimeSpan.FromMinutes(1);

    /// <summary>Maximum stored supply and maximum recoverable withdrawal deficit.</summary>
    [DataField]
    public TimeSpan MaxReserve = TimeSpan.FromMinutes(30);

    [DataField]
    public TimeSpan MaxDeficit = TimeSpan.FromMinutes(5);

    /// <summary>Persisted across gene deactivation. Initial reserve is granted only on the first activation.</summary>
    [DataField]
    public TimeSpan Reserve = TimeSpan.FromMinutes(30);

    /// <summary>Stages are selected by time spent below zero reserve.</summary>
    [DataField]
    public List<ChemicalWithdrawalStage> Stages = new();

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [DataField, AutoPausedField]
    public TimeSpan NextUpdate;

    /// <summary>Only displayed minutes and current penalties are replicated, not the ticking reserve.</summary>
    [DataField, AutoNetworkedField]
    public int RemainingMinutes;

    [DataField, AutoNetworkedField]
    public int Stage = -1;

    [DataField, AutoNetworkedField]
    public float MovementMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float AttackRateMultiplier = 1f;

    /// <summary>Alert used by this dependency, including its minute counter.</summary>
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<AlertPrototype> Alert;
}

[DataDefinition]
public sealed partial class ChemicalWithdrawalStage
{
    /// <summary>Required deficit before this stage applies.</summary>
    [DataField]
    public TimeSpan After;

    [DataField]
    public float MovementMultiplier = 1f;

    [DataField]
    public float AttackRateMultiplier = 1f;

    /// <summary>Physiological damage per second; remains after withdrawal is relieved.</summary>
    [DataField]
    public DamageSpecifier Damage = new();

    [DataField(required: true)]
    public LocId Message;
}

public sealed partial class ChemicalDependencyAlertEvent : BaseAlertEvent;
