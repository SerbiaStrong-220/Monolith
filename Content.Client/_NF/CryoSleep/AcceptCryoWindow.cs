using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._NF.CryoSleep;
public sealed class AcceptCryoWindow : DefaultWindow
{
    public readonly Button DenyButton;
    public readonly Button AcceptButton;

    // Exodus-Start
    private const string CryopodBodyContainerId = "body_container";
    private const float CheckInterval = 1f; // seconds
    private readonly IEntityManager _entMan;
    private readonly IPlayerManager _player;
    private readonly SharedContainerSystem _container;
    private float _checkAccumulator;
    // Exodus-End

    public AcceptCryoWindow()
    {
        // Exodus-Start
        _entMan = IoCManager.Resolve<IEntityManager>();
        _player = IoCManager.Resolve<IPlayerManager>();
        _container = _entMan.System<SharedContainerSystem>();
        // Exodus-End

        Title = Loc.GetString("accept-cryo-window-title");

        Contents.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Children =
            {
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    Children =
                    {
                        (new Label()
                        {
                            Text = Loc.GetString("accept-cryo-window-prompt-text-part")
                        }),
                        new BoxContainer
                        {
                            Orientation = LayoutOrientation.Horizontal,
                            Align = AlignMode.Center,
                            Children =
                            {
                                (AcceptButton = new Button
                                {
                                    Text = Loc.GetString("accept-cryo-window-accept-button"),
                                }),

                                (new Control()
                                {
                                    MinSize = new Vector2i(20, 0)
                                }),

                                (DenyButton = new Button
                                {
                                    Text = Loc.GetString("accept-cryo-window-deny-button"),
                                })
                            }
                        },
                    }
                },
            }
        });
    }

    // Exodus-Start
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _checkAccumulator += args.DeltaSeconds;
        if (_checkAccumulator < CheckInterval)
            return;
        _checkAccumulator = 0f;

        if (_player.LocalEntity is not { } player
            || !_container.TryGetContainingContainer((player, null, null), out var container)
            || container.ID != CryopodBodyContainerId)
        {
            Close();
        }
    }
    // Exodus-End
}

