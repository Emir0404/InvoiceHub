using InvoiceHub.Web.Domain;

namespace InvoiceHub.Tests;

public class InvoiceCalculatorTests
{
    private static InvoiceLine Line(decimal qty, decimal price, decimal vat) =>
        new() { Description = "x", Quantity = qty, UnitPrice = price, VatRate = vat };

    [Fact]
    public void No_lines_means_zero_everywhere()
    {
        var totals = InvoiceCalculator.Calculate([]);

        Assert.Equal(0m, totals.Net);
        Assert.Equal(0m, totals.Vat);
        Assert.Equal(0m, totals.Gross);
        Assert.Empty(totals.Breakdown);
    }

    [Fact]
    public void Single_line_without_vat()
    {
        var totals = InvoiceCalculator.Calculate([Line(2, 50m, 0)]);

        Assert.Equal(100m, totals.Net);
        Assert.Equal(0m, totals.Vat);
        Assert.Equal(100m, totals.Gross);
    }

    [Fact]
    public void Vat_is_added_on_top_of_the_net_amount()
    {
        var totals = InvoiceCalculator.Calculate([Line(1, 100m, 17)]);

        Assert.Equal(100m, totals.Net);
        Assert.Equal(17m, totals.Vat);
        Assert.Equal(117m, totals.Gross);
    }

    [Fact]
    public void Fractional_quantities_are_supported()
    {
        // 2.5 hours at 60.00
        var totals = InvoiceCalculator.Calculate([Line(2.5m, 60m, 0)]);

        Assert.Equal(150m, totals.Net);
    }

    [Fact]
    public void Each_line_net_is_rounded_to_cents_half_away_from_zero()
    {
        // 3 x 0.335 = 1.005 -> 1.01 (banker's rounding would give 1.00)
        Assert.Equal(1.01m, InvoiceCalculator.LineNet(Line(3, 0.335m, 0)));
    }

    [Fact]
    public void Vat_is_calculated_per_rate_on_the_sum_not_per_line()
    {
        // Three lines of 0.10 at 17 %: per-line VAT would be 0.02 each (0.06 total),
        // per-rate VAT is 17 % of 0.30 = 0.051 -> 0.05.
        var totals = InvoiceCalculator.Calculate([Line(1, 0.10m, 17), Line(1, 0.10m, 17), Line(1, 0.10m, 17)]);

        Assert.Equal(0.30m, totals.Net);
        Assert.Equal(0.05m, totals.Vat);
        Assert.Equal(0.35m, totals.Gross);
    }

    [Fact]
    public void Different_rates_get_their_own_breakdown_sorted_by_rate()
    {
        var totals = InvoiceCalculator.Calculate([Line(1, 200m, 17), Line(1, 100m, 0), Line(1, 50m, 17), Line(1, 100m, 5)]);

        Assert.Equal(new[] { 0m, 5m, 17m }, totals.Breakdown.Select(b => b.Rate));
        Assert.Equal(new[] { 100m, 100m, 250m }, totals.Breakdown.Select(b => b.Net));
        Assert.Equal(new[] { 0m, 5m, 42.5m }, totals.Breakdown.Select(b => b.Vat));
        Assert.Equal(450m, totals.Net);
        Assert.Equal(47.5m, totals.Vat);
        Assert.Equal(497.5m, totals.Gross);
    }

    [Fact]
    public void Gross_always_equals_net_plus_vat_of_the_rounded_parts()
    {
        var totals = InvoiceCalculator.Calculate([Line(7, 13.37m, 17), Line(3, 9.99m, 5), Line(1, 0.01m, 17)]);

        Assert.Equal(totals.Net + totals.Vat, totals.Gross);
        Assert.Equal(totals.Breakdown.Sum(b => b.Net), totals.Net);
        Assert.Equal(totals.Breakdown.Sum(b => b.Vat), totals.Vat);
    }

    [Fact]
    public void Large_amounts_do_not_lose_precision()
    {
        var totals = InvoiceCalculator.Calculate([Line(1_000_000, 999_999.99m, 17)]);

        Assert.Equal(999_999_990_000m, totals.Net);
        Assert.Equal(169_999_998_300m, totals.Vat);
    }
}
