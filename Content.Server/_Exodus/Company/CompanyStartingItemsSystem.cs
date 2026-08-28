using Content.Shared._Mono.Company;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Company;

/// <summary>
/// Spawns <see cref="CompanyPrototype.StartingItems"/> after company is assigned on join.
/// </summary>
public sealed class CompanyStartingItemsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    private readonly HashSet<NetUserId> _grantedPlayers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn,
            after: new[] { typeof(Content.Server._Mono.Company.CompanySystem) });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent args)
    {
        if (!TryComp<CompanyComponent>(args.Mob, out var company)
            || company.CompanyName == "None"
            || !_prototypes.TryIndex(company.CompanyName, out CompanyPrototype? companyProto)
            || companyProto.StartingItems.Count == 0)
            return;

        if (!_grantedPlayers.Add(args.Player.UserId))
            return;

        var coords = Transform(args.Mob).Coordinates;
        foreach (var itemProto in companyProto.StartingItems)
        {
            var item = Spawn(itemProto, coords);
            _hands.TryPickupAnyHand(args.Mob, item);
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent _)
    {
        _grantedPlayers.Clear();
    }
}
