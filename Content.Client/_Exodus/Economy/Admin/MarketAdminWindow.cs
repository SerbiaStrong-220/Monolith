using System.Globalization;
using System.Numerics;
using Content.Client._Exodus.Economy.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Exodus.Economy;
using Content.Shared._Exodus.Economy.Admin;
using Content.Shared.Eui;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Localization;

namespace Content.Client._Exodus.Economy.Admin;

public sealed partial class MarketAdminWindow : FancyWindow
{
    [Dependency] private ILocalizationManager _loc = default!;

    public event Action<EuiMessageBase>? Send;
    private readonly RichTextLabel _status = new();
    private readonly RichTextLabel _draftStatus = new();
    private readonly RichTextLabel _globalPressurePreview = new();
    private readonly BoxContainer _global = Vertical();
    private readonly BoxContainer _groups = Vertical();
    private readonly BoxContainer _quotes = Vertical();
    private readonly List<SettingField> _fields = new();
    private SettingField _strengthField = default!;
    private SettingField _referenceField = default!;
    private readonly List<MarketGroupRow> _groupRows = new();
    private readonly Dictionary<string, string> _quoteDrafts = new();
    private readonly CheckBox _enabled = new() { Text = Loc.GetString("economy-admin-enabled") };
    private readonly CheckBox _enabledOverride = new() { Text = Loc.GetString("economy-admin-override") };
    private readonly LineEdit _search = new() { HorizontalExpand = true, PlaceHolder = Loc.GetString("economy-admin-search") };
    private readonly OptionButton _groupFilter = new() { MinWidth = 150, MaxWidth = 230 };
    private readonly List<ProtoId<MarketCommodityGroupPrototype>?> _filterIds = new();
    private readonly Label _page = new();
    private readonly Button _previous = new() { Text = Loc.GetString("economy-admin-previous") };
    private readonly Button _next = new() { Text = Loc.GetString("economy-admin-next") };
    private readonly Button _apply = new() { Text = Loc.GetString("economy-admin-apply") };
    private readonly Button _resetAll = new() { Text = Loc.GetString("economy-admin-reset-all") };
    private readonly FancyWindow _confirmation;
    private readonly RichTextLabel _confirmationText = new();
    private readonly RichTextLabel _confirmationScope = new();
    private readonly MarketResetWarning _warning = new();
    private readonly Button _confirm = new() { Text = Loc.GetString("economy-admin-confirm") };
    private MarketAdminState? _state;
    private MarketSettingsSnapshot? _draftSnapshot;
    private bool _dirty;
    private bool _loading;
    private bool _pendingApply;
    private bool _applyAwaitingCommit;
    private bool _quoteAwaitingCommit;
    private bool _closingConfirmation;
    private MarketAdminMutationKind _confirmationKind;
    private int _confirmationStage;
    private string? _confirmationToken;
    private int _draftVersion;
    private int _submittedVersion;
    private string? _submittedQuoteKey;
    private string? _submittedQuoteText;
    private bool _clearUnknownGroupOverrides;

