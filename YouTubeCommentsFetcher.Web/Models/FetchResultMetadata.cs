namespace YouTubeCommentsFetcher.Web.Models;

/// <summary>
/// Метаданные о результате выборки комментариев
/// </summary>
public class FetchResultMetadata
{
    /// <summary>
    /// Уникальный идентификатор задачи
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Идентификатор YouTube канала
    /// </summary>
    public required string ChannelId { get; init; }

    /// <summary>
    /// Название канала (если доступно)
    /// </summary>
    public string? ChannelName { get; init; }

    /// <summary>
    /// Дата и время создания результата
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Общее количество комментариев
    /// </summary>
    public required int TotalComments { get; init; }

    /// <summary>
    /// Общее количество видео
    /// </summary>
    public required int TotalVideos { get; init; }

    /// <summary>
    /// Количество уникальных авторов
    /// </summary>
    public required int UniqueAuthors { get; init; }

    /// <summary>
    /// Относительный путь к файлу с данными
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Размер файла в байтах
    /// </summary>
    public required long FileSize { get; init; }

    /// <summary>
    /// Дата самого старого комментария
    /// </summary>
    public DateTime? OldestCommentDate { get; init; }

    /// <summary>
    /// Дата самого нового комментария
    /// </summary>
    public DateTime? NewestCommentDate { get; init; }

    /// <summary>
    /// Идентификатор пользователя, создавшего результат (API ключ)
    /// </summary>
    public string? UserId { get; init; }
}

/// <summary>
/// Статистика по всем результатам выборки
/// </summary>
public class FetchResultsStatistics
{
    /// <summary>
    /// Общее количество сохраненных результатов
    /// </summary>
    public required int TotalResults { get; init; }

    /// <summary>
    /// Общий размер всех файлов в байтах
    /// </summary>
    public required long TotalFileSize { get; init; }

    /// <summary>
    /// Общее количество комментариев во всех результатах
    /// </summary>
    public required int TotalComments { get; init; }

    /// <summary>
    /// Дата создания самого старого результата
    /// </summary>
    public DateTime? OldestResultDate { get; init; }

    /// <summary>
    /// Дата создания самого нового результата
    /// </summary>
    public DateTime? NewestResultDate { get; init; }

    /// <summary>
    /// Средний размер файла в байтах
    /// </summary>
    public double AverageFileSize => TotalResults > 0 ? (double)TotalFileSize / TotalResults : 0;

    /// <summary>
    /// Среднее количество комментариев на результат
    /// </summary>
    public double AverageCommentsPerResult => TotalResults > 0 ? (double)TotalComments / TotalResults : 0;
}
