using System.Collections.Generic;
using Content.Server._NF.Market.Extensions;
using Content.Shared._NF.Market;
using NUnit.Framework;

namespace Content.Tests.Server._Exodus.Economy;

[TestFixture]
public sealed class MarketStockTest
{
    [Test]
    public void StockOverflowDoesNotCreateDuplicateListing()
    {
        var stock = new List<MarketData> { new("Steel", null, int.MaxValue, 10) };

        stock.Upsert("Steel", 1, 20);

        Assert.Multiple(() =>
        {
            Assert.That(stock, Has.Count.EqualTo(1));
            Assert.That(stock[0].Quantity, Is.EqualTo(int.MaxValue));
            Assert.That(stock[0].Price, Is.EqualTo(10));
        });
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(-1)]
    public void InvalidAppraisalDoesNotEnterStock(double price)
    {
        var stock = new List<MarketData>();

        stock.Upsert("Steel", 1, price);

        Assert.That(stock, Is.Empty);
    }

    [Test]
    public void FailedTransferPreservesSourceStock()
    {
        var source = new List<MarketData> { new("Steel", null, 1, 20) };
        var destination = new List<MarketData> { new("Steel", null, int.MaxValue, 10) };

        source.Move(destination, "Steel");

        Assert.Multiple(() =>
        {
            Assert.That(source, Has.Count.EqualTo(1));
            Assert.That(source[0].Quantity, Is.EqualTo(1));
            Assert.That(destination, Has.Count.EqualTo(1));
            Assert.That(destination[0].Quantity, Is.EqualTo(int.MaxValue));
        });
    }

    [Test]
    public void RemovingStockKeepsItsWeightedBasePrice()
    {
        var stock = new List<MarketData> { new("Steel", null, 2, 100) };
        stock.Upsert("Steel", 2, 200);

        stock.Upsert("Steel", -1, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(stock[0].Quantity, Is.EqualTo(3));
            Assert.That(stock[0].Price, Is.EqualTo(150));
        });
    }
}
