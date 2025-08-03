using System.Text.Json;
using YouTubeCommentsFetcher.Web.Configuration;
using YouTubeCommentsFetcher.Web.Models;

namespace YouTubeCommentsFetcher.Web.Services;

public interface IApiAuthService
{
    /// <summary>
    /// Проверяет валидность API ключа и возвращает пользователя
    /// </summary>
    /// <param name="apiKey">API ключ для проверки</param>
    /// <returns>Пользователь если ключ валиден, иначе null</returns>
    Task<ApiUser?> ValidateApiKeyAsync(string apiKey);

    /// <summary>
    /// Получает всех пользователей
    /// </summary>
    /// <returns>Список всех пользователей</returns>
    Task<List<ApiUser>> GetAllUsersAsync();

    /// <summary>
    /// Создает нового пользователя
    /// </summary>
    /// <param name="userName">Имя пользователя</param>
    /// <param name="customApiKey">Пользовательский API ключ (если не указан, генерируется автоматически)</param>
    /// <returns>Созданный пользователь или null если создание не удалось</returns>
    Task<ApiUser?> CreateUserAsync(string userName, string? customApiKey = null);

    /// <summary>
    /// Деактивирует пользователя по API ключу
    /// </summary>
    /// <param name="apiKey">API ключ пользователя</param>
    /// <returns>true если пользователь деактивирован успешно</returns>
    Task<bool> DeactivateUserAsync(string apiKey);

    /// <summary>
    /// Получает пользователя по API ключу
    /// </summary>
    /// <param name="apiKey">API ключ</param>
    /// <returns>Пользователь если найден, иначе null</returns>
    Task<ApiUser?> GetUserByApiKeyAsync(string apiKey);
}

public class JsonApiAuthService(
    IDataPathService dataPathService,
    ILogger<JsonApiAuthService> logger)
    : IApiAuthService
{
    private const string UsersFileName = "users.json";
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public async Task<ApiUser?> ValidateApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var users = await LoadUsersAsync();
        var user = users.FirstOrDefault(u => u.ApiKey == apiKey && u.IsActive);

        if (user != null)
        {
            logger.LogInformation("API ключ валиден для пользователя {UserName}", user.UserName);
        }
        else
        {
            logger.LogWarning("Невалидный или неактивный API ключ: {ApiKey}", apiKey);
        }

        return user;
    }

    public async Task<List<ApiUser>> GetAllUsersAsync()
    {
        return await LoadUsersAsync();
    }

    public async Task<ApiUser?> CreateUserAsync(string userName, string? customApiKey = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var apiKey = customApiKey ?? Guid.NewGuid().ToString();

        if (Guid.TryParse(apiKey, out _) == false)
        {
            logger.LogError("API ключ должен быть в формате GUID: {ApiKey}", apiKey);
            return null;
        }

        var users = await LoadUsersAsync();

        if (users.Any(u => u.ApiKey == apiKey))
        {
            logger.LogError("API ключ уже существует: {ApiKey}", apiKey);
            return null;
        }

        var newUser = new ApiUser
        {
            ApiKey = apiKey,
            UserName = userName,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
        };

        users.Add(newUser);
        await SaveUsersAsync(users);

        logger.LogInformation("Создан новый пользователь {UserName} с API ключом {ApiKey}", userName, apiKey);
        return newUser;
    }

    public async Task<bool> DeactivateUserAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        var users = await LoadUsersAsync();
        var user = users.FirstOrDefault(u => u.ApiKey == apiKey);

        if (user == null)
        {
            logger.LogWarning("Пользователь с API ключом не найден: {ApiKey}", apiKey);
            return false;
        }

        user.IsActive = false;
        await SaveUsersAsync(users);

        logger.LogInformation("Пользователь {UserName} деактивирован", user.UserName);
        return true;
    }

    public async Task<ApiUser?> GetUserByApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var users = await LoadUsersAsync();
        return users.FirstOrDefault(u => u.ApiKey == apiKey);
    }

    private async Task<List<ApiUser>> LoadUsersAsync()
    {
        await _fileLock.WaitAsync();

        try
        {
            var filePath = GetUsersFilePath();
            logger.LogInformation("Попытка загрузки пользователей из файла: {FilePath}", filePath);

            if (File.Exists(filePath) == false)
            {
                logger.LogWarning("Файл пользователей не найден по пути: {FilePath}", filePath);
                return [];
            }

            var json = await File.ReadAllTextAsync(filePath);
            var usersData = JsonSerializer.Deserialize<UsersData>(json, JsonConfiguration.Export);

            return usersData?.Users ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при загрузке пользователей из файла");
            return [];
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task SaveUsersAsync(List<ApiUser> users)
    {
        await _fileLock.WaitAsync();

        try
        {
            var filePath = GetUsersFilePath();
            var usersData = new UsersData { Users = users };

            var json = JsonSerializer.Serialize(usersData, JsonConfiguration.Export);
            await File.WriteAllTextAsync(filePath, json);

            logger.LogInformation("Сохранено {Count} пользователей в файл", users.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при сохранении пользователей в файл");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private string GetUsersFilePath()
    {
        return Path.Combine(dataPathService.GetAbsoluteDataDirectory(), UsersFileName);
    }

    private class UsersData
    {
        public List<ApiUser> Users { get; set; } = [];
    }
}
