// Exodus: preserve and refresh the active tooltip when recipe availability changes.
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Client.Lathe.UI;

public sealed partial class RecipeControl
{
    public ProtoId<LatheRecipePrototype> Recipe { get; }
    private RecipeTooltip? _tooltip;

    public void UpdateAvailability(bool canProduce)
    {
        Button.Disabled = !canProduce;
        if (_tooltip is { Disposed: false, IsInsideTree: true })
            _tooltip.UpdateText(TooltipTextSupplier());
    }
}
