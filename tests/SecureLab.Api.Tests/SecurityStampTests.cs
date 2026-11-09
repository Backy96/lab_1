using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class SecurityStampTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static HttpClient CookieClientFor(WebApplicationFactory<Program> host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task LoginAsync(HttpClient client, string userName, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task DeleteUserAsync(string userName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(userName);
        if (user is not null) await users.DeleteAsync(user);
    }

    [Fact]
    public async Task PasswordChange_RevokesOldCookie_OnceStampIsChecked()
    {
        var userName = $"stamp-{Guid.NewGuid():N}"[..24];
        var oldPassword = $"Pw-{Guid.NewGuid():N}!Aa1";
        var newPassword = $"Pw-{Guid.NewGuid():N}!Bb2";
        await using var strict = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:SecurityStampValidationSeconds"] = "0"
                })));
        try
        {
            using (var registerClient = factory.CreateClient())
            {
                using var register = await registerClient.PostAsJsonAsync("/api/auth/register", new
                {
                    userName,
                    email = $"{userName}@example.test",
                    displayName = "Stamp Test",
                    password = oldPassword
                });
                Assert.Equal(HttpStatusCode.Created, register.StatusCode);
            }

            using var lenient = CookieClientFor(factory);
            using var strictClient = CookieClientFor(strict);
            await LoginAsync(lenient, userName, oldPassword);
            await LoginAsync(strictClient, userName, oldPassword);

            using var lenientBefore = await lenient.GetAsync("/api/me");
            using var strictBefore = await strictClient.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, lenientBefore.StatusCode);
            Assert.Equal(HttpStatusCode.OK, strictBefore.StatusCode);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await users.FindByNameAsync(userName);
                var change = await users.ChangePasswordAsync(user!, oldPassword, newPassword);
                Assert.True(change.Succeeded);
            }

            using var lenientAfter = await lenient.GetAsync("/api/me");
            using var strictAfter = await strictClient.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, lenientAfter.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, strictAfter.StatusCode);
        }
        finally
        {
            await DeleteUserAsync(userName);
        }
    }
}