using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client._Exodus.Guidebook.RichText;

public sealed class LocIdTag : IMarkupTagHandler
{
    public string Name => "locid";

    /// <inheritdoc/>
    public string TextBefore(MarkupNode node)
    {
        // Do nothing with an empty tag
        if (!node.Value.TryGetString(out var text))
            return string.Empty;

        return Loc.GetString(text);
    }
}