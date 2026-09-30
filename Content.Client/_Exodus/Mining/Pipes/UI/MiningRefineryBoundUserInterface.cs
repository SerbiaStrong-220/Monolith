using System.Numerics;
using Content.Client.Lathe.UI;
using Content.Shared._Exodus.Mining.Pipes;
using JetBrains.Annotations;

namespace Content.Client._Exodus.Mining.Pipes.UI;

[UsedImplicitly]
public sealed class MiningRefineryBoundUserInterface(EntityUid owner, Enum uiKey) : LatheBoundUserInterface(owner, uiKey)
{
    private MiningRefineryStorageControl? _storage;

    protected override void ConfigureMenu(LatheMenu menu)
    {
        menu.MinSize = new Vector2(650, 580);
        menu.SetSize = new Vector2(750, 620);
        menu.MaterialsContainer.Visible = false;
        menu.StatusContainer.Visible = true;
        menu.StatusContainer.VerticalExpand = true;
        _storage = new MiningRefineryStorageControl(Owner);
        menu.StatusContainer.AddChild(_storage);
    }

    public void UpdateStorage(MiningRefineryComponent refinery)
    {
        _storage?.UpdateReadings(refinery);
    }
}
