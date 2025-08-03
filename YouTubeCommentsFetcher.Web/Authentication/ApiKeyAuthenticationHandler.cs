using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using YouTubeCommentsFetcher.Web.Services;

namespace YouTubeCommentsFetcher.Web.Authentication;

/// <summary>
/// Обработчик аутентификации через API ключи
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiAuthService apiAuthService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = GetApiKeyFromRequest();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AuthenticateResult.NoResult();
        }

        try
        {
            var user = await apiAuthService.ValidateApiKeyAsync(apiKey);

            if (user == null)
            {
                Logger.LogWarning("Неудачная попытка аутентификации с API ключом: {ApiKey}", apiKey);
                return AuthenticateResult.Fail("Невалидный API ключ");
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.ApiKey),
                new(ClaimTypes.Name, user.UserName),
                new("ApiKey", user.ApiKey),
                new("CreatedAt", user.CreatedAt.ToString("O")),
            };

            if (apiAuthService.IsAdminApiKey(apiKey))
            {
                claims.Add(new("IsAdmin", "true"));
                Logger.LogInformation("Пользователь аутентифицирован как администратор: {UserName}", user.UserName);
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            Logger.LogDebug("Пользователь аутентифицирован: {UserName} ({ApiKey})", user.UserName, user.ApiKey);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Ошибка при аутентификации с API ключом: {ApiKey}", apiKey);
            return AuthenticateResult.Fail("Ошибка аутентификации");
        }
    }

    /// <summary>
    /// Извлекает API ключ из HTTP запроса
    /// Приоритет: 1) X-API-Key заголовок, 2) apiKey кука
    /// </summary>
    private string? GetApiKeyFromRequest()
    {
        if (Request.Headers.TryGetValue("X-API-Key", out var headerValue))
        {
            var apiKey = headerValue.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(apiKey) == false)
            {
                return apiKey;
            }
        }

        if (Request.Cookies.TryGetValue("apiKey", out var cookieValue))
        {
            if (string.IsNullOrWhiteSpace(cookieValue) == false)
            {
                return cookieValue;
            }
        }

        return null;
    }
}
