using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Security;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, HttpContext httpContext, SecureLabDbContext db, CancellationToken ct) =>
        {
            if (!string.IsNullOrEmpty(sortBy) && sortBy is not ("createdAtUtc" or "severity" or "status"))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status."]
                });

            if (!IncidentViewer.TryCreate(httpContext.User, out var viewer))
                return Results.Unauthorized();

            // Scoping до фільтра пошуку, сортування і Take(50).
            var query = db.Incidents.AsNoTracking().VisibleTo(viewer);

            if (!string.IsNullOrEmpty(q))
            {
                var escaped = q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                var pattern = "%" + escaped + "%";
                query = query.Where(i => EF.Functions.ILike(i.Title, pattern, "\\")
                    || EF.Functions.ILike(i.Description, pattern, "\\"));
            }

            IOrderedQueryable<Incident> ordered = sortBy switch
            {
                "severity" => query.OrderBy(i =>
                    i.Severity == IncidentSeverity.Critical ? 0
                    : i.Severity == IncidentSeverity.High ? 1
                    : i.Severity == IncidentSeverity.Medium ? 2 : 3),
                "status" => query.OrderBy(i =>
                    i.Status == IncidentStatus.New ? 0
                    : i.Status == IncidentStatus.Triaged ? 1
                    : i.Status == IncidentStatus.InProgress ? 2
                    : i.Status == IncidentStatus.Resolved ? 3 : 4),
                _ => query.OrderByDescending(i => i.CreatedAtUtc)
            };

            var rows = await ordered.ThenBy(i => i.Id).Take(50).ToListAsync(ct);
            return Results.Ok(rows.Select(row => new IncidentSearchItem(
                row.Id, row.Title, row.Description,
                row.Severity.ToString(), row.Status.ToString(), row.CreatedAtUtc)));
        }).RequireAuthorization().RequireRateLimiting(SearchRateLimiting.PolicyName);

        app.MapPost("/api/incidents", async (CreateIncidentRequest request, HttpContext httpContext, SecureLabDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Title))
                errors["title"] = ["Заголовок обов'язковий."];
            else if (request.Title.Length > 160)
                errors["title"] = ["Заголовок не довший за 160 символів."];

            if (string.IsNullOrWhiteSpace(request.Description))
                errors["description"] = ["Опис обов'язковий."];
            else if (request.Description.Length > 4000)
                errors["description"] = ["Опис не довший за 4000 символів."];

            var severityIsValid = Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
                && Enum.IsDefined(severity);
            if (!severityIsValid)
                errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];

            if (request.OccurredAtUtc is null)
                errors["occurredAtUtc"] = ["Час події обов'язковий."];
            else if (request.OccurredAtUtc.Value > now.AddMinutes(5))
                errors["occurredAtUtc"] = ["Час події не може бути пізніше ніж на 5 хвилин у майбутньому."];

            var title = request.Title?.Trim() ?? "";
            var description = request.Description?.Trim() ?? "";

            if (severityIsValid && (severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
                && !errors.ContainsKey("description") && description.Length < 40)
                errors["description"] = ["Для High або Critical опис має містити щонайменше 40 символів."];
            if (!errors.ContainsKey("description") && !errors.ContainsKey("title")
                && string.Equals(description, title, StringComparison.OrdinalIgnoreCase))
                errors["description"] = ["Опис не може збігатися із заголовком."];
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var duplicate = await db.Incidents.AnyAsync(
                item => item.Title == title && item.Status != IncidentStatus.Closed, ct);
            if (duplicate)
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Інцидент уже існує",
                    detail: "Незакритий інцидент з таким заголовком уже зареєстровано.");
            if (!Guid.TryParse(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var ownerId))
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

            var incident = new Incident
            {
                Id = Guid.NewGuid(), OwnerUserId = ownerId,
                Title = title, Description = description,
                Severity = severity, Status = IncidentStatus.New,
                OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
                CreatedAtUtc = now, UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            var response = new CreatedIncidentResponse(
                incident.Id, incident.Title, incident.Severity.ToString(), incident.Status.ToString(),
                incident.OccurredAtUtc, incident.CreatedAtUtc, incident.UpdatedAtUtc);
            return Results.Created($"/api/incidents/{incident.Id}", response);
        }).RequireAuthorization().RequireAntiforgeryValidation();
    }
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Severity, string Status,
    DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record IncidentSearchItem(
    Guid Id, string Title, string Description,
    string Severity, string Status, DateTimeOffset CreatedAtUtc);
