namespace YouTubeCommentsFetcher.Web.Models;

/// <summary>
/// Модель представления для административной страницы со всеми результатами
/// </summary>
public class AdminAllResultsViewModel
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
    /// Словарь для сопоставления API ключей пользователей с их именами
    /// </summary>
    public required Dictionary<string, string> UserNames { get; init; }
}
