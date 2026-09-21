using Content.Client.Guidebook.RichText;
using Content.Client.UserInterface.Systems.Guidebook;
using Robust.Client.UserInterface.RichText;

namespace Content.Client.UserInterface.Systems.Ghost.Controls.Roles;

public sealed partial class GhostRoleRulesWindow : ILinkClickHandler
{
    private static readonly Type[] _ruleMarkupTags =
    [
        typeof(BoldItalicTag),
        typeof(BoldTag),
        typeof(BulletTag),
        typeof(ColorTag),
        typeof(HeadingTag),
        typeof(ItalicTag),
        typeof(TextLinkTag),
    ];

    public void HandleClick(string link)
    {
        UserInterfaceManager.GetUIController<GuidebookUIController>().OpenGuidebook(selected: link);
    }
}
