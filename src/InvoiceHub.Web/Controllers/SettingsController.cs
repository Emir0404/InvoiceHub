using InvoiceHub.Web.Domain;
using InvoiceHub.Web.Infrastructure;
using InvoiceHub.Web.Services;
using InvoiceHub.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Web.Controllers;

/// <summary>"My company": the details printed at the top of every invoice.</summary>
[Authorize]
public class SettingsController(CompanyProfileService company) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var profile = await company.FindAsync(User.GetOwnerId(), ct);
        return View(profile is null
            ? new CompanyForm()
            : new CompanyForm
            {
                Name = profile.Name, Address = profile.Address, TaxId = profile.TaxId,
                Email = profile.Email, Iban = profile.Iban, Currency = profile.Currency,
            });
    }

    [HttpPost]
    public async Task<IActionResult> Index(CompanyForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(form);

        try
        {
            await company.SaveAsync(User.GetOwnerId(), new CompanyInput(
                form.Name, form.Address, form.TaxId, form.Email, form.Iban, form.Currency), ct);
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(nameof(form.Currency), ex.Message);
            return View(form);
        }

        TempData["Success"] = "Company details saved. New invoices will use them.";
        return RedirectToAction(nameof(Index));
    }
}
