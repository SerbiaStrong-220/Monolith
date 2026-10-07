// (c) Space Exodus Team - EXDS-RL with CLA
using System.Globalization;
using Content.Server._Exodus.Economy.Admin;
using Content.Server.Administration;
using Content.Shared._Exodus.Economy.Admin;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Exodus.Economy;

[AdminCommand(AdminFlags.EconomyDB)]
public sealed partial class MarketQuoteCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "marketquote";
    public string Description => "Show global dynamic market factor and commodity group for a key (stack:X / proto:Y / gas:Z).";
    public string Help => "Usage: marketquote <marketKey>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("Usage: marketquote <marketKey>");
            return;
        }

        var market = _entities.System<DynamicMarketSystem>();
        var groups = _entities.System<MarketCommodityGroupSystem>();
        var key = args[0];
        var factor = market.GetFactor(key);
        market.TryGetQuote(key, out var quote);
        shell.WriteLine($"{key}: factor={factor:F4} trend={quote.Trend:F4} change%={quote.ChangePercent:F2} group={groups.GetGroup(key)}");
    }
}

[AdminCommand(AdminFlags.EconomyDB)]
public sealed partial class MarketSetCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "marketset";
    public string Description => "Set global dynamic market factor for a key.";
    public string Help => "Usage: marketset <marketKey> <factor>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 ||
            !double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) ||
            !double.IsFinite(factor))
        {
            shell.WriteError("Usage: marketset <marketKey> <factor>");
            return;
        }

        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("economy-admin-reset-console"));
            return;
        }

        if (!_entities.System<MarketAdminSystem>().TryOpen(player, new MarketAdminSetQuoteMessage(args[0], factor)))
            shell.WriteError(Loc.GetString("economy-admin-error-permission"));
    }
}

[AdminCommand(AdminFlags.EconomyDB)]
public sealed partial class MarketResetCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "marketreset";
    public string Description => "Reset global market factors (all keys, or one key).";
    public string Help => "Usage: marketreset [marketKey]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError("Usage: marketreset [marketKey]");
            return;
        }

        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("economy-admin-reset-console"));
            return;
        }

        var admin = _entities.System<MarketAdminSystem>();
        if (!admin.TryOpen(player, args.Length == 0
                ? new MarketAdminBeginResetAllMessage()
                : new MarketAdminResetQuoteMessage(args[0])))
            shell.WriteError(Loc.GetString("economy-admin-error-permission"));
    }
}

[AdminCommand(AdminFlags.EconomyDB)]
public sealed partial class MarketListCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "marketlist";
    public string Description => "List non-base global market factors.";
    public string Help => "Usage: marketlist";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var market = _entities.System<DynamicMarketSystem>();
        var quotes = market.GetAllQuotes();
        if (quotes.Count == 0)
        {
            shell.WriteLine("No active market deviations (all at base 1.0).");
            return;
        }

        foreach (var (key, quote) in quotes)
        {
            shell.WriteLine($"{key}: {quote.Factor:F4} ({quote.ChangePercent:+0.00;-0.00}%) trend={quote.Trend:F4}");
        }
    }
}
