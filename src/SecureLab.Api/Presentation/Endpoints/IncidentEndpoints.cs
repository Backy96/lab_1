using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Security;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAntiforgeryValidation()
            .WithName("UpdateIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAntiforgeryValidation()
            .WithName("DeleteIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/triage", TriageAsync)
            .RequireAntiforgeryValidation()
            .WithName("TriageIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var invalid = ParseStatus(status, out var parsedStatus);
        if (invalid is not null)
        {
            return invalid;
        }

        if (!IncidentViewer.TryCreate(httpContext.User, out var viewer))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await queries.GetListAsync(parsedStatus, viewer, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        HttpContext httpContext,
        IAuthorizationService authorization,
        ILoggerFactory loggers,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var incident = await queries.FindAsync(id, cancellationToken);
        if (incident is null)
        {
            return IncidentProblems.NotFound();
        }

        var denied = await IncidentAccess.CheckAsync(
            httpContext, authorization, loggers, incident, IncidentOperations.Read);
        if (denied is not null)
        {
            return denied;
        }

        var includeInternal = (await authorization.AuthorizeAsync(
            httpContext.User, incident, IncidentOperations.ReadInternalComments)).Succeeded;

        var details = await queries.GetDetailsAsync(id, includeInternal, cancellationToken);
        return details is null ? IncidentProblems.NotFound() : Results.Ok(details);
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var invalid = ParseStatus(status, out var parsedStatus);
        if (invalid is not null)
        {
            return invalid;
        }

        if (!IncidentViewer.TryCreate(httpContext.User, out var viewer))
        {
            return Results.Unauthorized();
        }

        var traceId = System.Diagnostics.Activity.Current?.Id ?? httpContext.TraceIdentifier;
        return Results.Ok(await queries.GetSeveritySummaryAsync(parsedStatus, viewer, traceId, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateIncidentRequest request,
        HttpContext httpContext,
        IAuthorizationService authorization,
        ILoggerFactory loggers,
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return IncidentProblems.NotFound();
        }

        var denied = await IncidentAccess.CheckAsync(
            httpContext, authorization, loggers, incident, IncidentOperations.Update);
        if (denied is not null)
        {
            return denied;
        }

        var now = DateTimeOffset.UtcNow;
        var errors = ValidateUpdate(request, incident.Severity, now);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        incident.Title = request.Title!.Trim();
        incident.Description = request.Description!.Trim();
        incident.OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime();
        incident.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext httpContext,
        IAuthorizationService authorization,
        ILoggerFactory loggers,
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return IncidentProblems.NotFound();
        }

        var denied = await IncidentAccess.CheckAsync(
            httpContext, authorization, loggers, incident, IncidentOperations.Delete);
        if (denied is not null)
        {
            return denied;
        }

        dbContext.Incidents.Remove(incident);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> TriageAsync(
        Guid id,
        HttpContext httpContext,
        IAuthorizationService authorization,
        ILoggerFactory loggers,
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return IncidentProblems.NotFound();
        }

        var denied = await IncidentAccess.CheckAsync(
            httpContext, authorization, loggers, incident, IncidentOperations.Triage);
        if (denied is not null)
        {
            return denied;
        }

        if (!Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Results.Unauthorized();
        }

        if (incident.Status != IncidentStatus.New)
        {
            return IncidentProblems.StateConflict();
        }

        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new IncidentStatusHistory
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ChangedByUserId = userId,
            OldStatus = IncidentStatus.New,
            NewStatus = IncidentStatus.Triaged,
            Note = "Первинну оцінку завершено.",
            CreatedAtUtc = now
        });
        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Audit event дозволеного привілейованого переходу – у тому самому каналі, що й відмови.
        // Формат полів узгоджено з подією відмови: ISO 8601 час і traceId запиту для зіставлення.
        var traceId = System.Diagnostics.Activity.Current?.Id ?? httpContext.TraceIdentifier;
        loggers.CreateLogger("SecureLab.Audit").LogInformation(
            "incident.status.changed subject={Subject} action={Action} resource={Resource} result={Result} from={From} to={To} occurredAtUtc={OccurredAtUtc} traceId={TraceId}",
            userId, "incident.triage", incident.Id, "allowed",
            IncidentStatus.New, IncidentStatus.Triaged, now.ToString("O"), traceId);
        return Results.NoContent();
    }

    private static IResult? ParseStatus(string? status, out IncidentStatus? parsed)
    {
        parsed = null;
        if (status is null)
        {
            return null;
        }

        if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
            || !Enum.IsDefined(candidate)
            || !string.Equals(status, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
            });
        }

        parsed = candidate;
        return null;
    }

    private static Dictionary<string, string[]> ValidateUpdate(
        UpdateIncidentRequest request,
        IncidentSeverity severity,
        DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Title))
            errors["title"] = ["Заголовок обов'язковий."];
        else if (request.Title.Length > 160)
            errors["title"] = ["Заголовок не довший за 160 символів."];

        if (string.IsNullOrWhiteSpace(request.Description))
            errors["description"] = ["Опис обов'язковий."];
        else if (request.Description.Length > 4000)
            errors["description"] = ["Опис не довший за 4000 символів."];

        if (request.OccurredAtUtc is null)
            errors["occurredAtUtc"] = ["Час події обов'язковий."];
        else if (request.OccurredAtUtc.Value > now.AddMinutes(5))
            errors["occurredAtUtc"] = ["Час події не може бути пізніше ніж на 5 хвилин у майбутньому."];

        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";

        if ((severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
            && !errors.ContainsKey("description") && description.Length < 40)
            errors["description"] = ["Для High або Critical опис має містити щонайменше 40 символів."];
        if (!errors.ContainsKey("description") && !errors.ContainsKey("title")
            && string.Equals(description, title, StringComparison.OrdinalIgnoreCase))
            errors["description"] = ["Опис не може збігатися із заголовком."];

        return errors;
    }
}