using System.ComponentModel.DataAnnotations;
using InvoiceHub.Web.Domain;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InvoiceHub.Web.ViewModels;

public class ClientForm
{
    [Required, StringLength(120)]
    public string Name { get; set; } = "";

    [EmailAddress, StringLength(200)]
    public string? Email { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [Display(Name = "Tax ID"), StringLength(40)]
    public string? TaxId { get; set; }
}

public class CompanyForm
{
    [Required, Display(Name = "Company name"), StringLength(120)]
    public string Name { get; set; } = "";

    [StringLength(300)]
    public string? Address { get; set; }

    [Display(Name = "Tax ID"), StringLength(40)]
    public string? TaxId { get; set; }

    [EmailAddress, StringLength(200)]
    public string? Email { get; set; }

    [StringLength(40)]
    public string? Iban { get; set; }

    [Required, StringLength(3, MinimumLength = 3), Display(Name = "Currency")]
    public string Currency { get; set; } = "EUR";
}

public class LineForm
{
    [Required, StringLength(200)]
    public string Description { get; set; } = "";

    [Range(0.0001, 1_000_000)]
    public decimal Quantity { get; set; } = 1;

    [Display(Name = "Unit price"), Range(0, 1_000_000_000)]
    public decimal UnitPrice { get; set; }

    [Display(Name = "VAT %"), Range(0, 100)]
    public decimal VatRate { get; set; }
}

public class InvoiceForm
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Please choose a client."), Display(Name = "Client")]
    public int? ClientId { get; set; }

    [Required, Display(Name = "Issue date")]
    public DateOnly? IssueDate { get; set; }

    [Required, Display(Name = "Due date")]
    public DateOnly? DueDate { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public List<LineForm> Lines { get; set; } = [];

    /// <summary>For the drop-down only; never posted back.</summary>
    public List<SelectListItem> Clients { get; set; } = [];
}

public class InvoiceListModel
{
    public required IReadOnlyList<Invoice> Invoices { get; init; }
    public string? Search { get; init; }
    public string? Status { get; init; }
}

public class LoginForm
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(100)]
    public string Password { get; set; } = "";

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }
}

public class RegisterForm
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(100)]
    public string Password { get; set; } = "";

    [DataType(DataType.Password), Display(Name = "Repeat password"), Compare(nameof(Password), ErrorMessage = "The two passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

public class ChangePasswordForm
{
    [Required, DataType(DataType.Password), Display(Name = "Current password"), StringLength(100)]
    public string CurrentPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "New password"), StringLength(100)]
    public string NewPassword { get; set; } = "";

    [DataType(DataType.Password), Display(Name = "Repeat new password"), Compare(nameof(NewPassword), ErrorMessage = "The two passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

