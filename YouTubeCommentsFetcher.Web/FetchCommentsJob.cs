using Quartz;
using System.Text.Json;
using YouTubeCommentsFetcher.Web.Models;
using YouTubeCommentsFetcher.Web.Services;

namespace YouTubeCommentsFetcher.Web;

public class FetchCommentsJob(
    IYouTubeService youTubeService,
    IJobStatusService statusService,
    ILogger<FetchCommentsJob> logger) : IJob
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
    };

    public async Task Execute(IJobExecutionContext context)
    {
        var dataMap = context.MergedJobDataMap;
        var jobId = dataMap.GetString("jobId")!;
        var channelId = dataMap.GetString("channelId")!;
        var pageSize = dataMap.GetInt("pageSize");
        var maxPages = dataMap.GetInt("maxPages");

        logger.LogInformation("Background fetch started for channel {ChannelId}", channelId);

        var uploadsPlaylistId = await youTubeService.GetUploadsPlaylistIdAsync(channelId);

        if (uploadsPlaylistId == null)
        {
            logger.LogWarning("Channel {ChannelId} not found", channelId);
            return;
        }

        var videoIds = await youTubeService.GetVideoIdsFromPlaylistAsync(uploadsPlaylistId, pageSize, maxPages);
        var model = new YouTubeCommentsViewModel();

        var total = videoIds.Count;

        for (var i = 0; i < total; i++)
        {
            var videoId = videoIds[i];
            var comments = await youTubeService.GetVideoCommentsAsync(videoId);
            model.Videos.Add(comments);
            var percent = (int)Math.Round((i + 1) * 100.0 / total);
            statusService.ReportProgress(jobId, percent);
        }

        model.Comments = model.Videos.SelectMany(v => v.Comments).ToList();
        model.Statistics = Analyzer.Analyze(model.Comments, model.Videos);

        var json = JsonSerializer.Serialize(model, JsonSerializerOptions);
        var path = Path.Combine("Data", $"comments_{jobId}.json");
        Directory.CreateDirectory("Data");
        await File.WriteAllTextAsync(path, json);

        logger.LogInformation("Background fetch completed, data saved to {FileName}", path);
        statusService.MarkCompleted(jobId);
    }
}
