using Content.Shared._Exodus.Chemistry;
using Content.Shared._Exodus.Nutrition;
using Content.Shared.Alert;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Chemistry;

/// <summary>Consumes a persisted chemical reserve, independently of the drug's short combat effect.</summary>
public sealed partial class ChemicalDependencySystem : SharedChemicalEffectsSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private NeedsActivationSystem _needsActivation = default!;

    private EntityQuery<ActorComponent> _actorQuery;

    public override void Initialize()
    {
        base.Initialize();
        _actorQuery = GetEntityQuery<ActorComponent>();
        SubscribeLocalEvent<ChemicalDependencyComponent, ChemicalDependencyAlertEvent>(OnAlert);
    }

    protected override void OnDependencyStartup(Entity<ChemicalDependencyComponent> ent, ref ComponentStartup args)
    {
        // Dormant genes keep their reserve, but must not catch up the time they were disabled.
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.UpdateInterval;
        Refresh(ent, false);
        base.OnDependencyStartup(ent, ref args);
    }

    protected override void OnDependencyShutdown(Entity<ChemicalDependencyComponent> ent, ref ComponentShutdown args)
    {
        base.OnDependencyShutdown(ent, ref args);
        if (!TerminatingOrDeleted(ent))
            _alerts.ClearAlert(ent, ent.Comp.Alert);
    }

    public bool TrySatisfy(EntityUid uid, ProtoId<ReagentPrototype> reagent, FixedPoint2 amount)
    {
        if (amount <= FixedPoint2.Zero || !TryComp<ChemicalDependencyComponent>(uid, out var dependency) ||
            dependency.Reagent != reagent || !TryComp<MobStateComponent>(uid, out var mob) || mob.CurrentState == MobState.Dead)
            return false;

        var replenished = dependency.Reserve + dependency.ReservePerUnit * amount.Float();
        dependency.Reserve = replenished > dependency.MaxReserve ? dependency.MaxReserve : replenished;
        Refresh((uid, dependency));
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<ChemicalDependencyComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var dependency, out var mob))
        {
            if (_timing.CurTime < dependency.NextUpdate)
                continue;
            dependency.NextUpdate += dependency.UpdateInterval;
            if (mob.CurrentState == MobState.Dead || !_needsActivation.AreNeedsActive(uid))
                continue;

            // Keep advancing NextUpdate while absent so returning never charges missed withdrawal ticks.
            if (dependency.PauseWithoutPlayer &&
                (!_actorQuery.TryComp(uid, out var actor) || actor.PlayerSession.Status != SessionStatus.InGame))
                continue;

            dependency.Reserve -= dependency.UpdateInterval;
            if (dependency.Reserve < -dependency.MaxDeficit)
                dependency.Reserve = -dependency.MaxDeficit;
            Refresh((uid, dependency));
            if (dependency.Stage >= 0)
            {
                var damage = dependency.Stages[dependency.Stage].Damage;
                if (!damage.Empty)
                    _damage.TryChangeDamage(uid, damage * (float) dependency.UpdateInterval.TotalSeconds, true, false);
            }
        }
    }

    private void Refresh(Entity<ChemicalDependencyComponent> ent, bool notify = true)
    {
        var stage = -1;
        for (var i = 0; i < ent.Comp.Stages.Count; i++)
        {
            if (ent.Comp.Reserve <= -ent.Comp.Stages[i].After &&
                (stage < 0 || ent.Comp.Stages[i].After > ent.Comp.Stages[stage].After))
                stage = i;
        }

        var minutes = Math.Max(0, (int) Math.Ceiling(ent.Comp.Reserve.TotalMinutes));
        var movement = stage < 0 ? 1f : ent.Comp.Stages[stage].MovementMultiplier;
        var attackRate = stage < 0 ? 1f : ent.Comp.Stages[stage].AttackRateMultiplier;
        var changed = ent.Comp.Stage != stage || ent.Comp.RemainingMinutes != minutes ||
                      ent.Comp.MovementMultiplier != movement || ent.Comp.AttackRateMultiplier != attackRate;
        var previous = ent.Comp.Stage;
        var movementChanged = ent.Comp.MovementMultiplier != movement;
        ent.Comp.Stage = stage;
        ent.Comp.RemainingMinutes = minutes;
        ent.Comp.MovementMultiplier = movement;
        ent.Comp.AttackRateMultiplier = attackRate;
        if (changed)
            Dirty(ent);
        if (movementChanged)
            _movement.RefreshMovementSpeedModifiers(ent);
        _alerts.ShowAlert(ent, ent.Comp.Alert);
        if (notify && stage >= 0 && stage != previous)
            _popup.PopupEntity(Loc.GetString(ent.Comp.Stages[stage].Message), ent, ent, PopupType.MediumCaution);
    }

    private void OnAlert(Entity<ChemicalDependencyComponent> ent, ref ChemicalDependencyAlertEvent args)
    {
        if (args.Handled || args.AlertId != ent.Comp.Alert)
            return;
        args.Handled = true;
        var reagent = _prototypes.Index(ent.Comp.Reagent).LocalizedName;
        var message = ent.Comp.Reserve > TimeSpan.Zero
            ? Loc.GetString("chemical-dependency-reserve", ("reagent", reagent), ("minutes", ent.Comp.RemainingMinutes))
            : Loc.GetString("chemical-dependency-deficit", ("reagent", reagent),
                ("units", Math.Round(-ent.Comp.Reserve.TotalSeconds / ent.Comp.ReservePerUnit.TotalSeconds, 1)));
        _popup.PopupEntity(message, ent, ent);
    }
}
