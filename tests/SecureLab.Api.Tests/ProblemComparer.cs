using System.Net;
using System.Text.Json;

namespace SecureLab.Api.Tests;

public sealed record ProblemSample(HttpStatusCode Status, string? MediaType, string Body)
{
    public static async Task<ProblemSample> FromAsync(HttpResponseMessage response) =>
        new(response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync());
}

public static class ProblemComparer
{
    // Лише поля кореляції запиту можуть відрізнятися між двома 404.
    public static readonly string[] CorrelationFields = { "traceId", "requestId" };

    private static readonly string[] StandardMembers = { "type", "title", "status", "detail", "instance" };

    public static string Normalize(string json) =>
        Project(json, name => !CorrelationFields.Contains(name));

    public static bool AreSame(ProblemSample a, ProblemSample b) =>
        a.Status == b.Status
        && string.Equals(a.MediaType, b.MediaType, StringComparison.OrdinalIgnoreCase)
        && Normalize(a.Body) == Normalize(b.Body);

    // Стара нормалізація: видаляє всі extensions і не дивиться на Content-Type. Лише для контрприкладу.
    public static bool LegacyAreSame(ProblemSample a, ProblemSample b) =>
        a.Status == b.Status
        && Project(a.Body, name => StandardMembers.Contains(name)) == Project(b.Body, name => StandardMembers.Contains(name));

    private static string Project(string json, Func<string, bool> keep)
    {
        using var document = JsonDocument.Parse(json);
        return string.Join("|", document.RootElement.EnumerateObject()
            .Where(property => keep(property.Name))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={property.Value.GetRawText()}"));
    }
}