using Google;
using YouTubeCommentsFetcher.Core.Models;

namespace YouTubeCommentsFetcher.Core.Services;

public enum FetchOutcomeStatus
{
    None = 0,
    Completed = 1,
    Incomplete = 2,
    Failed = 3,
}

public record FetchOutcome(FetchOutcomeStatus Status, string? Message = null);

public class CommentsFetcher(
    IYouTubeService youTubeService,
    IFetchResultsService fetchResultsService,
    ILogger<CommentsFetcher> logger)
{
    public const int MaxPlaylistPages = 100;

    private const int MaxRateLimitAttempts = 3;
    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromSeconds(10);

    private const string ChannelNotFoundMessage = "Канал не найден. Проверьте идентификатор канала и запустите сбор ещё раз.";
    private const string QuotaFailedMessage = "Дневная квота запросов к YouTube исчерпана, собрать комментарии не удалось. Квота обновляется раз в сутки – попробуйте завтра.";
    private const string RateLimitFailedMessage = "YouTube временно ограничил число запросов. Подождите несколько минут и запустите сбор ещё раз.";
    private const string UnexpectedFailedMessage = "Сбор не удался из-за ошибки на сервере. Попробуйте запустить его ещё раз позже.";
    private const string QuotaIncompleteMessage = "Неполный результат: дневная квота запросов к YouTube исчерпана. Всё, что успели собрать, сохранено. Чтобы собрать остальное, запустите сбор завтра.";
    private const string RateLimitIncompleteMessage = "Неполный результат: YouTube временно ограничил число запросов. Всё, что успели собрать, сохранено. Чтобы собрать остальное, запустите сбор ещё раз через несколько минут.";
    private const string VideosFailedIncompleteMessage = "Неполный результат: у {0} видео из {1} не удалось получить комментарии. Всё остальное сохранено. Чтобы собрать недостающее, запустите сбор ещё раз позже.";
    private const string InterruptedIncompleteMessage ="Неполный результат: сбор прервался из-за ошибки. Всё, что успели собрать, сохранено. Чтобы собрать остальное, запустите сбор ещё раз позже.";

    public async Task<FetchOutcome> RunAsync(string jobId, string channelId, int maxPages, string? userId, Action<int>? reportProgress = null)
    {
        logger.LogInformation("Background fetch started for channel {ChannelId}", channelId);

        try
        {
            return await FetchAsync(jobId, channelId, maxPages, userId, reportProgress ?? (_ => { }));
        }
        catch (YouTubeApiException ex)
        {
            logger.LogWarning(ex, "Background fetch stopped by YouTube API for job {JobId}: {Kind}", jobId, ex.Kind);
            return new(FetchOutcomeStatus.Failed, ex.Kind == YouTubeApiErrorKind.QuotaExceeded ? QuotaFailedMessage : RateLimitFailedMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background fetch failed for job {JobId}", jobId);
            return new(FetchOutcomeStatus.Failed, UnexpectedFailedMessage);
        }
    }

    private async Task<FetchOutcome> FetchAsync(string jobId, string channelId, int maxPages, string? userId, Action<int> reportProgress)
    {
        var uploadsPlaylistId = await WithRateLimitRetryAsync(() => youTubeService.GetUploadsPlaylistIdAsync(channelId));

        if (uploadsPlaylistId == null)
        {
            logger.LogWarning("Channel {ChannelId} not found", channelId);
            return new(FetchOutcomeStatus.Failed, ChannelNotFoundMessage);
        }

        var model = new YouTubeCommentsViewModel();
        string? incompleteMessage = null;

        try
        {
            var failedVideos = await CollectCommentsAsync(uploadsPlaylistId, maxPages, model, reportProgress);

            if (failedVideos > 0)
            {
                logger.LogWarning("Comments of {FailedVideos} of {Total} videos were not fetched for job {JobId}",
                    failedVideos, model.Videos.Count, jobId);

                incompleteMessage = string.Format(VideosFailedIncompleteMessage, failedVideos, model.Videos.Count);
            }
        }
        catch (Exception ex) when (model.Videos.Count > 0)
        {
            logger.LogWarning(ex, "Fetch stopped after {Processed} videos for job {JobId}", model.Videos.Count, jobId);

            incompleteMessage = ex switch
            {
                YouTubeApiException { Kind: YouTubeApiErrorKind.QuotaExceeded } => QuotaIncompleteMessage,
                YouTubeApiException { Kind: YouTubeApiErrorKind.RateLimited } => RateLimitIncompleteMessage,
                _ => InterruptedIncompleteMessage,
            };
        }

        model.Comments = model.Videos.SelectMany(v => v.Comments).ToList();
        model.Statistics = Analyzer.Analyze(model.Comments, model.Videos);

        var isIncomplete = incompleteMessage != null;
        await fetchResultsService.SaveFetchResultAsync(jobId, channelId, model, userId: userId, isIncomplete: isIncomplete);

        if (isIncomplete)
        {
            logger.LogInformation("Background fetch saved incomplete result for job {JobId}", jobId);
            return new(FetchOutcomeStatus.Incomplete, incompleteMessage);
        }

        logger.LogInformation("Background fetch completed, data saved for job {JobId}", jobId);
        return new(FetchOutcomeStatus.Completed);
    }

    private async Task<int> CollectCommentsAsync(string uploadsPlaylistId, int maxPages, YouTubeCommentsViewModel model, Action<int> reportProgress)
    {
        var failedVideos = 0;
        var expectedVideos = maxPages * YouTubeService.PlaylistPageSize;
        string? pageToken = null;
        var page = 0;

        do
        {
            var token = pageToken;
            var playlistPage = await WithRateLimitRetryAsync(() => youTubeService.GetPlaylistPageAsync(uploadsPlaylistId, token));
            page++;

            if (playlistPage.Videos.Count == 0)
            {
                break;
            }

            pageToken = playlistPage.NextPageToken;

            if (pageToken == null || page >= maxPages)
            {
                expectedVideos = model.Videos.Count + playlistPage.Videos.Count;
            }
            else if (playlistPage.TotalResults is > 0)
            {
                expectedVideos = Math.Min(expectedVideos, playlistPage.TotalResults.Value);
            }

            foreach (var batch in playlistPage.Videos.Chunk(YouTubeService.StatsBatchSize))
            {
                var commentCounts = await GetCommentCountsAsync(batch);

                foreach (var video in batch)
                {
                    var comments = commentCounts.TryGetValue(video.VideoId, out var count) && count == 0
                        ? []
                        : await GetVideoCommentsAsync(video.VideoId);

                    if (comments == null)
                    {
                        failedVideos++;
                    }

                    model.Videos.Add(new()
                    {
                        VideoId = video.VideoId,
                        VideoTitle = video.Title,
                        VideoUrl = $"https://www.youtube.com/watch?v={video.VideoId}",
                        ThumbnailUrl = video.ThumbnailUrl,
                        Comments = comments ?? [],
                    });

                    var percent = Math.Min(99, (int)Math.Round(model.Videos.Count * 100.0 / Math.Max(expectedVideos, model.Videos.Count)));
                    reportProgress(percent);
                }
            }
        } while (pageToken != null && page < maxPages);

        logger.LogInformation("Обработано {VideoCount} видео из плейлиста {UploadsPlaylistId} за {Pages} страниц", model.Videos.Count, uploadsPlaylistId, page);
        return failedVideos;
    }

    private async Task<Dictionary<string, long?>> GetCommentCountsAsync(PlaylistVideo[] batch)
    {
        try
        {
            return await WithRateLimitRetryAsync(() => youTubeService.GetCommentCountsAsync(batch.Select(v => v.VideoId).ToList()));
        }
        catch (GoogleApiException ex)
        {
            logger.LogWarning(ex, "Не удалось получить число комментариев для {VideoCount} видео, комментарии будут запрошены у каждого", batch.Length);
            return [];
        }
    }

    private async Task<List<Comment>?> GetVideoCommentsAsync(string videoId)
    {
        try
        {
            return await WithRateLimitRetryAsync(() => youTubeService.GetVideoCommentsAsync(videoId));
        }
        catch (GoogleApiException ex)
        {
            logger.LogError(ex, "Ошибка при получении комментариев для видео с ID: {VideoId}", videoId);
            return null;
        }
    }

    private async Task<T> WithRateLimitRetryAsync<T>(Func<Task<T>> call)
    {
        for (var attempt = 1;; attempt++)
        {
            try
            {
                return await call();
            }
            catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.RateLimited && attempt < MaxRateLimitAttempts)
            {
                var delay = RateLimitDelay * attempt;
                logger.LogWarning("YouTube rate limit, attempt {Attempt} of {MaxAttempts}, retry in {Delay}", attempt, MaxRateLimitAttempts, delay);
                await Task.Delay(delay);
            }
        }
    }
}
