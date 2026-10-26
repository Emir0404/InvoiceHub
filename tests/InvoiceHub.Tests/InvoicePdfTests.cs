using System.Text;
using InvoiceHub.Web.Domain;
using InvoiceHub.Web.Pdf;

namespace InvoiceHub.Tests;

public class InvoicePdfTests
{
    private static Invoice Sample(int lines = 3, string? notes = "Thank you for your business.")
    {
        var invoice = new Invoice
        {
            Id = 7,
            Number = "2026-0007",
            Currency = "EUR",
            IssueDate = new DateOnly(2026, 10, 1),
            DueDate = new DateOnly(2026, 10, 15),
            Status = InvoiceStatus.Sent,
            Notes = notes,
            Client = new Client { Name = "Acme Logistics GmbH", Address = "Hafenstrasse 4, Hamburg", TaxId = "DE123", Email = "a@acme.test" },
        };
        for (var i = 1; i <= lines; i++)
            invoice.Lines.Add(new InvoiceLine { Description = $"Service number {i}", Quantity = i, UnitPrice = 99.5m, VatRate = i % 2 == 0 ? 17 : 0 });
        return invoice;
    }

    private static readonly CompanyProfile Company = new()
    {
        Name = "Northwind Web Studio", Address = "Zmaja od Bosne 12, Sarajevo", TaxId = "4200000000000",
        Email = "billing@northwind.test", Iban = "BA39 1990 4400 1234 5678", Currency = "EUR",
    };

    private static int PageCount(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]").Count;

    [Fact]
    public void Produces_a_real_pdf_document()
    {
        var pdf = InvoicePdf.Render(Sample(), Company);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Contains("%%EOF", Encoding.Latin1.GetString(pdf));
    }

    [Fact]
    public void A_short_invoice_fits_on_one_page()
    {
        Assert.Equal(1, PageCount(InvoicePdf.Render(Sample(lines: 3), Company)));
    }

    [Fact]
    public void A_long_invoice_flows_onto_more_pages()
    {
        Assert.True(PageCount(InvoicePdf.Render(Sample(lines: 50), Company)) >= 2);
    }

    [Fact]
    public void Works_for_a_draft_without_number_company_notes_or_optional_client_fields()
    {
        var invoice = Sample(notes: null);
        invoice.Number = null;
        invoice.Status = InvoiceStatus.Draft;
        invoice.Client = new Client { Name = "Walk-in customer" };

        var pdf = InvoicePdf.Render(invoice, company: null);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public void Handles_diacritics_and_very_long_text_without_crashing()
    {
        var invoice = Sample();
        invoice.Client.Name = "Šećerana Čapljina d.o.o. – Đoković & sinovi";
        invoice.Lines[0].Description = string.Concat(Enumerable.Repeat("Dugačak opis usluge sa čćšžđ znakovima. ", 12));
        invoice.Notes = string.Concat(Enumerable.Repeat("Napomena. ", 80));

        var pdf = InvoicePdf.Render(invoice, Company);

        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void A_paid_invoice_renders()
    {
        var invoice = Sample();
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidOn = new DateOnly(2026, 10, 9);

        Assert.True(InvoicePdf.Render(invoice, Company).Length > 1000);
    }
}
