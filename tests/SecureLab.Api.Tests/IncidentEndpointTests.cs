using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class IncidentEndpointTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static readonly Guid BobIncidentId = DbSeeder.BobIncidentId;
    private static readonly Guid BobSearchIncidentId = Guid.Parse("20000000-0000-0000-0000-000000000004");
    private static readonly Guid UsbIncidentId = Guid.Parse("20000000-0000-0000-0000-000000000005");

    // A-05: список містить лише власні інциденти користувача.
    [Fact]
    public async Task GetList_ReturnsOnlyOwnIncidents()
    {
        using var alice = await factory.LoginAsync("alice");
        var incidents = await alice.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");

        Assert.NotNull(incidents);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.AliceIncidentId);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.SafeTextIncidentId);
        Assert.Contains(incidents, incident => incident.Id == UsbIncidentId);
        Assert.DoesNotContain(incidents, incident => incident.Id == BobIncidentId);
        Assert.DoesNotContain(incidents, incident => incident.Id == BobSearchIncidentId);
    }

    [Fact]
    public async Task GetDetails_ForUnknownId_ReturnsProblemDetails404()
    {
        using var alice = await factory.LoginAsync("alice");
        using var response = await alice.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetDetails_DoesNotExposeInternalOwnerFields()
    {
        using var alice = await factory.LoginAsync("alice");
        using var response = await alice.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(document.RootElement.TryGetProperty("ownerUserId", out _));
        Assert.False(document.RootElement.TryGetProperty("email", out _));
        Assert.Equal("Аліса Коваль", document.RootElement.GetProperty("ownerDisplayName").GetString());
    }

    [Fact]
    public async Task GetSeveritySummary_ReturnsGroupsOrderedByCriticality()
    {
        using var alice = await factory.LoginAsync("alice");
        var summary = await alice.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            "/api/incidents/severity-summary");

        Assert.NotNull(summary);
        Assert.NotEmpty(summary);

        string[] order = ["Low", "Medium", "High", "Critical"];
        var positions = summary.Select(group => Array.IndexOf(order, group.Severity)).ToList();

        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order().ToList(), positions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?status=Triaged")]
    public async Task GetSeveritySummary_TotalEqualsNumberOfIncidents(string query)
    {
        using var alice = await factory.LoginAsync("alice");
        var incidents = await alice.GetFromJsonAsync<List<IncidentListItemResponse>>(
            $"/api/incidents{query}");
        var summary = await alice.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            $"/api/incidents/severity-summary{query}");

        Assert.NotNull(incidents);
        Assert.NotNull(summary);
        Assert.Equal(incidents.Count, summary.Sum(group => group.Count));
    }

    [Fact]
    public async Task GetSeveritySummary_ForUnknownStatus_ReturnsProblemDetails400()
    {
        using var alice = await factory.LoginAsync("alice");
        using var response = await alice.GetAsync(
            "/api/incidents/severity-summary?status=Unknown");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // Неописана проблема starter: список через кому (Triaged,InProgress = 1|2 = 3)
    // раніше проходив як Resolved; тепер відхиляється з 400.
    [Theory]
    [InlineData("/api/incidents?status=Triaged,InProgress")]
    [InlineData("/api/incidents/severity-summary?status=Triaged,InProgress")]
    public async Task StatusFilter_RejectsCommaSeparatedValues(string url)
    {
        using var alice = await factory.LoginAsync("alice");
        using var response = await alice.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ClientScript_DoesNotUseDangerousInnerHtmlSink()
    {
        using var client = factory.CreateClient();
        var script = await client.GetStringAsync("/app.js");

        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
    }
}