using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._Exodus.MedicalTracking;

/// <summary>Shared medical device styling, following the panel and button layout of the genetics instruments.</summary>
public static class MedicalTrackingUiTheme
{
    public const string ButtonClass = "MedicalButton";
    public const string CardClass = "MedicalCard";
    public static readonly Color Background = Color.FromHex("#101B22");
    public static readonly Color Surface = Color.FromHex("#172830");
    public static readonly Color Border = Color.FromHex("#304A54");
    public static readonly Color Accent = Color.FromHex("#7EDDD4");
    public static readonly Color Text = Color.FromHex("#E1EFF1");
    public static readonly Color Muted = Color.FromHex("#9BB1BA");
    public static readonly Color Alive = Color.FromHex("#97D5AB");
    public static readonly Color Warning = Color.FromHex("#F1BD79");
    public static readonly Color Danger = Color.FromHex("#F08E9B");

    public static StyleBoxFlat Box(Color background, Color border, int padding = 6) => new()
    {
        BackgroundColor = background,
        BorderColor = border,
        BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = padding,
        ContentMarginRightOverride = padding,
        ContentMarginTopOverride = padding,
        ContentMarginBottomOverride = padding,
    };

    public static Stylesheet CreateStylesheet(Stylesheet? inherited)
    {
        var rules = new List<StyleRule>();
        if (inherited != null)
            rules.AddRange(inherited.Rules);
        AddPanel(rules, "AngleRect", Background, Border, 0);
        AddPanel(rules, "WindowHeadingBackground", Surface, Border, 0);
        AddPanel(rules, "MedicalHeader", Surface, Border, 8);
        AddPanel(rules, "MedicalPanel", Surface, Border, 6);
        AddPanel(rules, "MedicalMap", Background, Border, 4);
        AddButton(rules, "normal", Surface, Border);
        AddButton(rules, "hover", Color.FromHex("#233C45"), Accent);
        AddButton(rules, "pressed", Color.FromHex("#2B4E56"), Accent);
        AddButton(rules, "disabled", Background, Border);
        rules.Add(Child().Parent(Element<Button>().Class(ButtonClass)).Child(Element<Label>())
            .Prop(Label.StylePropertyAlignMode, Label.AlignMode.Center)
            .Prop(Label.StylePropertyFontColor, Text));
        rules.Add(Element<Label>().Prop(Label.StylePropertyFontColor, Text));
        AddText(rules, "MedicalText", Text);
        AddText(rules, "FancyWindowTitle", Accent);
        AddText(rules, "MedicalMuted", Muted);
        AddText(rules, "MedicalAccent", Accent);
        AddText(rules, "MedicalAlive", Alive);
        AddText(rules, "MedicalWarning", Warning);
        AddText(rules, "MedicalDanger", Danger);
        rules.Add(Element<LineEdit>().Prop(LineEdit.StylePropertyStyleBox, Box(Background, Border)));
        return new Stylesheet(rules);
    }

    private static void AddPanel(List<StyleRule> rules, string style, Color background, Color border, int padding)
    {
        rules.Add(Element<PanelContainer>().Class(style)
            .Prop(PanelContainer.StylePropertyPanel, Box(background, border, padding))
            .Prop(Control.StylePropertyModulateSelf, Color.White));
    }

    private static void AddButton(List<StyleRule> rules, string state, Color background, Color border)
    {
        rules.Add(Element<ContainerButton>().Class(ButtonClass).Pseudo(state)
            .Prop(ContainerButton.StylePropertyStyleBox, Box(background, border, 4))
            .Prop(Control.StylePropertyModulateSelf, Color.White));
        rules.Add(Element<ContainerButton>().Class(ButtonClass).Class(CardClass).Pseudo(state)
            .Prop(ContainerButton.StylePropertyStyleBox, Box(background, border, 3)));
    }

    private static void AddText(List<StyleRule> rules, string style, Color color)
    {
        rules.Add(Element<Label>().Class(style).Prop(Label.StylePropertyFontColor, color));
        rules.Add(Child().Parent(Element<Button>().Class(style)).Child(Element<Label>())
            .Prop(Label.StylePropertyFontColor, color));
    }
}
