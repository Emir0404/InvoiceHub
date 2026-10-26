using InvoiceHub.Web.Domain;
using InvoiceHub.Web.Infrastructure;
using InvoiceHub.Web.Pdf;
using InvoiceHub.Web.Services;
using InvoiceHub.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InvoiceHub.Web.Controllers;

[Authorize]
public class InvoicesController(
    InvoiceService invoices, ClientService clients, CompanyProfileService company, TimeProvider clock) : Controller
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<IActionResult> Index(string? search, string? status, CancellationToken ct) =>
        View(new InvoiceListModel
        {
            Invoices = await invoices.ListAsync(User.GetOwnerId(), new InvoiceFilter(search, status), ct),
            Search = search,
            Status = status,
        });

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var invoice = await invoices.GetAsync(User.GetOwnerId(), id, ct);
        return invoice is null ? NotFound() : View(invoice);
    }

    public async Task<IActionResult> Create(int? clientId, CancellationToken ct)
    {
        var form = new InvoiceForm
        {
            ClientId = clientId,
            IssueDate = Today,
            DueDate = Today.AddDays(14),
            Lines = [new LineForm()],
        };
        await FillClientsAsync(form, ct);
        return View("Form", form);
    }

    [HttpPost]
    public async Task<IActionResult> Create(InvoiceForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var invoice = await invoices.CreateDraftAsync(User.GetOwnerId(), ToInput(form), ct);
                TempData["Success"] = "Draft saved. Review it, then send it to give it a number.";
                return RedirectToAction(nameof(Details), new { id = invoice.Id });
            }
            catch (DomainException ex)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        await FillClientsAsync(form, ct);
        return View("Form", form);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var invoice = await invoices.GetAsync(User.GetOwnerId(), id, ct);
        if (invoice is null) return NotFound();
        if (!invoice.IsEditable)
        {
            TempData["Error"] = "Only drafts can be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var form = new InvoiceForm
        {
            Id = invoice.Id,
            ClientId = invoice.ClientId,
            IssueDate = invoice.IssueDate,
            DueDate = invoice.DueDate,
            Notes = invoice.Notes,
            Lines = invoice.Lines.Select(l => new LineForm
            {
                Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice, VatRate = l.VatRate,
            }).ToList(),
        };
        await FillClientsAsync(form, ct);
        return View("Form", form);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, InvoiceForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var updated = await invoices.UpdateDraftAsync(User.GetOwnerId(), id, ToInput(form), ct);
                if (updated is null) return NotFound();

                TempData["Success"] = "Draft saved.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (DomainException ex)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        form.Id = id;
        await FillClientsAsync(form, ct);
        return View("Form", form);
    }

    [HttpPost]
    public Task<IActionResult> Send(int id, CancellationToken ct) =>
        Transition(id, () => invoices.SendAsync(User.GetOwnerId(), id, ct), "Invoice sent and numbered.");

    [HttpPost]
    public Task<IActionResult> MarkPaid(int id, DateOnly? paidOn, CancellationToken ct) =>
        Transition(id, () => invoices.MarkPaidAsync(User.GetOwnerId(), id, paidOn, ct), "Invoice marked as paid.");

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        try
        {
            if (!await invoices.DeleteDraftAsync(User.GetOwnerId(), id, ct)) return NotFound();
            TempData["Success"] = "Draft deleted.";
            return RedirectToAction(nameof(Index));
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    public async Task<IActionResult> Pdf(int id, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var invoice = await invoices.GetAsync(ownerId, id, ct);
        if (invoice is null) return NotFound();

        var bytes = InvoicePdf.Render(invoice, await company.FindAsync(ownerId, ct));
        var name = $"invoice-{invoice.Number ?? "draft-" + invoice.Id}.pdf";
        return File(bytes, "application/pdf", name);
    }

    private async Task<IActionResult> Transition(int id, Func<Task<Invoice?>> action, string success)
    {
        try
        {
            if (await action() is null) return NotFound();
            TempData["Success"] = success;
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task FillClientsAsync(InvoiceForm form, CancellationToken ct) =>
        form.Clients = (await clients.ListAsync(User.GetOwnerId(), ct))
            .Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == form.ClientId))
            .ToList();

    private static InvoiceInput ToInput(InvoiceForm form) => new(
        form.ClientId!.Value, form.IssueDate!.Value, form.DueDate!.Value, form.Notes,
        form.Lines.Select(l => new LineInput(l.Description, l.Quantity, l.UnitPrice, l.VatRate)).ToList());
}
