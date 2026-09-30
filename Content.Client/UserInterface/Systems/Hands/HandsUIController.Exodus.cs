// Exodus: alternate forms retain their real hands but hide the humanoid hotbar.
using Content.Client._Exodus.Genetics;

namespace Content.Client.UserInterface.Systems.Hands;

public sealed partial class HandsUIController
{
    public void RefreshGeneticFormVisibility()
    {
        if (HandsGui != null)
            HandsGui.Visible = _playerHandsComponent != null &&
                               !GeneticFormPresentationSystem.IsAlternateForm(_entities, _player.LocalEntity);
    }
}
