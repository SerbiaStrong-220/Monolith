using Content.Client.UserInterface.Systems.Gameplay;
using Content.Client.UserInterface.Systems.Hands;
using Content.Client.UserInterface.Systems.Inventory;
using Content.Client._Shitmed.UserInterface.Systems.PartStatus;
using Content.Client._Shitmed.UserInterface.Systems.Targeting;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticFormUIController : UIController
{
    public override void Initialize()
    {
        base.Initialize();
        UIManager.GetUIController<GameplayStateLoadController>().OnScreenLoad += RefreshHud;
    }

    public void RefreshHud()
    {
        UIManager.GetUIController<HandsUIController>().RefreshGeneticFormVisibility();
        UIManager.GetUIController<InventoryUIController>().RefreshGeneticFormVisibility();
        UIManager.GetUIController<PartStatusUIController>().RefreshGeneticFormVisibility();
        UIManager.GetUIController<TargetingUIController>().RefreshGeneticFormVisibility();
    }
}
