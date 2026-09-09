using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

public sealed class TerritoryRegenerationSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private EntityQuery<GridTerritoryComponent> _territoryQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        base.Initialize();
        _territoryQuery = GetEntityQuery<GridTerritoryComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();
        SubscribeLocalEvent<TerritoryRegenerationComponent, ModifyPassiveDamageEvent>(OnPassiveDamage);
    }

    private void OnPassiveDamage(Entity<TerritoryRegenerationComponent> ent, ref ModifyPassiveDamageEvent args)
    {
        if (_transformQuery.GetComponent(ent).GridUid is not { } grid ||
            !_territoryQuery.TryGetComponent(grid, out var territory) ||
            !territory.Claimable || territory.ControllingFaction != ent.Comp.Faction ||
            territory.ActiveClaimBanner == null ||
            !_prototype.TryIndex(ent.Comp.Faction, out var faction) ||
            !float.IsFinite(faction.PassiveHealingMultiplier) || faction.PassiveHealingMultiplier <= 1f)
        {
            return;
        }

        // The networked grid is authoritative; the distant physical core may be outside the client's PVS.
        // Only healing is multiplied. Keep the prototype's DamageSpecifier untouched.
        DamageSpecifier? modified = null;
        foreach (var (type, amount) in args.Damage.DamageDict)
        {
            if (amount >= FixedPoint2.Zero)
                continue;

            modified ??= new DamageSpecifier(args.Damage);
            modified.DamageDict[type] = amount * faction.PassiveHealingMultiplier;
        }

        if (modified != null)
            args.Damage = modified;
    }
}
