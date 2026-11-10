using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentAccess
{
    // null означає дозвіл. Інакше повертається 404 (principal не може читати ресурс) або 403.
    public static async Task<IResult?> CheckAsync(
        HttpContext httpContext,
        IAuthorizationService authorization,
        ILoggerFactory loggers,
        Incident incident,
        OperationAuthorizationRequirement operation)
    {
        var user = httpContext.User;
        if ((await authorization.AuthorizeAsync(user, incident, operation)).Succeeded)
        {
            return null;
        }

        WriteDenied(httpContext, loggers, "incident." + operation.Name.ToLowerInvariant(), incident.Id);

        if (operation == IncidentOperations.Read)
        {
            return IncidentProblems.NotFound();
        }

        var canRead = (await authorization.AuthorizeAsync(user, incident, IncidentOperations.Read)).Succeeded;
        return canRead ? IncidentProblems.Forbidden() : IncidentProblems.NotFound();
    }

    public static void WriteDenied(
        HttpContext httpContext,
        ILoggerFactory loggers,
        string action,
        Guid resourceId)
    {
        loggers.CreateLogger("SecureLab.Audit").LogWarning(
            "authorization.denied subject={SubjectId} action={Action} resource={ResourceId} result={Result} occurredAtUtc={OccurredAtUtc:O} traceId={TraceId}",
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier),
            action,
            resourceId,
            "denied",
            DateTimeOffset.UtcNow,
            Activity.Current?.Id ?? httpContext.TraceIdentifier);
    }
}