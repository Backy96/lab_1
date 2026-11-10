using System.Security.Claims;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Incidents;

public readonly record struct IncidentViewer(Guid UserId, bool CanSeeAll)
{
    public static bool TryCreate(ClaimsPrincipal principal, out IncidentViewer viewer)
    {
        viewer = default;
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return false;
        }

        viewer = new IncidentViewer(userId, principal.IsInRole(DbSeeder.AnalystRole));
        return true;
    }
}

public static class IncidentViewerExtensions
{
    
    public static IQueryable<Incident> VisibleTo(this IQueryable<Incident> query, IncidentViewer viewer)
    {
        var userId = viewer.UserId;
        var canSeeAll = viewer.CanSeeAll;
        return query.Where(incident => canSeeAll || incident.OwnerUserId == userId);
    }
}