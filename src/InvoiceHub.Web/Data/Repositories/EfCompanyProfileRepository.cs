using InvoiceHub.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Web.Data.Repositories;

public sealed class EfCompanyProfileRepository(ApplicationDbContext db) : ICompanyProfileRepository
{
    public Task<CompanyProfile?> FindReadOnlyAsync(string ownerId, CancellationToken ct = default) =>
        db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.OwnerId == ownerId, ct);

    public Task<CompanyProfile?> FindAsync(string ownerId, CancellationToken ct = default) =>
        db.CompanyProfiles.FirstOrDefaultAsync(p => p.OwnerId == ownerId, ct);

    public Task<string?> CurrencyAsync(string ownerId, CancellationToken ct = default) =>
        db.CompanyProfiles.Where(p => p.OwnerId == ownerId).Select(p => (string?)p.Currency).FirstOrDefaultAsync(ct);

    public void Add(CompanyProfile profile) => db.CompanyProfiles.Add(profile);
}
