// Exodus: hide the humanoid targeting doll in an alternate genetic form.
using Content.Client._Exodus.Genetics;

namespace Content.Client._Shitmed.UserInterface.Systems.Targeting;

public sealed partial class TargetingUIController
{
    public void RefreshGeneticFormVisibility()
    {
        TargetingControl?.SetTargetDollVisible(_targetingComponent != null &&
            !GeneticFormPresentationSystem.IsAlternateForm(_entManager, _playerManager.LocalEntity));
    }
}
