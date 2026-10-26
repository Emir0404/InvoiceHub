using InvoiceHub.Web.Domain;

namespace InvoiceHub.Web.Services;

public sealed record CompanyInput(string Name, string? Address, string? TaxId, string? Email, string? Iban, string Currency);

public sealed class CompanyProfileService(ICompanyProfileRepository profiles, IUnitOfWork unitOfWork)
{
    public Task<CompanyProfile?> FindAsync(string ownerId, CancellationToken ct = default) =>
        profiles.FindReadOnlyAsync(ownerId, ct);

    public async Task<CompanyProfile> SaveAsync(string ownerId, CompanyInput input, CancellationToken ct = default)
    {
        var currency = input.Currency.Trim().ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            throw new DomainException("Currency must be a three-letter code such as EUR or BAM.");

        var profile = await profiles.FindAsync(ownerId, ct);
        if (profile is null)
        {
            profile = new CompanyProfile { OwnerId = ownerId };
            profiles.Add(profile);
        }

        profile.Name = input.Name.Trim();
        profile.Address = Clean(input.Address);
        profile.TaxId = Clean(input.TaxId);
        profile.Email = Clean(input.Email);
        profile.Iban = Clean(input.Iban);
        profile.Currency = currency;

        await unitOfWork.SaveChangesAsync(ct);
        return profile;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
