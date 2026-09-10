// SS220 chatunban command, adapted with type checks and idempotent revocation.
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Exodus.Chat;

[AdminCommand(AdminFlags.Ban)]
public sealed class ChatUnbanCommand : LocalizedCommands
{
    [Dependency] private IBanManager _bans = default!;
    [Dependency] private ILogManager _logs = default!;

    public override string Command => "chatunban";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], out var id) || id <= 0)
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            shell.WriteLine(await _bans.TryPardonChatsBanAsync(id, shell.Player)
                ? Loc.GetString("chat-ban-revoked", ("id", id))
                : Loc.GetString("chat-unban-not-found"));
        }
        catch (Exception e)
        {
            _logs.GetSawmill("admin.chat_bans").Error($"Failed to revoke chat ban {id}: {e}");
            shell.WriteError(Loc.GetString("chat-ban-error"));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1 ? CompletionResult.FromHint(Loc.GetString("chat-unban-hint-id")) : CompletionResult.Empty;
    }
}
