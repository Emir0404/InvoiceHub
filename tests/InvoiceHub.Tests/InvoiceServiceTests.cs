using InvoiceHub.Tests.TestSupport;
using InvoiceHub.Web.Domain;
using InvoiceHub.Web.Services;
using Microsoft.EntityFrameworkCore;
using static InvoiceHub.Tests.TestSupport.TestDb;

namespace InvoiceHub.Tests;

public class InvoiceServiceTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    // ---- drafts ----

    [Fact]
    public async Task A_new_invoice_is_a_draft_without_number_and_takes_the_company_currency()
    {
        await new CompanyProfileService(_t.Db).SaveAsync(Alice, new CompanyInput("My Co", null, null, null, null, "bam"));
        var client = await _t.NewClient(Alice);

        var invoice = await _t.Invoices.CreateDraftAsync(Alice, _t.Input(client.Id));

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Null(invoice.Number);
        Assert.Equal("BAM", invoice.Currency);
    }

    [Fact]
    public async Task Currency_defaults_to_EUR_when_no_company_profile_exists()
    {
        var client = await _t.NewClient(Alice);

        var invoice = await _t.Invoices.CreateDraftAsync(Alice, _t.Input(client.Id));

        Assert.Equal("EUR", invoice.Currency);
    }

    [Fact]
    public async Task A_draft_can_be_edited_and_its_lines_are_replaced()
    {
        var client = await _t.NewClient(Alice);
        var draft = await _t.Invoices.CreateDraftAsync(Alice, _t.Input(client.Id, lines: [new("Old", 1, 10m, 0), new("Older", 1, 5m, 0)]));

        await _t.Invoices.UpdateDraftAsync(Alice, draft.Id, _t.Input(client.Id, lines: [new("New", 2, 50m, 17)]));

        var reloaded = await _t.Invoices.GetAsync(Alice, draft.Id);
        var line = Assert.Single(reloaded!.Lines);
        Assert.Equal("New", line.Description);
        Assert.Equal(1, await _t.Db.InvoiceLines.CountAsync());
    }

    [Fact]
    public async Task A_sent_invoice_cannot_be_edited_or_deleted()
    {
        var client = await _t.NewClient(Alice);
        var sent = await _t.SentInvoice(Alice, client.Id);

        await Assert.ThrowsAsync<DomainException>(() => _t.Invoices.UpdateDraftAsync(Alice, sent.Id, _t.Input(client.Id)));
        await Assert.ThrowsAsync<DomainException>(() => _t.Invoices.DeleteDraftAsync(Alice, sent.Id));
        Assert.NotNull(await _t.Invoices.GetAsync(Alice, sent.Id));
    }

    [Fact]
    public async Task A_draft_can_be_deleted_together_with_its_lines()
    {
        var client = await _t.NewClient(Alice);
        var draft = await _t.Invoices.CreateDraftAsync(Alice, _t.Input(client.Id));

        Assert.True(await _t.Invoices.DeleteDraftAsync(Alice, draft.Id));

        Assert.Null(await _t.Invoices.GetAsync(Alice, draft.Id));
        Assert.Equal(0, await _t.Db.InvoiceLines.CountAsync());
    }

    // ---- validation (the service must not trust its caller) ----

    public static TheoryData<string, LineInput[]> BadLines => new()
    {
        { "no lines", [] },
        { "blank description", [new("  ", 1, 10m, 0)] },
        { "description too long", [new(new string('x', 201), 1, 10m, 0)] },
        { "zero quantity", [new("x", 0, 10m, 0)] },
        { "negative price", [new("x", 1, -1m, 0)] },
        { "vat over 100", [new("x", 1, 10m, 101)] },
        { "negative vat", [new("x", 1, 10m, -1)] },
    };

    [Theory]
    [MemberData(nameof(BadLines))]
    public async Task Invalid_lines_are_rejected(string _, LineInput[] lines)
    {
        var client = await _t.NewClient(Alice);

        await Assert.ThrowsAsync<DomainException>(() =>
            _t.Invoices.CreateDraftAsync(Alice, new InvoiceInput(client.Id, _t.Today, _t.Today.AddDays(14), null, lines)));
        Assert.Equal(0, await _t.Db.Invoices.CountAsync());
    }

    [Fact]
    public async Task More_than_the_maximum_number_of_lines_is_rejected()
    {
        var client = await _t.NewClient(Alice);
        var lines = Enumerable.Range(0, InvoiceService.MaxLines + 1).Select(i => new LineInput($"L{i}", 1, 1m, 0)).ToArray();

        await Assert.ThrowsAsync<DomainException>(() => _t.Invoices.CreateDraftAsync(Alice, _t.Input(client.Id, lines: lines)));
    }

    [Fact]
    public async Task Due_date_before_issue_date_is_rejected()
    {
        var client = await _t.NewClient(Alice);

        await Assert.ThrowsAsync<DomainException>(() =>
            _t.Invoices.CreateDraftAsync(Alice, new InvoiceInput(client.Id, _t.Today, _t.Today.AddDays(-1), null, [new("x", 1, 1m, 0)])));
    }

    // ---- numbering ----

    [Fact]
    public async Task Numbers_are_sequential_per_year_and_start_at_0001()
    {
        var client = await _t.NewClient(Alice);

        var first = await _t.SentInvoice(Alice, client.Id);
        var second = await _t.SentInvoice(Alice, client.Id);

        Assert.Equal("2026-0001", first.Number);
        Assert.Equal("2026-0002", second.Number);
    }

    [Fact]
    public async Task Drafts_do_not_consume_numbers_so_there_are_no_gaps()
    {
        var client = await _t.NewClient(Alice);
        await _t.Invoices.CreateDraftAsync(Alice, _t.Input(client.Id)); // draft #1: never sent
        var draft2 = await _t.Invoices.CreateDraftAsync(Alice, _t.Input(client.Id));
        await _t.Invoices.DeleteDraftAsync(Alice, draft2.Id);

        var sent = await _t.SentInvoice(Alice, client.Id);

        Assert.Equal("2026-0001", sent.Number);
    }

    [Fact]
    public async Task Each_year_has_its_own_sequence_based_on_the_issue_date()
    {
        var client = await _t.NewClient(Alice);
        var in2026 = await _t.SentInvoice(Alice, client.Id, new DateOnly(2026, 12, 31));
        var in2027 = await _t.SentInvoice(Alice, client.Id, new DateOnly(2027, 1, 2));
        var again2026 = await _t.SentInvoice(Alice, client.Id, new DateOnly(2026, 6, 1));

        Assert.Equal("2026-0001", in2026.Number);
        Assert.Equal("2027-0001", in2027.Number);
        Assert.Equal("2026-0002", again2026.Number);
    }

    [Fact]
    public async Task Two_users_each_get_their_own_numbering()
    {
        var aliceClient = await _t.NewClient(Alice);
        var bobClient = await _t.NewClient(Bob);

        var a = await _t.SentInvoice(Alice, aliceClient.Id);
        var b = await _t.SentInvoice(Bob, bobClient.Id);

        Assert.Equal("2026-0001", a.Number);
        Assert.Equal("2026-0001", b.Number);
    }

    [Fact]
    public async Task The_database_itself_refuses_a_duplicate_number_for_the_same_user()
    {
        var client = await _t.NewClient(Alice);
        await _t.SentInvoice(Alice, client.Id);
        var other = await _t.Invoices.CreateDraftAsync(Alice, _t.Input(client.Id));

        other.Send("2026-0001"); // forced duplicate, bypassing the numbering service

        await Assert.ThrowsAsync<DbUpdateException>(() => _t.Db.SaveChangesAsync());
    }

    // ---- paying ----

    [Fact]
    public async Task Marking_paid_defaults_to_today()
    {
        var client = await _t.NewClient(Alice);
        var sent = await _t.SentInvoice(Alice, client.Id);

        var paid = await _t.Invoices.MarkPaidAsync(Alice, sent.Id, null);

        Assert.Equal(InvoiceStatus.Paid, paid!.Status);
        Assert.Equal(_t.Today, paid.PaidOn);
    }

    // ---- data isolation: the most important rule of a multi-user app ----

    [Fact]
    public async Task One_user_cannot_see_or_touch_another_users_invoice()
    {
        var aliceClient = await _t.NewClient(Alice);
        var bobClient = await _t.NewClient(Bob);
        var alicesDraft = await _t.Invoices.CreateDraftAsync(Alice, _t.Input(aliceClient.Id));
        var alicesSent = await _t.SentInvoice(Alice, aliceClient.Id);

        Assert.Null(await _t.Invoices.GetAsync(Bob, alicesDraft.Id));
        Assert.Null(await _t.Invoices.UpdateDraftAsync(Bob, alicesDraft.Id, _t.Input(bobClient.Id)));
        Assert.Null(await _t.Invoices.SendAsync(Bob, alicesDraft.Id));
        Assert.Null(await _t.Invoices.MarkPaidAsync(Bob, alicesSent.Id, null));
        Assert.False(await _t.Invoices.DeleteDraftAsync(Bob, alicesDraft.Id));
        Assert.Empty(await _t.Invoices.ListAsync(Bob, new InvoiceFilter()));

        // ...and nothing of Alice's changed.
        Assert.Equal(InvoiceStatus.Draft, (await _t.Invoices.GetAsync(Alice, alicesDraft.Id))!.Status);
        Assert.Equal(InvoiceStatus.Sent, (await _t.Invoices.GetAsync(Alice, alicesSent.Id))!.Status);
    }

    [Fact]
    public async Task An_invoice_cannot_be_created_for_another_users_client()
    {
        var alicesClient = await _t.NewClient(Alice);

        await Assert.ThrowsAsync<DomainException>(() => _t.Invoices.CreateDraftAsync(Bob, _t.Input(alicesClient.Id)));
        Assert.Equal(0, await _t.Db.Invoices.CountAsync());
    }

    [Fact]
    public async Task An_invoice_cannot_be_moved_to_another_users_client()
    {
        var aliceClient = await _t.NewClient(Alice);
        var bobClient = await _t.NewClient(Bob);
        var draft = await _t.Invoices.CreateDraftAsync(Alice, _t.Input(aliceClient.Id));

        await Assert.ThrowsAsync<DomainException>(() => _t.Invoices.UpdateDraftAsync(Alice, draft.Id, _t.Input(bobClient.Id)));
        Assert.Equal(aliceClient.Id, (await _t.Invoices.GetAsync(Alice, draft.Id))!.ClientId);
    }

    [Fact]
    public async Task Clients_are_private_too()
    {
        var alicesClient = await _t.NewClient(Alice, "Alice's client");

        Assert.Null(await _t.Clients.GetAsync(Bob, alicesClient.Id));
        Assert.Null(await _t.Clients.UpdateAsync(Bob, alicesClient.Id, new ClientInput("Hacked", null, null, null)));
        Assert.False(await _t.Clients.DeleteAsync(Bob, alicesClient.Id));
        Assert.Empty(await _t.Clients.ListAsync(Bob));
        Assert.Equal("Alice's client", (await _t.Clients.GetAsync(Alice, alicesClient.Id))!.Name);
    }

    // ---- clients ----

    [Fact]
    public async Task A_client_with_invoices_cannot_be_deleted_but_one_without_can()
    {
        var used = await _t.NewClient(Alice, "Used");
        var unused = await _t.NewClient(Alice, "Unused");
        await _t.Invoices.CreateDraftAsync(Alice, _t.Input(used.Id));

        await Assert.ThrowsAsync<DomainException>(() => _t.Clients.DeleteAsync(Alice, used.Id));
        Assert.True(await _t.Clients.DeleteAsync(Alice, unused.Id));
        Assert.Single(await _t.Clients.ListAsync(Alice));
    }

    [Fact]
    public async Task Clients_are_listed_alphabetically_and_blank_fields_are_stored_as_null()
    {
        await _t.Clients.CreateAsync(Alice, new ClientInput("Zeta", "  ", "", " "));
        await _t.Clients.CreateAsync(Alice, new ClientInput("Alpha", "a@a.test", null, null));

        var list = await _t.Clients.ListAsync(Alice);

        Assert.Equal(new[] { "Alpha", "Zeta" }, list.Select(c => c.Name));
        Assert.Null(list[1].Email);
        Assert.Null(list[1].Address);
        Assert.Null(list[1].TaxId);
    }

    // ---- listing and filters ----

    private async Task SeedForFilters()
    {
        var acme = await _t.NewClient(Alice, "Acme Corp");
        var bright = await _t.NewClient(Alice, "Bright Studio");

        await _t.Invoices.CreateDraftAsync(Alice, _t.Input(acme.Id));                                   // draft
        await _t.SentInvoice(Alice, acme.Id, _t.Today.AddDays(-30), termDays: 14);                       // overdue (2026-0001)
        await _t.SentInvoice(Alice, bright.Id, _t.Today.AddDays(-2), termDays: 14);                      // sent, in terms (2026-0002)
        var paid = await _t.SentInvoice(Alice, bright.Id, _t.Today.AddDays(-20), termDays: 14);          // paid (2026-0003)
        await _t.Invoices.MarkPaidAsync(Alice, paid.Id, _t.Today.AddDays(-1));
    }

    [Theory]
    [InlineData("draft", 1)]
    [InlineData("sent", 2)]
    [InlineData("overdue", 1)]
    [InlineData("paid", 1)]
    [InlineData("PAID", 1)]
    [InlineData("", 4)]
    [InlineData(null, 4)]
    public async Task Status_filter(string? status, int expected)
    {
        await SeedForFilters();

        var list = await _t.Invoices.ListAsync(Alice, new InvoiceFilter(Status: status));

        Assert.Equal(expected, list.Count);
    }

    [Fact]
    public async Task Overdue_means_sent_and_past_the_due_date_and_not_due_today()
    {
        var client = await _t.NewClient(Alice);
        var dueToday = await _t.SentInvoice(Alice, client.Id, _t.Today.AddDays(-14), termDays: 14);
        var dueYesterday = await _t.SentInvoice(Alice, client.Id, _t.Today.AddDays(-15), termDays: 14);

        var overdue = await _t.Invoices.ListAsync(Alice, new InvoiceFilter(Status: "overdue"));

        Assert.Equal(dueYesterday.Id, Assert.Single(overdue).Id);
        Assert.NotEqual(dueToday.Id, overdue[0].Id);
    }

    [Theory]
    [InlineData("bright", 2)]
    [InlineData("ACME", 2)]
    [InlineData("2026-0002", 1)]
    [InlineData("2026-000", 3)]
    [InlineData("nothing like this", 0)]
    public async Task Search_matches_client_name_and_invoice_number_case_insensitively(string search, int expected)
    {
        await SeedForFilters();

        var list = await _t.Invoices.ListAsync(Alice, new InvoiceFilter(Search: search));

        Assert.Equal(expected, list.Count);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task Wildcard_characters_in_search_are_searched_literally(string search)
    {
        await SeedForFilters();

        Assert.Empty(await _t.Invoices.ListAsync(Alice, new InvoiceFilter(Search: search)));
    }

    [Fact]
    public async Task Invoices_are_listed_newest_first()
    {
        var client = await _t.NewClient(Alice);
        var old = await _t.SentInvoice(Alice, client.Id, new DateOnly(2026, 1, 5));
        var recent = await _t.SentInvoice(Alice, client.Id, new DateOnly(2026, 9, 5));
        var middle = await _t.SentInvoice(Alice, client.Id, new DateOnly(2026, 5, 5));

        var list = await _t.Invoices.ListAsync(Alice, new InvoiceFilter());

        Assert.Equal(new[] { recent.Id, middle.Id, old.Id }, list.Select(i => i.Id));
    }

    // ---- dashboard ----

    [Fact]
    public async Task Dashboard_sums_outstanding_overdue_and_paid_this_month()
    {
        var client = await _t.NewClient(Alice);
        var vat17 = new LineInput("Work", 1, 100m, 17); // 117.00 gross

        await _t.Invoices.CreateDraftAsync(Alice, _t.Input(client.Id, lines: vat17));                         // draft: ignored in money
        await _t.SentInvoice(Alice, client.Id, _t.Today.AddDays(-40), 14, vat17);                           // overdue 117
        await _t.SentInvoice(Alice, client.Id, _t.Today.AddDays(-1), 14, new LineInput("Work", 1, 50m, 0)); // sent 50
        var paidNow = await _t.SentInvoice(Alice, client.Id, _t.Today.AddDays(-3), 14, new LineInput("Work", 1, 200m, 0));
        await _t.Invoices.MarkPaidAsync(Alice, paidNow.Id, _t.Today);                                       // paid this month 200
        var paidOld = await _t.SentInvoice(Alice, client.Id, new DateOnly(2026, 9, 1), 14, new LineInput("Work", 1, 999m, 0));
        await _t.Invoices.MarkPaidAsync(Alice, paidOld.Id, new DateOnly(2026, 9, 10));                      // paid last month: not counted

        var summary = await _t.Dashboard.GetAsync(Alice);

        Assert.Equal(167m, Assert.Single(summary.Outstanding).Amount);   // 117 + 50
        Assert.Equal(117m, Assert.Single(summary.Overdue).Amount);
        Assert.Equal(1, summary.OverdueCount);
        Assert.Equal(200m, Assert.Single(summary.PaidThisMonth).Amount);
        Assert.Equal(1, summary.DraftCount);
        Assert.Equal(5, summary.Recent.Count);
    }

    [Fact]
    public async Task Dashboard_keeps_currencies_apart_and_only_counts_own_invoices()
    {
        var company = new CompanyProfileService(_t.Db);
        var aliceClient = await _t.NewClient(Alice);
        await _t.SentInvoice(Alice, aliceClient.Id, lines: new LineInput("EUR work", 1, 100m, 0));

        await company.SaveAsync(Alice, new CompanyInput("Co", null, null, null, null, "BAM"));
        await _t.SentInvoice(Alice, aliceClient.Id, lines: new LineInput("BAM work", 1, 300m, 0));

        var bobClient = await _t.NewClient(Bob);
        await _t.SentInvoice(Bob, bobClient.Id, lines: new LineInput("Bob's work", 1, 5000m, 0));

        var summary = await _t.Dashboard.GetAsync(Alice);

        Assert.Equal(new[] { ("BAM", 300m), ("EUR", 100m) }, summary.Outstanding.Select(m => (m.Currency, m.Amount)));
    }

    [Fact]
    public async Task Dashboard_of_a_new_user_is_empty()
    {
        var summary = await _t.Dashboard.GetAsync("someone-new");

        Assert.Empty(summary.Outstanding);
        Assert.Empty(summary.Overdue);
        Assert.Empty(summary.PaidThisMonth);
        Assert.Equal(0, summary.OverdueCount);
        Assert.Equal(0, summary.DraftCount);
        Assert.Empty(summary.Recent);
    }

    // ---- company profile ----

    [Fact]
    public async Task Company_profile_is_saved_once_per_user_and_updated_afterwards()
    {
        var service = new CompanyProfileService(_t.Db);

        await service.SaveAsync(Alice, new CompanyInput("First", null, null, null, null, "eur"));
        await service.SaveAsync(Alice, new CompanyInput("Second", " Street 1 ", null, null, null, "BAM"));

        var profile = await service.FindAsync(Alice);
        Assert.Equal("Second", profile!.Name);
        Assert.Equal("Street 1", profile.Address);
        Assert.Equal("BAM", profile.Currency);
        Assert.Equal(1, await _t.Db.CompanyProfiles.CountAsync());
        Assert.Null(await service.FindAsync(Bob));
    }

    [Theory]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData("")]
    public async Task Company_currency_must_be_three_letters(string currency)
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            new CompanyProfileService(_t.Db).SaveAsync(Alice, new CompanyInput("Co", null, null, null, null, currency)));
    }
}
