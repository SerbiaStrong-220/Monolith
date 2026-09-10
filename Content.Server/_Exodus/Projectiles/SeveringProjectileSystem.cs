using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Administration.Logs;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Database;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Projectiles;

public sealed class SeveringProjectileSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLog = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BodyComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnDamageChanged(Entity<BodyComponent> ent, ref DamageChangedEvent args)
    {
        // DamageChanged runs after damage and reflection checks, so blocked hits cannot sever limbs.
        if (!args.DamageIncreased
            || !args.CanSever
            || args.Tool is not { } tool
            || !TryComp<SeveringProjectileComponent>(tool, out var severing)
            || TerminatingOrDeleted(ent)
            || !ent.Comp.Initialized
            || !_random.Prob(Math.Clamp(severing.Chance, 0f, 1f)))
        {
            return;
        }

        Entity<BodyPartComponent>? selected = null;
        var count = 0;
        // Reservoir sampling selects uniformly without allocating a candidate list.
        foreach (var (uid, part) in _body.GetBodyChildren(ent, ent.Comp))
        {
            if (!part.CanSever
                || part.Body != ent.Owner
                || !severing.Parts.Contains(part.PartType)
                || TerminatingOrDeleted(uid)
                || !part.Initialized)
            {
                continue;
            }

            if (_random.Next(++count) == 0)
                selected = (uid, part);
        }

        if (selected is not { } limb)
            return;

        var amputate = new AmputateAttemptEvent(limb.Owner);
        RaiseLocalEvent(limb, ref amputate);

        if (limb.Comp.Body != ent.Owner)
        {
            _adminLog.Add(LogType.BulletHit, LogImpact.High,
                $"Projectile {ToPrettyString(tool):tool} severed {ToPrettyString(limb):part} from {ToPrettyString(ent):target}");
        }
    }
}
