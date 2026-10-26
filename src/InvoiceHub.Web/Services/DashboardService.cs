using InvoiceHub.Web.Domain;

namespace InvoiceHub.Web.Services;

/// <summary>An amount in one currency. Currencies are never added together.</summary>
public sealed record Money(string Currency, decimal Amount);

public sealed record DashboardSummary(
    IReadOnlyList<Money> Outstanding,
    IReadOnlyList<Money> Overdue,
    int OverdueCount,
    IReadOnlyList<Money> PaidThisMonth,
    int DraftCount,
    IReadOnlyList<Invoice> Recent);

public sealed class DashboardService(IInvoiceRepository invoiceStore, TimeProvider clock)
{
    public async Task<DashboardSummary> GetAsync(string ownerId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        // A single user has at most a few thousand invoices. Summing in memory also avoids SQLite's
        // missing decimal SUM and keeps the money maths in one place (InvoiceCalculator).
        var invoices = await invoiceStore.ListAllAsync(ownerId, ct);

        var sent = invoices.Where(i => i.Status == InvoiceStatus.Sent).ToList();
        var overdue = sent.Where(i => i.IsOverdue(today)).ToList();
        var paidThisMonth = invoices.Where(i =>
            i.Status == InvoiceStatus.Paid && i.PaidOn is { } on && on.Year == today.Year && on.Month == today.Month);

        return new DashboardSummary(
            Outstanding: Sum(sent),
            Overdue: Sum(overdue),
            OverdueCount: overdue.Count,
            PaidThisMonth: Sum(paidThisMonth),
            DraftCount: invoices.Count(i => i.Status == InvoiceStatus.Draft),
            Recent: invoices.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Id).Take(5).ToList());
    }

    private static IReadOnlyList<Money> Sum(IEnumerable<Invoice> invoices) =>
        invoices.GroupBy(i => i.Currency)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Money(g.Key, g.Sum(i => i.Totals.Gross)))
            .ToList();
}
