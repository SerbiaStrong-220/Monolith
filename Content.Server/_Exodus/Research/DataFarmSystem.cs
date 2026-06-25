// (c) Space Exodus Team - EXDS-RL
// Authors: DarkBanOne

using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.EntitySystems;
using Content.Server._Exodus.Research.Components;
using Content.Shared.Power;
using Content.Server.Research.Components;
using Content.Shared._Exodus.Research.Visuals;
using Content.Shared.Audio;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._Exodus.Research.Systems;

public sealed class DataFarmSystem : EntitySystem
{
    [Dependency] private NodeContainerSystem _nodeContainer = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DataFarmComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
        SubscribeLocalEvent<DataFarmComponent, PowerChangedEvent>(OnPowerChanged);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<DataFarmComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Powered)
            {
                SetEnabled((uid, comp), false);
                SetState((uid, comp), DataFarmState.Off);
                continue;
            }

            if (comp.NextAt == null && comp.Powered)
            {
                comp.NextAt = _timing.CurTime + comp.StartupDuration;
                SetEnabled((uid, comp), false);
                SetState((uid, comp), DataFarmState.Process);
            }

            if (comp.CurrentState == DataFarmState.Destruct && _timing.CurTime >= comp.NextDamageAt)
            {
                ApplyDamage((uid, comp));
                comp.NextDamageAt = _timing.CurTime + comp.CycleDuration;
            }
        }
    }

    private void OnAtmosUpdate(Entity<DataFarmComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if ((ent.Comp.CurrentState == DataFarmState.Off || ent.Comp.CurrentState == DataFarmState.Process)
            && _timing.CurTime < ent.Comp.NextAt)
            return;

        if (ent.Comp.IntakePerSecond <= 0f ||
            !_nodeContainer.TryGetNode(ent.Owner, ent.Comp.InletName, out PipeNode? inlet))
        {
            SetEnabled((ent.Owner, ent.Comp), false);
            SetState((ent.Owner, ent.Comp), DataFarmState.Off);

            return;
        }

        var env = _atmos.GetContainingMixture(ent.Owner, ignoreExposed: true, excite: true);
        var takeNow = ent.Comp.IntakePerSecond * args.dt;

        if (env == null
            || env.Temperature < ent.Comp.MinTemp
            || env.TotalMoles < ent.Comp.MinMolesOnTile
            || env.Pressure < ent.Comp.MinPressure
            || inlet.Air.TotalMoles < takeNow)
        {
            SetEnabled((ent.Owner, ent.Comp), false);
            SetState((ent.Owner, ent.Comp), DataFarmState.Warning);

            return;
        }

        SetEnabled((ent.Owner, ent.Comp), true);

        var removed = inlet.Air.Remove(takeNow);
        _atmos.Merge(ent.Comp.Buffer, removed);

        if (_timing.CurTime < ent.Comp.NextAt || ent.Comp.Buffer.TotalMoles <= 0f || !ent.Comp.Enabled)
            return;

        if (env.Temperature > ent.Comp.MaxTemp)
        {
            SetState((ent.Owner, ent.Comp), DataFarmState.Destruct);
        }
        else
        {
            SetState((ent.Owner, ent.Comp), DataFarmState.Normal);
        }

        var c = _atmos.GetHeatCapacity(ent.Comp.Buffer, applyScaling: true);
        var dQ = c * ent.Comp.DeltaT;
        _atmos.AddHeat(ent.Comp.Buffer, dQ);

        _atmos.Merge(env, ent.Comp.Buffer);
        ent.Comp.Buffer.Clear();
        ent.Comp.NextAt += ent.Comp.CycleDuration;
    }

    private void OnPowerChanged(Entity<DataFarmComponent> ent, ref PowerChangedEvent args)
    {
        if (ent.Comp.Powered == args.Powered)
            return;

        ent.Comp.Powered = args.Powered;
    }

    public void SetEnabled(Entity<DataFarmComponent> ent, bool value)
    {
        if (!TryComp<ResearchPointSourceComponent>(ent.Owner, out var sourceComp))
            return;

        if (ent.Comp.Enabled == value)
            return;

        ent.Comp.Enabled = value;

        sourceComp.Active = ent.Comp.Enabled;
    }

    public void ApplyDamage(Entity<DataFarmComponent> ent)
    {
        var damagePerSecond = GetDamagePerSecond(ent);

        if (damagePerSecond <= 0f)
            return;

        var damageType = _prototypeManager.Index(ent.Comp.DamageType);
        var damage = new DamageSpecifier(damageType, damagePerSecond);

        _damageable.TryChangeDamage(ent.Owner, damage, ignoreResistances: true, interruptsDoAfters: false);
    }

    private void SetSound(Entity<DataFarmComponent> ent, DataFarmState state)
    {
        if (state == ent.Comp.CurrentState)
            return;

        EnsureComp<AmbientSoundComponent>(ent.Owner);

        SoundSpecifier? sound = state switch
        {
            DataFarmState.Off => null,
            DataFarmState.Process => ent.Comp.ProcessSound,
            DataFarmState.Normal => ent.Comp.NormalSound,
            DataFarmState.Warning => ent.Comp.WarningSound,
            DataFarmState.Destruct => ent.Comp.ErrorSound,
            _ => null
        };

        _ambient.SetAmbience(ent.Owner, sound != null);

        if (sound != null)
            _ambient.SetSound(ent.Owner, sound);
    }

    public void SetState(Entity<DataFarmComponent> ent, DataFarmState state)
    {
        if (ent.Comp.CurrentState == state)
            return;

        SetSound(ent, state);

        ent.Comp.CurrentState = state;

        if (state == DataFarmState.Destruct)
        {
            ent.Comp.NextDamageAt = _timing.CurTime + ent.Comp.CycleDuration;
        }
        else
        {
            ent.Comp.NextDamageAt = null;
        }

        if (TryComp<AppearanceComponent>(ent.Owner, out var appearance))
            _appearance.SetData(ent.Owner, DataFarmVisuals.State, state, appearance);
    }

    private float GetDamagePerSecond(Entity<DataFarmComponent> ent)
    {
        if (!TryComp<DestructibleComponent>(ent.Owner, out var destrComp))
            return 0f;

        int? destructionThreshold = null;

        foreach (var threshold in destrComp.Thresholds)
        {
            if (threshold.Trigger is not DamageTrigger dmgTrigger)
                continue;

            var hasDestructionAct = threshold.Behaviors
                .OfType<DoActsBehavior>()
                .Any(b => b.HasAct(ThresholdActs.Destruction));

            if (!hasDestructionAct)
                continue;

            destructionThreshold = destructionThreshold is null
                ? dmgTrigger.Damage
                : Math.Min(destructionThreshold.Value, dmgTrigger.Damage);
        }

        if (destructionThreshold is null || ent.Comp.DestroyTimer.TotalSeconds <= 0)
            return 0f;

        return (float)(destructionThreshold.Value / ent.Comp.DestroyTimer.TotalSeconds);
    }
}
