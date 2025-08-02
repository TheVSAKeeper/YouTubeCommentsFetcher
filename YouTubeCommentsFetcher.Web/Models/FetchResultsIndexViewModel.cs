namespace YouTubeCommentsFetcher.Web.Models;

/// <summary>
/// Модель представления для страницы управления результатами выборки
/// </summary>
public class FetchResultsIndexViewModel
{
    /// <summary>
    /// Список всех результатов выборки
    /// </summary>
    public required List<FetchResultMetadata> Results { get; init; }

    /// <summary>
    /// Общая статистика по результатам
    /// </summary>
    public required FetchResultsStatistics Statistics { get; init; }

    /// <summary>
    /// Список выполняющихся задач
    /// </summary>
    public required List<RunningJobInfo> RunningJobs { get; init; }
}
