using StackExchange.Redis;

namespace SecureLab.Api.Security;

// Advanced variant: спільний для всіх екземплярів API лічильник search rate limiting у Redis.
// Вмикається лише тоді, коли задано Redis:ConnectionString; інакше діє in-memory policy "search".
public static class RedisSearchRateLimit
{
    public static bool IsEnabled(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["Redis:ConnectionString"]);

    // Реєстрація ліниво читає конфігурацію: multiplexer створюється лише тоді,
    // коли filter справді використовується (Redis:ConnectionString задано).
    public static void AddRedisSearchRateLimit(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IConnectionMultiplexer>(services =>
        {
            var connectionString = services.GetRequiredService<IConfiguration>()["Redis:ConnectionString"]
                ?? throw new InvalidOperationException("Redis:ConnectionString не налаштовано.");
            var options = ConfigurationOptions.Parse(connectionString);
            // API стартує й без Redis; недоступність виявляється під час запиту (fail-closed).
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = Math.Min(options.ConnectTimeout, 2000);
            options.SyncTimeout = 1000;
            options.AsyncTimeout = 1000;
            return ConnectionMultiplexer.Connect(options);
        });
    }
}

public sealed class RedisSearchRateLimitFilter(
    IConnectionMultiplexer redis,
    IConfiguration config,
    ILogger<RedisSearchRateLimitFilter> logger) : IEndpointFilter
{
    // Атомарно: збільшити лічильник і задати TTL лише для першого запиту у вікні.
    private const string IncrementScript =
        "local c = redis.call('INCR', KEYS[1]) " +
        "if c == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end " +
        "return c";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var permitLimit = config.GetValue("RateLimiting:SearchPermitLimit", 10);
        var windowSeconds = config.GetValue("RateLimiting:SearchWindowSeconds", 60);
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / windowSeconds;
        // У ключі лише partition (перевірений userId) і номер вікна: без пошукового тексту й даних.
        var key = $"securelab:rl:search:{SearchRateLimiting.PartitionKey(context.HttpContext)}:{bucket}";

        long count;
        try
        {
            var result = await redis.GetDatabase().ScriptEvaluateAsync(
                IncrementScript, [key], [windowSeconds * 1000L]);
            count = (long)result;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // Fail-closed: без спільного лічильника пошук не виконується, решта API працює.
            logger.LogWarning("Rate limit store unavailable for {Path}; request rejected", context.HttpContext.Request.Path);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Пошук тимчасово недоступний.");
        }

        if (count > permitLimit)
        {
            logger.LogWarning("Rate limit exceeded for {Path}", context.HttpContext.Request.Path);
            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Забагато запитів пошуку.");
        }

        return await next(context);
    }
}
