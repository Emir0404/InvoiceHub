using InvoiceHub.Web.Data;
using InvoiceHub.Web.Domain;
using InvoiceHub.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace InvoiceHub.Tests.TestSupport;

/// <summary>A real (in-memory) SQLite database with the real schema, plus the services under test.</summary>
public sealed class TestDb : IDisposable
{
    public const string Alice = "user-alice";
    public const string Bob = "user-bob";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ApplicationDbContext Db { get; }
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    public InvoiceService Invoices { get; }
    public ClientService Clients { get; }
    public DashboardService Dashboard { get; }

    public DateOnly Today => DateOnly.FromDateTime(Clock.GetLocalNow().DateTime);

    public TestDb()
    {
        _connection.Open();
        Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        Db.Database.EnsureCreated();

        Invoices = new InvoiceService(Db, Clock);
        Clients = new ClientService(Db);
        Dashboard = new DashboardService(Db, Clock);
    }

    public Task<Client> NewClient(string owner, string name = "Acme") =>
        Clients.CreateAsync(owner, new ClientInput(name, null, null, null));

    public InvoiceInput Input(int clientId, DateOnly? issued = null, int termDays = 14, params LineInput[] lines)
    {
        var date = issued ?? Today;
        return new InvoiceInput(clientId, date, date.AddDays(termDays), null,
            lines.Length > 0 ? lines : [new LineInput("Work", 1, 100m, 0)]);
    }

    /// <summary>Creates a draft and sends it, so it has a number.</summary>
    public async Task<Invoice> SentInvoice(string owner, int clientId, DateOnly? issued = null, int termDays = 14, params LineInput[] lines)
    {
        var draft = await Invoices.CreateDraftAsync(owner, Input(clientId, issued, termDays, lines));
        return (await Invoices.SendAsync(owner, draft.Id))!;
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
