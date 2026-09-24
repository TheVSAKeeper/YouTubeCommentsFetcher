using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YouTubeCommentsFetcher.Web.Configuration;
using YouTubeCommentsFetcher.Web.Models;

namespace YouTubeCommentsFetcher.Web.Services;

/// <summary>
/// Сервис для управления результатами выборки комментариев
/// </summary>
public interface IFetchResultsService
{
    /// <summary>
    /// Сохранить результат выборки комментариев
    /// </summary>
    /// <param name="jobId">Идентификатор задачи</param>
    /// <param name="channelId">Идентификатор канала</param>
    /// <param name="model">Модель с данными комментариев</param>
    /// <param name="channelName">Название канала (опционально)</param>
    /// <param name="userId">Идентификатор пользователя (API ключ)</param>
    /// <param name="isIncomplete">Сбор остановлен досрочно</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Метаданные сохраненного результата</returns>
    Task<FetchResultMetadata> SaveFetchResultAsync(
        string jobId,
        string channelId,
        YouTubeCommentsViewModel model,
        string? channelName = null,
        string? userId = null,
        bool isIncomplete = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить результат выборки по идентификатору задачи
    /// </summary>
    /// <param name="jobId">Идентификатор задачи</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Модель с данными комментариев или null, если не найдено</returns>
    Task<YouTubeCommentsViewModel?> GetFetchResultAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить метаданные результата выборки по идентификатору задачи
    /// </summary>
    /// <param name="jobId">Идентификатор задачи</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Метаданные результата или null, если не найдено</returns>
    Task<FetchResultMetadata?> GetMetadataAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить список всех результатов выборки
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Список метаданных всех результатов</returns>
    Task<List<FetchResultMetadata>> GetAllFetchResultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить список результатов выборки для конкретного пользователя
    /// </summary>
    /// <param name="userId">Идентификатор пользователя (API ключ)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Список метаданных результатов пользователя</returns>
    Task<List<FetchResultMetadata>> GetUserFetchResultsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверить существование результата выборки
    /// </summary>
    /// <param name="jobId">Идентификатор задачи</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>True, если результат существует</returns>
    Task<bool> ExistsAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удалить результат выборки
    /// </summary>
    /// <param name="jobId">Идентификатор задачи</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>True, если результат был удален</returns>
    Task<bool> DeleteFetchResultAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удалить результаты старше указанного количества дней
    /// </summary>
    /// <param name="days">Количество дней</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Количество удаленных результатов</returns>
    Task<int> DeleteOlderThanAsync(int days, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить статистику по всем результатам выборки
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Статистика результатов</returns>
    Task<FetchResultsStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Перестроить индекс метаданных на основе существующих файлов
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Количество обработанных файлов</returns>
    Task<int> RebuildIndexAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Сервис для управления результатами выборки комментариев
/// </summary>
public class FetchResultsService : IFetchResultsService
{
    private const string LegacyUserId = "00000000-0000-0000-0000-000000000000"; // Legacy user для существующих данных

    private readonly IDataPathService _dataPathService;
    private readonly IApiAuthService _apiAuthService;
    private readonly ILogger<FetchResultsService> _logger;
    private readonly ConcurrentDictionary<string, FetchResultMetadata> _metadataIndex;
    private readonly SemaphoreSlim _indexLock;

    public FetchResultsService(IDataPathService dataPathService, IApiAuthService apiAuthService, ILogger<FetchResultsService> logger)
    {
        _dataPathService = dataPathService;
        _apiAuthService = apiAuthService;
        _logger = logger;
        _metadataIndex = new();
        _indexLock = new(1, 1);

        _ = Task.Run(() => LoadIndexAsync());
    }

    public async Task<FetchResultMetadata> SaveFetchResultAsync(
        string jobId,
        string channelId,
        YouTubeCommentsViewModel model,
        string? channelName = null,
        string? userId = null,
        bool isIncomplete = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("JobId не может быть пустым", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentException("ChannelId не может быть пустым", nameof(channelId));
        }

        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        _logger.LogInformation("Сохранение результата выборки для задачи {JobId}", jobId);

        model.IsIncomplete = isIncomplete;
        model.ChannelId = channelId;
        model.ChannelName = channelName;
        model.OwnerKeyHash = string.IsNullOrWhiteSpace(userId) ? null : HashApiKey(userId);
        var json = JsonSerializer.Serialize(model, JsonConfiguration.Default);
        var filePath = _dataPathService.GetAbsoluteCommentsFilePath(jobId);

        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        var fileInfo = new FileInfo(filePath);

        var metadata = new FetchResultMetadata
        {
            JobId = jobId,
            ChannelId = channelId,
            ChannelName = channelName,
            CreatedAt = DateTime.UtcNow,
            TotalComments = model.Comments.Count,
            TotalVideos = model.Videos.Count,
            UniqueAuthors = model.Comments.Select(c => c.AuthorDisplayName).Distinct().Count(),
            FilePath = _dataPathService.GetCommentsFilePath(jobId),
            FileSize = fileInfo.Length,
            OldestCommentDate = model.Comments.Where(c => c.PublishedAt.HasValue)
                .MinBy(c => c.PublishedAt)
                ?.PublishedAt,
            NewestCommentDate = model.Comments.Where(c => c.PublishedAt.HasValue)
                .MaxBy(c => c.PublishedAt)
                ?.PublishedAt,
            UserId = userId,
            IsIncomplete = isIncomplete,
        };

        _metadataIndex.AddOrUpdate(jobId, metadata, (_, _) => metadata);

        await SaveIndexAsync(cancellationToken);

        _logger.LogInformation("Результат выборки сохранен для задачи {JobId}. Размер файла: {FileSize} байт",
            jobId, fileInfo.Length);

        return metadata;
    }

    public async Task<YouTubeCommentsViewModel?> GetFetchResultAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var model = await ReadFetchResultFileAsync(jobId, cancellationToken);

        if (model != null)
        {
            model.OwnerKeyHash = null;
        }

        return model;
    }

    public async Task<FetchResultMetadata?> GetMetadataAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return null;
        }

        if (_metadataIndex.TryGetValue(jobId, out var metadata))
        {
            return metadata;
        }

        var filePath = _dataPathService.GetAbsoluteCommentsFilePath(jobId);

        if (File.Exists(filePath))
        {
            try
            {
                var model = await ReadFetchResultFileAsync(jobId, cancellationToken);

                if (model != null)
                {
                    var ownerUserId = ResolveOwnerUserId(model.OwnerKeyHash, await LoadOwnerKeysAsync());

                    if (ownerUserId == null)
                    {
                        _logger.LogWarning("Владелец результата {JobId} не определен: файл пользователей отсутствует или не прочитан, метаданные не выданы", jobId);
                        return null;
                    }

                    metadata = CreateMetadataFromFile(jobId, filePath, model, ownerUserId);
                    _metadataIndex.TryAdd(jobId, metadata);
                    await SaveIndexAsync(cancellationToken);
                    return metadata;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании метаданных для задачи {JobId}", jobId);
            }
        }

        return null;
    }

    public async Task<List<FetchResultMetadata>> GetAllFetchResultsAsync(CancellationToken cancellationToken = default)
    {
        await LoadIndexAsync(cancellationToken);
        return _metadataIndex.Values.OrderByDescending(m => m.CreatedAt).ToList();
    }

    public async Task<List<FetchResultMetadata>> GetUserFetchResultsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new();
        }

