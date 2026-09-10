using System.Threading.Tasks; // Exodus: observed asynchronous cleanup task.
using Content.Server.Database;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class CleanStaleSafetyDepositBoxesCommand : IConsoleCommand
{
    [Dependency] private IServerDbManager _db = default!;

    private bool _running; // Exodus: prevent overlapping cleanup tasks.

    public string Command => "cleanstalesafetyboxes";
    public string Description => "Deletes safety deposit boxes that have been withdrawn and have no items for more than the specified number of days.";
    public string Help => "cleanstalesafetyboxes <days>\nExample: cleanstalesafetyboxes 7\nDeletes boxes that have been withdrawn for more than 7 days with no items.";

    // Exodus-begin: keep async exceptions inside an observed task instead of an async-void command.
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("Usage: cleanstalesafetyboxes <days>");
            return;
        }

        if (!int.TryParse(args[0], out var days) || days <= 0)
        {
            shell.WriteError("Days must be a positive integer.");
            return;
        }

        if (_running)
        {
            shell.WriteError("A safety deposit box cleanup is already running.");
            return;
        }

        shell.WriteLine($"Searching for safety deposit boxes that have been withdrawn for more than {days} days with no items...");

        _running = true;
        _ = ExecuteAsync(shell, days);
    }

    private async Task ExecuteAsync(IConsoleShell shell, int days)
    {
        try
        {
            var count = await _db.DeleteStaleSafetyDepositBoxes(days);
            shell.WriteLine($"Successfully deleted {count} stale safety deposit box(es).");
        }
        catch (Exception ex)
        {
            shell.WriteError($"Error cleaning stale boxes: {ex.Message}");
        }
        finally
        {
            _running = false;
        }
    }
    // Exodus-end

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHint("days (e.g., 7)");
        }

        return CompletionResult.Empty;
    }
}
