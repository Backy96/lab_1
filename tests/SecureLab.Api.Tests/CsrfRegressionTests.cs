using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Security;

namespace SecureLab.Api.Tests;

// REG-CSRF-01: вхід і cookie-сеанс -> відмова без токена -> незмінний стан
// -> видача токена -> дозволена зміна -> змінений стан.
public sealed class CsrfRegressionTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private sealed record IncidentView(Guid Id, string Title, string Description);

    private static object UpdateBody(string title) => new
    {
        title,
        description = "Оновлений опис для автоматичної регресії CSRF у ЛР 6.",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30)
    };

    private static object CreateBody(string title) => new
    {
        title,
        description = "Початковий опис для автоматичної регресії CSRF у ЛР 6.",
        severity = "Low",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    private async Task<Guid> CreateOwnIncidentAsync(string userName, string title)
    {
        using var creator = await factory.LoginWithCsrfAsync(userName);
        using var response = await creator.PostAsJsonAsync("/api/incidents", CreateBody(title));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private async Task CleanupAsync(params string[] titlePrefixes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        foreach (var prefix in titlePrefixes)
            await db.Incidents.Where(item => item.Title.StartsWith(prefix)).ExecuteDeleteAsync();
    }

    private static async Task AssertAntiforgeryRejectionAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Antiforgery token is missing or invalid.", document.RootElement.GetProperty("title").GetString());
        // Відмова antiforgery – не помилка validation DTO.
        Assert.False(document.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task REG_CSRF_01_PutWithoutToken_IsRejected_StateUnchanged_ThenValidPairSucceeds()
    {
        var prefix = $"CSRF01-{Guid.NewGuid():N}";
        var originalTitle = prefix + "-original";
        var id = await CreateOwnIncidentAsync("alice", originalTitle);
        try
        {
            // Вхід і cookie-сеанс без request token.
            using var alice = await factory.LoginAsync("alice");
            var url = $"/api/incidents/{id}";

            using var rejected = await alice.PutAsJsonAsync(url, UpdateBody(prefix + "-without-token"));
            await AssertAntiforgeryRejectionAsync(rejected);
            Assert.Equal(originalTitle, (await alice.GetFromJsonAsync<IncidentView>(url))!.Title);

            using var invalidRequest = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = JsonContent.Create(UpdateBody(prefix + "-invalid-token"))
            };
            invalidRequest.Headers.Add(AntiforgeryProtection.HeaderName, "invalid-test-value");
            using var invalid = await alice.SendAsync(invalidRequest);
            await AssertAntiforgeryRejectionAsync(invalid);
            Assert.Equal(originalTitle, (await alice.GetFromJsonAsync<IncidentView>(url))!.Title);

            // Видача токена тим самим клієнтом і дозволена зміна.
            await alice.UseAntiforgeryTokenAsync();
            var newTitle = prefix + "-changed";
            using var accepted = await alice.PutAsJsonAsync(url, UpdateBody(newTitle));
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            Assert.Equal(newTitle, (await alice.GetFromJsonAsync<IncidentView>(url))!.Title);
        }
        finally
        {
            await CleanupAsync(prefix);
        }
    }

    [Fact]
    public async Task REG_CSRF_02_AllCookieMutations_WithoutToken_AreRejected_AndStateIsUnchanged()
    {
        var prefix = $"CSRF02-{Guid.NewGuid():N}";
        var title = prefix + "-existing";
        var id = await CreateOwnIncidentAsync("alice", title);
        try
        {
            using var alice = await factory.LoginAsync("alice");
            using var morgan = await factory.LoginAsync("morgan");
            var url = $"/api/incidents/{id}";

            using var create = await alice.PostAsJsonAsync("/api/incidents", CreateBody(prefix + "-new"));
            await AssertAntiforgeryRejectionAsync(create);

            using var delete = await alice.DeleteAsync(url);
            await AssertAntiforgeryRejectionAsync(delete);

            using var triage = await morgan.PostAsync($"{url}/triage", null);
            await AssertAntiforgeryRejectionAsync(triage);

            using var logout = await alice.PostAsync("/api/auth/logout", null);
            await AssertAntiforgeryRejectionAsync(logout);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var incident = await db.Incidents.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal(title, incident.Title);
            Assert.Equal(IncidentStatus.New, incident.Status);
            Assert.False(await db.Incidents.AnyAsync(item => item.Title == prefix + "-new"));

            using var me = await alice.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally
        {
            await CleanupAsync(prefix);
        }
    }

    [Fact]
    public async Task REG_CSRF_03_TokenOfAlice_IsRejected_AfterLoginAsBob_NewTokenWorks()
    {
        var prefix = $"CSRF03-{Guid.NewGuid():N}";
        var bobIncidentId = await CreateOwnIncidentAsync("bob", prefix + "-bob");
        try
        {
            var password = Environment.GetEnvironmentVariable("SeedUsers__Password")
                ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

            using var client = await factory.LoginWithCsrfAsync("alice");
            using var logout = await client.PostAsync("/api/auth/logout", null);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

            // Той самий браузер, новий principal; header ще містить токен alice.
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName = "bob", password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            var url = $"/api/incidents/{bobIncidentId}";
            using var stale = await client.PutAsJsonAsync(url, UpdateBody(prefix + "-stale"));
            await AssertAntiforgeryRejectionAsync(stale);
            Assert.Equal(prefix + "-bob", (await client.GetFromJsonAsync<IncidentView>(url))!.Title);

            await client.UseAntiforgeryTokenAsync();
            using var accepted = await client.PutAsJsonAsync(url, UpdateBody(prefix + "-bob-changed"));
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            Assert.Equal(prefix + "-bob-changed", (await client.GetFromJsonAsync<IncidentView>(url))!.Title);
        }
        finally
        {
            await CleanupAsync(prefix);
        }
    }
}