        await LoadIndexAsync(cancellationToken);

        return _metadataIndex.Values
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .ToList();
    }

    public async Task<bool> ExistsAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return false;
        }

        if (_metadataIndex.ContainsKey(jobId))
        {
            return true;
        }

        var filePath = _dataPathService.GetAbsoluteCommentsFilePath(jobId);
        return File.Exists(filePath);
    }

    public async Task<bool> DeleteFetchResultAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return false;
        }

        _logger.LogInformation("Удаление результата выборки для задачи {JobId}", jobId);

        var deleted = false;

        var filePath = _dataPathService.GetAbsoluteCommentsFilePath(jobId);

        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                deleted = true;
                _logger.LogInformation("Файл результата удален: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении файла {FilePath}", filePath);
            }
        }

        if (_metadataIndex.TryRemove(jobId, out _))
        {
            await SaveIndexAsync(cancellationToken);
            deleted = true;
        }

        return deleted;
    }

    public async Task<int> DeleteOlderThanAsync(int days, CancellationToken cancellationToken = default)
    {
        if (days < 0)
        {
            throw new ArgumentException("Количество дней должно быть положительным", nameof(days));
        }

        var cutoffDate = DateTime.UtcNow.AddDays(-days);

        var toDelete = _metadataIndex.Values
            .Where(m => m.CreatedAt < cutoffDate)
            .ToList();

        _logger.LogInformation("Удаление {Count} результатов старше {Days} дней", toDelete.Count, days);

        var deletedCount = 0;

        foreach (var metadata in toDelete)
        {
            if (await DeleteFetchResultAsync(metadata.JobId, cancellationToken))
            {
                deletedCount++;
            }
        }

        _logger.LogInformation("Удалено {DeletedCount} из {TotalCount} результатов", deletedCount, toDelete.Count);
        return deletedCount;
    }

    public async Task<FetchResultsStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await LoadIndexAsync(cancellationToken);

        var results = _metadataIndex.Values.ToList();

        return new()
        {
            TotalResults = results.Count,
            TotalFileSize = results.Sum(r => r.FileSize),
            TotalComments = results.Sum(r => r.TotalComments),
            OldestResultDate = results.Count > 0 ? results.Min(r => r.CreatedAt) : null,
            NewestResultDate = results.Count > 0 ? results.Max(r => r.CreatedAt) : null,
        };
    }

    public async Task<int> RebuildIndexAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Ручная перестройка индекса метаданных");

        await _indexLock.WaitAsync(cancellationToken);

        try
        {
            var initialCount = _metadataIndex.Count;
            await RebuildIndexInternalAsync(cancellationToken);
            var finalCount = _metadataIndex.Count;

            _logger.LogInformation("Ручная перестройка индекса завершена. Обработано файлов: {ProcessedCount}", finalCount);
            return finalCount;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task LoadIndexAsync(CancellationToken cancellationToken = default)
    {
        var indexPath = _dataPathService.GetAbsoluteFetchResultsIndexFilePath();
        var dataDirectory = _dataPathService.GetAbsoluteDataDirectory();

        await _indexLock.WaitAsync(cancellationToken);

        try
        {
            var shouldRebuildIndex = false;

            if (!File.Exists(indexPath))
            {
                _logger.LogInformation("Файл индекса не найден, будет выполнена автоматическая перестройка");
                shouldRebuildIndex = true;
            }
            else
            {
                try
                {
                    var json = await File.ReadAllTextAsync(indexPath, cancellationToken);
                    var metadata = JsonSerializer.Deserialize<List<FetchResultMetadata>>(json, JsonConfiguration.Default);

                    if (metadata != null)
                    {
                        _metadataIndex.Clear();

                        foreach (var item in metadata)
                        {
                            _metadataIndex.TryAdd(item.JobId, item);
                        }

                        if (Directory.Exists(dataDirectory))
                        {
                            var existingFiles = Directory.GetFiles(dataDirectory, "comments_*.json");
                            var filesInIndex = _metadataIndex.Keys.ToHashSet();

                            var missingFiles = existingFiles
                                .Select(Path.GetFileName)
                                .Where(fileName => fileName != null && fileName.StartsWith("comments_") && fileName.EndsWith(".json"))
                                .Select(fileName => fileName!.Substring(9, fileName.Length - 14))
                                .Where(jobId => filesInIndex.Contains(jobId) == false)
                                .ToList();

                            if (missingFiles.Count > 0)
                            {
                                _logger.LogInformation("Найдено {MissingCount} файлов, отсутствующих в индексе. Выполняется обновление индекса", missingFiles.Count);

                                var ownerKeys = await LoadOwnerKeysAsync();

                                foreach (var jobId in missingFiles)
                                {
                                    try
                                    {
                                        await CreateMetadataForExistingFileAsync(jobId, ownerKeys, cancellationToken);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Ошибка при создании метаданных для файла {JobId}", jobId);
                                    }
                                }

                                await SaveIndexInternalAsync(cancellationToken);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Индекс поврежден, будет выполнена перестройка");
                        shouldRebuildIndex = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при загрузке индекса метаданных, будет выполнена перестройка");
                    shouldRebuildIndex = true;
                }
            }

            if (shouldRebuildIndex)
            {
                await RebuildIndexInternalAsync(cancellationToken);
            }
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task SaveIndexAsync(CancellationToken cancellationToken = default)
    {
        await _indexLock.WaitAsync(cancellationToken);

        try
        {
            await SaveIndexInternalAsync(cancellationToken);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task SaveIndexInternalAsync(CancellationToken cancellationToken = default)
    {
        var indexPath = _dataPathService.GetAbsoluteFetchResultsIndexFilePath();

        try
        {
            var metadata = _metadataIndex.Values.ToList();
            var json = JsonSerializer.Serialize(metadata, JsonConfiguration.Default);
            await File.WriteAllTextAsync(indexPath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при сохранении индекса метаданных");
        }
    }

    private async Task RebuildIndexInternalAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Автоматическая перестройка индекса метаданных");

        _metadataIndex.Clear();

        var dataDirectory = _dataPathService.GetAbsoluteDataDirectory();

        if (Directory.Exists(dataDirectory) == false)
        {
            _logger.LogInformation("Директория данных не существует, индекс остается пустым");
            await SaveIndexInternalAsync(cancellationToken);
            return;
        }

        var pattern = "comments_*.json";
        var files = Directory.GetFiles(dataDirectory, pattern);
        var ownerKeys = await LoadOwnerKeysAsync();

        var processedCount = 0;

        foreach (var filePath in files)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);

                if (fileName.StartsWith("comments_") && fileName.EndsWith(".json"))
                {
                    var jobId = fileName.Substring(9, fileName.Length - 14);
                    await CreateMetadataForExistingFileAsync(jobId, ownerKeys, cancellationToken);
                    processedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке файла {FilePath}", filePath);
            }
        }

        await SaveIndexInternalAsync(cancellationToken);
        _logger.LogInformation("Автоматическая перестройка индекса завершена. Обработано файлов: {ProcessedCount}", processedCount);
    }

    private async Task<YouTubeCommentsViewModel?> ReadFetchResultFileAsync(string jobId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return null;
        }

        var filePath = _dataPathService.GetAbsoluteCommentsFilePath(jobId);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Файл результата не найден для задачи {JobId}: {FilePath}", jobId, filePath);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var model = JsonSerializer.Deserialize<YouTubeCommentsViewModel>(json, JsonConfiguration.Default);

            _logger.LogInformation("Результат выборки загружен для задачи {JobId}", jobId);
            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при загрузке результата выборки для задачи {JobId}", jobId);
            return null;
        }
    }

    private async Task CreateMetadataForExistingFileAsync(
        string jobId,
        OwnerKeys ownerKeys,
        CancellationToken cancellationToken = default)
    {
        var filePath = _dataPathService.GetAbsoluteCommentsFilePath(jobId);

        if (File.Exists(filePath) == false)
        {
            _logger.LogWarning("Файл не найден для создания метаданных: {JobId}", jobId);
            return;
        }

        try
        {
            var model = await ReadFetchResultFileAsync(jobId, cancellationToken);

            if (model != null)
            {
                var ownerUserId = ResolveOwnerUserId(model.OwnerKeyHash, ownerKeys);

                if (ownerUserId == null)
                {
                    _logger.LogWarning("Владелец результата {JobId} не определен: файл пользователей отсутствует или не прочитан, файл не добавлен в индекс", jobId);
                    return;
                }

                var metadata = CreateMetadataFromFile(jobId, filePath, model, ownerUserId);

                _metadataIndex.TryAdd(jobId, metadata);
                _logger.LogDebug("Созданы метаданные для существующего файла: {JobId}", jobId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании метаданных для файла {JobId}", jobId);
        }
    }

    private FetchResultMetadata CreateMetadataFromFile(
        string jobId,
        string filePath,
        YouTubeCommentsViewModel model,
        string ownerUserId)
    {
        var fileInfo = new FileInfo(filePath);

        return new()
        {
            JobId = jobId,
            ChannelId = string.IsNullOrWhiteSpace(model.ChannelId) ? "unknown" : model.ChannelId,
            ChannelName = model.ChannelName,
            CreatedAt = fileInfo.CreationTimeUtc,
            TotalComments = model.Comments.Count,
            TotalVideos = model.Videos.Count,
            UniqueAuthors = model.Comments.Select(c => c.AuthorDisplayName).Distinct().Count(),
            FilePath = _dataPathService.GetCommentsFilePath(jobId),
            FileSize = fileInfo.Length,
            OldestCommentDate = model.Comments.Where(c => c.PublishedAt.HasValue)
                .MinBy(c => c.PublishedAt)
                ?.PublishedAt,
            NewestCommentDate = model.Comments.Where(c => c.PublishedAt.HasValue)
                .MaxBy(c => c.PublishedAt)
                ?.PublishedAt,
            UserId = ownerUserId,
            IsIncomplete = model.IsIncomplete,
        };
    }

    private static string? ResolveOwnerUserId(string? ownerKeyHash, OwnerKeys ownerKeys)
    {
        if (string.IsNullOrWhiteSpace(ownerKeyHash))
        {
            return LegacyUserId;
        }

        if (ownerKeys.KeysByHash.TryGetValue(ownerKeyHash, out var apiKey))
        {
            return apiKey;
        }

        return ownerKeys.IsComplete ? LegacyUserId : null;
    }

    private async Task<OwnerKeys> LoadOwnerKeysAsync()
    {
        var (users, isComplete) = await _apiAuthService.GetAllUsersWithCompletenessAsync();

        var keysByHash = users.Select(user => user.ApiKey)
            .Where(apiKey => string.IsNullOrWhiteSpace(apiKey) == false)
            .Distinct()
            .ToDictionary(HashApiKey, apiKey => apiKey, StringComparer.OrdinalIgnoreCase);

        return new(keysByHash, isComplete);
    }

    private static string HashApiKey(string apiKey)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
    }

    private sealed record OwnerKeys(IReadOnlyDictionary<string, string> KeysByHash, bool IsComplete);
}
