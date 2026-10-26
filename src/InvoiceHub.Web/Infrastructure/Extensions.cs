using System.Security.Claims;
using InvoiceHub.Web.Domain;

namespace InvoiceHub.Web.Infrastructure;

public static class Extensions
{
    /// <summary>The logged-in user's id. Every service call is scoped with it.</summary>
    public static string GetOwnerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("No signed-in user.");
}

/// <summary>How an invoice status looks on screen.</summary>
public static class StatusDisplay
{
    public static (string Text, string Css) Of(Invoice invoice, DateOnly today) => invoice.Status switch
    {
        InvoiceStatus.Draft => ("Draft", "secondary"),
        InvoiceStatus.Paid => ("Paid", "success"),
        _ when invoice.IsOverdue(today) => ("Overdue", "danger"),
        _ => ("Sent", "primary"),
    };

    public static string Money(decimal amount, string currency) =>
        $"{amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-US"))} {currency}";
}
