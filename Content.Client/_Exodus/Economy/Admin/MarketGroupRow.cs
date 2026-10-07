using System.Globalization;
using Content.Shared._Exodus.Economy.Admin;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;

namespace Content.Client._Exodus.Economy.Admin;

public sealed partial class MarketGroupRow : BoxContainer
{
    [Dependency] private ILocalizationManager _loc = default!;

    public event Action? Changed;
    public event Action? ResetQuotes;
    public readonly MarketAdminGroup Group;
    public readonly CheckBox Override;
    public readonly MarketNumericEdit Pressure = new(true);
    private readonly MarketNumericEdit _quantity = new();
    private readonly RichTextLabel _preview = new();
    private readonly RichTextLabel _source = new();
    private readonly Button _reset;
    private MarketGlobalSettings _global;
    private bool _loading;

    public MarketGroupRow(MarketAdminGroup group, MarketGlobalSettings global)
    {
        IoCManager.InjectDependencies(this);
        Group = group;
        _global = global;
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        Margin = new Thickness(0, 4, 0, 12);
        var title = new RichTextLabel();
        title.SetMessage(Loc.GetString(group.Name));
        AddChild(title);
        AddChild(_source);
        var input = new BoxContainer { SeparationOverride = 8 };
        input.AddChild(new Label { Text = Loc.GetString(group.Gases ? "economy-admin-rise-mole" : "economy-admin-rise") });
        input.AddChild(Pressure);
        Override = new CheckBox { Text = Loc.GetString("economy-admin-override"), Pressed = group.OverrideImpactStrength.HasValue };
        input.AddChild(Override);
        AddChild(input);
        Pressure.SetReferenceVolume(global.ReferenceVolume);
        Pressure.SetValue(group.ImpactStrength);
        Pressure.Input.Editable = Override.Pressed;
        Pressure.Changed += OnEdited;
        Override.OnToggled += _ =>
        {
            Pressure.Input.Editable = Override.Pressed;
            if (!Override.Pressed)
                Pressure.SetValue(_global.ImpactStrength * Group.ImpactMultiplier);
            OnEdited();
        };

        var batch = new BoxContainer { SeparationOverride = 8 };
        batch.AddChild(new Label { Text = Loc.GetString(group.Gases ? "economy-admin-preview-moles" : "economy-admin-preview-units") });
        batch.AddChild(_quantity);
        _quantity.SetValue(1);
        _quantity.Changed += UpdatePreview;
        foreach (var quantity in new[] { 1, 10, 100 })
        {
            var button = new Button { Text = quantity.ToString(CultureInfo.InvariantCulture) };
            button.OnPressed += _ =>
            {
                _quantity.SetValue(quantity);
                UpdatePreview();
            };
            batch.AddChild(button);
        }

        AddChild(batch);
        AddChild(_preview);
        _reset = new Button { Text = Loc.GetString("economy-admin-reset-group") };
        _reset.OnPressed += _ => ResetQuotes?.Invoke();
        AddChild(_reset);
        UpdatePreview();
    }

    public void SetQuoteActionsEnabled(bool enabled)
    {
        _reset.Disabled = !enabled;
    }

    public bool TryGetOverride(out double? strength)
    {
        strength = null;
        if (!Override.Pressed)
            return true;
        if (!Pressure.TryGetValue(out var value) || value < 0)
            return false;

        strength = value;
        return true;
    }

    public void UpdateGlobal(MarketGlobalSettings global)
    {
        _global = global;
        _loading = true;
        Pressure.SetReferenceVolume(global.ReferenceVolume);
        Pressure.Input.Editable = Override.Pressed;
        if (!Override.Pressed)
            Pressure.SetValue(global.ImpactStrength * Group.ImpactMultiplier);
        _loading = false;
        UpdatePreview();
    }

    private void OnEdited()
    {
        UpdatePreview();
        if (!_loading)
            Changed?.Invoke();
    }

    private void UpdatePreview()
    {
        _source.SetMessage(Loc.GetString(Override.Pressed ? "economy-admin-source-override" : "economy-admin-source-group",
            ("percent", MarketAdminFormatting.Number(Group.ImpactMultiplier * 100, Culture)),
            ("multiplier", MarketAdminFormatting.Factor(Group.ImpactMultiplier, Culture))));
        if (!Pressure.TryGetValue(out var strength) || !_quantity.TryGetValue(out var quantity) || quantity < 0 || !_global.IsValid())
        {
            _preview.SetMessage(Loc.GetString("economy-admin-invalid-number"));
            return;
        }

        var exponent = _global.Enabled ? strength * quantity / _global.ReferenceVolume : 0;
        // Preview only factors, from nominal 1.0. Clamp the exponent before Exp to avoid overflow.
        var rise = Math.Clamp(Math.Exp(Math.Clamp(exponent, -700, 700)), _global.MinFactor, _global.MaxFactor);
        var fall = Math.Clamp(Math.Exp(Math.Clamp(-exponent, -700, 700)), _global.MinFactor, _global.MaxFactor);
        _preview.SetMessage(Loc.GetString("economy-admin-group-preview",
            ("unit", Loc.GetString(Group.Gases ? "economy-admin-per-mole" : "economy-admin-per-unit")),
            ("drop", MarketAdminFormatting.Number(MarketAdminFormatting.FallPercent(strength, _global.ReferenceVolume), Culture, signed: true)),
            ("dropFactor", MarketAdminFormatting.Factor(Math.Exp(-strength / _global.ReferenceVolume), Culture)),
            ("buyPercent", MarketAdminFormatting.Number(MarketAdminFormatting.FactorChange(rise), Culture, signed: true)),
            ("sellPercent", MarketAdminFormatting.Number(MarketAdminFormatting.FactorChange(fall), Culture, signed: true)),
            ("buy", MarketAdminFormatting.Factor(rise, Culture)),
            ("sell", MarketAdminFormatting.Factor(fall, Culture))));
    }

    private CultureInfo Culture => _loc.DefaultCulture ?? CultureInfo.InvariantCulture;
}
