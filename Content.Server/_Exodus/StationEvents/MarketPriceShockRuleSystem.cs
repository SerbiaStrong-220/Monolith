// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server._Exodus.StationEvents.Components;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Exodus.StationEvents;

/// <summary>
/// Changes global commodity prices once, then leaves ordinary trading and mean reversion in control.
/// </summary>
public sealed partial class MarketPriceShockRuleSystem : StationEventSystem<MarketPriceShockRuleComponent>
{
    [Dependency] private DynamicMarketSystem _market = default!;
    [Dependency] private MarketBasketSystem _baskets = default!;
    [Dependency] private MarketCommodityGroupSystem _commodityGroups = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeCandidates();
    }

    public override void Shutdown()
    {
        ShutdownCandidates();
        base.Shutdown();
    }

    protected override void Started(EntityUid uid, MarketPriceShockRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        if (component.Processed)
            return;

        component.Processed = true;
        base.Started(uid, component, gameRule, args);

        if (TryApplyShock((uid, component)))
            AnnounceChanges((uid, component));

        ForceEndSelf(uid, gameRule);
    }

    private bool TryApplyShock(Entity<MarketPriceShockRuleComponent> ent)
    {
        var component = ent.Comp;
        if (!_market.Enabled || component.AllowedGroups.Count == 0)
            return false;

        if (component.ProductCount <= 0 || component.MaxSelectionAttempts < component.ProductCount ||
            !double.IsFinite(component.PriceChange) || component.PriceChange <= 0 || component.PriceChange >= 1)
        {
            Sawmill.Warning($"Invalid market price shock settings on {ToPrettyString(ent)}.");
            return false;
        }

        var candidates = GetCandidates(component.AllowedGroups);
        var attempts = Math.Min(component.MaxSelectionAttempts, candidates.Length);
        if (attempts < component.ProductCount)
            return false;

        var changes = new Dictionary<string, MarketPriceShockChange>(component.ProductCount);
        for (var i = 0; i < attempts && changes.Count < component.ProductCount; i++)
        {
            // Partial Fisher-Yates samples distinct keys without copying or shuffling the entire pool.
            var selected = RobustRandom.Next(i, candidates.Length);
            (candidates[i], candidates[selected]) = (candidates[selected], candidates[i]);
            var candidate = candidates[i];
            var multiplier = RobustRandom.Prob(0.5f) ? 1 + component.PriceChange : 1 - component.PriceChange;
            if (!_market.TryGetScaledFactor(candidate.MarketKey, multiplier, out var factor) ||
                !TryGetCandidateName(candidate, out var name))
                continue;

            changes.Add(candidate.MarketKey, new MarketPriceShockChange(name, _market.GetFactor(candidate.MarketKey), factor));
        }

        // Never publish a partial event or claim a full percentage change for a price clipped at a limit.
        if (changes.Count != component.ProductCount)
        {
            Sawmill.Warning($"Market price shock found only {changes.Count}/{component.ProductCount} eligible commodities in {attempts} attempts.");
            return false;
        }

        foreach (var (key, change) in changes)
            _market.SetFactor(key, change.NewFactor);

        component.Changes = changes;
        return true;
    }

    private void AnnounceChanges(Entity<MarketPriceShockRuleComponent> ent)
    {
        var component = ent.Comp;
        if (component.Announcement is not { } announcement)
            return;

        var lines = new string[component.Changes.Count];
        var index = 0;
        foreach (var change in component.Changes.Values)
        {
            var message = change.NewFactor > change.OldFactor
                ? "station-event-market-price-shock-increase"
                : "station-event-market-price-shock-decrease";
            lines[index++] = Loc.GetString(message, ("name", change.Name), ("percent", component.PriceChange * 100));
        }

        var players = Filter.Empty().AddWhere(GameTicker.UserHasJoinedGame);
        ChatSystem.DispatchFilteredAnnouncement(players,
            Loc.GetString(announcement, ("changes", string.Join("\n", lines))),
            sender: component.Sender is { } sender ? Loc.GetString(sender) : null,
            playSound: component.AnnouncementSound != null,
            announcementSound: component.AnnouncementSound,
            colorOverride: component.AnnouncementColor);
    }
}
