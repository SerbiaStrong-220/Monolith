using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client._Exodus.Economy.Admin;
using Content.Client.UserInterface.Controls;
using Content.Shared._Exodus.Economy;
using Content.Shared._Exodus.Economy.Admin;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketAdminLayoutTest
{
    [TestCase("ru-RU", "0,15")]
    [TestCase("en-US", "0.15")]
    public async Task PressureEditorPreservesUneditedPrecisionAndAcceptsDecimalSeparators(string culture, string edited)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var loc = pair.Client.ResolveDependency<ILocalizationManager>();
            var original = loc.DefaultCulture;
            loc.SetCulture(CultureInfo.GetCultureInfo(culture));
            try
            {
                const double strength = 0.00024000000208616257;
                using var editor = new MarketNumericEdit(true);
                editor.SetValue(strength);
                Assert.That(editor.Input.Text, Is.EqualTo(culture == "ru-RU" ? "0,00024" : "0.00024"),
                    "Small gas pressure must remain visible as a nonzero percentage per mole.");
                Assert.That(editor.TryGetValue(out var unchanged), Is.True);
                Assert.That(unchanged, Is.EqualTo(strength), "An untouched gas field must retain its original raw strength.");
                editor.Input.SetText(edited, invokeEvent: true);
                Assert.That(editor.TryGetValue(out var changed), Is.True);
                Assert.That(changed, Is.EqualTo(0.149887612373589179857).Within(1e-15));
                editor.SetReferenceVolume(200);
                Assert.That(editor.TryGetValue(out var resized), Is.True);
                Assert.That(resized, Is.EqualTo(changed), "Changing the reference volume must preserve an edited raw strength exactly.");
                editor.Input.SetText("invalid draft", invokeEvent: true);
                editor.SetReferenceVolume(300);
                Assert.That(editor.Input.Text, Is.EqualTo("invalid draft"));
                Assert.That(editor.TryGetValue(out _), Is.False, "Changing reference volume must not silently discard an invalid draft.");
                editor.Input.Text = "NaN";
                Assert.That(editor.TryGetValue(out _), Is.False);
            }
            finally
            {
                loc.DefaultCulture = original;
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ru-RU")]
    [TestCase("en-US")]
    public async Task ReferenceChangesPreserveRawSettingsAndApplyPercentBounds(string culture)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        MarketAdminWindow window = null!;
        MarketNumericEdit reference = null!;
        MarketGroupRow gas = null!;
        Button apply = null!;
        CultureInfo original = null;
        var sent = new List<Content.Shared.Eui.EuiMessageBase>();
        const double gasStrength = 0.00024000000208616257;
        await pair.Client.WaitAssertion(() =>
        {
            var loc = pair.Client.ResolveDependency<ILocalizationManager>();
            original = loc.DefaultCulture;
            loc.SetCulture(CultureInfo.GetCultureInfo(culture));
            window = new MarketAdminWindow();
            var state = CreateState();
            state.Settings = state.Settings! with
            {
                Overrides = new MarketGlobalSettingsOverride(ImpactStrength: 0.08, ReferenceVolume: 100, MinFactor: 0.01, MaxFactor: 9.99),
            };
            state.Settings.GroupOverrides["Gases"] = gasStrength;
            state.Groups[0] = state.Groups[0] with { ImpactStrength = gasStrength, OverrideImpactStrength = gasStrength };
            state.Groups.Add(new MarketAdminGroup("General", "market-commodity-group-general", false, 1, 0.08, 0.08, null));
            window.UpdateState(state);
            window.OpenCentered();
            window.Send += sent.Add;
            var strength = NamedEditor(window, "strength");
            reference = NamedEditor(window, "reference");
            gas = Descendants(window).OfType<MarketGroupRow>().Single(row => row.Group.Id.Id == "Gases");
            var inherited = Descendants(window).OfType<MarketGroupRow>().Single(row => row.Group.Id.Id == "General");
            Assert.That(strength.Input.Text, Is.EqualTo(culture == "ru-RU" ? "0,08" : "0.08"));
            Assert.That(NamedEditor(window, "minimum").Input.Text, Is.EqualTo("-99"));
            Assert.That(NamedEditor(window, "maximum").Input.Text, Is.EqualTo("+899"));
            foreach (var row in new[] { gas, inherited })
            {
                var quantity = Descendants(row).OfType<MarketNumericEdit>().Single(editor => editor != row.Pressure);
                Assert.That(quantity.TryGetValue(out var units), Is.True);
                Assert.That(units, Is.EqualTo(1), "The initial preview must describe one unit or mole.");
            }

            reference.Input.SetText("200", invokeEvent: true);
            Assert.That(strength.Input.Text, Is.EqualTo(culture == "ru-RU" ? "0,04" : "0.04"));
            Assert.That(strength.TryGetValue(out var actualStrength), Is.True);
            Assert.That(actualStrength, Is.EqualTo(0.08));
            Assert.That(gas.Pressure.Input.Text, Is.EqualTo(culture == "ru-RU" ? "0,00012" : "0.00012"));
            Assert.That(gas.TryGetOverride(out var actualGasStrength), Is.True);
            Assert.That(actualGasStrength, Is.EqualTo(gasStrength));
            Assert.That(inherited.Pressure.Input.Text, Is.EqualTo(strength.Input.Text));
            Assert.That(inherited.Pressure.TryGetValue(out var inheritedStrength), Is.True);
            Assert.That(inheritedStrength, Is.EqualTo(0.08));
            Assert.That(inherited.TryGetOverride(out var inheritedOverride), Is.True);
            Assert.That(inheritedOverride, Is.Null, "Reformatting an inherited group must not create an override.");
            NamedEditor(window, "minimum").Input.SetText(culture == "ru-RU" ? "-99,0" : "-99.0", invokeEvent: true);
            NamedEditor(window, "maximum").Input.SetText(culture == "ru-RU" ? "899,0" : "899.0", invokeEvent: true);
            apply = Descendants(window).OfType<Button>().Single(button => button.Text == Loc.GetString("economy-admin-apply"));
            Assert.That(apply.Disabled, Is.False);
            apply.Mode = BaseButton.ActionMode.Press;
        });
        try
        {
            await pair.Client.DoGuiEvent(apply, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, default, default));
            await pair.Client.DoGuiEvent(apply, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, default, default));
            await pair.Client.WaitAssertion(() =>
            {
                var submission = sent.OfType<MarketAdminApplySettingsMessage>().Single();
                Assert.That(submission.Globals.ImpactStrength, Is.EqualTo(0.08));
                Assert.That(submission.Globals.ReferenceVolume, Is.EqualTo(200));
                Assert.That(submission.Globals.MinFactor, Is.EqualTo(0.01).Within(1e-14));
                Assert.That(submission.Globals.MaxFactor, Is.EqualTo(9.99).Within(1e-14));
                Assert.That(submission.GroupOverrides["Gases"], Is.EqualTo(gasStrength),
                    "Applying compact percentages must retain every bit of an untouched gas override.");
                Assert.That(submission.GroupOverrides.ContainsKey("General"), Is.False);
                gas.Pressure.Input.SetText("unfinished", invokeEvent: true);
                reference.Input.SetText("300", invokeEvent: true);
                Assert.That(gas.Pressure.Input.Text, Is.EqualTo("unfinished"));
                Assert.That(gas.TryGetOverride(out _), Is.False);
                var strength = NamedEditor(window, "strength");
                strength.Input.SetText("unfinished", invokeEvent: true);
                reference.Input.SetText("400", invokeEvent: true);
                Assert.That(strength.Input.Text, Is.EqualTo("unfinished"));
                Assert.That(strength.TryGetValue(out _), Is.False);
            });
        }
        finally
        {
            await pair.Client.WaitAssertion(() =>
            {
                window.Close();
                window.Dispose();
                pair.Client.ResolveDependency<ILocalizationManager>().DefaultCulture = original;
            });
        }

        await pair.CleanReturnAsync();
    }

    [TestCase("ru-RU")]
    [TestCase("en-US")]
    public async Task QuoteShowsLocalizedGroupAndSendsPercentageAsFactor(string culture)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        MarketAdminWindow window = null!;
        Button submit = null!;
        CultureInfo original = null;
        var sent = new List<Content.Shared.Eui.EuiMessageBase>();
        await pair.Client.WaitAssertion(() =>
        {
            var loc = pair.Client.ResolveDependency<ILocalizationManager>();
            original = loc.DefaultCulture;
            loc.SetCulture(CultureInfo.GetCultureInfo(culture));
            window = new MarketAdminWindow();
            window.UpdateState(CreateState());
            window.OpenCentered();
            window.Send += sent.Add;
            var tabs = Descendants(window).OfType<TabContainer>().Single();
            tabs.CurrentTab = 2;
            var groupName = culture == "ru-RU" ? "Газы" : "Gases";
            var group = Descendants(window).OfType<Label>().Single(label =>
                label.Text == Loc.GetString("economy-admin-quote-group", ("group", groupName)));
            Assert.That(group.VisibleInTree, Is.True, "The quote's group must be visible without opening a tooltip.");
            var editor = Descendants(tabs.Children.ElementAt(2)).OfType<MarketNumericEdit>().Single();
            Assert.That(editor.Input.Text, Is.EqualTo(culture == "ru-RU" ? "+23,46" : "+23.46"));
            Assert.That(editor.TryGetValue(out var originalFactor), Is.True);
            Assert.That(originalFactor, Is.EqualTo(1.23456789), "Compact formatting must not change the unedited quote.");
            editor.Input.SetText(culture == "ru-RU" ? "0,15" : "0.15", invokeEvent: true);
            submit = Descendants(window).OfType<Button>().Single(button => button.Text == Loc.GetString("economy-admin-set-quote"));
            submit.Mode = BaseButton.ActionMode.Press;
        });
        try
        {
            await pair.Client.DoGuiEvent(submit, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, default, default));
            await pair.Client.DoGuiEvent(submit, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, default, default));
            await pair.Client.WaitAssertion(() =>
            {
                var submission = sent.OfType<MarketAdminSetQuoteMessage>().Single();
                Assert.That(submission.MarketKey, Is.EqualTo("gas:Oxygen"));
                Assert.That(submission.Factor, Is.EqualTo(1.0015).Within(1e-15));
                var state = CreateState();
                state.Quotes[0] = state.Quotes[0] with { Group = "RemovedGroup" };
                window.UpdateState(state);
                Assert.That(Descendants(window).OfType<Label>().Any(label => label.VisibleInTree &&
                    label.Text == Loc.GetString("economy-admin-quote-group", ("group", "RemovedGroup"))), Is.True,
                    "A saved quote for a removed group must still show its ID without a missing-localization error.");
                var tabs = Descendants(window).OfType<TabContainer>().Single();
                var editor = Descendants(tabs.Children.ElementAt(2)).OfType<MarketNumericEdit>().Single();
                Assert.That(Descendants(editor).OfType<Label>().Any(label => label.Text ==
                    Loc.GetString("economy-admin-number-factor", ("factor", culture == "ru-RU" ? "1,0015" : "1.0015"))), Is.True,
                    "Refreshing a quote must show the coefficient of the preserved percent draft, not the old server value.");
            });
        }
        finally
        {
            await pair.Client.WaitAssertion(() =>
            {
                window.Close();
                window.Dispose();
                pair.Client.ResolveDependency<ILocalizationManager>().DefaultCulture = original;
            });
        }

        await pair.CleanReturnAsync();
    }

    [TestCase("ru-RU", 860, 620)]
    [TestCase("en-US", 1000, 720)]
    public async Task WindowAndFinalWarningFitSupportedSizes(string culture, int width, int height)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var loc = pair.Client.ResolveDependency<ILocalizationManager>();
            var original = loc.DefaultCulture;
            loc.SetCulture(CultureInfo.GetCultureInfo(culture));
            try
            {
                using var window = new MarketAdminWindow();
                window.UpdateState(CreateState());
                window.OpenCentered();
                Assert.That(window.IsInsideTree, Is.True);
                window.SetSize = new Vector2(width, height);
                var tabs = Descendants(window).OfType<TabContainer>().Single();
                for (var index = 0; index < tabs.ChildCount; index++)
                {
                    tabs.CurrentTab = index;
                    window.Measure(new Vector2(width, height));
                    window.Arrange(new UIBox2(0, 0, width, height));
                    foreach (var control in Descendants(window).Where(control => control.VisibleInTree && control is Button or ScrollContainer or RichTextLabel or LineEdit or Label))
                    {
                        Assert.That(control.GlobalRect.Right, Is.LessThanOrEqualTo(width + 0.1f), control.Name ?? control.GetType().Name);
                        if (!InsideScroll(control))
                            Assert.That(control.GlobalRect.Bottom, Is.LessThanOrEqualTo(height + 0.1f), control.Name ?? control.GetType().Name);
                    }
                }

                var normalLabel = Descendants(window).OfType<Label>().First(label => !string.IsNullOrEmpty(label.Text));
                Assert.That(normalLabel.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var normalFont), Is.True);
                Assert.That(normalFont.GetLineHeight(normalLabel.UIScale), Is.GreaterThan(0), "Window bounds must be measured with the real UI font.");
                var finalState = CreateState();
                finalState.ConfirmationKind = MarketAdminMutationKind.ResetAllQuotes;
                finalState.ConfirmationStage = 1;
                finalState.ConfirmationToken = "layout-test-token-1";
                window.UpdateState(finalState);
                var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
                var confirmation = ui.WindowRoot.Children.OfType<FancyWindow>()
                    .Single(candidate => Descendants(candidate).OfType<MarketResetWarning>().Any());
                confirmation.Measure(new Vector2(620, float.PositiveInfinity));
                confirmation.Arrange(UIBox2.FromDimensions(Vector2.Zero, confirmation.DesiredSize));
                var firstHeight = confirmation.DesiredSize.Y;
                Assert.That(firstHeight, Is.LessThan(360), "The first confirmation must fit its contents without the previous empty minimum height.");
                finalState.ConfirmationStage = 3;
                finalState.ConfirmationToken = "layout-test-token-3";
                window.UpdateState(finalState);
                confirmation.Measure(new Vector2(620, float.PositiveInfinity));
                confirmation.Arrange(UIBox2.FromDimensions(Vector2.Zero, confirmation.DesiredSize));
                Assert.That(confirmation.DesiredSize.Y, Is.GreaterThan(firstHeight), "The final warning must increase the dialog height instead of clipping its text.");
                var warning = Descendants(confirmation).OfType<MarketResetWarning>().Single();
                Assert.That(warning.IsInsideTree && warning.VisibleInTree, Is.True);
                Assert.That(warning.Text, Is.EqualTo(Loc.GetString("economy-admin-reset-confirm-3")));
                Assert.That(warning.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var warningFont), Is.True);
                Assert.That(warningFont.GetLineHeight(warning.UIScale), Is.GreaterThan(0), "The actual final confirmation must inherit the styled UI font.");
                foreach (var control in Descendants(confirmation).Where(control => control.VisibleInTree && control is Button or RichTextLabel or MarketResetWarning))
                {
                    Assert.That(control.GlobalRect.Right, Is.LessThanOrEqualTo(confirmation.GlobalRect.Right + 0.1f));
                    Assert.That(control.GlobalRect.Bottom, Is.LessThanOrEqualTo(confirmation.GlobalRect.Bottom + 0.1f));
                }

                var confirm = Descendants(confirmation).OfType<Button>().Single(button => button.Text == Loc.GetString("economy-admin-confirm"));
                var cancel = Descendants(confirmation).OfType<Button>().Single(button => button.Text == Loc.GetString("economy-admin-cancel"));
                Assert.That((confirm.GlobalRect.Left + cancel.GlobalRect.Right) / 2,
                    Is.EqualTo(confirmation.GlobalRect.Center.X).Within(1), "Confirmation actions must be centered together.");

                warning.Measure(new Vector2(520, float.PositiveInfinity));
                Assert.That(warning.DesiredSize.X, Is.LessThanOrEqualTo(520));
                Assert.That(warning.DesiredSize.Y, Is.GreaterThan(30), "The full warning must wrap onto multiple lines.");
                var wideHeight = warning.DesiredSize.Y;
                warning.Measure(new Vector2(260, float.PositiveInfinity));
                Assert.That(warning.DesiredSize.X, Is.LessThanOrEqualTo(260));
                Assert.That(warning.DesiredSize.Y, Is.GreaterThan(wideHeight), "Reducing width must reflow the cached glyph layout.");
                window.Close();
                Assert.That(warning.IsInsideTree, Is.False, "Closing the owner must remove the animated confirmation from the UI tree.");
            }
            finally
            {
                loc.DefaultCulture = original;
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NewServerRevisionPreservesGroupDraftAndPreventsSilentOverwrite()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var window = new MarketAdminWindow();
            window.UpdateState(CreateState());
            var group = Descendants(window).OfType<MarketGroupRow>().Single();
            group.Pressure.Input.SetText("12,3456789", invokeEvent: true);
            var next = CreateState();
            next.Settings = next.Settings! with { Revision = 2 };
            window.UpdateState(next);
            Assert.That(Descendants(window).OfType<MarketGroupRow>().Single(), Is.SameAs(group));
            Assert.That(group.Pressure.Input.Text, Is.EqualTo("12,3456789"));
            var apply = Descendants(window).OfType<Button>().Single(button => button.Text == Loc.GetString("economy-admin-apply"));
            Assert.That(apply.Disabled, Is.True, "A newer server revision must not silently overwrite or apply an old draft.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task CancellingSettingsConfirmationKeepsDraftAndAllowsResubmission(bool editDraft, bool beforePrompt)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        MarketAdminWindow window = null!;
        MarketGroupRow group = null!;
        Button apply = null!;
        Button cancel = null!;
        var sent = new List<Content.Shared.Eui.EuiMessageBase>();
        var finalText = editDraft ? "14,56789123" : "12,3456789";
        await pair.Client.WaitAssertion(() =>
        {
            window = new MarketAdminWindow();
            window.UpdateState(CreateState());
            window.OpenCentered();
            window.Send += sent.Add;
            group = Descendants(window).OfType<MarketGroupRow>().Single();
            group.Pressure.Input.SetText("12,3456789", invokeEvent: true);
            apply = Descendants(window).OfType<Button>().Single(button => button.Text == Loc.GetString("economy-admin-apply"));
            apply.Mode = BaseButton.ActionMode.Press;
        });
        try
        {
            await Click(apply);
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(sent.OfType<MarketAdminApplySettingsMessage>().Count(), Is.EqualTo(1));
                if (beforePrompt)
                    group.Pressure.Input.SetText(finalText, invokeEvent: true);
                var state = CreateState();
                state.ConfirmationKind = MarketAdminMutationKind.ApplySettings;
                state.ConfirmationStage = 1;
                state.ConfirmationToken = "cancel-settings-test";
                state.ConfirmationTarget = "1";
                window.UpdateState(state);
                var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
                if (beforePrompt)
                {
                    Assert.That(ui.WindowRoot.Children.OfType<FancyWindow>()
                        .Any(candidate => Descendants(candidate).OfType<MarketResetWarning>().Any()), Is.False,
                        "A changed draft must cancel a delayed challenge before opening its dialog.");
                    return;
                }

                var confirmation = ui.WindowRoot.Children.OfType<FancyWindow>()
                    .Single(candidate => Descendants(candidate).OfType<MarketResetWarning>().Any());
                cancel = Descendants(confirmation).OfType<Button>().Single(button => button.Text == Loc.GetString("economy-admin-cancel"));
                cancel.Mode = BaseButton.ActionMode.Press;
                if (editDraft)
                {
                    group.Pressure.Input.SetText(finalText, invokeEvent: true);
                    Assert.That(confirmation.IsOpen, Is.False, "Editing behind the dialog must cancel its frozen payload.");
                }
            });
            if (!editDraft)
                await Click(cancel);
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(sent.OfType<MarketAdminCancelMutationMessage>().Count(), Is.EqualTo(1));
                var state = CreateState();
                state.Status = "economy-admin-cancelled";
                window.UpdateState(state);
                Assert.That(Descendants(window).OfType<MarketGroupRow>().Single(), Is.SameAs(group));
                Assert.That(group.Pressure.Input.Text, Is.EqualTo(finalText));
                Assert.That(apply.Disabled, Is.False, "Cancelling confirmation must release the pending submission without discarding edits.");
            });
            await Click(apply);
            await pair.Client.WaitAssertion(() =>
            {
                var submissions = sent.OfType<MarketAdminApplySettingsMessage>().ToArray();
                Assert.That(submissions.Length, Is.EqualTo(2));
                Assert.That(submissions[1].GroupOverrides["Gases"],
                    Is.EqualTo(100 * double.LogP1(double.Parse(finalText.Replace(',', '.'), CultureInfo.InvariantCulture) / 100)).Within(1e-13));
            });
        }
        finally
        {
            await pair.Client.WaitAssertion(() =>
            {
                window.Close();
                window.Dispose();
            });
        }

        await pair.CleanReturnAsync();
        return;

        async Task Click(Button button)
        {
            await pair.Client.DoGuiEvent(button, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, default, default));
            await pair.Client.DoGuiEvent(button, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, default, default));
        }
    }

    private static MarketAdminState CreateState()
    {
        var global = new MarketGlobalSettings();
        var groupOverrides = new Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> { ["Gases"] = 0.00024 };
        return new MarketAdminState
        {
            Ready = true,
            QuotesPersistenceEnabled = true,
            Settings = new MarketSettingsSnapshot(1, global, global, new MarketGlobalSettingsOverride(), groupOverrides),
            Groups = [new MarketAdminGroup("Gases", "market-commodity-group-gases", true, 0.003, 0.00024, 0.00024, 0.00024)],
            Quotes = [new MarketAdminQuote("gas:Oxygen", "Газ для восстановления экспериментального атмосферного оборудования исследовательского корабля", "Gases", null, 1.23456789, 0.001f)],
            TotalQuotes = 1,
        };
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StaleSuccessBeforeOwnThirdConfirmationPreservesPendingDraft(bool quote)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        MarketAdminWindow window = null!;
        MarketNumericEdit editor = null!;
        Button submit = null!;
        Button confirm = null!;
        var sent = new List<Content.Shared.Eui.EuiMessageBase>();
        var draftText = quote ? "2,56789123" : "12,3456789";
        var success = quote ? "economy-admin-quotes-saved" : "economy-admin-saved";
        var kind = quote ? MarketAdminMutationKind.SetQuote : MarketAdminMutationKind.ApplySettings;
        await pair.Client.WaitAssertion(() =>
        {
            window = new MarketAdminWindow();
            window.UpdateState(CreateState());
            window.OpenCentered();
            window.Send += sent.Add;
            editor = DraftEditor();
            Descendants(window).OfType<TabContainer>().Single().CurrentTab = quote ? 2 : 1;
            editor.Input.SetText(draftText, invokeEvent: true);
            submit = Descendants(window).OfType<Button>().Single(button =>
                button.Text == Loc.GetString(quote ? "economy-admin-set-quote" : "economy-admin-apply"));
            submit.Mode = BaseButton.ActionMode.Press;
        });
        try
        {
            await Click(submit);
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(sent.Count, Is.EqualTo(1));
                var stale = CreateState();
                stale.StatusSuccess = true;
                stale.Status = success;
                window.UpdateState(stale);
                Assert.That(DraftEditor().Input.Text, Is.EqualTo(draftText),
                    "A delayed success from an earlier refresh must not discard the draft awaiting confirmation.");
                if (!quote)
                {
                    Assert.That(DraftEditor(), Is.SameAs(editor));
                    Assert.That(submit.Disabled, Is.True, "The pending settings submission must remain pending after an unrelated success.");
                }
            });

            for (var stage = 1; stage <= 3; stage++)
            {
                var currentStage = stage;
                await pair.Client.WaitAssertion(() =>
                {
                    var challenge = CreateState();
                    challenge.ConfirmationKind = kind;
                    challenge.ConfirmationStage = currentStage;
                    challenge.ConfirmationToken = "stale-success-test-" + currentStage;
                    challenge.ConfirmationTarget = quote ? "gas:Oxygen" : "1";
                    challenge.ConfirmationFactor = quote ? 1.0256789123 : null;
                    window.UpdateState(challenge);
                    var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
                    var dialog = ui.WindowRoot.Children.OfType<FancyWindow>()
                        .Single(candidate => Descendants(candidate).OfType<MarketResetWarning>().Any());
                    confirm = Descendants(dialog).OfType<Button>().Single(button => button.Text == Loc.GetString("economy-admin-confirm"));
                    confirm.Mode = BaseButton.ActionMode.Press;
                    Assert.That(DraftEditor().Input.Text, Is.EqualTo(draftText));
                });
                await Click(confirm);
            }

            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(sent.OfType<MarketAdminConfirmMutationMessage>().Select(message => message.Stage), Is.EqualTo(new[] { 1, 2, 3 }));
                var completed = CreateState();
                completed.StatusSuccess = true;
                completed.Status = success;
                completed.Settings = completed.Settings! with { Revision = 2 };
                if (quote)
                    completed.Quotes[0] = completed.Quotes[0] with { Factor = 1.0256789123 };
                else
                    completed.Groups[0] = completed.Groups[0] with { ImpactStrength = 100 * double.LogP1(12.3456789 / 100) };
                window.UpdateState(completed);

                // After our actual commit, a later server value must replace the now-clean draft.
                var refreshed = CreateState();
                refreshed.Settings = refreshed.Settings! with { Revision = 3 };
                window.UpdateState(refreshed);
                Assert.That(DraftEditor().TryGetValue(out var actual), Is.True);
                Assert.That(actual, Is.EqualTo(quote ? 1.23456789 : 0.00024),
                    "Only the acknowledged result of our third confirmation may clear the submitted draft.");
            });
        }
        finally
        {
            await pair.Client.WaitAssertion(() =>
            {
                window.Close();
                window.Dispose();
            });
        }

        await pair.CleanReturnAsync();
        return;

        MarketNumericEdit DraftEditor()
        {
            var tabs = Descendants(window).OfType<TabContainer>().Single();
            return quote
                ? Descendants(tabs.Children.ElementAt(2)).OfType<MarketNumericEdit>().Single()
                : Descendants(window).OfType<MarketGroupRow>().Single().Pressure;
        }

        async Task Click(Button button)
        {
            await pair.Client.DoGuiEvent(button, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, default, default));
            await pair.Client.DoGuiEvent(button, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, default, default));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ApplyingPreservesInactiveOverridesUnlessAllSettingsAreReset(bool resetAll)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        MarketAdminWindow window = null!;
        Button apply = null!;
        Button inherit = null!;
        MarketAdminApplySettingsMessage message = null;
        await pair.Client.WaitAssertion(() =>
        {
            window = new MarketAdminWindow();
            var state = CreateState();
            state.Settings!.GroupOverrides.Add("RemovedGroup", 0.123456789);
            window.UpdateState(state);
            window.Send += sent => message = sent as MarketAdminApplySettingsMessage;
            var group = Descendants(window).OfType<MarketGroupRow>().Single();
            group.Pressure.Input.SetText("12,3456789", invokeEvent: true);
            apply = Descendants(window).OfType<Button>().Single(button => button.Text == Loc.GetString("economy-admin-apply"));
            inherit = Descendants(window).OfType<Button>().Single(button => button.Text == Loc.GetString("economy-admin-inherit-all"));
            apply.Mode = BaseButton.ActionMode.Press;
            inherit.Mode = BaseButton.ActionMode.Press;
        });
        try
        {
            if (resetAll)
            {
                await pair.Client.DoGuiEvent(inherit, new GUIBoundKeyEventArgs(
                    EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, default, default));
                await pair.Client.DoGuiEvent(inherit, new GUIBoundKeyEventArgs(
                    EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, default, default));
            }

            await pair.Client.DoGuiEvent(apply, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, default, default));
            await pair.Client.DoGuiEvent(apply, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, default, default));
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(message, Is.Not.Null);
                if (resetAll)
                    Assert.That(message!.GroupOverrides, Is.Empty);
                else
                    Assert.That(message!.GroupOverrides["RemovedGroup"], Is.EqualTo(0.123456789));
            });
        }
        finally
        {
            await pair.Client.WaitAssertion(window.Dispose);
        }

        await pair.CleanReturnAsync();
    }

    private static bool InsideScroll(Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ScrollContainer)
                return true;
        }

        return false;
    }

    private static MarketNumericEdit NamedEditor(Control control, string name)
    {
        return Descendants(control).OfType<MarketNumericEdit>().Single(editor => editor.Name == name);
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
