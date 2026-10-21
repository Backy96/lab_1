using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class IncidentEndpointTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetList_ReturnsSeededIncidents()
    {
        var incidents = await _client.GetFromJsonAsync<List<IncidentListItemResponse>>(
            "/api/incidents");

        Assert.NotNull(incidents);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.AliceIncidentId);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.BobIncidentId);
    }

    [Fact]
    public async Task GetDetails_ForUnknownId_ReturnsProblemDetails404()
    {
        using var response = await _client.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetDetails_DoesNotExposeInternalOwnerFields()
    {
        using var response = await _client.GetAsync(
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
        var summary = await _client.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
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
        var incidents = await _client.GetFromJsonAsync<List<IncidentListItemResponse>>(
            $"/api/incidents{query}");
        var summary = await _client.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            $"/api/incidents/severity-summary{query}");

        Assert.NotNull(incidents);
        Assert.NotNull(summary);
        Assert.Equal(incidents.Count, summary.Sum(group => group.Count));
    }

    [Fact]
    public async Task GetSeveritySummary_ForUnknownStatus_ReturnsProblemDetails400()
    {
        using var response = await _client.GetAsync(
            "/api/incidents/severity-summary?status=Unknown");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ClientScript_DoesNotUseDangerousInnerHtmlSink()
    {
        var script = await _client.GetStringAsync("/app.js");

        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
    }
}