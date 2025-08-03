namespace YouTubeCommentsFetcher.Web.Options;

/// <summary>
/// Настройки администратора приложения
/// </summary>
public class AdminOptions
{
    /// <summary>
    /// API ключ администратора для доступа к административным функциям
    /// </summary>
    public string AdminApiKey { get; set; } = string.Empty;
}
