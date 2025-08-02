using Microsoft.Extensions.Options;

namespace YouTubeCommentsFetcher.Web.Services;

/// <summary>
/// Сервис для управления путями к директории данных
/// </summary>
public interface IDataPathService
{
    /// <summary>
    /// Относительное имя директории данных
    /// </summary>
    string RelativeDataDirectory { get; }

    /// <summary>
    /// Получить абсолютный путь к директории данных
    /// </summary>
    /// <returns>Абсолютный путь к директории данных</returns>
    string GetAbsoluteDataDirectory();

    /// <summary>
    /// Получить путь к файлу данных относительно директории данных
    /// </summary>
    /// <param name="fileName">Имя файла</param>
    /// <returns>Путь к файлу</returns>
    string GetDataFilePath(string fileName);

    /// <summary>
    /// Получить URL для доступа к файлу данных
    /// </summary>
    /// <param name="fileName">Имя файла</param>
    /// <returns>URL для доступа к файлу</returns>
    string GetDataFileUrl(string fileName);

    /// <summary>
    /// Создать директорию данных если она не существует
    /// </summary>
    void EnsureDataDirectoryExists();

    /// <summary>
    /// Получить стандартное имя файла комментариев для задачи
    /// </summary>
    /// <param name="jobId">Идентификатор задачи</param>
    /// <returns>Стандартное имя файла</returns>
    string GetCommentsFileName(string jobId);

    /// <summary>
    /// Получить путь к файлу комментариев для задачи
    /// </summary>
    /// <param name="jobId">Идентификатор задачи</param>
    /// <returns>Путь к файлу комментариев</returns>
    string GetCommentsFilePath(string jobId);

    /// <summary>
    /// Получить URL для доступа к файлу комментариев задачи
    /// </summary>
    /// <param name="jobId">Идентификатор задачи</param>
    /// <returns>URL для доступа к файлу комментариев</returns>
    string GetCommentsFileUrl(string jobId);

    /// <summary>
    /// Получить имя файла для экспорта комментариев с временной меткой
    /// </summary>
    /// <returns>Имя файла для экспорта</returns>
    string GetExportFileName();
}

public class DataPathService(IOptions<DataPathOptions> options, IWebHostEnvironment environment) : IDataPathService
{
    private readonly DataPathOptions _options = options.Value;

    public string RelativeDataDirectory => _options.DataDirectory;

    public string GetAbsoluteDataDirectory()
    {
        return Path.Combine(environment.ContentRootPath, _options.DataDirectory);
    }

    public string GetDataFilePath(string fileName)
    {
        return Path.Combine(_options.DataDirectory, fileName);
    }

    public string GetDataFileUrl(string fileName)
    {
        return $"/{_options.DataDirectory}/{fileName}";
    }

    public void EnsureDataDirectoryExists()
    {
        var absolutePath = GetAbsoluteDataDirectory();
        Directory.CreateDirectory(absolutePath);
    }

    public string GetCommentsFileName(string jobId)
    {
        return $"comments_{jobId}.json";
    }

    public string GetCommentsFilePath(string jobId)
    {
        return GetDataFilePath(GetCommentsFileName(jobId));
    }

    public string GetCommentsFileUrl(string jobId)
    {
        return GetDataFileUrl(GetCommentsFileName(jobId));
    }

    public string GetExportFileName()
    {
        return $"youtube_comments_{DateTime.Now:yyyyMMddHHmmss}.json";
    }
}
