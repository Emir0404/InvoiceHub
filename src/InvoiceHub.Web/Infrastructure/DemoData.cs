using InvoiceHub.Web.Data;
using InvoiceHub.Web.Domain;
using InvoiceHub.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Web.Infrastructure;

/// <summary>
/// Development only: one demo user with a company, clients and invoices in every state,
/// so the app is not empty on first run. Credentials come from configuration, never from code.
/// </summary>
public static class DemoData
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var email = config["Demo:Email"];
        var password = config["Demo:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        if (await users.FindByEmailAsync(email) is not null) return; // already seeded

        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException("Demo user could not be created: " +
                string.Join("; ", created.Errors.Select(e => e.Description)));

        var db = services.GetRequiredService<ApplicationDbContext>();
        var clock = services.GetRequiredService<TimeProvider>();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        var company = services.GetRequiredService<CompanyProfileService>();
        await company.SaveAsync(user.Id, new CompanyInput(
            "Northwind Web Studio", "Zmaja od Bosne 12, 71000 Sarajevo, Bosnia and Herzegovina",
            "4200000000000", "billing@northwind.example", "BA39 1990 4400 1234 5678", "EUR"));

        var clients = services.GetRequiredService<ClientService>();
        var acme = await clients.CreateAsync(user.Id, new ClientInput("Acme Logistics GmbH", "accounts@acme.example", "Hafenstrasse 4, 20457 Hamburg, Germany", "DE123456789"));
        var bright = await clients.CreateAsync(user.Id, new ClientInput("Bright Dental Studio", "office@bright.example", "Maršala Tita 8, 71000 Sarajevo", "4201234560000"));
        var oak = await clients.CreateAsync(user.Id, new ClientInput("Oak & Pine Cafe", "hello@oakpine.example", null, null));

        var invoices = services.GetRequiredService<InvoiceService>();

        // Paid, last month
        var paid = await invoices.CreateDraftAsync(user.Id, Input(acme.Id, today.AddDays(-45), 14,
            Line("Website redesign: design and development", 1, 2400m, 0),
            Line("Hosting setup", 1, 150m, 0)));
        await invoices.SendAsync(user.Id, paid.Id);
        await invoices.MarkPaidAsync(user.Id, paid.Id, today.AddDays(-30));

        // Overdue
        var overdue = await invoices.CreateDraftAsync(user.Id, Input(bright.Id, today.AddDays(-40), 14,
            Line("Online booking system: implementation", 1, 1800m, 17),
            Line("Staff training (hours)", 3, 60m, 17)));
        await invoices.SendAsync(user.Id, overdue.Id);

        // Sent, still within terms
        var sent = await invoices.CreateDraftAsync(user.Id, Input(acme.Id, today.AddDays(-5), 30,
            Line("Monthly maintenance, October", 1, 320m, 0)));
        await invoices.SendAsync(user.Id, sent.Id);

        // Paid this month
        var paidNow = await invoices.CreateDraftAsync(user.Id, Input(oak.Id, today.AddDays(-12), 14,
            Line("Menu QR code page", 1, 250m, 17)));
        await invoices.SendAsync(user.Id, paidNow.Id);
        await invoices.MarkPaidAsync(user.Id, paidNow.Id, today.AddDays(-2));

        // Draft
        await invoices.CreateDraftAsync(user.Id, Input(oak.Id, today, 14,
            Line("Logo refresh", 1, 400m, 17),
            Line("Social media templates", 5, 30m, 17)));

        await db.SaveChangesAsync();
    }

    private static InvoiceInput Input(int clientId, DateOnly issued, int termDays, params LineInput[] lines) =>
        new(clientId, issued, issued.AddDays(termDays), null, lines);

    private static LineInput Line(string text, decimal qty, decimal price, decimal vat) => new(text, qty, price, vat);
}
