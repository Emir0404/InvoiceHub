using InvoiceHub.Tests.TestSupport;
using InvoiceHub.Web.Domain;
using InvoiceHub.Web.Services;
using Microsoft.EntityFrameworkCore;
using static InvoiceHub.Tests.TestSupport.TestDb;

namespace InvoiceHub.Tests;

/// <summary>The repositories and the unit of work, tested directly against a real SQLite database.</summary>
public class RepositoryTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Repositories_only_return_the_owners_own_rows()
    {
        var aliceClient = await _t.NewClient(Alice, "Alice's client");
        var invoice = await _t.SentInvoice(Alice, aliceClient.Id);

        Assert.Null(await _t.InvoiceStore.FindAsync(Bob, invoice.Id));
        Assert.Null(await _t.ClientStore.FindAsync(Bob, aliceClient.Id));
        Assert.False(await _t.ClientStore.ExistsAsync(Bob, aliceClient.Id));
        Assert.Empty(await _t.InvoiceStore.ListAllAsync(Bob));
        Assert.Empty(await _t.ClientStore.ListAsync(Bob));
        Assert.Empty(await _t.InvoiceStore.NumbersStartingWithAsync(Bob, "2026-"));
        Assert.Equal(0, await _t.ClientStore.InvoiceCountAsync(Bob, aliceClient.Id));
        Assert.Null(await _t.CompanyStore.FindAsync(Bob));
        Assert.Null(await _t.CompanyStore.CurrencyAsync(Bob));

        Assert.NotNull(await _t.InvoiceStore.FindAsync(Alice, invoice.Id));
        Assert.Equal(1, await _t.ClientStore.InvoiceCountAsync(Alice, aliceClient.Id));
    }

    [Fact]
    public async Task Adding_through_a_repository_saves_nothing_until_the_unit_of_work_is_saved()
    {
        _t.ClientStore.Add(new Client { OwnerId = Alice, Name = "Pending" });

        Assert.Equal(0, await _t.Db.Clients.CountAsync()); // not in the database yet

        await _t.UnitOfWork.SaveChangesAsync();

        Assert.Equal(1, await _t.Db.Clients.CountAsync());
    }

    [Fact]
    public async Task One_save_stores_changes_made_through_several_repositories_together()
    {
        var client = await _t.NewClient(Alice);
        var draft = new Invoice { OwnerId = Alice, ClientId = client.Id, Currency = "EUR", IssueDate = _t.Today, DueDate = _t.Today.AddDays(14) };
        draft.Lines.Add(new InvoiceLine { Description = "Work", Quantity = 1, UnitPrice = 10m });

        _t.InvoiceStore.Add(draft);
        _t.ClientStore.Add(new Client { OwnerId = Alice, Name = "Second" });
        await _t.UnitOfWork.SaveChangesAsync();

        Assert.Equal(1, await _t.Db.Invoices.CountAsync());
        Assert.Equal(1, await _t.Db.InvoiceLines.CountAsync());
        Assert.Equal(2, await _t.Db.Clients.CountAsync());
    }

    [Fact]
    public async Task A_duplicate_invoice_number_surfaces_as_a_DataConflictException_not_an_EF_exception()
    {
        var client = await _t.NewClient(Alice);
        var first = await _t.Invoices.CreateDraftAsync(Alice, _t.Input(client.Id));
        var second = await _t.Invoices.CreateDraftAsync(Alice, _t.Input(client.Id));
        first.Send("2026-0001");
        await _t.UnitOfWork.SaveChangesAsync();

        second.Send("2026-0001");

        await Assert.ThrowsAsync<DataConflictException>(() => _t.UnitOfWork.SaveChangesAsync());
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    public async Task Search_treats_percent_and_underscore_literally(string search)
    {
        var client = await _t.NewClient(Alice, "Acme");
        await _t.SentInvoice(Alice, client.Id);

        Assert.Empty(await _t.InvoiceStore.SearchAsync(Alice, new InvoiceFilter(Search: search), _t.Today));
    }

    [Fact]
    public async Task Search_by_overdue_uses_the_date_it_is_given()
    {
        var client = await _t.NewClient(Alice);
        await _t.SentInvoice(Alice, client.Id, issued: _t.Today.AddDays(-30), termDays: 14);

        Assert.Single(await _t.InvoiceStore.SearchAsync(Alice, new InvoiceFilter(Status: "overdue"), _t.Today));
        Assert.Empty(await _t.InvoiceStore.SearchAsync(Alice, new InvoiceFilter(Status: "overdue"), _t.Today.AddDays(-30)));
    }

    [Fact]
    public void Services_depend_on_repositories_never_on_Entity_Framework()
    {
        var serviceTypes = typeof(InvoiceService).Assembly.GetTypes()
            .Where(t => t.Namespace == "InvoiceHub.Web.Services" && t.IsClass && !t.IsAbstract && t.Name.EndsWith("Service"));

        foreach (var type in serviceTypes)
            foreach (var parameter in type.GetConstructors().SelectMany(c => c.GetParameters()))
                Assert.False(
                    typeof(DbContext).IsAssignableFrom(parameter.ParameterType),
                    $"{type.Name} takes a DbContext. Go through a repository instead.");
    }
}
