namespace YouTubeCommentsFetcher.Web.Models;

/// <summary>
/// Информация о выполняющейся задаче
/// </summary>
public class RunningJobInfo
{
    /// <summary>
    /// Идентификатор задачи
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Прогресс выполнения (0-100)
    /// </summary>
    public required int Progress { get; init; }

    /// <summary>
    /// Время начала задачи
    /// </summary>
    public required DateTime StartTime { get; init; }

    /// <summary>
    /// Идентификатор канала (если доступен)
    /// </summary>
    public string? ChannelId { get; init; }

    /// <summary>
    /// Время выполнения задачи
    /// </summary>
    public TimeSpan Duration => DateTime.UtcNow - StartTime;

    /// <summary>
    /// Статус задачи в текстовом виде
    /// </summary>
    public string StatusText => Progress switch
    {
        0 => "Инициализация...",
        < 10 => "Получение информации о канале...",
        < 50 => "Загрузка комментариев...",
        < 90 => "Обработка данных...",
        < 100 => "Сохранение результатов...",
        _ => "Завершение...",
    };
}
