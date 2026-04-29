using System.Diagnostics.CodeAnalysis;
using Content.Client.UserInterface.RichText;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client._Exodus.Guidebook.RichText;

[UsedImplicitly]
public sealed class LocIdTag : IMarkupTagHandler
{
    public string Name => "locid";

    private static readonly Type[] AllowedTags = new Type[] {
            typeof(BoldItalicTag),
            typeof(BoldTag),
            typeof(BulletTag),
            typeof(ColorTag),
            typeof(HeadingTag),
            typeof(ItalicTag),
            typeof(MonoTag),
            typeof(LocIdTag),
        };

    /// <inheritdoc/>
    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        if (!node.Value.TryGetString(out var text))
        {
            control = null;
            return false;
        }

        var label = new RichTextLabel();
        label.SetMessage(Loc.GetString(text), tagsAllowed: AllowedTags);

        control = label;
        return true;
    }
}