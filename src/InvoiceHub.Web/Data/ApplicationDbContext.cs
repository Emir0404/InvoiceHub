using InvoiceHub.Web.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<CompanyProfile>(e =>
        {
            e.Property(x => x.OwnerId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.TaxId).HasMaxLength(40);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Iban).HasMaxLength(40);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.HasIndex(x => x.OwnerId).IsUnique();
        });

        builder.Entity<Client>(e =>
        {
            e.Property(x => x.OwnerId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.TaxId).HasMaxLength(40);
            e.HasIndex(x => x.OwnerId);
        });

        builder.Entity<Invoice>(e =>
        {
            e.Property(x => x.OwnerId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Number).HasMaxLength(20);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);

            // A client with invoices can never disappear: invoices are legal documents.
            e.HasOne(x => x.Client).WithMany(c => c.Invoices).HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.OwnerId, x.Status });
            // The database itself refuses a duplicate number for the same user (drafts have no number yet).
            e.HasIndex(x => new { x.OwnerId, x.Number }).IsUnique().HasFilter("\"Number\" IS NOT NULL");
            e.Ignore(x => x.Totals);
            e.Ignore(x => x.IsEditable);
        });

        builder.Entity<InvoiceLine>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.VatRate).HasPrecision(5, 2);
        });
    }
}
