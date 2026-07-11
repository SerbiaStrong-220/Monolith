using Content.Shared._Exodus.Territory;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Territory;

public sealed partial class TerritoryClaimRulesSystem : EntitySystem
{
    private static readonly ProtoId<TerritoryClaimRulesPrototype> DefaultRulesId = "Default";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    private static readonly TerritoryClaimRulesPrototype FallbackRules = new();

    private readonly Dictionary<ProtoId<TerritoryFactionPrototype>, TimeSpan> _nextClaimByFaction = new();
    private TimeSpan? _roundStartedAt;
    private TimeSpan? _roundClaimUnlockAt;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    public bool CanStartClaim(ProtoId<TerritoryFactionPrototype> faction, out string popup)
    {
        popup = string.Empty;

        var curTime = _timing.CurTime;
        if (_roundClaimUnlockAt is { } roundUnlockAt && curTime < roundUnlockAt)
        {
            popup = Loc.GetString(
                "grid-territory-claim-round-cooldown",
                ("minutes", GetRemainingMinutes(roundUnlockAt - curTime)));
            return false;
        }

        if (_nextClaimByFaction.TryGetValue(faction, out var factionUnlockAt) &&
            curTime < factionUnlockAt)
        {
            popup = Loc.GetString(
                "grid-territory-claim-faction-cooldown",
                ("minutes", GetRemainingMinutes(factionUnlockAt - curTime)));
            return false;
        }

        return true;
    }

    public void RecordSuccessfulClaim(ProtoId<TerritoryFactionPrototype> faction)
    {
        if (_roundStartedAt == null)
            return;

        var cooldown = GetFactionClaimCooldown(faction);
        if (cooldown <= TimeSpan.Zero)
        {
            _nextClaimByFaction.Remove(faction);
            return;
        }

        _nextClaimByFaction[faction] = _timing.CurTime + cooldown;
    }

    public float GetDefaultMinClaimRepairIntegrity()
    {
        return Math.Clamp(ResolveRules().DefaultMinClaimRepairIntegrity, 0f, 1f);
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        _roundStartedAt = _timing.CurTime;
        RefreshRoundUnlockTime();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _roundStartedAt = null;
        _roundClaimUnlockAt = null;
        _nextClaimByFaction.Clear();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<TerritoryClaimRulesPrototype>() && _roundStartedAt != null)
            RefreshRoundUnlockTime();
    }

    private void RefreshRoundUnlockTime()
    {
        if (_roundStartedAt is not { } startedAt)
        {
            _roundClaimUnlockAt = null;
            return;
        }

        var cooldown = ResolveRules().RoundStartClaimCooldown;
        _roundClaimUnlockAt = cooldown > TimeSpan.Zero
            ? startedAt + cooldown
            : null;
    }

    private TimeSpan GetFactionClaimCooldown(ProtoId<TerritoryFactionPrototype> faction)
    {
        if (_prototype.TryIndex(faction, out var factionPrototype) &&
            factionPrototype.ClaimCooldown is { } cooldown)
        {
            return cooldown;
        }

        return ResolveRules().DefaultFactionClaimCooldown;
    }

    private TerritoryClaimRulesPrototype ResolveRules()
    {
        return _prototype.TryIndex(DefaultRulesId, out var rules)
            ? rules
            : FallbackRules;
    }

    private static int GetRemainingMinutes(TimeSpan remaining)
    {
        return Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
    }
}
