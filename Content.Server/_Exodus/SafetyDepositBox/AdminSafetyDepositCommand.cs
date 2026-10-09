using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Exodus.SafetyDepositBox;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class AdminSafetyDepositCommand : LocalizedCommands
{
    [Dependency] private EuiManager _euis = default!;

    public override string Command => "adminsafetydeposit";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } admin)
        {
            shell.WriteError(Loc.GetString("admin-safety-deposit-command-player"));
            return;
        }

        if (args.Length > 1)
        {
            shell.WriteError(Help);
            return;
        }

        _euis.OpenEui(new AdminSafetyDepositEui(args.Length == 1 ? args[0] : null), admin);
    }
}
