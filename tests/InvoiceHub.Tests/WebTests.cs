using System.Net;
using System.Reflection;
using InvoiceHub.Web.Controllers;
using InvoiceHub.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Tests;

public class WebTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;

    public WebTests(AppFactory factory) => _factory = factory;

    private static string Email() => $"user-{Guid.NewGuid():N}@test.dev";

    private async Task<Browser> SignedInBrowser()
    {
        var browser = _factory.NewBrowser();
        await browser.Register(Email());
        return browser;
    }

    // ---- access control ----

    [Theory]
    [InlineData("/Invoices")]
    [InlineData("/Invoices/Create")]
    [InlineData("/Invoices/Details/1")]
    [InlineData("/Invoices/Pdf/1")]
    [InlineData("/Clients")]
    [InlineData("/Clients/Create")]
    [InlineData("/Settings")]
    public async Task Anonymous_visitors_are_sent_to_the_login_page(string url)
    {
        var response = await _factory.NewBrowser().Get(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public void Every_controller_except_Home_requires_login()
    {
        // A guard against the classic mistake: adding a new controller and forgetting [Authorize].
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract && t.Name != "HomeController" && t.Name != "AccountController");

        Assert.All(controllers, type =>
            Assert.True(type.GetCustomAttribute<AuthorizeAttribute>() is not null, $"{type.Name} has no [Authorize]"));
    }

    [Fact]
    public void Only_the_sign_in_actions_of_AccountController_are_public()
    {
        var publicActions = typeof(AccountController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AuthorizeAttribute>() is null)
            .Select(m => m.Name).Distinct().Order().ToArray();

        Assert.Equal(["Login", "Logout", "Register"], publicActions);
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_a_strong_new_one()
    {
        var browser = _factory.NewBrowser();
        var email = Email();
        await browser.Register(email, "Password123!");

        var wrong = await browser.PostForm("/account/password", "/account/password",
            [new("CurrentPassword", "Nope12345!"), new("NewPassword", "Another123!"), new("ConfirmPassword", "Another123!")]);
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("current password is not correct", await wrong.Content.ReadAsStringAsync());

        var weak = await browser.PostForm("/account/password", "/account/password",
            [new("CurrentPassword", "Password123!"), new("NewPassword", "short"), new("ConfirmPassword", "short")]);
        Assert.Equal(HttpStatusCode.OK, weak.StatusCode);

        var ok = await browser.PostForm("/account/password", "/account/password",
            [new("CurrentPassword", "Password123!"), new("NewPassword", "Another123!"), new("ConfirmPassword", "Another123!")]);
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);

        // The old password no longer works, the new one does.
        var fresh = _factory.NewBrowser();
        var oldLogin = await fresh.PostForm("/account/login", "/account/login", [new("Email", email), new("Password", "Password123!")]);
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);
        var newLogin = await fresh.PostForm("/account/login", "/account/login", [new("Email", email), new("Password", "Another123!")]);
        Assert.Equal(HttpStatusCode.Redirect, newLogin.StatusCode);
    }

    [Fact]
    public async Task Login_gives_the_same_answer_for_an_unknown_email_and_a_wrong_password()
    {
        var browser = _factory.NewBrowser();
        var email = Email();
        await browser.Register(email);
        var other = _factory.NewBrowser();

        var unknown = await (await other.PostForm("/account/login", "/account/login", [new("Email", Email()), new("Password", "Password123!")])).Content.ReadAsStringAsync();
        var wrong = await (await other.PostForm("/account/login", "/account/login", [new("Email", email), new("Password", "Wrong12345!")])).Content.ReadAsStringAsync();

        Assert.Contains("The email or password is not correct.", unknown);
        Assert.Contains("The email or password is not correct.", wrong);
    }

    [Fact]
    public async Task Login_never_redirects_to_another_site()
    {
        var browser = _factory.NewBrowser();
        var email = Email();
        await browser.Register(email);
        var other = _factory.NewBrowser();

        var response = await other.PostForm("/account/login?returnUrl=https%3A%2F%2Fevil.example%2F", "/account/login",
            [new("Email", email), new("Password", "Password123!")]);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("evil.example", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task The_landing_page_is_public()
    {
        var html = await _factory.NewBrowser().GetHtml("/");

        Assert.Contains("Invoicing that stays out of your way", html);
        Assert.Contains("Create a free account", html);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var response = await _factory.NewBrowser().Get("/");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task A_post_without_an_anti_forgery_token_is_rejected()
    {
        var browser = await SignedInBrowser();

        var response = await browser.Client.PostAsync("/Clients/Create", new FormUrlEncodedContent([new("Name", "Evil Ltd")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("Evil Ltd", await browser.GetHtml("/Clients"));
    }

    [Fact]
    public async Task Unknown_pages_show_the_friendly_404_page()
    {
        var response = await _factory.NewBrowser().Get("/nothing/here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("could not find that page", await response.Content.ReadAsStringAsync());
    }

    // ---- the full journey ----

    [Fact]
    public async Task A_user_can_run_the_whole_invoicing_flow()
    {
        var browser = await SignedInBrowser();

        // Dashboard nudges towards the company details, then it is saved.
        Assert.Contains("Add your company details", await browser.GetHtml("/"));
        var saved = await browser.PostForm("/Settings", "/Settings",
            [new("Name", "Test Studio"), new("Currency", "EUR"), new("Iban", "BA39 1990 4400 1234 5678")]);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.DoesNotContain("Add your company details", await browser.GetHtml("/"));

        // Client and draft invoice.
        var clientId = await browser.CreateClient("Acme Ltd");
        var invoiceId = await browser.CreateInvoice(clientId, "Consulting", qty: 2, price: 100m, vat: 17);

        var draft = await browser.GetHtml($"/Invoices/Details/{invoiceId}");
        Assert.Contains("Draft", draft);
        Assert.Contains("234.00 EUR", draft); // 200 net + 34 VAT
        Assert.Contains("Send invoice", draft);

        // Send: gets the number and locks.
        var sendPage = $"/Invoices/Details/{invoiceId}";
        var sent = await browser.PostForm(sendPage, $"/Invoices/Send/{invoiceId}", []);
        Assert.Equal(HttpStatusCode.Redirect, sent.StatusCode);
        var afterSend = await browser.GetHtml(sendPage);
        Assert.Contains("Invoice 2026-0001", afterSend);
        Assert.DoesNotContain("Send invoice", afterSend);
        Assert.Contains("Mark as paid", afterSend);

        // Editing a sent invoice is refused.
        var edit = await browser.Get($"/Invoices/Edit/{invoiceId}");
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);

        // PDF download.
        var pdf = await browser.Get($"/Invoices/Pdf/{invoiceId}");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal("invoice-2026-0001.pdf", pdf.Content.Headers.ContentDisposition?.FileName);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));

        // Paid.
        var paid = await browser.PostForm(sendPage, $"/Invoices/MarkPaid/{invoiceId}", [new("paidOn", "2026-10-09")]);
        Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
        var afterPaid = await browser.GetHtml(sendPage);
        Assert.Contains("Paid", afterPaid);
        Assert.Contains("09 Oct 2026", afterPaid);

        // It shows up in the list and can be found by number.
        var list = await browser.GetHtml("/Invoices?search=2026-0001");
        Assert.Contains("Acme Ltd", list);
    }

    [Fact]
    public async Task Invalid_forms_are_shown_again_with_messages_and_nothing_is_saved()
    {
        var browser = await SignedInBrowser();
        var clientId = await browser.CreateClient("Beta GmbH");

        var response = await browser.PostForm("/Invoices/Create", "/Invoices/Create",
        [
            new("ClientId", clientId.ToString()), new("IssueDate", "2026-10-01"), new("DueDate", "2026-10-15"),
            new("Lines[0].Description", ""), new("Lines[0].Quantity", "1"), new("Lines[0].UnitPrice", "10"), new("Lines[0].VatRate", "0"),
        ]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        Assert.Contains("No invoices match", await browser.GetHtml("/Invoices"));
    }

    [Fact]
    public async Task A_client_with_invoices_cannot_be_deleted_from_the_ui()
    {
        var browser = await SignedInBrowser();
        var clientId = await browser.CreateClient("Gamma Inc");
        await browser.CreateInvoice(clientId);

        var response = await browser.PostForm("/Clients", $"/Clients/Delete/{clientId}", []);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var html = await browser.GetHtml("/Clients");
        Assert.Contains("Gamma Inc", html);
        Assert.Contains("has invoices and cannot be deleted", html);
    }

    // ---- data isolation over HTTP ----

    [Fact]
    public async Task One_user_gets_404_for_everything_that_belongs_to_another_user()
    {
        var alice = await SignedInBrowser();
        var aliceClient = await alice.CreateClient("Alices Secret Client");
        var aliceInvoice = await alice.CreateInvoice(aliceClient, "Alices secret work");

        var bob = await SignedInBrowser();
        await bob.CreateClient("Bobs Client"); // give Bob a page that has a token

        foreach (var url in new[] { $"/Invoices/Details/{aliceInvoice}", $"/Invoices/Edit/{aliceInvoice}", $"/Invoices/Pdf/{aliceInvoice}", $"/Clients/Edit/{aliceClient}" })
            Assert.Equal(HttpStatusCode.NotFound, (await bob.Get(url)).StatusCode);

        foreach (var url in new[] { $"/Invoices/Send/{aliceInvoice}", $"/Invoices/MarkPaid/{aliceInvoice}", $"/Invoices/Delete/{aliceInvoice}", $"/Clients/Delete/{aliceClient}" })
            Assert.Equal(HttpStatusCode.NotFound, (await bob.PostForm("/Clients", url, [])).StatusCode);

        var bobsInvoices = await bob.GetHtml("/Invoices");
        Assert.DoesNotContain("Alices Secret Client", bobsInvoices);
        Assert.DoesNotContain("Alices Secret Client", await bob.GetHtml("/Clients"));

        // Alice's invoice is untouched.
        Assert.Contains("Draft", await alice.GetHtml($"/Invoices/Details/{aliceInvoice}"));
    }

    [Fact]
    public async Task A_forged_client_id_in_the_form_does_not_attach_an_invoice_to_someone_elses_client()
    {
        var alice = await SignedInBrowser();
        var aliceClient = await alice.CreateClient("Alice Only Ltd");

        var bob = await SignedInBrowser();
        await bob.CreateClient("Bob Ltd");
        var response = await bob.PostForm("/Invoices/Create", "/Invoices/Create",
        [
            new("ClientId", aliceClient.ToString()), new("IssueDate", "2026-10-01"), new("DueDate", "2026-10-15"),
            new("Lines[0].Description", "Sneaky"), new("Lines[0].Quantity", "1"), new("Lines[0].UnitPrice", "10"), new("Lines[0].VatRate", "0"),
        ]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // form shown again with an error
        Assert.Contains("choose one of your clients", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Sneaky", await alice.GetHtml("/Invoices"));
    }

    // ---- accounts ----

    [Fact]
    public async Task Registration_with_a_weak_password_is_refused()
    {
        var browser = _factory.NewBrowser();

        var response = await browser.PostForm("/account/register", "/account/register",
            [new("Email", Email()), new("Password", "short"), new("ConfirmPassword", "short")]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.Get("/Invoices")).StatusCode); // still not signed in
    }
}
