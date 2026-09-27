using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._Exodus.Genetics;

/// <summary>Local Hive styling. Does not change the stylesheet of other machinery windows.</summary>
public static class GeneticsUiTheme
{
    public const string ButtonClass = "GeneticsButton";
    public const string CellClass = "GeneticsCell";
    public const string DangerClass = "GeneticsDanger";
    public static readonly Color Background = Color.FromHex("#15101F");
    public static readonly Color Surface = Color.FromHex("#21182F");
    public static readonly Color Border = Color.FromHex("#493458");
    public static readonly Color Accent = Color.FromHex("#D0ACEF");
    public static readonly Color Text = Color.FromHex("#F0E7FA");
    public static readonly Color Muted = Color.FromHex("#B8A5C9");
    public static readonly Color Warning = Color.FromHex("#E4BA81");

    public static StyleBoxFlat Box(Color background, Color border, int padding = 8)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = padding,
            ContentMarginRightOverride = padding,
            ContentMarginTopOverride = padding,
            ContentMarginBottomOverride = padding,
        };
    }

    public static Stylesheet CreateStylesheet(Stylesheet? inherited)
    {
        var rules = new List<StyleRule>();
        if (inherited != null)
            rules.AddRange(inherited.Rules);
        rules.Add(Element<PanelContainer>().Class("AngleRect")
            .Prop(PanelContainer.StylePropertyPanel, Box(Background, Border, 0))
            .Prop(Control.StylePropertyModulateSelf, Color.White));
        rules.Add(Element<PanelContainer>().Class("WindowHeadingBackground")
            .Prop(PanelContainer.StylePropertyPanel, Box(Surface, Border, 0))
            .Prop(Control.StylePropertyModulateSelf, Color.White));
        AddButtonRule(rules, "normal", Surface, Border);
        AddButtonRule(rules, "hover", Color.FromHex("#3C2A50"), Accent);
        AddButtonRule(rules, "pressed", Color.FromHex("#583D73"), Accent);
        AddButtonRule(rules, "disabled", Color.FromHex("#1B1624"), Color.FromHex("#30263C"));
        AddButtonRule(rules, "confirm-normal", Color.FromHex("#513340"), Warning);
        AddButtonRule(rules, "confirm-hover", Color.FromHex("#694354"), Warning);
        AddButtonRule(rules, "confirm-pressed", Color.FromHex("#694354"), Warning);
        AddButtonRule(rules, "confirm-disabled", Color.FromHex("#392832"), Warning);
        rules.Add(Element<ContainerButton>().Class(ButtonClass).Class(DangerClass).Pseudo("normal")
            .Prop(ContainerButton.StylePropertyStyleBox, Box(Color.FromHex("#30202E"), Color.FromHex("#785266"), 6)));
        return new Stylesheet(rules);
    }

    private static void AddButtonRule(List<StyleRule> rules, string state, Color background, Color border)
    {
        rules.Add(Element<ContainerButton>().Class(ButtonClass).Pseudo(state)
            .Prop(ContainerButton.StylePropertyStyleBox, Box(background, border, 6))
            .Prop(Control.StylePropertyModulateSelf, Color.White));
        rules.Add(Element<ContainerButton>().Class(ButtonClass).Class(CellClass).Pseudo(state)
            .Prop(ContainerButton.StylePropertyStyleBox, Box(background, border, 2)));
    }

    public static T StyleButton<T>(T button) where T : ContainerButton
    {
        button.AddStyleClass(ButtonClass);
        button.MinHeight = 32;
        button.HorizontalExpand = true;
        if (button is Button textButton)
        {
            textButton.ClipText = true;
            textButton.TextAlign = Label.AlignMode.Center;
        }
        return button;
    }

    public static BoxContainer Column(int separation = 6) => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = separation,
    };

    public static BoxContainer Row(int separation = 6) => new() { SeparationOverride = separation };

    public static Label Caption(string key) => new()
    {
        Text = Loc.GetString(key),
        FontColorOverride = Muted,
        StyleClasses = { "LabelSubText" },
    };

    public static PanelContainer Panel(Control contents) => new()
    {
        PanelOverride = Box(Surface, Border),
        Children = { contents },
    };
}
