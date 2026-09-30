// Exodus: hide the humanoid body-status doll while using an alternate genetic form.
using Content.Client._Exodus.Genetics;
using Robust.Client.Player;

namespace Content.Client._Shitmed.UserInterface.Systems.PartStatus;

public sealed partial class PartStatusUIController
{
    [Dependency] private IPlayerManager _geneticPlayer = default!;

    public void RefreshGeneticFormVisibility()
    {
        PartStatusControl?.SetVisible(_targetingComponent != null &&
            !GeneticFormPresentationSystem.IsAlternateForm(_entManager, _geneticPlayer.LocalEntity));
    }
}
