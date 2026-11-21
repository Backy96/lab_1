using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SecureLab.Api.Security;

// Обмеження частоти пошуку: захист від зловживання, а не заміна authentication чи authorization.
public static class SearchRateLimiting
{
    public const string PolicyName = "search";

    public static void AddSearchRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<RateLimiterOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                var permitLimit = config.GetValue("RateLimiting:SearchPermitLimit", 10);
                var window = TimeSpan.FromSeconds(config.GetValue("RateLimiting:SearchWindowSeconds", 60));
                options.AddPolicy(PolicyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = window,
                        QueueLimit = 0
                    }));
            });
    }

    // Partition – перевірений ідентифікатор користувача з cookie-сеансу, а не значення від клієнта.
    // Для анонімного запиту (його потім відхилить authorization) – IP-адреса.
    public static string PartitionKey(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is not null
            ? "user:" + userId
            : "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
}
