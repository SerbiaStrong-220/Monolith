// SS220 chat bans in the common ban panel; Exodus validates input and handles asynchronous failures.
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Database._Exodus.Chat;

namespace Content.Server.Administration;

public sealed partial class BanPanelEui
{
    private bool _chatBanBusy;
    private bool _chatBanClosed;
    private string? _chatBanError;

    private async Task BanChatPlayer(Ban request)
    {
        if (_chatBanBusy || _chatBanClosed || !_admins.HasAdminFlag(Player, AdminFlags.Ban))
            return;

        if (request.BannedChats is not { } chats || !ChatBanLimits.ValidChats(chats) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > ChatBanLimits.MaxReasonLength ||
            request.BanDurationMinutes > ChatBanLimits.MaxDurationMinutes || !Enum.IsDefined(request.Severity) ||
            request.BannedJobs?.Length > 0 || request.BannedAntags?.Length > 0)
        {
            _chatBanError = Loc.GetString("chat-ban-invalid-input");
            StateDirty();
            return;
        }

        _chatBanBusy = true;
        _chatBanError = null;
        StateDirty();
        try
        {
            var info = new CreateChatsBanInfo(request.Reason);
            info.WithSeverity(request.Severity);
            if (request.BanDurationMinutes > 0)
                info.WithMinutes(request.BanDurationMinutes);
            foreach (var chat in chats)
                info.AddChat(chat);

            var located = request.Target == null ? null : await _playerLocator.LookupIdByNameOrIdAsync(request.Target);
            if (request.Target != null && located == null)
            {
                _chatBanError = Loc.GetString("cmd-ban-player");
                return;
            }

            if (located != null)
                info.AddUser(located.UserId, located.Username);
            if (request.UseLastIp)
                info.AddAddress(located?.LastAddress);
            else if (!string.IsNullOrWhiteSpace(request.IpAddress))
            {
                if (!IPAddress.TryParse(request.IpAddress, out var address) ||
                    !int.TryParse(request.IpAddressHid, out var mask) || mask < 0 ||
                    mask > (address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128))
                {
                    _chatBanError = Loc.GetString("ban-panel-invalid-ip");
                    return;
                }

                info.AddAddressRange(address, mask);
            }

            info.AddHWId(request.UseLastHwid ? located?.LastHWId : request.Hwid);
            if (request.Target == null && string.IsNullOrWhiteSpace(request.IpAddress) && request.Hwid == null)
            {
                _chatBanError = Loc.GetString("ban-panel-no-data");
                return;
            }

            if (_chatBanClosed || !_admins.HasAdminFlag(Player, AdminFlags.Ban))
                return;

            var ban = await _banManager.TryCreateChatsBanAsync(info, Player);
            if (ban == null)
            {
                _chatBanError = Loc.GetString("chat-ban-invalid-input");
                return;
            }

            if (!_chatBanClosed)
                Close();
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to create chat ban: {e}");
            _chatBanError = Loc.GetString("chat-ban-error");
        }
        finally
        {
            _chatBanBusy = false;
            if (!_chatBanClosed)
                StateDirty();
        }
    }
}
