using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YouTubeCommentsFetcher.Core.Configuration;
using YouTubeCommentsFetcher.Core.Models;

namespace YouTubeCommentsFetcher.Core.Services;

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
    private static readonly TimeSpan IndexWriteLockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IndexReadLockTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IndexFileLockRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan FileShareRetryDelay = TimeSpan.FromMilliseconds(200);
    private const int FileShareRetryAttempts = 10;
    private const int SharingViolationHResult = unchecked((int)0x80070020);
    private const int LockViolationHResult = unchecked((int)0x80070021);

    private readonly ILogger<FetchResultsService> _logger;
    private readonly SemaphoreSlim _indexLock;
    private readonly List<IndexChange> _pendingChanges = [];
    private ConcurrentDictionary<string, FetchResultMetadata> _metadataIndex;
    private FileStamp? _indexStamp;

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

        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Файл результата {JobId} не записан во временный файл {TempPath}, недописанный файл удаляется", jobId, tempPath);

            try
            {
                File.Delete(tempPath);
            }
            catch (Exception deleteException)
            {
                _logger.LogWarning(deleteException, "Недописанный временный файл {TempPath} не удален", tempPath);
            }

            throw;
        }

        try
        {
            await RetryOnSharingViolationAsync(() => File.Move(tempPath, filePath, true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Файл результата {JobId} не перенесен на место, собранные данные остались во временном файле {TempPath}", jobId, tempPath);
            throw;
        }

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

        await UpdateIndexAsync(new(IndexChangeKind.Upsert, jobId, metadata), null, cancellationToken);

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

        var deadline = DateTime.UtcNow + IndexReadLockTimeout;

        await RefreshIndexAsync(deadline, cancellationToken);

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

                    var created = CreateMetadataFromFile(jobId, filePath, model, ownerUserId);
                    await UpdateIndexAsync(new(IndexChangeKind.AddIfAbsent, jobId, created), deadline, cancellationToken);
                    return _metadataIndex.TryGetValue(jobId, out var stored) ? stored : created;
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

        await RefreshIndexAsync(DateTime.UtcNow + IndexReadLockTimeout, cancellationToken);

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
                await RetryOnSharingViolationAsync(() => File.Delete(filePath));
                deleted = true;
                _logger.LogInformation("Файл результата удален: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении файла {FilePath}, запись индекса оставлена", filePath);
                return false;
            }
        }

        if (await UpdateIndexAsync(new(IndexChangeKind.Remove, jobId), null, cancellationToken))
        {
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

        await LoadIndexAsync(cancellationToken);

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

        var scannedRows = await ScanResultFilesAsync(ListResultJobIds(), cancellationToken);

        await _indexLock.WaitAsync(cancellationToken);

        try
        {
            var result = await MergeIndexAsync(IndexWriteLockTimeout, scannedRows, true, true, cancellationToken);

            if (result.Status == IndexMergeStatus.LockUnavailable)
            {
                throw new TimeoutException("Индекс метаданных занят другим процессом, перестройка не выполнена");
            }

            if (result.Status == IndexMergeStatus.NotSaved)
            {
                throw new IOException("Индекс метаданных перестроен, но не записан на диск");
            }

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
        var refreshStatus = await RefreshIndexAsync(DateTime.UtcNow + IndexReadLockTimeout, cancellationToken);

        if (refreshStatus == IndexMergeStatus.LockUnavailable)
        {
            return;
        }

        var fullScan = refreshStatus == IndexMergeStatus.NeedsScan;
        var existingJobIds = ListResultJobIds();
        var memoryIndex = _metadataIndex;

        var missingJobIds = existingJobIds
            .Where(jobId => fullScan || memoryIndex.ContainsKey(jobId) == false)
            .ToList();

        var hasOrphanedRows = memoryIndex.Keys.Any(jobId => existingJobIds.Contains(jobId) == false);

        if (fullScan == false && missingJobIds.Count == 0 && hasOrphanedRows == false)
        {
            return;
        }

        if (fullScan)
        {
            _logger.LogInformation("Индекс отсутствует или не прочитан, будет выполнена автоматическая перестройка");
        }
        else if (missingJobIds.Count > 0)
        {
            _logger.LogInformation("Найдено {MissingCount} файлов, отсутствующих в индексе. Выполняется обновление индекса", missingJobIds.Count);
        }

        var scannedRows = await ScanResultFilesAsync(missingJobIds, cancellationToken);

        if (fullScan == false && scannedRows.Count == 0 && hasOrphanedRows == false)
        {
            return;
        }

        var deadline = DateTime.UtcNow + IndexReadLockTimeout;

        if (await _indexLock.WaitAsync(IndexReadLockTimeout, cancellationToken) == false)
        {
            _logger.LogWarning("Индекс метаданных занят, сверка с файлами отложена");
            return;
        }

        try
        {
            await MergeIndexAsync(RemainingUntil(deadline), scannedRows, fullScan, false, cancellationToken);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task<IndexMergeStatus> RefreshIndexAsync(DateTime deadline, CancellationToken cancellationToken)
    {
        if (IsMemoryIndexCurrent())
        {
            return IndexMergeStatus.Merged;
        }

        if (await _indexLock.WaitAsync(RemainingUntil(deadline), cancellationToken) == false)
        {
            _logger.LogWarning("Индекс метаданных занят, отдан снимок из памяти");
            return IndexMergeStatus.LockUnavailable;
        }

        try
        {
            if (IsMemoryIndexCurrent())
            {
                return IndexMergeStatus.Merged;
            }

            var result = await MergeIndexAsync(RemainingUntil(deadline), null, false, false, cancellationToken);

            if (result.Status == IndexMergeStatus.LockUnavailable && _indexStamp == null)
            {
                await LoadIndexSnapshotWithoutLockAsync(cancellationToken);
            }

            return result.Status;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private bool IsMemoryIndexCurrent()
    {
        var stamp = _indexStamp;
        return stamp != null && _pendingChanges.Count == 0 && stamp == ReadIndexStamp();
    }

    private async Task LoadIndexSnapshotWithoutLockAsync(CancellationToken cancellationToken)
    {
        var index = await ReadIndexFileAsync(cancellationToken);

        if (index == null)
        {
            return;
        }

        foreach (var change in _pendingChanges)
        {
            change.ApplyTo(index, null);
        }

        _logger.LogWarning("Индекс метаданных прочитан без блокировки: файл блокировки занят");
        PublishIndex(index);
    }

    // TODO: очередь несохранённых изменений живёт только в памяти процесса – если процесс упадёт до удачной записи, изменение существующей строки пропадёт (новую строку вернёт сверка с файлами с датой файла), а свежесть индекса определяется по времени записи и длине файла, и на томе с грубым временем две записи одной длины за один тик не видны до следующего изменения; если это начнёт проявляться – журнал изменений рядом с индексом и счётчик поколения в отдельном файле
    private async Task<bool> UpdateIndexAsync(IndexChange change, DateTime? incidentalDeadline, CancellationToken cancellationToken)
    {
        var isIncidental = incidentalDeadline != null;

        if (await _indexLock.WaitAsync(incidentalDeadline is { } semaphoreDeadline ? RemainingUntil(semaphoreDeadline) : Timeout.InfiniteTimeSpan, cancellationToken) == false)
        {
            _logger.LogWarning("Индекс метаданных занят, попутное изменение {JobId} пропущено до следующей сверки с файлами", change.JobId);
            return false;
        }

        try
        {
            _pendingChanges.Add(change);

            var memoryIndex = new Dictionary<string, FetchResultMetadata>(_metadataIndex);
            var appliedInMemory = change.ApplyTo(memoryIndex, null);

            var lockTimeout = incidentalDeadline is { } fileLockDeadline ? RemainingUntil(fileLockDeadline) : IndexWriteLockTimeout;
            var result = await MergeIndexAsync(lockTimeout, null, false, false, cancellationToken);

            if (result.Status == IndexMergeStatus.NeedsScan && isIncidental)
            {
                _logger.LogWarning("Индекс отсутствует или не прочитан, попутное изменение {JobId} ждет следующей записи", change.JobId);
                PublishIndex(memoryIndex);
                return appliedInMemory;
            }

            if (result.Status == IndexMergeStatus.NeedsScan)
            {
                _logger.LogWarning("Индекс отсутствует или не прочитан, перед записью он перестраивается по файлам");

                var scannedRows = await ScanResultFilesAsync(ListResultJobIds(), cancellationToken);
                result = await MergeIndexAsync(IndexWriteLockTimeout, scannedRows, true, false, cancellationToken);
            }

            if (result.Status == IndexMergeStatus.LockUnavailable)
            {
                _logger.LogError("Индекс метаданных не сохранен: изменение {JobId} ждет следующей записи, в очереди {PendingCount}", change.JobId, _pendingChanges.Count);
                PublishIndex(memoryIndex);
                return appliedInMemory;
            }

            if (result.Status == IndexMergeStatus.NotSaved)
            {
                _logger.LogError("Индекс метаданных не записан на диск: изменение {JobId} ждет следующей записи, в очереди {PendingCount}", change.JobId, _pendingChanges.Count);
            }

            return result.LastChangeApplied;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task<IndexMergeResult> MergeIndexAsync(
        TimeSpan lockTimeout,
        IReadOnlyDictionary<string, ScannedRow>? scannedRows,
        bool fullScan,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        FileStream fileLock;

        try
        {
            fileLock = await AcquireIndexFileLockAsync(lockTimeout, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Файл блокировки индекса недоступен");
            return new(IndexMergeStatus.LockUnavailable, false);
        }

        await using (fileLock)
        {
            var diskIndex = await ReadIndexFileAsync(cancellationToken);

            if (diskIndex == null && fullScan == false)
            {
                return new(IndexMergeStatus.NeedsScan, false);
            }

            var index = diskIndex ?? new Dictionary<string, FetchResultMetadata>();
            var changed = diskIndex == null;
            var existingJobIds = ListResultJobIds();

            if (scannedRows != null)
            {
                foreach (var (jobId, scanned) in scannedRows)
                {
                    if ((replaceExisting || index.ContainsKey(jobId) == false)
                        && scanned.Stamp == ReadFileStamp(_dataPathService.GetAbsoluteCommentsFilePath(jobId)))
                    {
                        index[jobId] = scanned.Metadata;
                        changed = true;
                    }
                }
            }

            var lastChangeApplied = false;

            foreach (var change in _pendingChanges)
            {
                lastChangeApplied = change.ApplyTo(index, existingJobIds);
                changed |= lastChangeApplied;
            }

            var orphanedJobIds = index.Keys.Where(jobId => existingJobIds.Contains(jobId) == false).ToList();

            if (orphanedJobIds.Count > 0)
            {
                _logger.LogInformation("Найдено {OrphanedCount} записей индекса без файла результата. Записи удаляются из индекса", orphanedJobIds.Count);

                foreach (var jobId in orphanedJobIds)
                {
                    index.Remove(jobId);
                }

                changed = true;
            }

            PublishIndex(index);

            if (changed && await WriteIndexFileAsync(index, cancellationToken) == false)
            {
                _indexStamp = null;
                return new(IndexMergeStatus.NotSaved, lastChangeApplied);
            }

            _pendingChanges.Clear();
            _indexStamp = ReadIndexStamp();
            return new(IndexMergeStatus.Merged, lastChangeApplied);
        }
    }

    private async Task<FileStream> AcquireIndexFileLockAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var lockPath = _dataPathService.GetAbsoluteFetchResultsIndexFilePath() + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            try
            {
                return new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(IndexFileLockRetryDelay, cancellationToken);
            }
            catch (IOException ex)
            {
                throw new TimeoutException($"Файл блокировки индекса {lockPath} занят дольше {timeout.TotalSeconds} с", ex);
            }
        }
    }

    private async Task<Dictionary<string, FetchResultMetadata>?> ReadIndexFileAsync(CancellationToken cancellationToken)
    {
        var indexPath = _dataPathService.GetAbsoluteFetchResultsIndexFilePath();

        if (File.Exists(indexPath) == false)
        {
            return null;
        }

        try
        {
            var json = await ReadSharedTextAsync(indexPath, cancellationToken);
            var metadata = JsonSerializer.Deserialize<List<FetchResultMetadata>>(json, JsonConfiguration.Default);

            if (metadata == null)
            {
                _logger.LogWarning("Индекс поврежден: {IndexPath}", indexPath);
                return null;
            }

            var index = new Dictionary<string, FetchResultMetadata>();

            foreach (var item in metadata)
            {
                index.TryAdd(item.JobId, item);
            }

            return index;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Ошибка при чтении индекса метаданных");
            return null;
        }
    }

    private async Task<bool> WriteIndexFileAsync(Dictionary<string, FetchResultMetadata> index, CancellationToken cancellationToken)
    {
        var indexPath = _dataPathService.GetAbsoluteFetchResultsIndexFilePath();
        var tempPath = indexPath + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(index.Values.ToList(), JsonConfiguration.Default);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            await RetryOnSharingViolationAsync(() => File.Move(tempPath, indexPath, true));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при сохранении индекса метаданных");
            return false;
        }
    }

    private void PublishIndex(Dictionary<string, FetchResultMetadata> index)
    {
        _metadataIndex = new(index);
    }

    private async Task<Dictionary<string, ScannedRow>> ScanResultFilesAsync(
        IReadOnlyCollection<string> jobIds,
        CancellationToken cancellationToken)
    {
        var rows = new Dictionary<string, ScannedRow>();

        if (jobIds.Count == 0)
        {
            return rows;
        }

        var ownerKeys = await LoadOwnerKeysAsync();

        foreach (var jobId in jobIds)
        {
            var stamp = ReadFileStamp(_dataPathService.GetAbsoluteCommentsFilePath(jobId));

            if (stamp == null)
            {
                continue;
            }

            var metadata = await CreateMetadataForExistingFileAsync(jobId, ownerKeys, cancellationToken);

            if (metadata != null)
            {
                rows[jobId] = new(metadata, stamp);
            }
        }

        return rows;
    }

    private HashSet<string> ListResultJobIds()
    {
        var dataDirectory = _dataPathService.GetAbsoluteDataDirectory();

        if (Directory.Exists(dataDirectory) == false)
        {
            return [];
        }

        return Directory.GetFiles(dataDirectory, "comments_*.json")
            .Select(Path.GetFileName)
            .Where(fileName => fileName != null && fileName.StartsWith("comments_") && fileName.EndsWith(".json"))
            .Select(fileName => fileName!.Substring(9, fileName.Length - 14))
            .ToHashSet();
    }

    private FileStamp? ReadIndexStamp()
    {
        return ReadFileStamp(_dataPathService.GetAbsoluteFetchResultsIndexFilePath());
    }

    private static async Task<string> ReadSharedTextAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task RetryOnSharingViolationAsync(Action action)
    {
        for (var attempt = 1;; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (IsSharingViolation(ex) && attempt < FileShareRetryAttempts)
            {
                await Task.Delay(FileShareRetryDelay);
            }
        }
    }

    private static bool IsSharingViolation(Exception exception)
    {
        return exception is UnauthorizedAccessException
            or IOException { HResult: SharingViolationHResult or LockViolationHResult };
    }

    private static TimeSpan RemainingUntil(DateTime deadline)
    {
        var remaining = deadline - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static FileStamp? ReadFileStamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new(info.LastWriteTimeUtc, info.Length) : null;
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
            var json = await ReadSharedTextAsync(filePath, cancellationToken);
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

    private async Task<FetchResultMetadata?> CreateMetadataForExistingFileAsync(
        string jobId,
        OwnerKeys ownerKeys,
        CancellationToken cancellationToken = default)
    {
        var filePath = _dataPathService.GetAbsoluteCommentsFilePath(jobId);

        if (File.Exists(filePath) == false)
        {
            _logger.LogWarning("Файл не найден для создания метаданных: {JobId}", jobId);
            return null;
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
                    return null;
                }

                var metadata = CreateMetadataFromFile(jobId, filePath, model, ownerUserId);

                _logger.LogDebug("Созданы метаданные для существующего файла: {JobId}", jobId);
                return metadata;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании метаданных для файла {JobId}", jobId);
        }

        return null;
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

    private sealed record FileStamp(DateTime LastWriteTimeUtc, long Length);

    private sealed record ScannedRow(FetchResultMetadata Metadata, FileStamp Stamp);

    private sealed record IndexMergeResult(IndexMergeStatus Status, bool LastChangeApplied);

    private sealed record IndexChange(IndexChangeKind Kind, string JobId, FetchResultMetadata? Metadata = null)
    {
        public bool ApplyTo(Dictionary<string, FetchResultMetadata> index, IReadOnlySet<string>? existingJobIds)
        {
            if (Kind == IndexChangeKind.Remove)
            {
                return index.Remove(JobId);
            }

            if (Metadata == null || existingJobIds?.Contains(JobId) == false)
            {
                return false;
            }

            switch (Kind)
            {
                case IndexChangeKind.Upsert:
                    index[JobId] = Metadata;
                    return true;
                case IndexChangeKind.AddIfAbsent:
                    return index.TryAdd(JobId, Metadata);
                default:
                    return false;
            }
        }
    }

    private enum IndexChangeKind
    {
        None = 0,
        Upsert = 1,
        AddIfAbsent = 2,
        Remove = 3,
    }

    private enum IndexMergeStatus
    {
        None = 0,
        Merged = 1,
        NotSaved = 2,
        NeedsScan = 3,
        LockUnavailable = 4,
    }
}
