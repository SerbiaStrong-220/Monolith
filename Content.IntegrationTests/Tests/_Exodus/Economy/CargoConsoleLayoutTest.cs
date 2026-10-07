using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.Cargo.UI;
using Content.Shared._Exodus.Economy;
using Content.Shared.Cargo;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class CargoConsoleLayoutTest
{
    private const string ProductId = "ExodusCargoLayoutTestProduct";
    private const string UnavailableId = "ExodusCargoLayoutTestUnavailable";
    private const string EntityId = "ExodusCargoLayoutTestItem";
    private const string Requester = "Александр Константинович Длиннофамильский";
    private const string Reason = "Для восстановления атмосферного оборудования исследовательского корабля после аварии";
    private const float Tolerance = 0.1f;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusCargoLayoutTestItem
  name: ящик комплектующих для восстановления экспериментального атмосферного оборудования

- type: cargoProduct
  id: ExodusCargoLayoutTestProduct
  icon:
    sprite: Objects/Materials/Sheets/metal.rsi
    state: steel
  product: ExodusCargoLayoutTestItem
  description: Комплектующие для ремонта корабельной атмосферы.
  cost: 2147483647
  category: cargoproduct-category-name-engineering
  group: market

- type: cargoProduct
  id: ExodusCargoLayoutTestUnavailable
  parent: ExodusCargoLayoutTestProduct
";

    [TestCase(300, 280)]
    [TestCase(340, 300)]
    public async Task LongCatalogAndOrderTextFitsCards(int productWidth, int orderWidth)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var loc = pair.Client.ResolveDependency<ILocalizationManager>();
            var original = loc.DefaultCulture;
            loc.SetCulture(CultureInfo.GetCultureInfo("ru-RU"));
            try
            {
                using var menu = new CargoConsoleMenu(default, pair.Client.EntMan,
                    pair.Client.ResolveDependency<IPrototypeManager>(), pair.Client.EntMan.System<SpriteSystem>());
                Populate(menu);
                var font = pair.Client.ResolveDependency<IUserInterfaceManager>().ThemeDefaults.LabelFont;
                var products = Descendants(menu).OfType<CargoProductRow>().ToArray();
                var orders = Descendants(menu).OfType<CargoOrderRow>().ToArray();
                Assert.That(products, Has.Length.EqualTo(2));
                Assert.That(orders, Has.Length.EqualTo(3));
                foreach (var row in products)
                    AssertCard(row, productWidth, font);
                foreach (var row in orders)
                    AssertCard(row, orderWidth, font);
            }
            finally
            {
                loc.DefaultCulture = original;
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ru-RU", 900, 560)]
    [TestCase("ru-RU", 1060, 680)]
    [TestCase("en-US", 900, 560)]
    public async Task PopulatedConsoleFitsAtSupportedSizes(string culture, int width, int height)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var loc = pair.Client.ResolveDependency<ILocalizationManager>();
            var original = loc.DefaultCulture;
            loc.SetCulture(CultureInfo.GetCultureInfo(culture));
            try
            {
                using var menu = new CargoConsoleMenu(default, pair.Client.EntMan,
                    pair.Client.ResolveDependency<IPrototypeManager>(), pair.Client.EntMan.System<SpriteSystem>());
                Populate(menu);
                menu.SetSize = new Vector2(width, height);
                var tabs = Descendants(menu).OfType<TabContainer>().ToArray();
                AssertLayout();
                foreach (var tab in tabs)
                {
                    for (var index = 0; index < tab.ChildCount; index++)
                    {
                        tab.CurrentTab = index;
                        AssertLayout();
                    }
                }

                void AssertLayout()
                {
                    menu.Measure(new Vector2(width, height));
                    menu.Arrange(new UIBox2(0, 0, width, height));
                    foreach (var control in Descendants(menu))
                    {
                        if (!control.VisibleInTree ||
                            control is not (PanelContainer or Button or Label or RichTextLabel or ScrollContainer))
                            continue;

                        AssertHorizontalBounds(control, menu);
                        if (!IsInsideScrollContainer(control))
                        {
                            Assert.That(control.GlobalRect.Bottom, Is.LessThanOrEqualTo(height + Tolerance),
                                $"{control.Name ?? control.GetType().Name} must fit vertically.");
                        }
                    }
                }
            }
            finally
            {
                loc.DefaultCulture = original;
            }
        });
        await pair.CleanReturnAsync();
    }

    private static void Populate(CargoConsoleMenu menu)
    {
        menu.SetMarketListings(
        [
            new CargoMarketListing
            {
                ProductId = ProductId,
                EntityProtoId = EntityId,
                Category = "cargoproduct-category-name-engineering",
                UnitPrice = int.MaxValue,
                Trend = 1,
                ChangePercent = 99999.9,
            },
            new CargoMarketListing
            {
                ProductId = UnavailableId,
                EntityProtoId = EntityId,
                Category = "cargoproduct-category-name-engineering",
                Available = false,
                Trend = -1,
                ChangePercent = -99999.9,
            },
        ]);
        menu.PopulateCategories();
        menu.PopulateProducts();
        menu.PopulateOrders(
        [
            new CargoOrderData(1, EntityId, string.Empty, int.MaxValue, 20, Requester, Reason, null)
            {
                TotalPrice = int.MaxValue,
            },
            new CargoOrderData(2, EntityId, string.Empty, int.MaxValue, 20, Requester, Reason, null)
            {
                TotalPrice = int.MaxValue,
                Approved = true,
            },
            new CargoOrderData(3, EntityId, string.Empty, int.MaxValue, 20, Requester, Reason, null),
        ]);
        menu.UpdateBankData("Счёт экспедиционного научно-исследовательского объединения", int.MaxValue);
        menu.UpdateCargoCapacity(17, 20);
    }

    private static void AssertCard(Control row, int width, Font defaultFont)
    {
        row.Measure(new Vector2(width, float.PositiveInfinity));
        row.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(width, row.DesiredSize.Y)));
        var children = Descendants(row).Where(control => IsVisibleWithin(control, row)).ToArray();
        var labels = children.OfType<Label>().Where(label => !string.IsNullOrEmpty(label.Text)).ToArray();
        foreach (var label in labels)
        {
            AssertHorizontalBounds(label, row);
            Assert.That(label.Width, Is.GreaterThan(0), $"{label.Name}: text must have visible space.");
            using var natural = new Label
            {
                Text = label.Text,
                FontOverride = label.FontOverride ??
                    (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var font) ? font : defaultFont),
            };
            natural.Measure(new Vector2(float.PositiveInfinity));
            Assert.That(natural.DesiredSize.X, Is.GreaterThan(0), "The test requires nonzero font metrics.");
            if (natural.DesiredSize.X <= label.Width + Tolerance)
                continue;

            Assert.That(label.RectClipContent, Is.True, $"{label.Name}: long text must not paint over neighboring fields.");
            Assert.That(children.Append(row).Any(control => control.MouseFilter != Control.MouseFilterMode.Ignore &&
                    control.ToolTip?.Contains(label.Text!, StringComparison.Ordinal) == true),
                Is.True, $"{label.Name}: the full clipped text must remain accessible in a tooltip.");
        }

        foreach (var button in children.OfType<Button>())
            AssertHorizontalBounds(button, row);

        for (var i = 0; i < labels.Length; i++)
        {
            for (var j = i + 1; j < labels.Length; j++)
            {
                var first = labels[i].GlobalRect;
                var second = labels[j].GlobalRect;
                var overlaps = Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left) > Tolerance &&
                    Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top) > Tolerance;
                Assert.That(overlaps, Is.False, $"{labels[i].Name} and {labels[j].Name} must not overlap.");
            }
        }
    }

    private static void AssertHorizontalBounds(Control control, Control container)
    {
        Assert.That(control.GlobalRect.Left, Is.GreaterThanOrEqualTo(container.GlobalRect.Left - Tolerance),
            $"{control.Name ?? control.GetType().Name} must fit the left edge.");
        Assert.That(control.GlobalRect.Right, Is.LessThanOrEqualTo(container.GlobalRect.Right + Tolerance),
            $"{control.Name ?? control.GetType().Name} must fit the right edge.");
    }

    private static bool IsInsideScrollContainer(Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ScrollContainer)
                return true;
        }

        return false;
    }

    private static bool IsVisibleWithin(Control control, Control ancestor)
    {
        for (var current = control; current != ancestor; current = current.Parent!)
        {
            if (!current.Visible)
                return false;
        }

        return true;
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
