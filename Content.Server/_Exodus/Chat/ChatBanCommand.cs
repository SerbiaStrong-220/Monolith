// SS220 chatban command, adapted with strict parsing, multiple channels and awaited error handling.
using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Database._Exodus.Chat;
using Robust.Shared.Console;

namespace Content.Server._Exodus.Chat;

[AdminCommand(AdminFlags.Ban)]
public sealed class ChatBanCommand : LocalizedCommands
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IBanManager _bans = default!;
    [Dependency] private ILogManager _logs = default!;

    public override string Command => "chatban";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 3 or > 5)
        {
            shell.WriteError(Help);
            return;
        }

        uint minutes = 0;
        var severity = NoteSeverity.Medium;
        var chats = args[1].Split(',');
        var selected = new HashSet<BannableChats>();
        foreach (var name in chats)
        {
            var normalized = name.Equals("Ghost", StringComparison.OrdinalIgnoreCase) ? "Dead" : name;
            if (!Enum.TryParse<BannableChats>(normalized, true, out var chat) || !ChatBanLimits.ValidChat(chat))
            {
                shell.WriteError(Loc.GetString("chat-ban-invalid-chats"));
                return;
            }

            selected.Add(chat);
        }

        if (!ChatBanLimits.ValidChats(selected) || string.IsNullOrWhiteSpace(args[2]) ||
            args[2].Length > ChatBanLimits.MaxReasonLength ||
            args.Length >= 4 && (!uint.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) ||
                                 minutes > ChatBanLimits.MaxDurationMinutes) ||
            args.Length == 5 && (!Enum.TryParse(args[4], true, out severity) || !Enum.IsDefined(severity)))
        {
            shell.WriteError(Loc.GetString("chat-ban-invalid-input"));
            shell.WriteLine(Help);
            return;
        }

        try
        {
            var target = await _locator.LookupIdByNameOrIdAsync(args[0]);
            if (target == null)
            {
                shell.WriteError(Loc.GetString("cmd-ban-player"));
                return;
            }

            var info = new CreateChatsBanInfo(args[2]);
            info.AddUser(target.UserId, target.Username);
            info.AddHWId(target.LastHWId);
            info.WithSeverity(severity);
            if (minutes > 0)
                info.WithMinutes(minutes);
            foreach (var chat in selected)
                info.AddChat(chat);

            var ban = await _bans.TryCreateChatsBanAsync(info, shell.Player);
            if (ban == null)
                shell.WriteError(Loc.GetString("chat-ban-invalid-input"));
            else
                shell.WriteLine(Loc.GetString("chat-ban-created", ("id", ban.Id!.Value)));
        }
        catch (Exception e)
        {
            _logs.GetSawmill("admin.chat_bans").Error($"Failed to create chat ban: {e}");
            shell.WriteError(Loc.GetString("chat-ban-error"));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), Loc.GetString("chat-ban-hint-player")),
            2 => CompletionResult.FromHintOptions(Enum.GetValues<BannableChats>().Where(ChatBanLimits.ValidChat).Select(c => c.ToString()), Loc.GetString("chat-ban-hint-chats")),
            3 => CompletionResult.FromHint(Loc.GetString("chat-ban-hint-reason")),
            4 => CompletionResult.FromHintOptions(["60", "1440", "10080", "0"], Loc.GetString("chat-ban-hint-minutes")),
            5 => CompletionResult.FromHintOptions(Enum.GetNames<NoteSeverity>(), Loc.GetString("chat-ban-hint-severity")),
            _ => CompletionResult.Empty,
        };
    }
}
