using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Tamkeen.API.Tests;

public sealed partial class BackendCoreTests
{
    [Fact]
    public async Task GoogleVerifiesSignedIdTokenAudienceAndAccountOwnership()
    {
        using var rsa = RSA.Create(2048);
        using var factory = new CoreFactory();
        var key = new RsaSecurityKey(rsa) { KeyId = "test-google-key" };
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<GoogleJsonWebSignature.ValidationSettings>(options =>
            {
                options.Audience = ["tamkeen-client"];
                options.HttpClientFactory = new TestGoogleKeys(rsa.ExportParameters(false));
                options.ForceGoogleCertRefresh = true;
            })));
        using var client = app.CreateClient();
        Task<HttpResponseMessage> Google(string subject, string email, string audience = "tamkeen-client", bool verified = true)
        {
            var now = DateTime.UtcNow;
            var jwt = new JwtSecurityToken("https://accounts.google.com", audience,
                [new Claim("sub", subject), new Claim("email", email), new Claim("name", "حساب تجريبي"),
                 new Claim("email_verified", verified ? "true" : "false", ClaimValueTypes.Boolean),
                 new Claim("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
                now.AddSeconds(-2), now.AddMinutes(10), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            return client.PostAsJsonAsync("/api/auth/google", new { idToken = new JwtSecurityTokenHandler().WriteToken(jwt) });
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/google", new { idToken = "malformed" })).StatusCode);
        const string email = "google@example.test";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Google("subject-one", email, "another-client")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Google("subject-one", email, verified: false)).StatusCode);
        var session = await Read(await Google("subject-one", email));
        var again = await Read(await Google("subject-one", email));
        Assert.Equal(Text(session["user"]!, "id"), Text(again["user"]!, "id"));
        Authorize(client, again);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        const string existing = "password@example.test";
        await Register(client, existing);
        var credentials = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Conflict, (await Google("subject-two", existing)).StatusCode);
        client.DefaultRequestHeaders.Authorization = credentials;
        var linked = await Read(await Google("subject-two", existing));
        Assert.Equal(existing, Text(linked["user"]!, "email"));
    }

    private sealed class TestGoogleKeys(RSAParameters key) : Google.Apis.Http.HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(Google.Apis.Http.CreateHttpClientArgs args) => new KeysHandler(key);
        private sealed class KeysHandler(RSAParameters key) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { keys = new[] { new { kty = "RSA", alg = "RS256", use = "sig",
                        kid = "test-google-key", n = Base64UrlEncoder.Encode(key.Modulus!), e = Base64UrlEncoder.Encode(key.Exponent!) } } })
                });
        }
    }
}
