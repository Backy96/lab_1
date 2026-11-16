using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data.Entities;
using Microsoft.Net.Http.Headers;

namespace SecureLab.Api.Tests;

public sealed class AuthenticationHardeningTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string Password => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private async Task DeleteUserAsync(string userName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(userName);
        if (user is not null) await users.DeleteAsync(user);
    }

    [Fact]
    public async Task Register_DuplicateUserNameOrEmail_Returns409WithSameMessage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var userName = $"reg-{suffix}";
        var email = $"{userName}@example.test";
        var password = $"Pw-{Guid.NewGuid():N}!Aa1";
        using var client = factory.CreateClient();
        try
        {
            using var first = await client.PostAsJsonAsync("/api/auth/register",
                new { userName, email, displayName = "Test User", password });
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.DoesNotContain("password", await first.Content.ReadAsStringAsync(),
                StringComparison.OrdinalIgnoreCase);

            using var sameName = await client.PostAsJsonAsync("/api/auth/register",
                new { userName, email = $"other-{email}", displayName = "Test User", password });
            using var sameEmail = await client.PostAsJsonAsync("/api/auth/register",
                new { userName = $"other-{userName}", email, displayName = "Test User", password });

            Assert.Equal(HttpStatusCode.Conflict, sameName.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
            Assert.Equal(await sameName.Content.ReadAsStringAsync(),
                await sameEmail.Content.ReadAsStringAsync());
        }
        finally
        {
            await DeleteUserAsync(userName);
            await DeleteUserAsync($"other-{userName}");
        }
    }

       [Fact]
    public async Task LoginCookie_HasSecurityFlags_AndIsSessionOnly()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = Password });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values));
        var cookie = values!.Single(value =>
            value.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
        var attributes = cookie.Split(';').Skip(1)
            .Select(attribute => attribute.Trim().ToLowerInvariant()).ToList();

        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=lax", attributes);

        
        var parsed = SetCookieHeaderValue.Parse(cookie);
        Assert.False(parsed.Expires.HasValue,
            $"Сеансова cookie не повинна мати Expires, отримано {parsed.Expires:O}.");
        Assert.False(parsed.MaxAge.HasValue,
            $"Сеансова cookie не повинна мати Max-Age, отримано {parsed.MaxAge}.");
    }

    [Fact]
    public async Task LoginRateLimit_Returns429_ThenRecoversAfterWindow()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:LoginPermitLimit"] = "3",
                    ["RateLimiting:LoginWindowSeconds"] = "2"
                })));
        using var client = limited.CreateClient();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var failed = await client.PostAsJsonAsync("/api/auth/login",
                new { userName = "alice", password = $"Wrong-{Guid.NewGuid():N}" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        using var knownBlocked = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = $"Wrong-{Guid.NewGuid():N}" });
        using var unknownBlocked = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = $"nouser-{Guid.NewGuid():N}", password = $"Wrong-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.TooManyRequests, knownBlocked.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, unknownBlocked.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        using var recovered = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = Password });
        Assert.Equal(HttpStatusCode.NoContent, recovered.StatusCode);
    }
}