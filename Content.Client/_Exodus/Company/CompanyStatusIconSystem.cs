using Content.Shared._Mono.Company;
using Content.Shared.Ghost;
using Content.Shared.StatusIcon.Components;
using Content.Shared.Whitelist;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Company;

public sealed class CompanyStatusIconSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    private EntityQuery<GhostComponent> _ghostQuery;

    public override void Initialize()
    {
        base.Initialize();

        _ghostQuery = GetEntityQuery<GhostComponent>();
        SubscribeLocalEvent<CompanyComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<CompanyComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_player.LocalEntity is not { } viewer ||
            !_prototype.TryIndex(ent.Comp.CompanyName, out var company) ||
            company.StatusIcon is not { } iconId ||
            !_prototype.TryIndex(iconId, out var icon))
        {
            return;
        }

        // The standard overlay always shows an entity its own icons, so also apply the audience here.
        if (!(icon.VisibleToGhosts && _ghostQuery.HasComp(viewer)) &&
            icon.ShowTo != null && !_whitelist.IsValid(icon.ShowTo, viewer))
        {
            return;
        }

        args.StatusIcons.Add(icon);
    }
}
