using System;
using System.Globalization;
using Content.Client._Exodus.Economy.Admin;
using NUnit.Framework;

namespace Content.Tests.Client._Exodus.Economy;

[TestFixture]
public sealed class MarketAdminFormattingTest
{
    [TestCase("ru-RU", 0.15, "0,15")]
    [TestCase("en-US", 0.15, "0.15")]
    [TestCase("ru-RU", 8.3287067674959, "8,33")]
    [TestCase("en-US", 0.000240000288, "0.00024")]
    [TestCase("ru-RU", 0.00987654, "0,00988")]
    [TestCase("en-US", 0.00000000000001, "1E-14")]
    public void PercentagesAreCompactWithoutHidingSmallGasPressure(string culture, double value, string expected)
    {
        Assert.That(MarketAdminFormatting.Number(value, CultureInfo.GetCultureInfo(culture)), Is.EqualTo(expected));
    }

    [TestCase("ru-RU", 0.15, "+0,15")]
    [TestCase("en-US", -0.15, "-0.15")]
    [TestCase("en-US", 0, "0")]
    [TestCase("ru-RU", 0.000240000288, "+0,00024")]
    [TestCase("en-US", -0.00000000000001, "-1E-14")]
    public void QuoteChangesRetainTheirDirection(string culture, double value, string expected)
    {
        Assert.That(MarketAdminFormatting.Number(value, CultureInfo.GetCultureInfo(culture), signed: true), Is.EqualTo(expected));
    }

    [TestCase("ru-RU", "1,1534654765")]
    [TestCase("en-US", "1.1534654765")]
    public void SecondaryFactorRetainsMorePrecisionThanPrimaryPercent(string culture, string expected)
    {
        Assert.That(MarketAdminFormatting.Factor(1.1534654765, CultureInfo.GetCultureInfo(culture)), Is.EqualTo(expected));
    }

    [TestCase(1, 0)]
    [TestCase(1.0015, 0.15)]
    [TestCase(1.1534654765, 15.34654765)]
    [TestCase(0.01, -99)]
    public void FactorChangeIsRelativeToTheBasePrice(double factor, double expected)
    {
        Assert.That(MarketAdminFormatting.FactorChange(factor), Is.EqualTo(expected).Within(1e-12));
    }

    [Test]
    public void PressureIsReportedPerUnitInsteadOfPerReferenceBatch()
    {
        Assert.That(MarketAdminFormatting.RisePercent(0.08, 100), Is.EqualTo(0.08003200853504094).Within(1e-14));
        Assert.That(MarketAdminFormatting.RisePercent(0.08, 200), Is.EqualTo(0.040008001066773345).Within(1e-14));
        Assert.That(MarketAdminFormatting.StrengthFromPercent(0.15, 100), Is.EqualTo(0.1498876123735892).Within(1e-14));
    }

    [TestCase(0)]
    [TestCase(0.00024000000208616257)]
    [TestCase(0.08)]
    [TestCase(1e-20)]
    public void UnitPercentRoundTripPreservesSmallAndZeroPressure(double strength)
    {
        var percent = MarketAdminFormatting.RisePercent(strength, 100);
        var restored = MarketAdminFormatting.StrengthFromPercent(percent, 100);
        Assert.That(restored, Is.EqualTo(strength).Within(Math.Max(double.Epsilon, Math.Abs(strength) * 1e-14)));
    }

    [TestCase(0, 0)]
    [TestCase(0.08, -0.0799680085316269397)]
    [TestCase(0.00024000000208616257, -0.000239999714086387963)]
    [TestCase(1e-20, -1e-20)]
    public void SalePercentPreservesSmallNegativeChanges(double strength, double expected)
    {
        Assert.That(MarketAdminFormatting.FallPercent(strength, 100),
            Is.EqualTo(expected).Within(Math.Max(double.Epsilon, Math.Abs(expected) * 1e-14)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidReferenceVolumeDoesNotProduceAUsablePressure(double reference)
    {
        Assert.That(double.IsNaN(MarketAdminFormatting.RisePercent(0.08, reference)), Is.True);
        Assert.That(double.IsNaN(MarketAdminFormatting.FallPercent(0.08, reference)), Is.True);
        Assert.That(double.IsNaN(MarketAdminFormatting.StrengthFromPercent(0.15, reference)), Is.True);
    }
}
