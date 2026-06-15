using System.Numerics;
using Content.Client.Message;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Exodus.LifeInsurance.UI;

public sealed class LifeInsuranceWakeUpWindow : DefaultWindow
{
    public readonly Button OkButton;

    public LifeInsuranceWakeUpWindow()
    {
        Title = Loc.GetString("life-insurance-wakeup-title");
        SetSize = MinSize = new Vector2(480, 320);

        var narrative = new RichTextLabel { HorizontalExpand = true, MaxWidth = 440 };
        narrative.SetMarkup(Loc.GetString("life-insurance-wakeup-text"));

        OkButton = new Button
        {
            Text = Loc.GetString("life-insurance-wakeup-ok"),
            HorizontalAlignment = HAlignment.Center,
            MinWidth = 160
        };

        var screen = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#16131C") },
            HorizontalExpand = true,
            VerticalExpand = true
        };

        var header = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#2A2140") }
        };
        header.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            Margin = new Thickness(8, 6, 8, 6),
            VerticalAlignment = VAlignment.Center,
            Children =
            {
                new TextureRect
                {
                    TexturePath = "/Textures/_Mono/Companies/ne_rd.png",
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    SetSize = new Vector2(48, 48),
                    Margin = new Thickness(0, 0, 10, 0)
                },
                new Label
                {
                    Text = Loc.GetString("life-insurance-brand-title"),
                    StyleClasses = { "LabelHeading" },
                    Modulate = Color.FromHex("#C9B3E6"),
                    VerticalAlignment = VAlignment.Center
                }
            }
        });

        var divider = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#9573AF") },
            MinSize = new Vector2(0, 2),
            Margin = new Thickness(0, 4, 0, 8)
        };

        screen.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(10),
            Children =
            {
                header,
                divider,
                narrative,
                new Control { MinSize = new Vector2(0, 14), VerticalExpand = true },
                OkButton
            }
        });

        Contents.AddChild(screen);
    }
}
