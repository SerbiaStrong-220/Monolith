using Content.Shared._Exodus.Economy.Admin;
using NUnit.Framework;

namespace Content.Tests.Server._Exodus.Economy;

[TestFixture]
public sealed class MarketSettingsValidationTest
{
    [Test]
    public void OverridesPreserveInheritanceAndZeroStrength()
    {
        var defaults = new MarketGlobalSettings();
        var overrides = new MarketGlobalSettingsOverride(ImpactStrength: 0, DecayRate: 0.2);
        var effective = overrides.Resolve(defaults);
        Assert.Multiple(() =>
        {
            Assert.That(effective.ImpactStrength, Is.Zero);
            Assert.That(effective.DecayRate, Is.EqualTo(0.2));
            Assert.That(effective.ReferenceVolume, Is.EqualTo(defaults.ReferenceVolume));
            Assert.That(new MarketGlobalSettingsOverride().Resolve(defaults), Is.EqualTo(defaults));
        });
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(-0.1)]
    public void InvalidStrengthIsRejected(double strength)
    {
        Assert.That((new MarketGlobalSettings() with { ImpactStrength = strength }).IsValid(), Is.False);
    }

    [Test]
    public void InvalidCombinedBoundsAndIntervalsAreRejected()
    {
        var settings = new MarketGlobalSettings();
        Assert.Multiple(() =>
        {
            Assert.That(settings.IsValid(), Is.True);
            Assert.That((settings with { MinFactor = 2 }).IsValid(), Is.False);
            Assert.That((settings with { MaxFactor = 0.5 }).IsValid(), Is.False);
            Assert.That((settings with { ReferenceVolume = 0 }).IsValid(), Is.False);
            Assert.That((settings with { DecayIntervalSeconds = double.MaxValue }).IsValid(), Is.False);
            Assert.That((settings with { PurchaseMargin = 1.01 }).IsValid(), Is.False);
        });
    }
}
