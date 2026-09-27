using Content.Shared._Mono.PersonalShield;

namespace Content.Shared._Mono.MAWC.Shields;

public sealed class SharedShieldLinkSystem : EntitySystem
{
    private EntityQuery<ShieldLinkSourceComponent> _sourceQuery; // Exodus: shield stats are refreshed every tick.

    public override void Initialize()
    {
        base.Initialize();
        _sourceQuery = GetEntityQuery<ShieldLinkSourceComponent>(); // Exodus

        SubscribeLocalEvent<ShieldLinkSourceComponent, GetPersonalShieldStatsEvent>(OnGetSourceShieldStats);
        SubscribeLocalEvent<ShieldLinkReceiverComponent, GetPersonalShieldStatsEvent>(OnGetReceiverShieldStats);
    }

    private void OnGetSourceShieldStats(Entity<ShieldLinkSourceComponent> ent, ref GetPersonalShieldStatsEvent args)
    {
        args.MaxCharge += GetMaxChargeBonus(ent.Comp);
    }

    private void OnGetReceiverShieldStats(Entity<ShieldLinkReceiverComponent> ent, ref GetPersonalShieldStatsEvent args)
    {
        foreach (var sourceUid in ent.Comp.LinkedSources)
        {
            if (_sourceQuery.TryGetComponent(sourceUid, out var source) && // Exodus: cached lookup in the shield update path.
                source.LifeStage < ComponentLifeStage.Stopping && // Exodus: ignore shutting-down sources.
                source.LinkedShields.Contains(ent.Owner))
            {
                args.MaxCharge += GetMaxChargeBonus(source);
            }
        }
    }

    private static float GetMaxChargeBonus(ShieldLinkSourceComponent source)
    {
        return source.ShieldBonusPerLink * source.LinkedShields.Count;
    }
}
