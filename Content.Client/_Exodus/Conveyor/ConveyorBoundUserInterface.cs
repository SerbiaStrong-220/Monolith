using Content.Shared._Exodus.Conveyor;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.Conveyor;

[UsedImplicitly]
public sealed class ConveyorBoundUserInterface : BoundUserInterface
{
    [ViewVariables] private ConveyorWindow? _window;

    public ConveyorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ConveyorWindow>();
        _window.OnTierSelected += tier =>
        {
            SendMessage(new ConveyorSetSpeedMessage(tier));
            Close();
        };
    }
}
