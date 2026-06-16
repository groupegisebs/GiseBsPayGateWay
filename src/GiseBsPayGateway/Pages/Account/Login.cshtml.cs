using System.ComponentModel.DataAnnotations;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Options;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<AdminUser> _signInManager;
    private readonly IAuditService _auditService;
    private readonly IWebHostEnvironment _environment;
    private readonly SeedOptions _seedOptions;

    public LoginModel(
        SignInManager<AdminUser> signInManager,
        IAuditService auditService,
        IWebHostEnvironment environment,
        IOptions<SeedOptions> seedOptions)
    {
        _signInManager = signInManager;
        _auditService = auditService;
        _environment = environment;
        _seedOptions = seedOptions.Value;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; private set; }
    public bool ShowTestCredentials { get; private set; }
    public string TestEmail { get; private set; } = string.Empty;
    public string TestPassword { get; private set; } = string.Empty;

    public class InputModel
    {
        [Required, EmailAddress, Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Display(Name = "Mot de passe")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Se souvenir de moi")]
        public bool RememberMe { get; set; }
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/Admin");
        ShowTestCredentials = _environment.IsDevelopment();
        TestEmail = _seedOptions.TestEmail;
        TestPassword = _seedOptions.TestPassword;

        if (_environment.IsDevelopment())
        {
            Input.Email = _seedOptions.TestEmail;
        }
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/Admin");
        ShowTestCredentials = _environment.IsDevelopment();
        TestEmail = _seedOptions.TestEmail;
        TestPassword = _seedOptions.TestPassword;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await _auditService.LogAsync("AdminLogin", nameof(AdminUser), Input.Email, true, userName: Input.Email);
            return LocalRedirect(ReturnUrl);
        }

        await _auditService.LogAsync("AdminLoginFailed", nameof(AdminUser), Input.Email, false, userName: Input.Email);
        ModelState.AddModelError(string.Empty, "Identifiants invalides.");
        return Page();
    }
}
