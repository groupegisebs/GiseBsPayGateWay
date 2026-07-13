using System.Security.Claims;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiseBsPayGateway.Pages.Admin.Disbursements;

public class IndexModel(IDisbursementQueueService queue) : PageModel
{
    public IReadOnlyList<SellerDisbursementRequest> Items { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await queue.ListPendingAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostReconcileAsync(Guid id, string? notes, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "admin";
        var (ok, error) = await queue.MarkReconciledAsync(id, userId, notes, cancellationToken);
        TempData[ok ? "Success" : "Error"] = ok ? "Rapprochement validé — prêt à payer." : error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPayAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "admin";
        var (ok, error) = await queue.ApproveAndPayAsync(id, userId, cancellationToken);
        TempData[ok ? "Success" : "Error"] = ok ? "Paiement exécuté / marqué payé." : error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "admin";
        var (ok, error) = await queue.RejectAsync(id, userId, reason ?? "rejected", cancellationToken);
        TempData[ok ? "Success" : "Error"] = ok ? "Demande refusée." : error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostHoldAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "admin";
        var (ok, error) = await queue.HoldAsync(id, userId, reason ?? "hold", cancellationToken);
        TempData[ok ? "Success" : "Error"] = ok ? "Demande mise en attente." : error;
        return RedirectToPage();
    }
}
