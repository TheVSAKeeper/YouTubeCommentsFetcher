using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using YouTubeCommentsFetcher.Core;
using YouTubeCommentsFetcher.Core.Services;

namespace YouTubeCommentsFetcher.Cli;

public sealed class CliHost : IDisposable
{
    private const string DataDirectorySetting = $"{DataPathOptions.SectionName}:{nameof(DataPathOptions.DataDirectory)}";

    private readonly IHost _host;

    private CliHost(IHost host, string dataDirectory)
    {
        _host = host;
        DataDirectory = dataDirectory;
    }

    public string DataDirectory { get; }

    public IServiceProvider Services => _host.Services;

    public IConfiguration Configuration => _host.Services.GetRequiredService<IConfiguration>();

    public static CliHost Create(string? dataPathArgument)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });

        builder.Configuration
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .AddUserSecrets(typeof(CliHost).Assembly, optional: true)
            .AddEnvironmentVariables();

        var dataDirectory = ResolveDataDirectory(dataPathArgument ?? builder.Configuration[DataDirectorySetting]);
        var parentDirectory = Path.GetDirectoryName(dataDirectory);
        builder.Environment.ContentRootPath = parentDirectory ?? dataDirectory;

        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddYouTubeCommentsCore(builder.Configuration);
        builder.Services.Configure<DataPathOptions>(options =>
            options.DataDirectory = parentDirectory == null ? "." : Path.GetFileName(dataDirectory));

        return new(builder.Build(), dataDirectory);
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private static string ResolveDataDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CliException($"Не задан каталог данных. Укажите его через --data-path или настройку {DataDirectorySetting}.");
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

        if (Directory.Exists(fullPath) == false)
        {
            throw new CliException($"Каталог данных не найден: {fullPath}. Запустите команду из корня репозитория или укажите каталог через --data-path.");
        }

        return fullPath;
    }
}