    public MarketAdminWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("economy-admin-title");
        MinSize = new Vector2(860, 620);
        SetSize = new Vector2(1040, 760);
        var root = Vertical();
        root.SeparationOverride = 8;
        root.AddChild(Text("economy-admin-help"));
        root.AddChild(_status);
        root.AddChild(_draftStatus);
        var tabs = new TabContainer { VerticalExpand = true };
        tabs.AddChild(Scroll(_global));
        tabs.SetTabTitle(0, Loc.GetString("economy-admin-tab-global"));
        tabs.AddChild(Scroll(_groups));
        tabs.SetTabTitle(1, Loc.GetString("economy-admin-tab-groups"));
        tabs.AddChild(BuildQuotes());
        tabs.SetTabTitle(2, Loc.GetString("economy-admin-tab-quotes"));
        root.AddChild(tabs);
        var actions = new BoxContainer { SeparationOverride = 8, Align = BoxContainer.AlignMode.Center };
        _apply.OnPressed += _ => Apply();
        actions.AddChild(_apply);
        var discard = new Button
        {
            Text = Loc.GetString("economy-admin-discard"),
            ToolTip = Loc.GetString("economy-admin-discard-tooltip"),
        };
        discard.OnPressed += _ =>
        {
            if (!_pendingApply && _state?.Settings != null)
                LoadDraft(_state);
        };
        actions.AddChild(discard);
        var refresh = new Button
        {
            Text = Loc.GetString("economy-admin-refresh"),
            ToolTip = Loc.GetString("economy-admin-refresh-tooltip"),
        };
        refresh.OnPressed += _ => Send?.Invoke(new MarketAdminRefreshMessage());
        actions.AddChild(refresh);
        root.AddChild(actions);
        ContentsContainer.AddChild(root);
        BuildGlobal();
        _confirmation = BuildConfirmation();
        UpdateDraftStatus();
    }

    private void BuildGlobal()
    {
        _global.AddChild(Text("economy-admin-global-help"));
        var enabled = new BoxContainer { SeparationOverride = 8 };
        enabled.AddChild(_enabled);
        enabled.AddChild(_enabledOverride);
        _enabled.OnToggled += _ => MarkDirty();
        _enabledOverride.OnToggled += _ =>
        {
            _enabled.Disabled = !_enabledOverride.Pressed;
            if (!_enabledOverride.Pressed && _draftSnapshot != null)
                _enabled.Pressed = _draftSnapshot.Defaults.Enabled;
            MarkDirty();
        };
        _global.AddChild(enabled);
        _strengthField = AddSetting("strength", s => s.ImpactStrength, s => s.ImpactStrength, (s, v) => s with { ImpactStrength = v }, true);
        _global.AddChild(_globalPressurePreview);
        _referenceField = AddSetting("reference", s => s.ReferenceVolume, s => s.ReferenceVolume, (s, v) => s with { ReferenceVolume = v });
        _global.AddChild(Text("economy-admin-reference-help"));
        AddSetting("minimum", s => s.MinFactor, s => s.MinFactor, (s, v) => s with { MinFactor = v }, factor: true);
        AddSetting("maximum", s => s.MaxFactor, s => s.MaxFactor, (s, v) => s with { MaxFactor = v }, factor: true);
        AddSetting("interval", s => s.DecayIntervalSeconds, s => s.DecayIntervalSeconds, (s, v) => s with { DecayIntervalSeconds = v });
        AddSetting("decay", s => s.DecayRate, s => s.DecayRate, (s, v) => s with { DecayRate = v }, scale: 100);
        AddSetting("margin", s => s.PurchaseMargin, s => s.PurchaseMargin, (s, v) => s with { PurchaseMargin = v }, scale: 100);
        _global.AddChild(Text("economy-admin-decay-help"));
        _global.AddChild(Text("economy-admin-range-help"));
        var inherit = new Button { Text = Loc.GetString("economy-admin-inherit-all") };
        inherit.OnPressed += _ =>
        {
            if (_draftSnapshot == null || _pendingApply)
                return;

            _clearUnknownGroupOverrides = true;
            _enabledOverride.Pressed = false;
            _enabled.Pressed = _draftSnapshot.Defaults.Enabled;
            _enabled.Disabled = true;
            foreach (var field in _fields)
            {
                field.Override.Pressed = false;
                field.Edit.SetValue(field.Effective(_draftSnapshot.Defaults));
                field.Edit.Input.Editable = false;
                field.Source.Text = Loc.GetString("economy-admin-source-default");
            }
            foreach (var row in _groupRows)
                row.Override.Pressed = false;
            MarkDirty();
        };
        _global.AddChild(inherit);
    }

    private SettingField AddSetting(string key, Func<MarketGlobalSettings, double> effective,
        Func<MarketGlobalSettingsOverride, double?> overridden,
        Func<MarketGlobalSettingsOverride, double?, MarketGlobalSettingsOverride> assign,
        bool pressure = false, double scale = 1, bool factor = false)
    {
        var row = Vertical();
        row.AddChild(Text("economy-admin-setting-" + key));
        var controls = new BoxContainer { SeparationOverride = 8 };
        var numeric = new MarketNumericEdit(pressure, scale, factor) { Name = key, HorizontalExpand = true };
        var useOverride = new CheckBox { Text = Loc.GetString("economy-admin-override") };
        var source = new Label();
        controls.AddChild(numeric);
        controls.AddChild(useOverride);
        controls.AddChild(source);
        row.AddChild(controls);
        _global.AddChild(row);
        var field = new SettingField(numeric, useOverride, source, effective, overridden, assign);
        _fields.Add(field);
        numeric.Changed += MarkDirty;
        useOverride.OnToggled += _ =>
        {
            numeric.Input.Editable = useOverride.Pressed;
            source.Text = Loc.GetString(useOverride.Pressed ? "economy-admin-source-override" : "economy-admin-source-default");
            if (!useOverride.Pressed && _draftSnapshot != null)
                numeric.SetValue(effective(_draftSnapshot.Defaults));
            MarkDirty();
        };
        return field;
    }

    private BoxContainer BuildQuotes()
    {
        var root = Vertical();
        root.AddChild(Text("economy-admin-quotes-help"));
        var search = new BoxContainer { SeparationOverride = 8 };
        search.AddChild(_search);
        search.AddChild(_groupFilter);
        var find = new Button { Text = Loc.GetString("economy-admin-find") };
        find.OnPressed += _ => Query(0);
        _search.OnTextEntered += _ => Query(0);
        _groupFilter.OnItemSelected += args =>
        {
            _groupFilter.SelectId(args.Id);
            Query(0);
        };
        search.AddChild(find);
        root.AddChild(search);
        root.AddChild(Scroll(_quotes));
        var pagination = new BoxContainer { SeparationOverride = 8, Align = BoxContainer.AlignMode.Center };
        _previous.OnPressed += _ => Query((_state?.Page ?? 0) - 1);
        _next.OnPressed += _ => Query((_state?.Page ?? 0) + 1);
        pagination.AddChild(_previous);
        pagination.AddChild(_page);
        pagination.AddChild(_next);
        root.AddChild(pagination);
        _resetAll.HorizontalAlignment = HAlignment.Center;
        _resetAll.OnPressed += _ => Send?.Invoke(new MarketAdminBeginResetAllMessage());
        root.AddChild(_resetAll);
        return root;
    }

    public void UpdateState(MarketAdminState state)
    {
        _state = state;
        if (_pendingApply && !state.Saving && state.Status != null &&
            (!state.StatusSuccess || _applyAwaitingCommit && state.Status == "economy-admin-saved"))
        {
            _pendingApply = false;
            _applyAwaitingCommit = false;
            if (state.StatusSuccess && state.Status == "economy-admin-saved" && _draftVersion == _submittedVersion)
                _dirty = false;
        }

        if (_confirmationKind == MarketAdminMutationKind.ApplySettings &&
            state.ConfirmationKind == MarketAdminMutationKind.None && !state.Saving && !state.StatusSuccess)
        {
            _pendingApply = false;
            _applyAwaitingCommit = false;
        }

        if (!state.Saving && state.Status != null && _submittedQuoteKey != null &&
            (!state.StatusSuccess || _quoteAwaitingCommit && state.Status == "economy-admin-quotes-saved"))
        {
            if (state.StatusSuccess && state.Status == "economy-admin-quotes-saved" &&
                _quoteDrafts.TryGetValue(_submittedQuoteKey, out var text) && text == _submittedQuoteText)
                _quoteDrafts.Remove(_submittedQuoteKey);
            _submittedQuoteKey = null;
            _submittedQuoteText = null;
            _quoteAwaitingCommit = false;
        }

        if (state.Settings != null && (!_dirty || _draftSnapshot == null))
            LoadDraft(state);
        if (state.Status != null)
            _status.SetMessage(Loc.GetString(state.Status));
        else
            _status.SetMessage(Loc.GetString(state.Saving ? "economy-admin-saving" : state.Ready ? "economy-admin-ready" : "economy-admin-loading"));
        UpdateQuotes(state);
        foreach (var row in _groupRows)
            row.SetQuoteActionsEnabled(CanChangeQuotes(state));
        UpdateDraftStatus();
        UpdateConfirmation(state);
    }

    private void LoadDraft(MarketAdminState state)
    {
        if (state.Settings is not { } snapshot)
            return;

        _loading = true;
        _draftSnapshot = snapshot;
        _enabledOverride.Pressed = snapshot.Overrides.Enabled.HasValue;
        _enabled.Pressed = snapshot.Global.Enabled;
        _enabled.Disabled = !_enabledOverride.Pressed;
        _strengthField.Edit.SetReferenceVolume(snapshot.Global.ReferenceVolume);
        foreach (var field in _fields)
        {
            field.Override.Pressed = field.Overridden(snapshot.Overrides).HasValue;
            field.Edit.SetValue(field.Effective(snapshot.Global));
            field.Edit.Input.Editable = field.Override.Pressed;
            field.Source.Text = Loc.GetString(field.Override.Pressed ? "economy-admin-source-override" : "economy-admin-source-default");
        }

        _groups.DisposeAllChildren();
        _groupRows.Clear();
        _groups.AddChild(Text("economy-admin-groups-help"));
        foreach (var group in state.Groups)
        {
            var row = new MarketGroupRow(group, snapshot.Global);
            row.Changed += MarkDirty;
            row.ResetQuotes += () => Send?.Invoke(new MarketAdminResetGroupMessage(group.Id));
            _groups.AddChild(row);
            _groupRows.Add(row);
        }

        var selected = _filterIds.Count > _groupFilter.SelectedId ? _filterIds[_groupFilter.SelectedId] : null;
        _filterIds.Clear();
        _groupFilter.Clear();
        _filterIds.Add(null);
        _groupFilter.AddItem(Loc.GetString("economy-admin-all-groups"), 0);
        var selection = 0;
        foreach (var group in state.Groups)
        {
            var id = _filterIds.Count;
            _filterIds.Add(group.Id);
            _groupFilter.AddItem(Loc.GetString(group.Name), id);
            if (group.Id == selected)
                selection = id;
        }

        _groupFilter.SelectId(selection);
        _dirty = false;
        _clearUnknownGroupOverrides = false;
        _loading = false;
        UpdatePressurePreview(snapshot.Global);
        UpdateDraftStatus();
    }

    private void MarkDirty()
    {
        if (_loading)
            return;

        CancelConfirmationOnEdit();
        _dirty = true;
        _draftVersion++;
        if (TryGetGlobals(out var globals) && _draftSnapshot != null)
        {
            var effective = globals.Resolve(_draftSnapshot.Defaults);
            UpdatePressurePreview(effective);
            foreach (var row in _groupRows)
                row.UpdateGlobal(effective);
        }

        UpdateDraftStatus();
    }

    private bool TryGetGlobals(out MarketGlobalSettingsOverride globals)
    {
        globals = new MarketGlobalSettingsOverride(Enabled: _enabledOverride.Pressed ? _enabled.Pressed : null);
        if (_draftSnapshot == null)
            return false;

        // The displayed per-unit percentage depends on the reference volume. Reformat before reading strength,
        // preserving its raw draft value when the reference changes (including inherited group settings).
        var reference = _draftSnapshot.Defaults.ReferenceVolume;
        if (_referenceField.Override.Pressed && !_referenceField.Edit.TryGetValue(out reference))
            return false;
        if (!double.IsFinite(reference) || reference <= 0)
            return false;
        _strengthField.Edit.SetReferenceVolume(reference);
        foreach (var field in _fields)
        {
            double? value = null;
            if (field.Override.Pressed)
            {
                if (!field.Edit.TryGetValue(out var number))
                    return false;
                value = number;
            }

            globals = field.Assign(globals, value);
        }

        return globals.Resolve(_draftSnapshot.Defaults).IsValid();
    }

    private void UpdatePressurePreview(MarketGlobalSettings global)
    {
        _globalPressurePreview.SetMessage(Loc.GetString("economy-admin-linked-drop",
            ("drop", MarketAdminFormatting.Number(MarketAdminFormatting.FallPercent(global.ImpactStrength, global.ReferenceVolume), Culture, signed: true)),
            ("factor", MarketAdminFormatting.Factor(Math.Exp(-global.ImpactStrength / global.ReferenceVolume), Culture))));
    }

    private void Apply()
    {
        if (_draftSnapshot == null || !TryGetGlobals(out var globals))
        {
            _status.SetMessage(Loc.GetString("economy-admin-error-invalid"));
            return;
        }

        var groups = _clearUnknownGroupOverrides
            ? new Dictionary<ProtoId<MarketCommodityGroupPrototype>, double>()
            : new Dictionary<ProtoId<MarketCommodityGroupPrototype>, double>(_draftSnapshot.GroupOverrides);
        foreach (var row in _groupRows)
        {
            if (!row.TryGetOverride(out var strength))
            {
                _status.SetMessage(Loc.GetString("economy-admin-error-invalid"));
                return;
            }

            groups.Remove(row.Group.Id);
            if (strength.HasValue)
                groups.Add(row.Group.Id, strength.Value);
        }

        _pendingApply = true;
        _applyAwaitingCommit = false;
        _submittedVersion = _draftVersion;
        UpdateDraftStatus();
        Send?.Invoke(new MarketAdminApplySettingsMessage(_draftSnapshot.Revision, globals, groups));
    }

    private void UpdateDraftStatus()
    {
        var conflict = _dirty && _draftSnapshot?.Revision != _state?.Settings?.Revision;
        _draftStatus.SetMessage(Loc.GetString(conflict ? "economy-admin-draft-conflict" : _dirty ? "economy-admin-draft-changed" : "economy-admin-draft-clean"));
        _apply.Disabled = !_dirty || conflict || _pendingApply || _state is not { Ready: true, Saving: false, ConfirmationKind: MarketAdminMutationKind.None };
        _resetAll.Disabled = _state == null || !CanChangeQuotes(_state);
    }

    private void Query(int page)
    {
        if (_loading || page < 0 || _filterIds.Count == 0)
            return;

        Send?.Invoke(new MarketAdminQueryMessage(_search.Text, _filterIds[_groupFilter.SelectedId], page));
    }

    private void UpdateQuotes(MarketAdminState state)
    {
        _quotes.DisposeAllChildren();
        if (state.Quotes.Count == 0)
            _quotes.AddChild(Text("economy-admin-empty"));
        foreach (var quote in state.Quotes)
        {
            var groupName = GroupName(state, quote.Group.Id);
            var row = Vertical();
            row.Margin = new Thickness(0, 0, 0, 8);
            var name = new Label
            {
                Text = quote.Name,
                ToolTip = Loc.GetString("economy-admin-quote-details", ("name", quote.Name), ("key", quote.MarketKey),
                    ("group", groupName), ("groupId", quote.Group.Id), ("rule", quote.Rule?.Id ?? Loc.GetString("economy-admin-default-rule"))),
                ClipText = true,
                MouseFilter = MouseFilterMode.Stop,
            };
            row.AddChild(name);
            row.AddChild(new Label
            {
                Text = Loc.GetString("economy-admin-quote-group", ("group", groupName)),
                ClipText = true,
                ToolTip = groupName,
                MouseFilter = MouseFilterMode.Stop,
            });
            var signals = new BoxContainer { SeparationOverride = 18 };
            signals.AddChild(new Label
            {
                Text = Loc.GetString("economy-admin-price-vs-base",
                    ("percent", MarketAdminFormatting.Number(MarketAdminFormatting.FactorChange(quote.Factor), Culture, signed: true)),
                    ("factor", MarketAdminFormatting.Factor(quote.Factor, Culture))),
            });
            var trend = new Label { ToolTip = Loc.GetString("economy-admin-last-movement-tooltip"), MouseFilter = MouseFilterMode.Stop };
            var previous = quote.Factor - quote.Trend;
            var change = quote.Trend / previous * 100;
            MarketTerminalTheme.ApplyTrend(trend, quote.Trend, change);
            if (!double.IsFinite(previous) || previous <= 0 || !double.IsFinite(change))
            {
                trend.Text = Loc.GetString(quote.Trend > 0.00005f ? "economy-admin-trend-up"
                    : quote.Trend < -0.00005f ? "economy-admin-trend-down" : "economy-admin-trend-flat");
            }

            trend.Text = Loc.GetString("economy-admin-last-movement", ("movement", trend.Text ?? string.Empty));
            signals.AddChild(trend);
            row.AddChild(signals);
            var actions = new BoxContainer { SeparationOverride = 8 };
            var factor = new MarketNumericEdit(factor: true)
            {
                SetWidth = 240,
                ToolTip = Loc.GetString("economy-admin-quote-percent-tooltip"),
            };
            factor.SetValue(quote.Factor);
            if (_quoteDrafts.TryGetValue(quote.MarketKey, out var draft))
                factor.Input.SetText(draft, invokeEvent: true);
            factor.Changed += () =>
            {
                CancelConfirmationOnEdit();
                _quoteDrafts[quote.MarketKey] = factor.Input.Text;
            };
            actions.AddChild(factor);
            var apply = new Button { Text = Loc.GetString("economy-admin-set-quote"), Disabled = !CanChangeQuotes(state) };
            apply.OnPressed += _ =>
            {
                if (!factor.TryGetValue(out var value))
                {
                    _status.SetMessage(Loc.GetString("economy-admin-invalid-number"));
                    return;
                }

                _submittedQuoteKey = quote.MarketKey;
                _submittedQuoteText = factor.Input.Text;
                _quoteAwaitingCommit = false;
                Send?.Invoke(new MarketAdminSetQuoteMessage(quote.MarketKey, value));
            };
            actions.AddChild(apply);
            var reset = new Button { Text = Loc.GetString("economy-admin-reset-quote"), Disabled = !CanChangeQuotes(state) };
            reset.OnPressed += _ => Send?.Invoke(new MarketAdminResetQuoteMessage(quote.MarketKey));
            actions.AddChild(reset);
            row.AddChild(actions);
            _quotes.AddChild(row);
        }

        _page.Text = Loc.GetString("economy-admin-page", ("page", state.Page + 1), ("pages", Math.Max(1, (state.TotalQuotes + state.PageSize - 1) / state.PageSize)), ("total", state.TotalQuotes));
        _previous.Disabled = state.Page == 0;
        _next.Disabled = (state.Page + 1) * state.PageSize >= state.TotalQuotes;
    }

    private FancyWindow BuildConfirmation()
    {
        var window = new FancyWindow
        {
            Title = Loc.GetString("economy-admin-confirm-title"),
            MinSize = new Vector2(480, 0),
            SetWidth = 620,
            Resizable = false,
        };
        var contents = Vertical();
        contents.SeparationOverride = 12;
        contents.Margin = new Thickness(12);
        contents.AddChild(_confirmationText);
        contents.AddChild(_warning);
        contents.AddChild(_confirmationScope);
        var actions = new BoxContainer { SeparationOverride = 12, Align = BoxContainer.AlignMode.Center };
        actions.AddChild(_confirm);
        _confirm.OnPressed += _ =>
        {
            if (_confirmationToken == null || _confirm.Disabled)
                return;

            _confirm.Disabled = true;
            if (_confirmationStage == 3)
            {
                _applyAwaitingCommit = _confirmationKind == MarketAdminMutationKind.ApplySettings && _pendingApply;
                _quoteAwaitingCommit = _confirmationKind == MarketAdminMutationKind.SetQuote &&
                    _submittedQuoteKey != null && _state?.ConfirmationTarget == _submittedQuoteKey;
            }

            Send?.Invoke(new MarketAdminConfirmMutationMessage(_confirmationStage, _confirmationToken));
        };
        var cancel = new Button { Text = Loc.GetString("economy-admin-cancel") };
        cancel.OnPressed += _ => window.Close();
        actions.AddChild(cancel);
        contents.AddChild(actions);
        window.ContentsContainer.AddChild(contents);
        window.OnClose += () =>
        {
            if (_closingConfirmation)
                return;

            if (_confirmationKind == MarketAdminMutationKind.ApplySettings)
                _pendingApply = false;
            _applyAwaitingCommit = false;
            _quoteAwaitingCommit = false;
            _submittedQuoteKey = null;
            _submittedQuoteText = null;
            _confirmationKind = MarketAdminMutationKind.None;
            _confirmationStage = 0;
            _confirmationToken = null;
            Send?.Invoke(new MarketAdminCancelMutationMessage());
            UpdateDraftStatus();
        };
        return window;
    }

    private void UpdateConfirmation(MarketAdminState state)
    {
        if (state.ConfirmationKind == MarketAdminMutationKind.None || state.ConfirmationStage is < 1 or > 3 || state.ConfirmationToken == null)
        {
            CloseConfirmation();
            return;
        }

        // A draft can change while the initial challenge is travelling back from the server.
        if ((state.ConfirmationKind == MarketAdminMutationKind.ApplySettings && _pendingApply && _draftVersion != _submittedVersion) ||
            (state.ConfirmationKind == MarketAdminMutationKind.SetQuote && _submittedQuoteKey != null &&
                _quoteDrafts.TryGetValue(_submittedQuoteKey, out var draft) && draft != _submittedQuoteText))
        {
            _pendingApply = false;
            _applyAwaitingCommit = false;
            _quoteAwaitingCommit = false;
            _submittedQuoteKey = null;
            _submittedQuoteText = null;
            CloseConfirmation();
            Send?.Invoke(new MarketAdminCancelMutationMessage());
            return;
        }

        var changed = _confirmationKind != state.ConfirmationKind || _confirmationStage != state.ConfirmationStage ||
            _confirmationToken != state.ConfirmationToken;
        if (changed)
            _confirm.Disabled = false;
        _confirmationKind = state.ConfirmationKind;
        _confirmationStage = state.ConfirmationStage;
        _confirmationToken = state.ConfirmationToken;
        var finalReset = _confirmationStage == 3 && _confirmationKind == MarketAdminMutationKind.ResetAllQuotes;
        var text = Loc.GetString(_confirmationStage == 3 && !finalReset
            ? "economy-admin-mutation-confirm-3" : "economy-admin-reset-confirm-" + _confirmationStage);
        _confirmationText.SetMessage(text);
        _confirmationText.Visible = !finalReset;
        _warning.Text = text;
        _warning.Visible = finalReset;
        _confirmationScope.SetMessage(ConfirmationScope(state));
        if (!_confirmation.IsOpen)
            _confirmation.Open();

        // Child invalidation is queued, so update the ancestor chain before synchronously measuring/recentering.
        for (var control = _confirmationScope.Parent; control != null; control = control.Parent)
        {
            control.InvalidateMeasure();
            if (control == _confirmation)
                break;
        }

        // Attach before measuring so wrapping uses the real stylesheet font, then size to content.
        _confirmation.Measure(new Vector2(_confirmation.SetWidth, float.PositiveInfinity));
        if (changed)
            _confirmation.RecenterWindow(new Vector2(0.5f, 0.5f));
    }

    private string ConfirmationScope(MarketAdminState state)
    {
        var target = state.ConfirmationTarget ?? string.Empty;
        return state.ConfirmationKind switch
        {
            MarketAdminMutationKind.ApplySettings => Loc.GetString("economy-admin-confirm-settings"),
            MarketAdminMutationKind.SetQuote => Loc.GetString("economy-admin-confirm-set-quote", ("target", target),
                ("percent", state.ConfirmationFactor is { } factor ? MarketAdminFormatting.Number(MarketAdminFormatting.FactorChange(factor), Culture, signed: true) : string.Empty),
                ("factor", state.ConfirmationFactor is { } value ? MarketAdminFormatting.Factor(value, Culture) : string.Empty)),
            MarketAdminMutationKind.ResetQuote => Loc.GetString("economy-admin-confirm-reset-quote", ("target", target)),
            MarketAdminMutationKind.ResetGroup => Loc.GetString("economy-admin-confirm-reset-group", ("target", GroupName(state, target))),
            MarketAdminMutationKind.ResetAllQuotes => Loc.GetString("economy-admin-reset-scope", ("count", state.ResetQuoteCount)),
            _ => string.Empty,
        };
    }

    private CultureInfo Culture => _loc.DefaultCulture ?? CultureInfo.InvariantCulture;

    private static string GroupName(MarketAdminState state, string id)
    {
        foreach (var group in state.Groups)
        {
            if (group.Id.Id == id)
                return Loc.GetString(group.Name);
        }

        return id;
    }

    private void CloseConfirmation()
    {
        _closingConfirmation = true;
        _confirmation.Close();
        _closingConfirmation = false;
        _confirmationToken = null;
        _confirmationStage = 0;
        _confirmationKind = MarketAdminMutationKind.None;
    }

    private void CancelConfirmationOnEdit()
    {
        if (_confirmation.IsOpen)
            _confirmation.Close();
    }

    protected override void ExitedTree()
    {
        CloseConfirmation();
        base.ExitedTree();
    }

    private static BoxContainer Vertical() => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };

    private static bool CanChangeQuotes(MarketAdminState state) =>
        state is { Ready: true, Saving: false, QuotesPersistenceEnabled: true, ConfirmationKind: MarketAdminMutationKind.None };

    private static ScrollContainer Scroll(Control child)
    {
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(child);
        return scroll;
    }

    private static RichTextLabel Text(string key)
    {
        var label = new RichTextLabel();
        label.SetMessage(Loc.GetString(key));
        return label;
    }

    private sealed record SettingField(
        MarketNumericEdit Edit,
        CheckBox Override,
        Label Source,
        Func<MarketGlobalSettings, double> Effective,
        Func<MarketGlobalSettingsOverride, double?> Overridden,
        Func<MarketGlobalSettingsOverride, double?, MarketGlobalSettingsOverride> Assign);
}
