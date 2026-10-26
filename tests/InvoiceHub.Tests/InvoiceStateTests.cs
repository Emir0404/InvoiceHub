using InvoiceHub.Web.Domain;

namespace InvoiceHub.Tests;

public class InvoiceStateTests
{
    private static readonly DateOnly Issued = new(2026, 10, 1);

    private static Invoice Draft(int lines = 1, int termDays = 14) => new()
    {
        IssueDate = Issued,
        DueDate = Issued.AddDays(termDays),
        Lines = Enumerable.Range(0, lines).Select(_ => new InvoiceLine { Description = "x", Quantity = 1, UnitPrice = 10 }).ToList(),
    };

    [Fact]
    public void A_new_invoice_is_an_editable_draft_without_a_number()
    {
        var invoice = Draft();

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.True(invoice.IsEditable);
        Assert.Null(invoice.Number);
    }

    [Fact]
    public void Sending_assigns_the_number_and_locks_the_invoice()
    {
        var invoice = Draft();

        invoice.Send("2026-0001");

        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal("2026-0001", invoice.Number);
        Assert.False(invoice.IsEditable);
    }

    [Fact]
    public void An_invoice_without_lines_cannot_be_sent()
    {
        var ex = Assert.Throws<DomainException>(() => Draft(lines: 0).Send("2026-0001"));

        Assert.Contains("at least one line", ex.Message);
    }

    [Fact]
    public void An_invoice_due_before_it_was_issued_cannot_be_sent()
    {
        Assert.Throws<DomainException>(() => Draft(termDays: -1).Send("2026-0001"));
    }

    [Fact]
    public void An_invoice_cannot_be_sent_twice()
    {
        var invoice = Draft();
        invoice.Send("2026-0001");

        Assert.Throws<DomainException>(() => invoice.Send("2026-0002"));
        Assert.Equal("2026-0001", invoice.Number);
    }

    [Fact]
    public void Marking_paid_records_the_payment_date()
    {
        var invoice = Draft();
        invoice.Send("2026-0001");

        invoice.MarkPaid(new DateOnly(2026, 10, 9));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(new DateOnly(2026, 10, 9), invoice.PaidOn);
    }

    [Fact]
    public void A_draft_cannot_be_marked_paid()
    {
        Assert.Throws<DomainException>(() => Draft().MarkPaid(Issued));
    }

    [Fact]
    public void A_paid_invoice_cannot_be_paid_again()
    {
        var invoice = Draft();
        invoice.Send("2026-0001");
        invoice.MarkPaid(Issued);

        Assert.Throws<DomainException>(() => invoice.MarkPaid(Issued.AddDays(1)));
        Assert.Equal(Issued, invoice.PaidOn);
    }

    [Fact]
    public void Payment_cannot_be_dated_before_the_invoice()
    {
        var invoice = Draft();
        invoice.Send("2026-0001");

        Assert.Throws<DomainException>(() => invoice.MarkPaid(Issued.AddDays(-1)));
    }

    [Theory]
    [InlineData(-1, true)]  // yesterday was the due date
    [InlineData(0, false)]  // due today is not overdue yet
    [InlineData(5, false)]
    public void A_sent_invoice_is_overdue_only_after_its_due_date(int daysUntilDue, bool expected)
    {
        var today = new DateOnly(2026, 10, 20);
        var invoice = Draft();
        invoice.DueDate = today.AddDays(daysUntilDue);
        invoice.Send("2026-0001");

        Assert.Equal(expected, invoice.IsOverdue(today));
    }

    [Fact]
    public void Drafts_and_paid_invoices_are_never_overdue()
    {
        var farFuture = new DateOnly(2030, 1, 1);
        var draft = Draft();
        var paid = Draft();
        paid.Send("2026-0001");
        paid.MarkPaid(Issued);

        Assert.False(draft.IsOverdue(farFuture));
        Assert.False(paid.IsOverdue(farFuture));
    }
}
