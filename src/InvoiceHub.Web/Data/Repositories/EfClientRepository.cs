using InvoiceHub.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Web.Data.Repositories;

public sealed class EfClientRepository(ApplicationDbContext db) : IClientRepository
{
    public Task<List<Client>> ListAsync(string ownerId, CancellationToken ct = default) =>
        db.Clients.AsNoTracking().Where(c => c.OwnerId == ownerId).OrderBy(c => c.Name).ToListAsync(ct);

    public Task<Client?> FindAsync(string ownerId, int id, CancellationToken ct = default) =>
        db.Clients.FirstOrDefaultAsync(c => c.OwnerId == ownerId && c.Id == id, ct);

    public Task<bool> ExistsAsync(string ownerId, int id, CancellationToken ct = default) =>
        db.Clients.AnyAsync(c => c.OwnerId == ownerId && c.Id == id, ct);

    public Task<int> InvoiceCountAsync(string ownerId, int clientId, CancellationToken ct = default) =>
        db.Invoices.CountAsync(i => i.OwnerId == ownerId && i.ClientId == clientId, ct);

    public void Add(Client client) => db.Clients.Add(client);

    public void Remove(Client client) => db.Clients.Remove(client);
}
