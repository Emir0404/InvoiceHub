using InvoiceHub.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Web.Data.Repositories;

public sealed class EfUnitOfWork(ApplicationDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The rest of the app should not need to know that this is Entity Framework underneath.
            throw new DataConflictException("The database refused the change.", ex);
        }
    }
}
