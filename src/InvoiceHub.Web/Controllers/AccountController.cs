using InvoiceHub.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Web.Controllers;

/// <summary>Sign up, sign in, sign out and change password. Our own pages instead of the generic scaffolded ones.</summary>
[Route("account")]
public class AccountController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) : Controller
{
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginForm());
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginForm form, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(form);

        // lockoutOnFailure: five wrong passwords in a row lock the account for a while.
        var result = await signIn.PasswordSignInAsync(form.Email, form.Password, form.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return LocalRedirectOrHome(returnUrl);

        // Same message for an unknown email and a wrong password, so nobody can probe which emails exist.
        ModelState.AddModelError("", result.IsLockedOut
            ? "Too many failed attempts. Please wait a few minutes and try again."
            : "The email or password is not correct.");
        return View(form);
    }

    [HttpGet("register")]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new RegisterForm());
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var user = new IdentityUser { UserName = form.Email, Email = form.Email };
        var result = await users.CreateAsync(user, form.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code.Contains("Password") ? nameof(form.Password) : nameof(form.Email), Friendly(error));
            return View(form);
        }

        await signIn.SignInAsync(user, isPersistent: false);
        TempData["Success"] = "Welcome to InvoiceHub! Start by adding your company details.";
        return RedirectToAction("Index", "Settings");
    }

    [Authorize]
    [HttpGet("password")]
    public IActionResult Password() => View(new ChangePasswordForm());

    [Authorize]
    [HttpPost("password")]
    public async Task<IActionResult> Password(ChangePasswordForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        var result = await users.ChangePasswordAsync(user, form.CurrentPassword, form.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code == "PasswordMismatch" ? nameof(form.CurrentPassword) : nameof(form.NewPassword), Friendly(error));
            return View(form);
        }

        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "Your password was changed.";
        return RedirectToAction(nameof(Password));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    private IActionResult LocalRedirectOrHome(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");

    private static string Friendly(IdentityError error) => error.Code switch
    {
        "DuplicateUserName" or "DuplicateEmail" => "An account with this email already exists.",
        "InvalidEmail" or "InvalidUserName" => "Please enter a valid email address.",
        "PasswordMismatch" => "The current password is not correct.",
        _ => error.Description,
    };
}
