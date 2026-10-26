using System.Globalization;
using InvoiceHub.Web.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InvoiceHub.Web.Pdf;

/// <summary>Draws an invoice as an A4 PDF.</summary>
public static class InvoicePdf
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private const string Ink = "#1f2933";
    private const string Muted = "#6b7280";
    private const string Line = "#d9dee5";
    private const string Accent = "#1d4ed8";

    static InvoicePdf() => QuestPDF.Settings.License = LicenseType.Community;

    public static byte[] Render(Invoice invoice, CompanyProfile? company)
    {
        var totals = invoice.Totals;
        var money = (decimal value) => value.ToString("N2", En) + " " + invoice.Currency;
        var number = invoice.Number ?? "DRAFT";

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink));

            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(company?.Name ?? "Your company").FontSize(18).Bold().FontColor(Accent);
                        if (!string.IsNullOrWhiteSpace(company?.Address))
                            c.Item().Text(company.Address).FontColor(Muted);
                        if (!string.IsNullOrWhiteSpace(company?.TaxId))
                            c.Item().Text($"Tax ID: {company.TaxId}").FontColor(Muted);
                        if (!string.IsNullOrWhiteSpace(company?.Email))
                            c.Item().Text(company.Email).FontColor(Muted);
                    });

                    row.ConstantItem(180).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("INVOICE").FontSize(22).Bold();
                        c.Item().AlignRight().Text(number).FontSize(12).FontColor(Muted);
                        if (invoice.Status == InvoiceStatus.Paid)
                            c.Item().AlignRight().Text("PAID").Bold().FontColor("#15803d");
                    });
                });
                col.Item().PaddingTop(12).LineHorizontal(1).LineColor(Line);
            });

            page.Content().PaddingVertical(16).Column(col =>
            {
                col.Spacing(18);

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("BILL TO").FontSize(8).Bold().FontColor(Muted);
                        c.Item().Text(invoice.Client.Name).Bold().FontSize(12);
                        if (!string.IsNullOrWhiteSpace(invoice.Client.Address)) c.Item().Text(invoice.Client.Address);
                        if (!string.IsNullOrWhiteSpace(invoice.Client.TaxId)) c.Item().Text($"Tax ID: {invoice.Client.TaxId}");
                        if (!string.IsNullOrWhiteSpace(invoice.Client.Email)) c.Item().Text(invoice.Client.Email);
                    });

                    row.ConstantItem(180).Column(c =>
                    {
                        MetaRow(c, "Issue date", invoice.IssueDate.ToString("dd MMM yyyy", En));
                        MetaRow(c, "Due date", invoice.DueDate.ToString("dd MMM yyyy", En));
                        if (invoice.PaidOn is { } paid)
                            MetaRow(c, "Paid on", paid.ToString("dd MMM yyyy", En));
                    });
                });

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(5);
                        c.RelativeColumn(1.2f);
                        c.RelativeColumn(1.6f);
                        c.RelativeColumn(1.1f);
                        c.RelativeColumn(1.8f);
                    });

                    table.Header(h =>
                    {
                        HeaderCell(h.Cell(), "Description", false);
                        HeaderCell(h.Cell(), "Qty", true);
                        HeaderCell(h.Cell(), "Unit price", true);
                        HeaderCell(h.Cell(), "VAT", true);
                        HeaderCell(h.Cell(), "Net amount", true);
                    });

                    foreach (var line in invoice.Lines)
                    {
                        BodyCell(table.Cell()).Text(line.Description);
                        BodyCell(table.Cell()).AlignRight().Text(line.Quantity.ToString("0.####", En));
                        BodyCell(table.Cell()).AlignRight().Text(line.UnitPrice.ToString("N2", En));
                        BodyCell(table.Cell()).AlignRight().Text($"{line.VatRate:0.##} %");
                        BodyCell(table.Cell()).AlignRight().Text(InvoiceCalculator.LineNet(line).ToString("N2", En));
                    }
                });

                col.Item().AlignRight().Width(240).Column(c =>
                {
                    TotalRow(c, "Net total", money(totals.Net), false);
                    foreach (var vat in totals.Breakdown.Where(b => b.Rate > 0 || b.Vat > 0))
                        TotalRow(c, $"VAT {vat.Rate:0.##} % on {vat.Net.ToString("N2", En)}", money(vat.Vat), false);
                    c.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Line);
                    TotalRow(c, "Total due", money(totals.Gross), true);
                });

                if (!string.IsNullOrWhiteSpace(company?.Iban))
                    col.Item().Column(c =>
                    {
                        c.Item().Text("PAYMENT").FontSize(8).Bold().FontColor(Muted);
                        c.Item().Text($"Please pay by {invoice.DueDate.ToString("dd MMM yyyy", En)} to IBAN {company.Iban}, reference {number}.");
                    });

                if (!string.IsNullOrWhiteSpace(invoice.Notes))
                    col.Item().Column(c =>
                    {
                        c.Item().Text("NOTES").FontSize(8).Bold().FontColor(Muted);
                        c.Item().Text(invoice.Notes);
                    });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span($"{number} · Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }

    private static void MetaRow(ColumnDescriptor c, string label, string value) =>
        c.Item().Row(r =>
        {
            r.RelativeItem().Text(label).FontColor(Muted);
            r.RelativeItem().AlignRight().Text(value).Bold();
        });

    private static void HeaderCell(IContainer cell, string text, bool right)
    {
        var box = cell.BorderBottom(1).BorderColor(Ink).PaddingVertical(5);
        (right ? box.AlignRight() : box).Text(text).Bold().FontSize(9);
    }

    private static IContainer BodyCell(IContainer cell) =>
        cell.BorderBottom(1).BorderColor(Line).PaddingVertical(6);

    private static void TotalRow(ColumnDescriptor c, string label, string value, bool strong) =>
        c.Item().PaddingVertical(2).Row(r =>
        {
            var l = r.RelativeItem().Text(label);
            var v = r.ConstantItem(110).AlignRight().Text(value);
            if (strong)
            {
                l.Bold().FontSize(12);
                v.Bold().FontSize(12);
            }
        });
}
