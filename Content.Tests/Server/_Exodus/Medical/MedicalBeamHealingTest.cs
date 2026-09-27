using Content.Server._Exodus.Medical;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using NUnit.Framework;

namespace Content.Tests.Server._Exodus.Medical;

[TestFixture]
[TestOf(typeof(MedicalBeamGunSystem))]
public sealed class MedicalBeamHealingTest
{
    private static readonly string[] Brute = { "Blunt", "Slash", "Piercing" };
    private static readonly string[] Burn = { "Heat", "Shock", "Cold", "Caustic" };

    [Test]
    public void SingleInjuryGetsTheEntireGroupBudget()
    {
        var damage = new DamageSpecifier { DamageDict = { ["Piercing"] = 20 } };
        var healing = new DamageSpecifier();
        MedicalBeamGunSystem.AddGroupHealing(damage, Brute, 5, healing);
        Assert.That(healing.DamageDict, Has.Count.EqualTo(1));
        Assert.That(healing.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(-5)));
        Assert.That(damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(20)));
    }

    [Test]
    public void MixedInjuriesShareTheBudgetAndPreserveRounding()
    {
        var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 1, ["Slash"] = 1, ["Piercing"] = 1 } };
        var healing = new DamageSpecifier();
        MedicalBeamGunSystem.AddGroupHealing(damage, Brute, 1, healing);
        Assert.That(healing.GetTotal(), Is.EqualTo(FixedPoint2.New(-1)));
        Assert.That(healing.DamageDict["Blunt"], Is.EqualTo(FixedPoint2.New(-0.33)));
        Assert.That(healing.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(-0.33)));
        Assert.That(healing.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(-0.34)));
    }

    [Test]
    public void SmallInjuriesAndOverlappingBudgetsCannotOverheal()
    {
        var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 0.01, ["Piercing"] = 2 } };
        var healing = new DamageSpecifier();
        MedicalBeamGunSystem.AddGroupHealing(damage, Brute, 5, healing);
        MedicalBeamGunSystem.AddTypeHealing(damage, "Piercing", 5, healing);
        Assert.That(healing.GetTotal(), Is.EqualTo(FixedPoint2.New(-2.01)));
    }

    [Test]
    public void BruteBurnAndAsphyxiationHaveIndependentBudgets()
    {
        var damage = new DamageSpecifier
        {
            DamageDict = { ["Slash"] = 20, ["Heat"] = 20, ["Asphyxiation"] = 20,
                ["Bloodloss"] = 10, ["Poison"] = 10, ["Radiation"] = 10, ["Cellular"] = 10 },
        };
        var healing = new DamageSpecifier();
        MedicalBeamGunSystem.AddGroupHealing(damage, Brute, 5, healing);
        MedicalBeamGunSystem.AddGroupHealing(damage, Burn, 5, healing);
        MedicalBeamGunSystem.AddTypeHealing(damage, "Asphyxiation", 5, healing);
        Assert.That(healing.GetTotal(), Is.EqualTo(FixedPoint2.New(-15)));
        Assert.That(healing.DamageDict.Keys, Is.EquivalentTo(new[] { "Slash", "Heat", "Asphyxiation" }));
    }
}
