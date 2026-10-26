namespace InvoiceHub.Web.Domain;

/// <param name="Status">draft, sent, paid or overdue (empty = all)</param>
public sealed record InvoiceFilter(string? Search = null, string? Status = null);

/// <summary>The database refused a change because it clashes with other data (for example a duplicate invoice number).</summary>
public class DataConflictException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Saves everything the repositories changed as one all-or-nothing step.
/// Repositories only collect changes (Add, Remove); nothing reaches the database until this is called.
/// </summary>
public interface IUnitOfWork
{
    /// <exception cref="DataConflictException">A unique index or other constraint refused the change.</exception>
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Where invoices are stored. Every method takes the owner's id and only ever returns that owner's rows,
/// so no caller can reach another user's data by guessing an id.
/// </summary>
public interface IInvoiceRepository
{
    /// <summary>Read-only list with client and lines, filtered, newest first.</summary>
    Task<List<Invoice>> SearchAsync(string ownerId, InvoiceFilter filter, DateOnly today, CancellationToken ct = default);

    /// <summary>Every invoice of the owner, read-only, with client and lines (for the dashboard).</summary>
    Task<List<Invoice>> ListAllAsync(string ownerId, CancellationToken ct = default);

    /// <summary>One invoice with client and lines, ready to be changed. Null when it is not the owner's.</summary>
    Task<Invoice?> FindAsync(string ownerId, int id, CancellationToken ct = default);

    /// <summary>Numbers already used by the owner that start with the prefix (for example "2026-").</summary>
    Task<List<string>> NumbersStartingWithAsync(string ownerId, string prefix, CancellationToken ct = default);

    void Add(Invoice invoice);
    void Remove(Invoice invoice);
    void RemoveLines(IEnumerable<InvoiceLine> lines);
}

public interface IClientRepository
{
    Task<List<Client>> ListAsync(string ownerId, CancellationToken ct = default);

    /// <summary>One client, ready to be changed. Null when it is not the owner's.</summary>
    Task<Client?> FindAsync(string ownerId, int id, CancellationToken ct = default);

    Task<bool> ExistsAsync(string ownerId, int id, CancellationToken ct = default);
    Task<int> InvoiceCountAsync(string ownerId, int clientId, CancellationToken ct = default);

    void Add(Client client);
    void Remove(Client client);
}

public interface ICompanyProfileRepository
{
    /// <summary>Read-only copy, for showing.</summary>
    Task<CompanyProfile?> FindReadOnlyAsync(string ownerId, CancellationToken ct = default);

    /// <summary>The profile ready to be changed.</summary>
    Task<CompanyProfile?> FindAsync(string ownerId, CancellationToken ct = default);

    Task<string?> CurrencyAsync(string ownerId, CancellationToken ct = default);

    void Add(CompanyProfile profile);
}
