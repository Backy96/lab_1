using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class IncidentInputTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static readonly Guid UsbId = Guid.Parse("20000000-0000-0000-0000-000000000005");
    private static readonly Guid OBrienId = Guid.Parse("20000000-0000-0000-0000-000000000004");

    private Task<HttpClient> LoginAliceAsync() => factory.LoginAsync("alice");

    private static object Body(string title, string description = "Штучний опис для автоматичного тесту.",
        string severity = "Low") => new
    {
        title,
        description,
        severity,
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    private async Task DeleteByTitleAsync(params string[] titles)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => titles.Contains(item.Title)).ExecuteDeleteAsync();
    }

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", json, StringComparison.Ordinal);
        return JsonDocument.Parse(json).RootElement.GetProperty("errors").Clone();
    }

    [Fact]
    public async Task Post_InvalidDto_Returns400WithFieldKeys()
    {
        using var alice = await LoginAliceAsync();
        try
        {
            using var response = await alice.PostAsJsonAsync("/api/incidents", new
            {
                title = "   ",
                description = "",
                severity = "7",
                occurredAtUtc = DateTimeOffset.UtcNow.AddYears(5)
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = await ErrorsAsync(response);
            Assert.True(errors.TryGetProperty("title", out _));
            Assert.True(errors.TryGetProperty("description", out _));
            Assert.True(errors.TryGetProperty("severity", out _));
            Assert.True(errors.TryGetProperty("occurredAtUtc", out _));
        }
        finally
        {
            await DeleteByTitleAsync("");
        }
    }

    [Fact]
    public async Task Post_DuplicateActiveTitle_Returns409()
    {
        using var alice = await LoginAliceAsync();
        var title = $"Regression-{Guid.NewGuid():N}";
        try
        {
            using var first = await alice.PostAsJsonAsync("/api/incidents", Body(title));
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            using var second = await alice.PostAsJsonAsync("/api/incidents", Body($"  {title}  "));
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
            var json = await second.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task Post_HighSeverityDescriptionLength_39Fails_40Passes()
    {
        using var alice = await LoginAliceAsync();
        var title39 = $"Len39-{Guid.NewGuid():N}";
        var title40 = $"Len40-{Guid.NewGuid():N}";
        try
        {
            using var short39 = await alice.PostAsJsonAsync("/api/incidents",
                Body(title39, new string('x', 39), "High"));
            Assert.Equal(HttpStatusCode.BadRequest, short39.StatusCode);
            var errors = await ErrorsAsync(short39);
            Assert.True(errors.TryGetProperty("description", out _));

            using var ok40 = await alice.PostAsJsonAsync("/api/incidents",
                Body(title40, new string('x', 40), "High"));
            Assert.Equal(HttpStatusCode.Created, ok40.StatusCode);
        }
        finally
        {
            await DeleteByTitleAsync(title39, title40);
        }
    }

    [Fact]
    public async Task Post_DescriptionEqualsTitle_Returns400_DifferentDescriptionReturns201()
    {
        using var alice = await LoginAliceAsync();
        var title = $"T10-{Guid.NewGuid():N}";
        try
        {
            using var invalid = await alice.PostAsJsonAsync("/api/incidents",
                Body(title, $"  {title.ToUpperInvariant()}  "));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var errors = await ErrorsAsync(invalid);
            Assert.True(errors.TryGetProperty("description", out _));

            using var valid = await alice.PostAsJsonAsync("/api/incidents",
                Body(title, "Окремий опис, який відрізняється від заголовка."));
            Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task Post_ExtraServerFields_AreIgnoredAndServerValuesUsed()
    {
        using var alice = await LoginAliceAsync();
        var title = $"Overposting-{Guid.NewGuid():N}";
        var sentId = Guid.NewGuid();
        try
        {
            using var response = await alice.PostAsJsonAsync("/api/incidents", new
            {
                title,
                description = "Спроба підмінити службові поля.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                id = sentId,
                ownerUserId = Guid.NewGuid(),
                status = "Closed",
                createdAtUtc = "2000-01-01T00:00:00Z"
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            var createdId = root.GetProperty("id").GetGuid();
            Assert.NotEqual(sentId, createdId);
            Assert.Equal("New", root.GetProperty("status").GetString());

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var saved = await db.Incidents.AsNoTracking().SingleAsync(item => item.Id == createdId);
            Assert.Equal(DbSeeder.AliceId, saved.OwnerUserId);
            Assert.True(saved.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5));
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task Search_UnknownSortBy_Returns400WithSortByKey()
    {
        using var alice = await LoginAliceAsync();
        using var response = await alice.GetAsync("/api/incidents/search?sortBy=unknown");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsAsync(response);
        Assert.True(errors.TryGetProperty("sortBy", out _));
    }

    [Fact]
    public async Task Search_InjectionInput_ReturnsEmptySet_AndUsbStillFound()
    {
        using var alice = await LoginAliceAsync();
        var injection = "zz-no-match' OR TRUE -- ";
        using var attack = await alice.GetAsync(
            "/api/incidents/search?q=" + Uri.EscapeDataString(injection));
        Assert.Equal(HttpStatusCode.OK, attack.StatusCode);
        var attackRows = await attack.Content.ReadFromJsonAsync<List<IncidentSearchItem>>();
        Assert.NotNull(attackRows);
        Assert.Empty(attackRows);

        var usbRows = await alice.GetFromJsonAsync<List<IncidentSearchItem>>(
            "/api/incidents/search?q=USB");
        Assert.NotNull(usbRows);
        var usb = Assert.Single(usbRows);
        Assert.Equal(UsbId, usb.Id);
    }

    // O'Brien належить Bob, тому пошук виконується від його імені.
    [Fact]
    public async Task Search_ApostropheInQuery_ReturnsMatchingRecord()
    {
        using var bob = await factory.LoginAsync("bob");
        var rows = await bob.GetFromJsonAsync<List<IncidentSearchItem>>(
            "/api/incidents/search?q=" + Uri.EscapeDataString("O'Brien"));

        Assert.NotNull(rows);
        var row = Assert.Single(rows);
        Assert.Equal(OBrienId, row.Id);
    }
}