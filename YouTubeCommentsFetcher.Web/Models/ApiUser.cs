namespace YouTubeCommentsFetcher.Web.Models;

/// <summary>
/// Модель пользователя API для системы аутентификации
/// </summary>
public class ApiUser
{
    /// <summary>
    /// Уникальный API ключ пользователя в формате GUID
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>
    /// Имя пользователя для отображения
    /// </summary>
    public required string UserName { get; set; }

    /// <summary>
    /// Дата и время создания пользователя
    /// </summary>
    public required DateTime CreatedAt { get; set; }

    /// <summary>
    /// Активен ли пользователь (может ли использовать API)
    /// </summary>
    public required bool IsActive { get; set; }
}
