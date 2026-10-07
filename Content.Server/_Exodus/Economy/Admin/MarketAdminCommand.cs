using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Exodus.Economy.Admin;

[AdminCommand(AdminFlags.EconomyDB)]
public sealed partial class MarketAdminCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "economyui";
    public string Description => Loc.GetString("economy-admin-command-description");
    public string Help => Loc.GetString("economy-admin-command-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0 || shell.Player is not { } player)
        {
            shell.WriteError(Help);
            return;
        }

        if (!_entities.System<MarketAdminSystem>().TryOpen(player))
            shell.WriteError(Loc.GetString("economy-admin-error-permission"));
    }
}
