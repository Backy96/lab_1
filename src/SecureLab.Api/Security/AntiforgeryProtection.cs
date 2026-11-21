using Microsoft.AspNetCore.Antiforgery;

namespace SecureLab.Api.Security;

// Контракт між сервером і клієнтом: request token передається в цьому header.
public static class AntiforgeryProtection
{
    public const string HeaderName = "X-CSRF-TOKEN";
    public const string TokenRoute = "/api/antiforgery/token";

    public static void AddSecureLabAntiforgery(this WebApplicationBuilder builder)
    {
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = "SecureLab.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
    }

    // Токен видається лише автентифікованому користувачу: він прив'язаний до identity.
    public static void MapAntiforgeryToken(this WebApplication app)
    {
        app.MapGet(TokenRoute, (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new AntiforgeryTokenResponse(tokens.RequestToken!));
        }).RequireAuthorization();
    }

    public static RouteHandlerBuilder RequireAntiforgeryValidation(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<AntiforgeryValidationFilter>();
}

// Єдине місце валідації: без валідної пари cookie + header відповідь 400 Problem Details.
public sealed class AntiforgeryValidationFilter(
    IAntiforgery antiforgery, ILogger<AntiforgeryValidationFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            logger.LogWarning("Antiforgery validation failed for {Method} {Path}",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Antiforgery token is missing or invalid.");
        }

        return await next(context);
    }
}

public sealed record AntiforgeryTokenResponse(string Token);