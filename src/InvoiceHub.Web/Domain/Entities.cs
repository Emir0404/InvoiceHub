namespace InvoiceHub.Web.Domain;

public enum InvoiceStatus
{
    Draft = 0,
    Sent = 1,
    Paid = 2,
}

/// <summary>The business that issues the invoices (one per user). Printed on every PDF.</summary>
public class CompanyProfile
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? TaxId { get; set; }
    public string? Email { get; set; }
    public string? Iban { get; set; }

    /// <summary>ISO 4217 code copied onto every new invoice.</summary>
    public string Currency { get; set; } = "EUR";
}

public class Client
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxId { get; set; }

    public List<Invoice> Invoices { get; set; } = [];
}

public class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>VAT in percent, e.g. 17 for 17 %.</summary>
    public decimal VatRate { get; set; }
}

public class Invoice
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    /// <summary>Assigned when the invoice is sent (2026-0001), never for drafts, so numbers have no gaps.</summary>
    public string? Number { get; set; }

    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateOnly? PaidOn { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? Notes { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];

    public bool IsEditable => Status == InvoiceStatus.Draft;

    /// <summary>"Overdue" is not stored: it is simply a sent invoice whose due date has passed.</summary>
    public bool IsOverdue(DateOnly today) => Status == InvoiceStatus.Sent && DueDate < today;

    public InvoiceTotals Totals => InvoiceCalculator.Calculate(Lines);

    public void Send(string number)
    {
        if (Status != InvoiceStatus.Draft)
            throw new DomainException("Only a draft can be sent.");
        if (Lines.Count == 0)
            throw new DomainException("Add at least one line before sending the invoice.");
        if (DueDate < IssueDate)
            throw new DomainException("The due date cannot be before the issue date.");

        Number = number;
        Status = InvoiceStatus.Sent;
    }

    public void MarkPaid(DateOnly paidOn)
    {
        if (Status != InvoiceStatus.Sent)
            throw new DomainException("Only a sent invoice can be marked as paid.");
        if (paidOn < IssueDate)
            throw new DomainException("The payment date cannot be before the issue date.");

        PaidOn = paidOn;
        Status = InvoiceStatus.Paid;
    }
}
