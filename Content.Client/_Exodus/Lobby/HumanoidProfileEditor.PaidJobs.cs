using Content.Shared._NF.Bank;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private void AddPaidJobPriorityNotice(BoxContainer category, JobPrototype job)
    {
        if (job.EntryPrice <= 0)
            return;

        category.AddChild(new RichTextLabel
        {
            HorizontalExpand = true,
            // The job list scrolls horizontally, so text needs a width limit to wrap.
            MaxWidth = 700,
            Margin = new Thickness(5, 2, 5, 6),
            Text = Loc.GetString("paid-job-priority-notice",
                ("price", BankSystemExtensions.ToSpesoString(job.EntryPrice))),
        });
    }
}
