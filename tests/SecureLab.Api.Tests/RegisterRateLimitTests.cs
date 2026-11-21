using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace SecureLab.Api.Tests;

// REG-RL-02: реєстрація обмежена за частотою, тому перевірку існування імені через 409
// неможливо виконувати без обмеження; login має окрему quota.
public sealed class RegisterRateLimitTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task REG_RL_02_Register_Returns429AfterQuota_LoginUnaffected_RecoversAfterWindow()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:RegisterPermitLimit"] = "3",
                    ["RateLimiting:RegisterWindowSeconds"] = "2"
                })));
        using var client = limited.CreateClient();

        object ExistingName(int attempt) => new
        {
            userName = "alice",
            email = $"probe-{attempt}-{Guid.NewGuid():N}@example.test",
            displayName = "Probe",
            password = $"Study-{Guid.NewGuid():N}"
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var conflict = await client.PostAsJsonAsync("/api/auth/register", ExistingName(attempt));
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        }

        using var blocked = await client.PostAsJsonAsync("/api/auth/register", ExistingName(4));
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        // Окрема policy: вичерпана quota реєстрації не блокує вхід.
        var password = Environment.GetEnvironmentVariable("SeedUsers__Password")
            ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName = "bob", password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        using var recovered = await client.PostAsJsonAsync("/api/auth/register", ExistingName(5));
        Assert.Equal(HttpStatusCode.Conflict, recovered.StatusCode);
    }
}
