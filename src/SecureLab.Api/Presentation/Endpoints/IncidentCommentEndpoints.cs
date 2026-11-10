using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentCommentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentCommentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/incident-comments/{id:guid}", GetAsync)
            .WithName("GetIncidentComment")
            .WithTags("Incident comments")
            .RequireAuthorization()
            .Produces<IncidentCommentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        HttpContext httpContext,
        IAuthorizationService authorization,
        ILoggerFactory loggers,
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var comment = await dbContext.IncidentComments
            .AsNoTracking()
            .Include(item => item.Incident)
            .Include(item => item.Author)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (comment is null)
        {
            return IncidentProblems.CommentNotFound();
        }

        // Доступ до child визначається parent-інцидентом і прапорцем internal.
        var user = httpContext.User;
        var canRead = (await authorization.AuthorizeAsync(
            user, comment.Incident, IncidentOperations.Read)).Succeeded;
        var canReadInternal = (await authorization.AuthorizeAsync(
            user, comment.Incident, IncidentOperations.ReadInternalComments)).Succeeded;

        if (!canRead || (comment.IsInternal && !canReadInternal))
        {
            IncidentAccess.WriteDenied(httpContext, loggers, "comment.read", comment.Id);
            return IncidentProblems.CommentNotFound();
        }

        return Results.Ok(new IncidentCommentResponse(
            comment.Id,
            comment.Author.DisplayName,
            comment.Text,
            comment.CreatedAtUtc,
            comment.IsInternal));
    }
}