using System.Globalization;
using Content.Shared._Exodus.Genetics;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Exodus.Guidebook.RichText;

/// <summary>Displays a mutation's localized name or current genetic load directly from its prototype.</summary>
public sealed partial class GeneticMutationTag : IMarkupTagHandler
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    public string Name => "geneticmutation";

    public string TextBefore(MarkupNode node)
    {
        return GetText(node, _prototypes);
    }

    /// <summary>Also used by the search index without constructing guidebook controls.</summary>
    public static string GetText(MarkupNode node, IPrototypeManager prototypes)
    {
        if (node.Closing || !node.Value.TryGetString(out var id))
            return string.Empty;

        ProtoId<GeneticMutationPrototype> mutationId = id;
        if (!prototypes.TryIndex(mutationId, out var mutation))
            return string.Empty;

        var field = node.Attributes.TryGetValue("field", out var attribute) ? attribute.StringValue : "name";
        return field switch
        {
            "name" => Loc.GetString(mutation.Name),
            "instability" => mutation.Instability.ToString(CultureInfo.CurrentCulture),
            _ => string.Empty,
        };
    }
}
