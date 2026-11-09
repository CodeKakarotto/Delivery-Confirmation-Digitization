using System.ComponentModel.DataAnnotations;
using Confirmations.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Confirmations.Api.Controllers;

/// <summary>
/// Sign in, sign out, and "who am I". Identity always comes from the
/// server-side login cookie, never from anything the browser sends.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    SignInManager<IdentityUser> signInManager,
    UserManager<IdentityUser> userManager) : ControllerBase
{
    private const string IncorrectLogin = "Incorrect email or password.";
    private const string LockedOut = "Too many wrong passwords. Please wait 5 minutes and try again.";
    private const string DriverRefused = "Driver accounts can't use the delivery confirmation app.";

    public sealed record LoginRequest([Required] string Email, [Required] string Password);
    public sealed record UserInfo(string Email, string Role);
    public sealed record ErrorMessage(string Message);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return Unauthorized(new ErrorMessage(IncorrectLogin));

        var check = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
            return Unauthorized(new ErrorMessage(LockedOut));
        if (!check.Succeeded)
            return Unauthorized(new ErrorMessage(IncorrectLogin));

        var role = await GetAppRoleAsync(user);
        if (role is null)
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorMessage(DriverRefused));

        await signInManager.SignInAsync(user, isPersistent: false);
        return Ok(new UserInfo(user.Email!, role));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await userManager.GetUserAsync(User);
        var role = user is null ? null : await GetAppRoleAsync(user);
        if (user is null || role is null)
            return Unauthorized();

        return Ok(new UserInfo(user.Email!, role));
    }

    /// <summary>Manager wins over Employee. Drivers (or no role) get null.</summary>
    private async Task<string?> GetAppRoleAsync(IdentityUser user)
    {
        if (await userManager.IsInRoleAsync(user, Roles.Manager)) return Roles.Manager;
        if (await userManager.IsInRoleAsync(user, Roles.Employee)) return Roles.Employee;
        return null;
    }
}
