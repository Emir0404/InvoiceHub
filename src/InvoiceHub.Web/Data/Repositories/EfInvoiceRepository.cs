using InvoiceHub.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Web.Data.Repositories;

public sealed class EfInvoiceRepository(ApplicationDbContext db) : IInvoiceRepository
{
    public async Task<List<Invoice>> SearchAsync(string ownerId, InvoiceFilter filter, DateOnly today, CancellationToken ct = default)
    {
        var query = db.Invoices.AsNoTracking().Include(i => i.Client).Include(i => i.Lines)
            .Where(i => i.OwnerId == ownerId);

        query = filter.Status?.ToLowerInvariant() switch
        {
            "draft" => query.Where(i => i.Status == InvoiceStatus.Draft),
            "sent" => query.Where(i => i.Status == InvoiceStatus.Sent),
            "paid" => query.Where(i => i.Status == InvoiceStatus.Paid),
            "overdue" => query.Where(i => i.Status == InvoiceStatus.Sent && i.DueDate < today),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // % and _ typed by the user must be searched literally, not act as wildcards.
            var like = $"%{filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(i => EF.Functions.Like(i.Client.Name, like, "\\")
                                     || (i.Number != null && EF.Functions.Like(i.Number, like, "\\")));
        }

        // Newest first. Sorted in memory because SQLite cannot order DateOnly + id combinations reliably across providers.
        var list = await query.ToListAsync(ct);
        return list.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Id).ToList();
    }

    public Task<List<Invoice>> ListAllAsync(string ownerId, CancellationToken ct = default) =>
        db.Invoices.AsNoTracking().Include(i => i.Client).Include(i => i.Lines)
            .Where(i => i.OwnerId == ownerId)
            .ToListAsync(ct);

    public Task<Invoice?> FindAsync(string ownerId, int id, CancellationToken ct = default) =>
        db.Invoices.Include(i => i.Client).Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.OwnerId == ownerId && i.Id == id, ct);

    public Task<List<string>> NumbersStartingWithAsync(string ownerId, string prefix, CancellationToken ct = default) =>
        db.Invoices
            .Where(i => i.OwnerId == ownerId && i.Number != null && i.Number.StartsWith(prefix))
            .Select(i => i.Number!)
            .ToListAsync(ct);

    public void Add(Invoice invoice) => db.Invoices.Add(invoice);

    public void Remove(Invoice invoice) => db.Invoices.Remove(invoice);

    public void RemoveLines(IEnumerable<InvoiceLine> lines) => db.InvoiceLines.RemoveRange(lines);
}
