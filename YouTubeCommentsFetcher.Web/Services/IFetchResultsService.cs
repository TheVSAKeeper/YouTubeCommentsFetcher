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
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Метаданные сохраненного результата</returns>
    Task<FetchResultMetadata> SaveFetchResultAsync(
        string jobId,
        string channelId,
        YouTubeCommentsViewModel model,
        string? channelName = null,
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
