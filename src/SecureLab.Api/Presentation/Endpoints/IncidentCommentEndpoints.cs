using Microsoft.EntityFrameworkCore;
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
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Child завантажується лише за власним id, без parent incident.
        var comment = await dbContext.IncidentComments
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new IncidentCommentResponse(
                item.Id,
                item.Author.DisplayName,
                item.Text,
                item.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return comment is null
            ? Results.Problem(title: "Коментар не знайдено", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(comment);
    }
}
