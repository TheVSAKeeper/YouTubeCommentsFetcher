namespace YouTubeCommentsFetcher.Web.Services;

/// <summary>
/// Опции конфигурации для путей к данным
/// </summary>
public class DataPathOptions
{
    public const string SectionName = "DataPath";

    /// <summary>
    /// Имя директории для хранения данных (по умолчанию "Data")
    /// </summary>
    public string DataDirectory { get; set; } = "Data";
}
