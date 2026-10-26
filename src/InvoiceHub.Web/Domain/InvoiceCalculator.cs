namespace InvoiceHub.Web.Domain;

/// <summary>Net, VAT and gross for one VAT rate.</summary>
public sealed record VatLine(decimal Rate, decimal Net, decimal Vat);

public sealed record InvoiceTotals(decimal Net, decimal Vat, decimal Gross, IReadOnlyList<VatLine> Breakdown);

public class DomainException(string message) : Exception(message);

/// <summary>
/// The money rules in one place, free of database and web code so they are easy to test.
/// Method (as used for EU invoices): each line net = quantity x price, rounded to cents;
/// VAT is calculated once per VAT rate on the sum of that rate's lines, then rounded.
/// </summary>
public static class InvoiceCalculator
{
    public static decimal LineNet(InvoiceLine line) => Round(line.Quantity * line.UnitPrice);

    public static InvoiceTotals Calculate(IEnumerable<InvoiceLine> lines)
    {
        var breakdown = lines
            .GroupBy(l => l.VatRate)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var net = g.Sum(LineNet);
                return new VatLine(g.Key, net, Round(net * g.Key / 100m));
            })
            .ToList();

        var totalNet = breakdown.Sum(b => b.Net);
        var totalVat = breakdown.Sum(b => b.Vat);
        return new InvoiceTotals(totalNet, totalVat, totalNet + totalVat, breakdown);
    }

    // Half away from zero is what people expect on an invoice (banker's rounding would surprise them).
    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
