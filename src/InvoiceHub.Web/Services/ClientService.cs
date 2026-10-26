using InvoiceHub.Web.Domain;

namespace InvoiceHub.Web.Services;

public sealed record ClientInput(string Name, string? Email, string? Address, string? TaxId);

/// <summary>Clients of one user. Like invoices, every query is scoped to the owner.</summary>
public sealed class ClientService(IClientRepository clients, IUnitOfWork unitOfWork)
{
    public Task<List<Client>> ListAsync(string ownerId, CancellationToken ct = default) =>
        clients.ListAsync(ownerId, ct);

    public Task<Client?> GetAsync(string ownerId, int id, CancellationToken ct = default) =>
        clients.FindAsync(ownerId, id, ct);

    public Task<int> InvoiceCountAsync(string ownerId, int clientId, CancellationToken ct = default) =>
        clients.InvoiceCountAsync(ownerId, clientId, ct);

    public async Task<Client> CreateAsync(string ownerId, ClientInput input, CancellationToken ct = default)
    {
        var client = new Client { OwnerId = ownerId };
        Apply(client, input);
        clients.Add(client);
        await unitOfWork.SaveChangesAsync(ct);
        return client;
    }

    public async Task<Client?> UpdateAsync(string ownerId, int id, ClientInput input, CancellationToken ct = default)
    {
        var client = await GetAsync(ownerId, id, ct);
        if (client is null) return null;
        Apply(client, input);
        await unitOfWork.SaveChangesAsync(ct);
        return client;
    }

    /// <returns>false when the client does not exist (or belongs to someone else).</returns>
    public async Task<bool> DeleteAsync(string ownerId, int id, CancellationToken ct = default)
    {
        var client = await GetAsync(ownerId, id, ct);
        if (client is null) return false;

        if (await InvoiceCountAsync(ownerId, id, ct) > 0)
            throw new DomainException("This client has invoices and cannot be deleted. Invoices must be kept.");

        clients.Remove(client);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    private static void Apply(Client client, ClientInput input)
    {
        client.Name = input.Name.Trim();
        client.Email = Clean(input.Email);
        client.Address = Clean(input.Address);
        client.TaxId = Clean(input.TaxId);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
