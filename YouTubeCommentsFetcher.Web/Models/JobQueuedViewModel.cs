namespace YouTubeCommentsFetcher.Web.Models;

/// <summary>
/// Модель представления для страницы ожидания выполнения задачи
/// </summary>
public class JobQueuedViewModel
{
    /// <summary>
    /// Идентификатор задачи
    /// </summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>
    /// URL для доступа к файлу с результатами
    /// </summary>
    public string DataFileUrl { get; set; } = string.Empty;

    /// <summary>
    /// Имя файла с результатами
    /// </summary>
    public string FileName { get; set; } = string.Empty;
}
