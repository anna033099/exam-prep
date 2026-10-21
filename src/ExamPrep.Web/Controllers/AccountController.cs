using ExamPrep.Web.Models;
using ExamPrep.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ExamPrep.Web.Controllers;

public class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : Controller
{
    [HttpGet]
    public IActionResult Register(string? returnUrl = null) => View(new RegisterVm { ReturnUrl = returnUrl });

    [HttpPost]
    public async Task<IActionResult> Register(RegisterVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = new ApplicationUser { UserName = vm.Email, Email = vm.Email, DisplayName = vm.DisplayName.Trim() };
        var result = await users.CreateAsync(user, vm.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }
        await signIn.SignInAsync(user, isPersistent: true);
        return LocalRedirect(SafeReturn(vm.ReturnUrl));
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginVm { ReturnUrl = returnUrl });

    [HttpPost]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var result = await signIn.PasswordSignInAsync(vm.Email, vm.Password, vm.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return LocalRedirect(SafeReturn(vm.ReturnUrl));
        ModelState.AddModelError("", result.IsLockedOut ? "登入失敗次數過多，帳號暫時鎖定，請稍後再試。" : "帳號或密碼錯誤。");
        return View(vm);
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    public IActionResult Denied() => View();

    private string SafeReturn(string? url) => !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? url : "/";
}
