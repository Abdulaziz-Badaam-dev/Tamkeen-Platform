using System.ComponentModel.DataAnnotations;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamkeen.API.Data;
using Tamkeen.API.Data.Entities;
using Tamkeen.API.Services.Auth;

namespace Tamkeen.API.Controllers;

public sealed record RegisterInput([Required, StringLength(160)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email, [Required, StringLength(256)] string Password);
public sealed record LoginInput([Required, EmailAddress, StringLength(256)] string Email, [Required, StringLength(256)] string Password);
public sealed record GoogleInput([Required, StringLength(8192)] string IdToken);

[ApiController, Route("api/auth")]
public sealed class AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    JwtTokens tokens, IOptions<GoogleJsonWebSignature.ValidationSettings> googleSettings, TamkeenDb db) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        var email = input.Email.Trim();
        var user = new ApplicationUser { FullName = input.FullName.Trim(), Email = email, UserName = email };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        return Ok(tokens.Issue(user));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginInput input)
    {
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user == null || !(await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true)).Succeeded)
            return Unauthorized();
        return Ok(tokens.Issue(user));
    }

    [HttpPost("google")]
    public async Task<IActionResult> Google(GoogleInput input)
    {
        var settings = googleSettings.Value;
        if (!settings.Audience.Any()) return Problem(statusCode: 503, detail: "Configure the Google client ID.");
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(input.IdToken, settings);
        }
        catch (InvalidJwtException) { return Unauthorized(); }
        catch (HttpRequestException) { return Problem(statusCode: 503, detail: "Google verification is unavailable."); }
        if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.Subject))
            return Unauthorized();

        var user = await db.Users.SingleOrDefaultAsync(u => u.GoogleSubjectId == payload.Subject);
        if (user == null)
        {
            user = await users.FindByEmailAsync(payload.Email);
            if (user == null)
            {
                user = new ApplicationUser { FullName = payload.Name ?? payload.Email, UserName = payload.Email, Email = payload.Email,
                    EmailConfirmed = true, GoogleSubjectId = payload.Subject };
                if (!(await users.CreateAsync(user)).Succeeded) return Conflict();
            }
            else
            {
                // Linking a password account requires proof of ownership of that account too.
                if (User.Identity?.IsAuthenticated != true || User.UserId() != user.Id || user.GoogleSubjectId != null)
                    return Problem(statusCode: 409, detail: "Sign in to the matching account before linking Google.");
                user.GoogleSubjectId = payload.Subject;
                user.EmailConfirmed = true;
                if (!(await users.UpdateAsync(user)).Succeeded) return Conflict();
            }
        }
        if (await users.IsLockedOutAsync(user)) return Unauthorized();
        return Ok(tokens.Issue(user));
    }

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var id = User.UserId();
        var user = await users.FindByIdAsync(id);
        if (user == null) return Unauthorized();
        var enrollments = await db.Enrollments.Where(e => e.UserId == id)
            .Select(e => new { e.Id, e.TrackId, e.Status }).ToListAsync(ct);
        return Ok(new { user = JwtTokens.Profile(user), enrollments });
    }
}
