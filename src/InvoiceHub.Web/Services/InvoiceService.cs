using InvoiceHub.Web.Data;
using InvoiceHub.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Web.Services;

public sealed record LineInput(string Description, decimal Quantity, decimal UnitPrice, decimal VatRate);

public sealed record InvoiceInput(
    int ClientId, DateOnly IssueDate, DateOnly DueDate, string? Notes, IReadOnlyList<LineInput> Lines);

/// <param name="Status">draft, sent, paid or overdue (empty = all)</param>
public sealed record InvoiceFilter(string? Search = null, string? Status = null);

/// <summary>
/// Everything about invoices. Every method takes the owner's id and every query is limited to it,
/// so one user can never read or change another user's data, whatever ids they put in a URL.
/// </summary>
public sealed class InvoiceService(ApplicationDbContext db, TimeProvider clock)
{
    public const int MaxLines = 50;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<List<Invoice>> ListAsync(string ownerId, InvoiceFilter filter, CancellationToken ct = default)
    {
        var query = db.Invoices.AsNoTracking().Include(i => i.Client).Include(i => i.Lines)
            .Where(i => i.OwnerId == ownerId);

        var today = Today;
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

    public Task<Invoice?> GetAsync(string ownerId, int id, CancellationToken ct = default) =>
        db.Invoices.Include(i => i.Client).Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.OwnerId == ownerId && i.Id == id, ct);

    public async Task<Invoice> CreateDraftAsync(string ownerId, InvoiceInput input, CancellationToken ct = default)
    {
        await ValidateAsync(ownerId, input, ct);

        var currency = await db.CompanyProfiles.Where(p => p.OwnerId == ownerId)
            .Select(p => p.Currency).FirstOrDefaultAsync(ct) ?? "EUR";

        var invoice = new Invoice
        {
            OwnerId = ownerId,
            Currency = currency,
            Status = InvoiceStatus.Draft,
        };
        Apply(invoice, input);

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return invoice;
    }

    /// <returns>null when the invoice does not exist (or belongs to someone else).</returns>
    public async Task<Invoice?> UpdateDraftAsync(string ownerId, int id, InvoiceInput input, CancellationToken ct = default)
    {
        var invoice = await GetAsync(ownerId, id, ct);
        if (invoice is null) return null;
        if (!invoice.IsEditable)
            throw new DomainException("Only drafts can be edited. A sent invoice is a legal document.");

        await ValidateAsync(ownerId, input, ct);

        db.InvoiceLines.RemoveRange(invoice.Lines);
        invoice.Lines.Clear();
        Apply(invoice, input);

        await db.SaveChangesAsync(ct);
        return invoice;
    }

    public async Task<Invoice?> SendAsync(string ownerId, int id, CancellationToken ct = default)
    {
        var invoice = await GetAsync(ownerId, id, ct);
        if (invoice is null) return null;

        invoice.Send(await NextNumberAsync(ownerId, invoice.IssueDate.Year, ct));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two requests picked the same number at once: the unique index stopped the second one.
            throw new DomainException("Could not assign an invoice number, please try again.");
        }
        return invoice;
    }

    public async Task<Invoice?> MarkPaidAsync(string ownerId, int id, DateOnly? paidOn, CancellationToken ct = default)
    {
        var invoice = await GetAsync(ownerId, id, ct);
        if (invoice is null) return null;

        invoice.MarkPaid(paidOn ?? Today);
        await db.SaveChangesAsync(ct);
        return invoice;
    }

    /// <returns>false when the invoice does not exist (or belongs to someone else).</returns>
    public async Task<bool> DeleteDraftAsync(string ownerId, int id, CancellationToken ct = default)
    {
        var invoice = await GetAsync(ownerId, id, ct);
        if (invoice is null) return false;
        if (!invoice.IsEditable)
            throw new DomainException("Only drafts can be deleted. Sent invoices must be kept.");

        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Next free number for that user and year: 2026-0001, 2026-0002, ...</summary>
    private async Task<string> NextNumberAsync(string ownerId, int year, CancellationToken ct)
    {
        var prefix = $"{year}-";
        var existing = await db.Invoices
            .Where(i => i.OwnerId == ownerId && i.Number != null && i.Number.StartsWith(prefix))
            .Select(i => i.Number!)
            .ToListAsync(ct);

        var last = existing
            .Select(n => int.TryParse(n.AsSpan(prefix.Length), out var seq) ? seq : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{last + 1:D4}";
    }

    private async Task ValidateAsync(string ownerId, InvoiceInput input, CancellationToken ct)
    {
        // The form already checks these, but a service must not trust its caller.
        if (!await db.Clients.AnyAsync(c => c.OwnerId == ownerId && c.Id == input.ClientId, ct))
            throw new DomainException("Please choose one of your clients.");
        if (input.DueDate < input.IssueDate)
            throw new DomainException("The due date cannot be before the issue date.");
        if (input.Lines.Count == 0)
            throw new DomainException("Add at least one line.");
        if (input.Lines.Count > MaxLines)
            throw new DomainException($"An invoice can have at most {MaxLines} lines.");

        foreach (var line in input.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Description) || line.Description.Length > 200)
                throw new DomainException("Every line needs a description of up to 200 characters.");
            if (line.Quantity <= 0 || line.Quantity > 1_000_000)
                throw new DomainException("Quantity must be greater than 0.");
            if (line.UnitPrice < 0 || line.UnitPrice > 1_000_000_000)
                throw new DomainException("Unit price must not be negative.");
            if (line.VatRate < 0 || line.VatRate > 100)
                throw new DomainException("VAT must be between 0 and 100 %.");
        }
        if (input.Notes?.Length > 1000)
            throw new DomainException("Notes can have at most 1000 characters.");
    }

    private static void Apply(Invoice invoice, InvoiceInput input)
    {
        invoice.ClientId = input.ClientId;
        invoice.IssueDate = input.IssueDate;
        invoice.DueDate = input.DueDate;
        invoice.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        foreach (var line in input.Lines)
            invoice.Lines.Add(new InvoiceLine
            {
                Description = line.Description.Trim(),
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                VatRate = line.VatRate,
            });
    }
}
