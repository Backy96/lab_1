using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class AuthenticationTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string Password => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private static object NewIncident(string title) => new
    {
        title,
        description = "Штучний опис для автоматичного тесту автентифікації.",
        severity = "Low",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    private async Task DeleteByTitleAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
    }

    private static string StableFields(string json)
    {
        using var document = JsonDocument.Parse(json);
        return string.Join("|", document.RootElement.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={property.Value}"));
    }

    [Fact]
    public async Task A01_AnonymousCreate_Returns401_AndNothingIsSaved()
    {
        var title = $"A01-{Guid.NewGuid():N}";
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        try
        {
            using var response = await client.PostAsJsonAsync("/api/incidents", NewIncident(title));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            Assert.False(await db.Incidents.AnyAsync(item => item.Title == title));
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task A02_LoginThenMe_ReturnsUserFromCookie()
    {
        using var client = await factory.LoginAsync("alice", Password);

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DbSeeder.AliceId, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("alice", document.RootElement.GetProperty("userName").GetString(),
            ignoreCase: true);
    }

    [Fact]
    public async Task A03_ForeignDemoHeader_DoesNotChangeCurrentUser()
    {
        using var alice = await factory.LoginAsync("alice", Password);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());

        using var response = await alice.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DbSeeder.AliceId, document.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A04_CreateWithoutOwner_OwnerIsAuthenticatedUser()
    {
        var title = $"A04-{Guid.NewGuid():N}";
        using var bob = await factory.LoginAsync("bob", Password);
        try
        {
            using var response = await bob.PostAsJsonAsync("/api/incidents", NewIncident(title));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var createdId = document.RootElement.GetProperty("id").GetGuid();

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var saved = await db.Incidents.AsNoTracking().SingleAsync(item => item.Id == createdId);
            Assert.Equal(DbSeeder.BobId, saved.OwnerUserId);
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task A05_ForgedOwnerInBody_OwnerStaysAuthenticatedUser()
    {
        var title = $"A05-{Guid.NewGuid():N}";
        using var bob = await factory.LoginAsync("bob", Password);
        try
        {
            using var response = await bob.PostAsJsonAsync("/api/incidents", new
            {
                title,
                description = "Спроба підмінити власника через тіло запиту.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                ownerUserId = DbSeeder.AliceId
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var createdId = document.RootElement.GetProperty("id").GetGuid();

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var saved = await db.Incidents.AsNoTracking().SingleAsync(item => item.Id == createdId);
            Assert.Equal(DbSeeder.BobId, saved.OwnerUserId);
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task A06_AfterLogout_MeReturns401()
    {
        using var client = await factory.LoginAsync("alice", Password);

        using var before = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var after = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task A07_UnknownUserAndWrongPassword_GiveSameExternalResult()
    {
        using var client = factory.CreateClient();
        using var unknown = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = $"nouser-{Guid.NewGuid():N}", password = $"Wrong-{Guid.NewGuid():N}" });
        using var wrong = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = $"Wrong-{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(unknown.Content.Headers.ContentType?.MediaType,
            wrong.Content.Headers.ContentType?.MediaType);

        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var wrongBody = await wrong.Content.ReadAsStringAsync();
        Assert.Equal(StableFields(unknownBody), StableFields(wrongBody));
        Assert.Contains("traceId", unknownBody, StringComparison.Ordinal);
        Assert.Contains("traceId", wrongBody, StringComparison.Ordinal);
    }
}