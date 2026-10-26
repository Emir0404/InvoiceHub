using System.Diagnostics;
using InvoiceHub.Web.Infrastructure;
using InvoiceHub.Web.Models;
using InvoiceHub.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Web.Controllers;

public class HomeController(DashboardService dashboard, CompanyProfileService company) : Controller
{
    /// <summary>Visitors see the landing page, signed-in users see their dashboard.</summary>
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
            return View("Landing");

        var ownerId = User.GetOwnerId();
        ViewBag.NeedsCompanyProfile = await company.FindAsync(ownerId, ct) is null;
        return View(await dashboard.GetAsync(ownerId, ct));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? code)
    {
        if (code is not null)
            Response.StatusCode = code.Value;

        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier, Code = code });
    }
}
