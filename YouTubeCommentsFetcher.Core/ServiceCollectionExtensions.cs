using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YouTubeCommentsFetcher.Core.Options;
using YouTubeCommentsFetcher.Core.Services;
using GoogleYouTubeService = Google.Apis.YouTube.v3.YouTubeService;

namespace YouTubeCommentsFetcher.Core;

public static class ServiceCollectionExtensions
{
    public const string YouTubeApiKeySetting = "YouTubeApiKey";
    public const string AdminSettingsSection = "AdminSettings";

    public static IServiceCollection AddYouTubeCommentsCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<GoogleYouTubeService>(_ => new(new()
        {
            ApiKey = configuration[YouTubeApiKeySetting],
            ApplicationName = "YouTubeCommentsFetcher",
        }));

        services.AddScoped<IYouTubeService, YouTubeService>();
        services.AddScoped<CommentsFetcher>();
        services.AddSingleton<IJobStatusService, InMemoryJobStatusService>();

        services.Configure<DataPathOptions>(configuration.GetSection(DataPathOptions.SectionName));
        services.Configure<AdminOptions>(configuration.GetSection(AdminSettingsSection));
        services.AddSingleton<IDataPathService, DataPathService>();
        services.AddSingleton<IFetchResultsService, FetchResultsService>();
        services.AddSingleton<IApiAuthService, JsonApiAuthService>();

        return services;
    }
}
