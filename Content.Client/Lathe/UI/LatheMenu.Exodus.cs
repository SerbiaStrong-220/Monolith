// Exodus: update recipe availability without rebuilding controls on every server update.
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;

namespace Content.Client.Lathe.UI;

public sealed partial class LatheMenu
{
    private bool TryRefreshRecipeControls(IEnumerable<LatheRecipePrototype> recipes, int count, int quantity, LatheComponent? lathe)
    {
        if (RecipeList.ChildCount != count)
            return false;

        var index = 0;
        foreach (var recipe in recipes)
        {
            if (RecipeList.GetChild(index++) is not RecipeControl control || control.Recipe != recipe.ID)
                return false;

            control.UpdateAvailability(_lathe.CanProduce(Entity, recipe, quantity, component: lathe));
        }

        return true;
    }
}
