using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class AuthorizationTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static readonly Guid BobSearchIncidentId = Guid.Parse("20000000-0000-0000-0000-000000000004");
    private static readonly Guid UsbIncidentId = Guid.Parse("20000000-0000-0000-0000-000000000005");
    private static readonly Guid UnknownId = Guid.Parse("20000000-0000-0000-0000-0000000000ff");
    private static readonly Guid CommentOfAliceId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid CommentOfBobId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid InternalCommentId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    private static object UpdateBody(string title, string description) => new
    {
        title,
        description,
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30)
    };

    private static async Task<Guid> CreateAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title,
            description = "Штучний опис для автоматичного тесту доступу.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Incident> LoadAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking()
            .Include(item => item.StatusHistory)
            .SingleAsync(item => item.Id == id);
    }

    private async Task<bool> ExistsAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AnyAsync(item => item.Id == id);
    }

    private async Task DeleteAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Id == id).ExecuteDeleteAsync();
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
    public async Task A01_Anonymous_Returns401_WithoutRedirect()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var incident = $"/api/incidents/{DbSeeder.AliceIncidentId}";
        var requests = new List<(HttpMethod Method, string Url)>
        {
            (HttpMethod.Get, "/api/incidents"),
            (HttpMethod.Get, incident),
            (HttpMethod.Get, "/api/incidents/severity-summary"),
            (HttpMethod.Get, "/api/incidents/search?q=USB"),
            (HttpMethod.Get, $"/api/incident-comments/{CommentOfAliceId}"),
            (HttpMethod.Put, incident),
            (HttpMethod.Delete, incident),
            (HttpMethod.Post, incident + "/triage")
        };

        foreach (var (method, url) in requests)
        {
            using var request = new HttpRequestMessage(method, url);
            if (method == HttpMethod.Put)
            {
                request.Content = JsonContent.Create(UpdateBody("A01 anonymous", "Анонімна спроба зміни."));
            }

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
        }

        Assert.True(await ExistsAsync(DbSeeder.AliceIncidentId));
    }

    [Fact]
    public async Task A02_Owner_CanReadAndUpdateOwnIncident()
    {
        using var alice = await factory.LoginWithCsrfAsync("alice");
        var id = await CreateAsync(alice, $"A02-{Guid.NewGuid():N}");
        try
        {
            using var read = await alice.GetAsync($"/api/incidents/{id}");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);

            var newTitle = $"A02-upd-{Guid.NewGuid():N}";
            using var update = await alice.PutAsJsonAsync(
                $"/api/incidents/{id}", UpdateBody(newTitle, "Оновлений опис власного інциденту."));
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

            var saved = await LoadAsync(id);
            Assert.Equal(newTitle, saved.Title);
            Assert.Equal(DbSeeder.AliceId, saved.OwnerUserId);
            Assert.Equal(IncidentStatus.New, saved.Status);
            Assert.Equal(IncidentSeverity.Low, saved.Severity);
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A03_ForeignRead_Returns404_WithoutForeignData()
    {
        var bob = await LoadAsync(DbSeeder.BobIncidentId);
        using var alice = await factory.LoginWithCsrfAsync("alice");

        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(bob.Title, body, StringComparison.Ordinal);
        Assert.DoesNotContain(bob.Description, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Боб", body, StringComparison.Ordinal);
        Assert.DoesNotContain(DbSeeder.BobIncidentId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A03_ForeignUpdate_Returns404_AndStateIsUnchanged()
    {
        var before = await LoadAsync(DbSeeder.BobIncidentId);
        using var alice = await factory.LoginWithCsrfAsync("alice");

        using var response = await alice.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}",
            UpdateBody("A03 changed by Alice", "Спроба зміни чужого інциденту від Alice."));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var after = await LoadAsync(DbSeeder.BobIncidentId);
        Assert.Equal(before.Title, after.Title);
        Assert.Equal(before.Description, after.Description);
        Assert.Equal(before.OccurredAtUtc, after.OccurredAtUtc);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
    }

    [Fact]
    public async Task A03R_Analyst_CanReadAnyIncident()
    {
        using var morgan = await factory.LoginWithCsrfAsync("morgan");

        using var alice = await morgan.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        using var bob = await morgan.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");

        Assert.Equal(HttpStatusCode.OK, alice.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bob.StatusCode);
    }

    [Fact]
    public async Task A04_ForeignDelete_Returns404_AndResourceRemains_OwnerDeleteSucceeds()
    {
        using var alice = await factory.LoginWithCsrfAsync("alice");
        using var bob = await factory.LoginWithCsrfAsync("bob");
        var id = await CreateAsync(bob, $"A04-{Guid.NewGuid():N}");
        try
        {
            using var foreign = await alice.DeleteAsync($"/api/incidents/{id}");
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            Assert.True(await ExistsAsync(id));

            using var own = await bob.DeleteAsync($"/api/incidents/{id}");
            Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
            Assert.False(await ExistsAsync(id));
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A06_ReporterTriage_Own403_Foreign404_StatusUnchanged()
    {
        using var alice = await factory.LoginWithCsrfAsync("alice");
        var id = await CreateAsync(alice, $"A06-{Guid.NewGuid():N}");
        var bobBefore = await LoadAsync(DbSeeder.BobIncidentId);
        try
        {
            using var own = await alice.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
            var ownAfter = await LoadAsync(id);
            Assert.Equal(IncidentStatus.New, ownAfter.Status);
            Assert.Empty(ownAfter.StatusHistory);

            using var foreign = await alice.PostAsync($"/api/incidents/{DbSeeder.BobIncidentId}/triage", null);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            var bobAfter = await LoadAsync(DbSeeder.BobIncidentId);
            Assert.Equal(bobBefore.Status, bobAfter.Status);
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A07_AnalystTriage_NewIncident_Returns204_RecordsHistory_ThenConflict()
    {
        using var alice = await factory.LoginWithCsrfAsync("alice");
        using var morgan = await factory.LoginWithCsrfAsync("morgan");
        var id = await CreateAsync(alice, $"A07-{Guid.NewGuid():N}");
        try
        {
            using var first = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

            var saved = await LoadAsync(id);
            Assert.Equal(IncidentStatus.Triaged, saved.Status);
            var entry = Assert.Single(saved.StatusHistory);
            Assert.Equal(IncidentStatus.New, entry.OldStatus);
            Assert.Equal(IncidentStatus.Triaged, entry.NewStatus);
            Assert.Equal(DbSeeder.MorganId, entry.ChangedByUserId);

            using var second = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            var afterSecond = await LoadAsync(id);
            Assert.Equal(IncidentStatus.Triaged, afterSecond.Status);
            Assert.Single(afterSecond.StatusHistory);
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A08a_ForgedOwnerRoleStatusSeverity_AreIgnored()
    {
        using var alice = await factory.LoginWithCsrfAsync("alice");
        var id = await CreateAsync(alice, $"A08-{Guid.NewGuid():N}");
        try
        {
            var newTitle = $"A08-upd-{Guid.NewGuid():N}";
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}", new
            {
                title = newTitle,
                description = "Опис із підробленими службовими полями.",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                id = Guid.NewGuid(),
                ownerUserId = DbSeeder.BobId,
                role = "Administrator",
                status = "Closed",
                severity = "Critical"
            });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var saved = await LoadAsync(id);
            Assert.Equal(newTitle, saved.Title);
            Assert.Equal(id, saved.Id);
            Assert.Equal(DbSeeder.AliceId, saved.OwnerUserId);
            Assert.Equal(IncidentStatus.New, saved.Status);
            Assert.Equal(IncidentSeverity.Low, saved.Severity);
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A09_DeniedTriage_WritesAuditEvent_WithMatchingTraceId_AndNoSecrets()
    {
        using var alice = await factory.LoginWithCsrfAsync("alice");
        var id = await CreateAsync(alice, $"A09-{Guid.NewGuid():N}");
        try
        {
            using var response = await alice.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var traceId = document.RootElement.GetProperty("traceId").GetString();
            Assert.False(string.IsNullOrEmpty(traceId));

            var entry = factory.Logs.Entries.Last(item =>
                item.Contains("authorization.denied", StringComparison.Ordinal)
                && item.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));

            Assert.Contains($"subject={DbSeeder.AliceId}", entry, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("action=incident.triage", entry, StringComparison.Ordinal);
            Assert.Contains("result=denied", entry, StringComparison.Ordinal);
            Assert.Contains("occurredAtUtc=", entry, StringComparison.Ordinal);
            Assert.Contains($"traceId={traceId}", entry, StringComparison.Ordinal);
            Assert.DoesNotContain("password", entry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cookie", entry, StringComparison.OrdinalIgnoreCase);
            var secret = Environment.GetEnvironmentVariable("SeedUsers__Password");
            if (!string.IsNullOrEmpty(secret))
            {
                Assert.DoesNotContain(secret, entry, StringComparison.Ordinal);
            }
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A10_ForeignAndUnknownResource_AreIndistinguishable()
    {
        using var alice = await factory.LoginWithCsrfAsync("alice");

        using var foreignGet = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        using var unknownGet = await alice.GetAsync($"/api/incidents/{UnknownId}");
        using var foreignPut = await alice.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}", UpdateBody("A10 title", "Опис для порівняння відповідей."));
        using var unknownPut = await alice.PutAsJsonAsync(
            $"/api/incidents/{UnknownId}", UpdateBody("A10 title", "Опис для порівняння відповідей."));

        foreach (var (foreign, unknown) in new[] { (foreignGet, unknownGet), (foreignPut, unknownPut) })
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            Assert.Equal(foreign.StatusCode, unknown.StatusCode);
            Assert.Equal(
                foreign.Content.Headers.ContentType?.MediaType,
                unknown.Content.Headers.ContentType?.MediaType);
            Assert.Equal(
                StableFields(await foreign.Content.ReadAsStringAsync()),
                StableFields(await unknown.Content.ReadAsStringAsync()));
        }
    }

    [Fact]
    public async Task A11_SearchAndSeveritySummary_AreLimitedToAllowedSet()
    {
        using var alice = await factory.LoginWithCsrfAsync("alice");

        var obrien = await alice.GetFromJsonAsync<List<IncidentSearchItem>>(
            "/api/incidents/search?q=" + Uri.EscapeDataString("O'Brien"));
        Assert.NotNull(obrien);
        Assert.Empty(obrien);

        var usb = await alice.GetFromJsonAsync<List<IncidentSearchItem>>("/api/incidents/search?q=USB");
        Assert.NotNull(usb);
        Assert.Equal(UsbIncidentId, Assert.Single(usb).Id);

        var summary = await alice.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            "/api/incidents/severity-summary");
        Assert.NotNull(summary);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var own = await db.Incidents.AsNoTracking()
            .Where(item => item.OwnerUserId == DbSeeder.AliceId)
            .GroupBy(item => item.Severity)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync();

        Assert.Equal(
            own.Select(group => $"{group.Key}={group.Count}").Order().ToList(),
            summary.Select(group => $"{group.Severity}={group.Count}").Order().ToList());

        using var morgan = await factory.LoginWithCsrfAsync("morgan");
        var analystSearch = await morgan.GetFromJsonAsync<List<IncidentSearchItem>>(
            "/api/incidents/search?q=" + Uri.EscapeDataString("O'Brien"));
        Assert.NotNull(analystSearch);
        Assert.Contains(analystSearch, item => item.Id == BobSearchIncidentId);

        var analystSummary = await morgan.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            "/api/incidents/severity-summary");
        Assert.NotNull(analystSummary);
        Assert.Equal(await db.Incidents.CountAsync(), analystSummary.Sum(group => group.Count));
    }

    [Fact]
    public async Task A12_ChildComment_FollowsParentAndInternalPolicy()
    {
        using var alice = await factory.LoginWithCsrfAsync("alice");

        using var foreignComment = await alice.GetAsync($"/api/incident-comments/{CommentOfBobId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignComment.StatusCode);

        using var ownComment = await alice.GetAsync($"/api/incident-comments/{CommentOfAliceId}");
        Assert.Equal(HttpStatusCode.OK, ownComment.StatusCode);
        var ownBody = await ownComment.Content.ReadFromJsonAsync<IncidentCommentResponse>();
        Assert.NotNull(ownBody);
        Assert.False(ownBody.IsInternal);

        using var internalForReporter = await alice.GetAsync($"/api/incident-comments/{InternalCommentId}");
        Assert.Equal(HttpStatusCode.NotFound, internalForReporter.StatusCode);

        var aliceDetails = await alice.GetFromJsonAsync<IncidentDetailsResponse>(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.NotNull(aliceDetails);
        Assert.Contains(aliceDetails.Comments, item => item.Id == CommentOfAliceId);
        Assert.DoesNotContain(aliceDetails.Comments, item => item.Id == InternalCommentId);
        Assert.DoesNotContain(aliceDetails.Comments, item => item.IsInternal);

        using var morgan = await factory.LoginWithCsrfAsync("morgan");
        using var internalForAnalyst = await morgan.GetAsync($"/api/incident-comments/{InternalCommentId}");
        Assert.Equal(HttpStatusCode.OK, internalForAnalyst.StatusCode);
        var internalBody = await internalForAnalyst.Content.ReadFromJsonAsync<IncidentCommentResponse>();
        Assert.NotNull(internalBody);
        Assert.True(internalBody.IsInternal);

        var morganDetails = await morgan.GetFromJsonAsync<IncidentDetailsResponse>(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.NotNull(morganDetails);
        Assert.Contains(morganDetails.Comments, item => item.Id == InternalCommentId && item.IsInternal);
    }
}