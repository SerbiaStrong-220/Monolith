// SS220 chat ban tab; Exodus uses typed checkbox values and validates before submitting.
using System.Linq;
using Content.Shared._Exodus.Chat;
using Content.Shared.Database;
using Content.Shared.Database._Exodus.Chat;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.Administration.UI.BanPanel;

public sealed partial class BanPanel
{
    private readonly Dictionary<BannableChats, CheckBox> _chatCheckboxes = new();
    private bool _chatBanBusy;

    private void InitializeChatBans()
    {
        Tabs.SetTabTitle((int)TabNumbers.Chats, Loc.GetString("chat-ban-panel-chats"));
        Tabs.SetTabVisible((int)TabNumbers.Chats, false);
        TypeOption.AddItem(Loc.GetString("chat-ban-panel-chats"), (int)Types.Chats);
        foreach (var chat in Enum.GetValues<BannableChats>())
        {
            if (!ChatBanLimits.ValidChat(chat))
                continue;
            var checkbox = new CheckBox { Text = ChatBanNames.Get(chat), Margin = new Thickness(5) };
            _chatCheckboxes.Add(chat, checkbox);
            ChatsContainer.AddChild(checkbox);
        }
    }

    private void UpdateChatBanType()
    {
        var chat = TypeOption.SelectedId == (int)Types.Chats;
        Tabs.SetTabVisible((int)TabNumbers.Chats, chat);
        EraseCheckbox.Disabled = chat;
        if (chat)
        {
            EraseCheckbox.Pressed = false;
            SeverityOption.SelectId((int)NoteSeverity.Medium);
        }

        UpdateExpiresLabel();
    }

    private BannableChats[] GetSelectedChats()
    {
        return TypeOption.SelectedId == (int)Types.Chats
            ? _chatCheckboxes.Where(p => p.Value.Pressed).Select(p => p.Key).ToArray()
            : [];
    }

    private bool TryGetBanMinutes(out uint minutes)
    {
        minutes = 0;
        if (Multiplier == 0)
            return true;

        if (!double.TryParse(TimeLine.Text.Length == 0 ? "0" : TimeLine.Text, out var value))
            return false;
        value *= Multiplier;
        var max = TypeOption.SelectedId == (int)Types.Chats
            ? ChatBanLimits.MaxDurationMinutes
            : Math.Min(uint.MaxValue, (DateTime.MaxValue - DateTime.Now).TotalMinutes - 1);
        if (!double.IsFinite(value) || value < 0 || value > max || value != Math.Truncate(value))
            return false;

        minutes = (uint)value;
        return true;
    }

    public void UpdateChatBanStatus(bool busy, string? error)
    {
        _chatBanBusy = busy;
        ChatBanStatus.Visible = busy || error != null;
        ChatBanStatus.SetMessage(FormattedMessage.FromUnformatted(busy ? Loc.GetString("chat-ban-saving") : error ?? string.Empty));
        if (!busy)
        {
            ButtonResetOn = null;
            SubmitButton.Text = Loc.GetString("ban-panel-submit");
            SubmitButton.ModulateSelfOverride = null;
        }

        UpdateSubmitEnabled();
    }
}
