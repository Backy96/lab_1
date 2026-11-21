using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

// REG-AUD-01: дозволений перехід Incident.Status має audit event (subject, action, resource, result, time),
// відмова Reporter теж є в audit, стан не змінюється, у журналі немає пароля.
public sealed class AuditTrailTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task REG_AUD_01_AllowedAndDeniedTriage_AreBothAudited_WithoutSecrets()
    {
        var title = $"AUD01-{Guid.NewGuid():N}";
        using var alice = await factory.LoginWithCsrfAsync("alice");
        using var morgan = await factory.LoginWithCsrfAsync("morgan");
        using var created = await alice.PostAsJsonAsync("/api/incidents", new
        {
            title,
            description = "Штучний опис для перевірки audit trail у ЛР 6.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = document.RootElement.GetProperty("id").GetGuid();
        try
        {
            using var denied = await alice.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
                Assert.Equal(IncidentStatus.New,
                    (await db.Incidents.AsNoTracking().SingleAsync(item => item.Id == id)).Status);
            }

            using var allowed = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);

            var audit = factory.Logs.Entries.Where(entry => entry.StartsWith("SecureLab.Audit|")).ToList();
            Assert.Contains(audit, entry => entry.Contains("authorization.denied")
                && entry.Contains($"subject={DbSeeder.AliceId}")
                && entry.Contains($"resource={id}") && entry.Contains("result=denied"));
            Assert.Contains(audit, entry => entry.Contains("incident.status.changed")
                && entry.Contains($"subject=10000000-0000-0000-0000-000000000003")
                && entry.Contains("action=incident.triage")
                && entry.Contains($"resource={id}") && entry.Contains("result=allowed")
                && entry.Contains("from=New") && entry.Contains("to=Triaged")
                && entry.Contains("occurredAtUtc=") && entry.Contains("traceId="));

            var password = Environment.GetEnvironmentVariable("SeedUsers__Password")!;
            Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Contains(password, StringComparison.Ordinal));
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await db.Incidents.Where(item => item.Id == id).ExecuteDeleteAsync();
        }
    }
}
