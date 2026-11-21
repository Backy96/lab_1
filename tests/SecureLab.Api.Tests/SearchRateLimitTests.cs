using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SecureLab.Api.Tests;

// REG-RL-01: quota пошуку окрема для кожного користувача, 429 після вичерпання, відновлення після вікна.
public sealed class SearchRateLimitTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private const string SearchUrl = "/api/incidents/search?q=USB";

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> host, string userName)
    {
        var password = Environment.GetEnvironmentVariable("SeedUsers__Password")
            ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        using var response = await client.PostAsync("/api/auth/login",
            System.Net.Http.Json.JsonContent.Create(new { userName, password }));
        response.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task REG_RL_01_SearchQuota_IsPerUser_Returns429_ThenRecoversAfterWindow()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:SearchPermitLimit"] = "3",
                    ["RateLimiting:SearchWindowSeconds"] = "2"
                })));
        using var alice = await LoginAsync(limited, "alice");
        using var bob = await LoginAsync(limited, "bob");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var allowed = await alice.GetAsync(SearchUrl);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var blocked = await alice.GetAsync(SearchUrl);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        // Інший partition: вичерпана quota alice не впливає на bob.
        using var otherUser = await bob.GetAsync(SearchUrl);
        Assert.Equal(HttpStatusCode.OK, otherUser.StatusCode);

        // Маршрут без policy search не обмежено.
        using var list = await alice.GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        using var recovered = await alice.GetAsync(SearchUrl);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }
}
