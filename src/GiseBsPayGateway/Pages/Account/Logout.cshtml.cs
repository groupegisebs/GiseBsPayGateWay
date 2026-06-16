using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiseBsPayGateway.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly SignInManager<AdminUser> _signInManager;
    private readonly IAuditService _auditService;

    public LogoutModel(SignInManager<AdminUser> signInManager, IAuditService auditService)
    {
        _signInManager = signInManager;
        _auditService = auditService;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userName = User.Identity?.Name;
        await _signInManager.SignOutAsync();
        await _auditService.LogAsync("AdminLogout", nameof(AdminUser), userName, true, userName: userName);
        return RedirectToPage("/Account/Login");
    }
}
