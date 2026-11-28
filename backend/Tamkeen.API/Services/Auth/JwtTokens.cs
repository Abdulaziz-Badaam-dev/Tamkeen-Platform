using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Tamkeen.API.Data.Entities;

namespace Tamkeen.API.Services.Auth;

public sealed class JwtTokens
{
    private readonly string issuer;
    private readonly string audience;
    private readonly SymmetricSecurityKey key;
    private readonly int expiryMinutes;

    public JwtTokens(IConfiguration config)
    {
        string Required(string name) => !string.IsNullOrWhiteSpace(config["Jwt:" + name])
            ? config["Jwt:" + name]! : throw new InvalidOperationException($"Configure Jwt:{name}.");
        issuer = Required("Issuer");
        audience = Required("Audience");
        var bytes = Encoding.UTF8.GetBytes(Required("Key"));
        if (bytes.Length < 32) throw new InvalidOperationException("Jwt:Key requires at least 32 UTF-8 bytes.");
        key = new SymmetricSecurityKey(bytes);
        expiryMinutes = config.GetValue<int>("Jwt:ExpiryMinutes");
        if (expiryMinutes <= 0) throw new InvalidOperationException("Configure a positive Jwt:ExpiryMinutes.");
    }

    public TokenValidationParameters Validation() => new()
    {
        ValidateIssuerSigningKey = true, IssuerSigningKey = key, ValidIssuer = issuer, ValidAudience = audience,
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };

    public object Issue(ApplicationUser user)
    {
        var expires = DateTime.UtcNow.AddMinutes(expiryMinutes);
        var token = new JwtSecurityToken(issuer, audience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            expires: expires, signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), expiresAt = expires, user = Profile(user) };
    }

    public static object Profile(ApplicationUser user) => new { user.Id, user.FullName, user.Email, user.CreatedAt };
}

public static class PrincipalExtensions
{
    public static string UserId(this ClaimsPrincipal principal) => principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Missing authenticated subject.");
}
