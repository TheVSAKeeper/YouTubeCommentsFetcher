using Microsoft.AspNetCore.Mvc;
using YouTubeCommentsFetcher.Web.Services;

namespace YouTubeCommentsFetcher.Web.Controllers;

/// <summary>
/// API контроллер для аутентификации
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController(IApiAuthService apiAuthService, ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>
    /// Валидация API ключа
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateApiKey([FromBody] ValidateApiKeyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return BadRequest(new { error = "API ключ не может быть пустым" });
        }

        try
        {
            var user = await apiAuthService.ValidateApiKeyAsync(request.ApiKey);

            if (user == null)
            {
                logger.LogWarning("Попытка валидации невалидного API ключа: {ApiKey}", request.ApiKey);
                return Unauthorized(new { error = "Невалидный API ключ" });
            }

            logger.LogInformation("Успешная валидация API ключа для пользователя: {UserName}", user.UserName);

            return Ok(new
            {
                valid = true,
                userName = user.UserName,
                createdAt = user.CreatedAt,
                isActive = user.IsActive,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при валидации API ключа: {ApiKey}", request.ApiKey);
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    /// <summary>
    /// Получить информацию о текущем пользователе
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        // Получаем API ключ из заголовка или куки
        var apiKey = GetApiKeyFromRequest();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unauthorized(new { error = "API ключ не предоставлен" });
        }

        try
        {
            var user = await apiAuthService.ValidateApiKeyAsync(apiKey);

            if (user == null)
            {
                return Unauthorized(new { error = "Невалидный API ключ" });
            }

            return Ok(new
            {
                apiKey = user.ApiKey,
                userName = user.UserName,
                createdAt = user.CreatedAt,
                isActive = user.IsActive,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при получении информации о пользователе");
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    /// <summary>
    /// Создать нового пользователя с автоматически сгенерированным API ключом
    /// </summary>
    [HttpPost("create-user")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return BadRequest(new { error = "Имя пользователя не может быть пустым" });
        }

        try
        {
            var newUser = await apiAuthService.CreateUserAsync(request.UserName);

            if (newUser == null)
            {
                logger.LogWarning("Не удалось создать пользователя: {UserName}", request.UserName);
                return BadRequest(new { error = "Не удалось создать пользователя" });
            }

            logger.LogInformation("Создан новый пользователь: {UserName} с API ключом: {ApiKey}", newUser.UserName, newUser.ApiKey);

            return Ok(new
            {
                success = true,
                apiKey = newUser.ApiKey,
                userName = newUser.UserName,
                createdAt = newUser.CreatedAt,
                message = "Пользователь успешно создан",
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при создании пользователя: {UserName}", request.UserName);
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    /// <summary>
    /// Сгенерировать новый API ключ для анонимного пользователя
    /// </summary>
    [HttpPost("generate-key")]
    public async Task<IActionResult> GenerateApiKey()
    {
        try
        {
            var userName = $"Пользователь_{DateTime.Now:yyyyMMdd_HHmmss}";
            var newUser = await apiAuthService.CreateUserAsync(userName);

            if (newUser == null)
            {
                logger.LogWarning("Не удалось сгенерировать API ключ для пользователя: {UserName}", userName);
                return BadRequest(new { error = "Не удалось сгенерировать API ключ" });
            }

            logger.LogInformation("Сгенерирован новый API ключ для пользователя: {UserName}", newUser.UserName);

            return Ok(new
            {
                success = true,
                apiKey = newUser.ApiKey,
                userName = newUser.UserName,
                createdAt = newUser.CreatedAt,
                message = "API ключ успешно сгенерирован",
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при генерации API ключа");
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    /// <summary>
    /// Извлекает API ключ из HTTP запроса
    /// </summary>
    private string? GetApiKeyFromRequest()
    {
        if (Request.Headers.TryGetValue("X-API-Key", out var headerValue))
        {
            var apiKey = headerValue.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                return apiKey;
            }
        }

        if (Request.Cookies.TryGetValue("apiKey", out var cookieValue))
        {
            if (!string.IsNullOrWhiteSpace(cookieValue))
            {
                return cookieValue;
            }
        }

        return null;
    }
}

/// <summary>
/// Модель запроса для валидации API ключа
/// </summary>
public class ValidateApiKeyRequest
{
    public required string ApiKey { get; set; }
}

/// <summary>
/// Модель запроса для создания пользователя
/// </summary>
public class CreateUserRequest
{
    public required string UserName { get; set; }
}
