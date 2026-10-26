using InvoiceHub.Web.Data;
using InvoiceHub.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Web.Services;

public sealed record CompanyInput(string Name, string? Address, string? TaxId, string? Email, string? Iban, string Currency);

public sealed class CompanyProfileService(ApplicationDbContext db)
{
    public Task<CompanyProfile?> FindAsync(string ownerId, CancellationToken ct = default) =>
        db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.OwnerId == ownerId, ct);

    public async Task<CompanyProfile> SaveAsync(string ownerId, CompanyInput input, CancellationToken ct = default)
    {
        var currency = input.Currency.Trim().ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            throw new DomainException("Currency must be a three-letter code such as EUR or BAM.");

        var profile = await db.CompanyProfiles.FirstOrDefaultAsync(p => p.OwnerId == ownerId, ct);
        if (profile is null)
        {
            profile = new CompanyProfile { OwnerId = ownerId };
            db.CompanyProfiles.Add(profile);
        }

        profile.Name = input.Name.Trim();
        profile.Address = Clean(input.Address);
        profile.TaxId = Clean(input.TaxId);
        profile.Email = Clean(input.Email);
        profile.Iban = Clean(input.Iban);
        profile.Currency = currency;

        await db.SaveChangesAsync(ct);
        return profile;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
