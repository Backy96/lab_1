using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Security;

namespace SecureLab.Api.Tests;

public static class CookieClient
{
    private sealed record TokenResponse(string Token);

    public static async Task<HttpClient> LoginAsync(this SecureLabApiFactory factory, string userName, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        if (!response.IsSuccessStatusCode)
        {
            client.Dispose();
            throw new InvalidOperationException("Study login failed (credentials omitted).");
        }
        return client;
    }

    // Пароль береться зі змінної середовища, у репозиторії його немає.
    public static Task<HttpClient> LoginAsync(this SecureLabApiFactory factory, string userName)
    {
        var password = Environment.GetEnvironmentVariable("SeedUsers__Password")
            ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");
        return factory.LoginAsync(userName, password);
    }

    // Вхід і одразу видача request token тим самим cookie-клієнтом:
    // для тестів, що перевіряють validation, ownership і ролі на змінювальних маршрутах.
    public static async Task<HttpClient> LoginWithCsrfAsync(this SecureLabApiFactory factory, string userName, string password)
    {
        var client = await factory.LoginAsync(userName, password);
        await client.UseAntiforgeryTokenAsync();
        return client;
    }

    public static async Task<HttpClient> LoginWithCsrfAsync(this SecureLabApiFactory factory, string userName)
    {
        var client = await factory.LoginAsync(userName);
        await client.UseAntiforgeryTokenAsync();
        return client;
    }

    // Лише для same-origin адрес власного API: header іде з кожним наступним запитом цього клієнта.
    public static async Task UseAntiforgeryTokenAsync(this HttpClient client)
    {
        client.DefaultRequestHeaders.Remove(AntiforgeryProtection.HeaderName);
        using var response = await client.GetAsync(AntiforgeryProtection.TokenRoute);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (string.IsNullOrWhiteSpace(body?.Token))
            throw new InvalidOperationException("Missing request token.");
        client.DefaultRequestHeaders.Add(AntiforgeryProtection.HeaderName, body.Token);
    }
}
