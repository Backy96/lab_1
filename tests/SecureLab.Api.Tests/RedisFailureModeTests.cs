using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SecureLab.Api.Tests;

// REG-RL-03: якщо спільне сховище лічильника недоступне, пошук відхиляється (fail-closed),
// а маршрути без search rate limiting продовжують працювати.
public sealed class RedisFailureModeTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task REG_RL_03_UnreachableRedis_SearchReturns503_OtherRoutesWork()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Порт, на якому Redis свідомо не запущено.
                    ["Redis:ConnectionString"] = "127.0.0.1:6399,connectTimeout=500"
                })));
        var password = Environment.GetEnvironmentVariable("SeedUsers__Password")
            ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        using var search = await client.GetAsync("/api/incidents/search?q=USB");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, search.StatusCode);
        Assert.Equal("application/problem+json", search.Content.Headers.ContentType?.MediaType);
        var body = await search.Content.ReadAsStringAsync();
        Assert.DoesNotContain("6399", body);
        Assert.DoesNotContain("Redis", body, StringComparison.OrdinalIgnoreCase);

        using var list = await client.GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }
}
