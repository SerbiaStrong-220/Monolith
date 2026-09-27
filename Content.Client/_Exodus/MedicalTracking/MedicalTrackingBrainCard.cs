using Content.Shared._Exodus.MedicalTracking;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.MedicalTracking;

/// <summary>A compact client row, retained across pinpointer updates.</summary>
public sealed class MedicalTrackingBrainCard : ContainerButton
{
    private readonly Label _name = new() { ClipText = true, HorizontalExpand = true };
    private readonly Label _tier = new() { StyleClasses = { "LabelSubText" } };
    private readonly StyleBoxFlat _stripeStyle = new();

    public MedicalTrackingBrain Brain { get; private set; }

    public MedicalTrackingBrainCard()
    {
        ToggleMode = true;
        MinHeight = 30;
        AddStyleClass(MedicalTrackingUiTheme.ButtonClass);
        AddStyleClass(MedicalTrackingUiTheme.CardClass);
        var row = new BoxContainer { SeparationOverride = 8, MouseFilter = MouseFilterMode.Ignore };
        row.AddChild(new PanelContainer { SetWidth = 3, PanelOverride = _stripeStyle, MouseFilter = MouseFilterMode.Ignore });
        row.AddChild(_name);
        row.AddChild(_tier);
        AddChild(row);
    }

    public void UpdateBrain(MedicalTrackingBrain brain)
    {
        if (Brain == brain)
            return;

        Brain = brain;
        _name.Text = brain.Name;
        _tier.Text = Loc.GetString(brain.TierName);
        _tier.FontColorOverride = brain.TierColor;
        _stripeStyle.BackgroundColor = brain.TierColor;
        ToolTip = Loc.GetString("medical-tracking-brain-client", ("name", brain.Name), ("tier", _tier.Text));
    }
}
