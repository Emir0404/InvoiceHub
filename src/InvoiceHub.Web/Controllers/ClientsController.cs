using InvoiceHub.Web.Domain;
using InvoiceHub.Web.Infrastructure;
using InvoiceHub.Web.Services;
using InvoiceHub.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Web.Controllers;

[Authorize]
public class ClientsController(ClientService clients) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await clients.ListAsync(User.GetOwnerId(), ct));

    public IActionResult Create() => View(new ClientForm());

    [HttpPost]
    public async Task<IActionResult> Create(ClientForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(form);

        await clients.CreateAsync(User.GetOwnerId(), ToInput(form), ct);
        TempData["Success"] = $"Client \"{form.Name.Trim()}\" added.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var client = await clients.GetAsync(User.GetOwnerId(), id, ct);
        if (client is null) return NotFound();

        ViewBag.ClientId = id;
        return View(new ClientForm { Name = client.Name, Email = client.Email, Address = client.Address, TaxId = client.TaxId });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ClientForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.ClientId = id;
            return View(form);
        }

        var updated = await clients.UpdateAsync(User.GetOwnerId(), id, ToInput(form), ct);
        if (updated is null) return NotFound();

        TempData["Success"] = "Client saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        try
        {
            if (!await clients.DeleteAsync(User.GetOwnerId(), id, ct)) return NotFound();
            TempData["Success"] = "Client deleted.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    private static ClientInput ToInput(ClientForm form) => new(form.Name, form.Email, form.Address, form.TaxId);
}
