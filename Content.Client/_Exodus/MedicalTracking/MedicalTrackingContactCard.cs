using Content.Shared._Exodus.MedicalTracking;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.MedicalTracking;

/// <summary>A reusable two-line contact row. Sampling updates labels without recreating controls.</summary>
public sealed class MedicalTrackingContactCard : ContainerButton
{
    private readonly Label _name = new() { ClipText = true, HorizontalExpand = true };
    private readonly Label _tier = new() { StyleClasses = { "LabelSubText" } };
    private readonly Label _status = new() { ClipText = true, HorizontalExpand = true, StyleClasses = { "LabelSubText" } };
    private readonly Label _age = new() { StyleClasses = { "LabelSubText" } };
    private readonly Label _coordinates = new()
    {
        StyleClasses = { "LabelSubText" },
        FontColorOverride = MedicalTrackingUiTheme.Muted,
    };
    private readonly StyleBoxFlat _stripeStyle = new();
    private int _lastAge = -1;

    public MedicalTrackingContact Contact { get; private set; }

    public MedicalTrackingContactCard()
    {
        ToggleMode = true;
        AddStyleClass(MedicalTrackingUiTheme.ButtonClass);
        AddStyleClass(MedicalTrackingUiTheme.CardClass);
        var row = new BoxContainer { SeparationOverride = 6, MouseFilter = MouseFilterMode.Ignore };
        row.AddChild(new PanelContainer { SetWidth = 3, PanelOverride = _stripeStyle, MouseFilter = MouseFilterMode.Ignore });
        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 0,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        };
        var header = new BoxContainer { SeparationOverride = 6, MouseFilter = MouseFilterMode.Ignore };
        header.AddChild(_name);
        header.AddChild(_tier);
        column.AddChild(header);
        var footer = new BoxContainer { SeparationOverride = 6, MouseFilter = MouseFilterMode.Ignore };
        footer.AddChild(_status);
        footer.AddChild(_coordinates);
        footer.AddChild(_age);
        column.AddChild(footer);
        row.AddChild(column);
        AddChild(row);
    }

    public void UpdateContact(MedicalTrackingContact contact)
    {
        if (Contact == contact)
            return;

        var previous = Contact;
        Contact = contact;
        if (previous.Name != contact.Name)
        {
            _name.Text = contact.Name;
            ToolTip = contact.Name;
        }
        if (previous.TierName != contact.TierName || previous.TierColor != contact.TierColor)
        {
            _tier.Text = Loc.GetString(contact.TierName);
            _tier.FontColorOverride = contact.TierColor;
        }
        if (previous.Coordinates != contact.Coordinates || previous.Name == null)
        {
            _coordinates.Text = Loc.GetString("medical-tracking-coordinates-short",
                ("x", (int) contact.Coordinates.X), ("y", (int) contact.Coordinates.Y));
            _coordinates.ToolTip = Loc.GetString("medical-tracking-coordinates",
                ("x", (int) contact.Coordinates.X), ("y", (int) contact.Coordinates.Y));
        }
        if (previous.State != contact.State || previous.Name == null)
        {
            var color = MedicalTrackingDisplay.StatusColor(contact.State);
            _status.Text = MedicalTrackingDisplay.ShortStatus(contact.State);
            _status.ToolTip = MedicalTrackingDisplay.Status(contact.State);
            _status.FontColorOverride = color;
            _stripeStyle.BackgroundColor = color;
        }
    }

    public void UpdateAge(TimeSpan now)
    {
        var seconds = MedicalTrackingDisplay.SignalAge(Contact.UpdatedAt, now);
        if (seconds == _lastAge)
            return;

        _lastAge = seconds;
        _age.Text = Loc.GetString("medical-tracking-age-compact", ("seconds", seconds));
        _age.FontColorOverride = MedicalTrackingDisplay.SignalColor(Contact.UpdatedAt, now);
        _age.ToolTip = Loc.GetString(seconds >= MedicalTrackingDisplay.StaleSignalSeconds
            ? "medical-tracking-signal-stale"
            : "medical-tracking-signal-age", ("seconds", seconds));
    }
}
