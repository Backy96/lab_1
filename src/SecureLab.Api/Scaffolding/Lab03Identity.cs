using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Security;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        builder.Services.AddOptions<SecurityStampValidatorOptions>()
            .Configure<IConfiguration>((options, config) =>
                options.ValidationInterval = TimeSpan.FromSeconds(
                    config.GetValue("Auth:SecurityStampValidationSeconds", 1800)));
        builder.Services.AddRateLimiter(_ => { });
        builder.Services.AddOptions<RateLimiterOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                var permitLimit = config.GetValue("RateLimiting:LoginPermitLimit", 5);
                var window = TimeSpan.FromSeconds(config.GetValue("RateLimiting:LoginWindowSeconds", 60));
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = (context, _) =>
                {
                    // Спільний обробник для всіх policies: шлях без query string, щоб не логувати пошуковий текст.
                    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("SecureLab.RateLimiting")
                        .LogWarning("Rate limit exceeded for {Path}", context.HttpContext.Request.Path);
                    return ValueTask.CompletedTask;
                };
                options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = window,
                        QueueLimit = 0
                    }));

                // Реєстрація відповідає 409 на зайняте ім'я або email, тому без обмеження
                // частоти вона дає необмежений перебір існуючих облікових записів.
                var registerPermitLimit = config.GetValue("RateLimiting:RegisterPermitLimit", 5);
                var registerWindow = TimeSpan.FromSeconds(config.GetValue("RateLimiting:RegisterWindowSeconds", 300));
                options.AddPolicy("register", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = registerPermitLimit,
                        Window = registerWindow,
                        QueueLimit = 0
                    }));
            });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (RegistrationInput input,
            UserManager<ApplicationUser> users, ILoggerFactory loggers) =>
        {
            var userName = input.UserName?.Trim();
            var email = input.Email?.Trim();
            var displayName = input.DisplayName?.Trim();
            if (!IsValidRegistration(userName, email, displayName, input.Password))
                return Results.BadRequest(new { message = RegistrationFailed });
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = userName, Email = email, DisplayName = displayName!
            };
            var result = await users.CreateAsync(user, input.Password!);
            if (!result.Succeeded)
            {
                var isDuplicate = result.Errors.Any(error =>
                    error.Code is "DuplicateUserName" or "DuplicateEmail");
                return isDuplicate
                    ? Results.Json(new { message = RegistrationFailed }, statusCode: StatusCodes.Status409Conflict)
                    : Results.BadRequest(new { message = RegistrationFailed });
            }
            var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
            if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");
            loggers.CreateLogger("SecureLab.Auth").LogInformation("User registered {UserId}", user.Id);
            return Results.Created("/api/me", new RegistrationResponse(user.Id, user.UserName!, user.DisplayName));
        }).RequireRateLimiting("register");
        app.MapPost("/api/auth/login", async (LoginInput input, SignInManager<ApplicationUser> signIn,
            UserManager<ApplicationUser> users, ILoggerFactory loggers) =>
        {
            var log = loggers.CreateLogger("SecureLab.Auth");
            if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
            {
                log.LogWarning("Login failed");
                return Results.Unauthorized();
            }
            var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);
            if (!result.Succeeded)
            {
                log.LogWarning("Login failed");
                return Results.Unauthorized();
            }
            var user = await users.FindByNameAsync(input.UserName);
            log.LogInformation("Login succeeded for user {UserId}", user?.Id);
            return Results.NoContent();
        }).RequireRateLimiting("login");
        // Фільтр перевіряє request token до виклику handler, тобто до SignOutAsync.
        app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signIn, ILoggerFactory loggers) =>
        {
            await signIn.SignOutAsync();
            loggers.CreateLogger("SecureLab.Auth").LogInformation("Logout completed");
            return Results.NoContent();
        }).RequireAntiforgeryValidation();

        app.MapGet("/api/me", (HttpContext httpContext) => GetCurrentUser(httpContext)).RequireAuthorization();
        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }

    private const string RegistrationFailed = "Registration failed";

    private static bool IsValidRegistration(string? userName, string? email, string? displayName, string? password) =>
        !string.IsNullOrWhiteSpace(userName) && userName.Length <= 256
        && !string.IsNullOrWhiteSpace(email) && email.Length <= 256
        && !string.IsNullOrWhiteSpace(displayName) && displayName.Length <= 120
        && !string.IsNullOrEmpty(password) && password.Length <= 128;

    private static IResult GetCurrentUser(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var verifiedUserId))
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        return Results.Ok(new CurrentUserResponse(verifiedUserId, userName));
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegistrationResponse(Guid Id, string UserName, string DisplayName);
public sealed record CurrentUserResponse(Guid Id, string UserName);
