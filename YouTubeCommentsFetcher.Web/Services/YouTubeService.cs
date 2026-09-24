using System.Net;
using Google;
using YouTubeCommentsFetcher.Web.Models;
using Comment = YouTubeCommentsFetcher.Web.Models.Comment;

namespace YouTubeCommentsFetcher.Web.Services;

public interface IYouTubeService
{
    Task<string?> GetUploadsPlaylistIdAsync(string channelId, CancellationToken cancellationToken = default);
    Task<PlaylistPage> GetPlaylistPageAsync(string uploadsPlaylistId, string? pageToken, CancellationToken cancellationToken = default);
    Task<Dictionary<string, long?>> GetCommentCountsAsync(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default);
    Task<List<Comment>> GetVideoCommentsAsync(string videoId, CancellationToken cancellationToken = default);
}

public class YouTubeService(Google.Apis.YouTube.v3.YouTubeService youtubeService, ILogger<YouTubeService> logger) : IYouTubeService
{
    public const int PlaylistPageSize = 50;
    public const int StatsBatchSize = 50;

    public async Task<string?> GetUploadsPlaylistIdAsync(string channelId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Запрос идентификатора плейлиста загрузок для канала с ID: {ChannelId}", channelId);

        var channelRequest = youtubeService.Channels.List("contentDetails");
        channelRequest.Id = channelId;
        var channelResponse = await ExecuteAsync(() => channelRequest.ExecuteAsync(cancellationToken));
        var uploadsPlaylistId = channelResponse?.Items?.FirstOrDefault()?.ContentDetails?.RelatedPlaylists?.Uploads;

        if (uploadsPlaylistId != null)
        {
            logger.LogInformation("Идентификатор плейлиста загрузок: {UploadsPlaylistId}", uploadsPlaylistId);
        }
        else
        {
            logger.LogWarning("Не удалось найти идентификатор плейлиста загрузок для канала с ID: {ChannelId}", channelId);
        }

        return uploadsPlaylistId;
    }

    public async Task<PlaylistPage> GetPlaylistPageAsync(string uploadsPlaylistId, string? pageToken, CancellationToken cancellationToken = default)
    {
        var playlistRequest = youtubeService.PlaylistItems.List("contentDetails,snippet");
        playlistRequest.PlaylistId = uploadsPlaylistId;
        playlistRequest.MaxResults = PlaylistPageSize;
        playlistRequest.PageToken = pageToken;

        logger.LogInformation("Отправка запроса на получение видео из плейлиста {UploadsPlaylistId}, токен страницы: {PageToken}", uploadsPlaylistId, pageToken);
        var playlistResponse = await ExecuteAsync(() => playlistRequest.ExecuteAsync(cancellationToken));

        var videos = (playlistResponse.Items ?? [])
            .Select(item => (VideoId: item.ContentDetails?.VideoId ?? item.Snippet?.ResourceId?.VideoId, item.Snippet))
            .Where(item => string.IsNullOrEmpty(item.VideoId) == false)
            .Select(item => new PlaylistVideo(item.VideoId!,
                item.Snippet?.Title ?? string.Empty,
                item.Snippet?.Thumbnails?.High?.Url ?? $"https://img.youtube.com/vi/{item.VideoId}/hqdefault.jpg"))
            .ToList();

        logger.LogInformation("Найдено видео на странице: {VideoCount}, следующая страница: {NextPageToken}", videos.Count, playlistResponse.NextPageToken);
        return new(videos, playlistResponse.NextPageToken, playlistResponse.PageInfo?.TotalResults);
    }

    public async Task<Dictionary<string, long?>> GetCommentCountsAsync(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default)
    {
        if (videoIds.Count is 0 or > StatsBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(videoIds), videoIds.Count, $"Ожидается от 1 до {StatsBatchSize} идентификаторов видео");
        }

        logger.LogInformation("Запрос числа комментариев для {VideoCount} видео", videoIds.Count);

        var statsRequest = youtubeService.Videos.BatchGetStats();
        statsRequest.Id = videoIds.ToList();
        statsRequest.Part = "statistics";
        var statsResponse = await ExecuteAsync(() => statsRequest.ExecuteAsync(cancellationToken));

        var counts = new Dictionary<string, long?>();

        foreach (var item in statsResponse.Items ?? [])
        {
            if (string.IsNullOrEmpty(item.Id) == false)
            {
                counts[item.Id] = item.Statistics?.CommentCount;
            }
        }

        return counts;
    }

    public async Task<List<Comment>> GetVideoCommentsAsync(string videoId, CancellationToken cancellationToken = default)
    {
        var commentsRequest = youtubeService.CommentThreads.List("snippet,replies");
        commentsRequest.VideoId = videoId;
        commentsRequest.MaxResults = 100;

        logger.LogInformation("Отправка запроса на получение комментариев для видео с ID: {VideoId}", videoId);

        Google.Apis.YouTube.v3.Data.CommentThreadListResponse commentsResponse;

        try
        {
            commentsResponse = await ExecuteAsync(() => commentsRequest.ExecuteAsync(cancellationToken));
        }
        catch (GoogleApiException ex) when (HasReason(ex, "commentsDisabled"))
        {
            logger.LogInformation("Комментарии отключены для видео с ID: {VideoId}", videoId);
            return [];
        }

        var comments = (commentsResponse.Items ?? []).Select(commentThread => new Comment
            {
                AuthorDisplayName = commentThread.Snippet.TopLevelComment.Snippet.AuthorDisplayName,
                TextDisplay = commentThread.Snippet.TopLevelComment.Snippet.TextDisplay,
                LikeCount = commentThread.Snippet.TopLevelComment.Snippet.LikeCount,
                PublishedAt = commentThread.Snippet.TopLevelComment.Snippet.PublishedAt,
                Replies = commentThread.Replies?.Comments?.Select(reply => new Comment
                              {
                                  AuthorDisplayName = reply.Snippet.AuthorDisplayName,
                                  TextDisplay = reply.Snippet.TextDisplay,
                                  PublishedAt = reply.Snippet.PublishedAt,
                                  LikeCount = reply.Snippet.LikeCount,
                              })
                              .ToList()
                          ?? [],
            })
            .ToList();

        logger.LogInformation("Получено {CommentCount} комментариев для видео с ID: {VideoId}", comments.Count, videoId);
        return comments;
    }

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (GoogleApiException ex) when (Classify(ex) != YouTubeApiErrorKind.None)
        {
            throw new YouTubeApiException(Classify(ex), ex);
        }
    }

    private static YouTubeApiErrorKind Classify(GoogleApiException ex)
    {
        if (HasReason(ex, "quotaExceeded") || HasReason(ex, "dailyLimitExceeded"))
        {
            return YouTubeApiErrorKind.QuotaExceeded;
        }

        if (ex.HttpStatusCode == HttpStatusCode.TooManyRequests
            || HasReason(ex, "rateLimitExceeded")
            || HasReason(ex, "userRateLimitExceeded"))
        {
            return YouTubeApiErrorKind.RateLimited;
        }

        return YouTubeApiErrorKind.None;
    }

    private static bool HasReason(GoogleApiException ex, string reason)
    {
        return ex.Error?.Errors?.Any(error => error.Reason == reason) == true;
    }
}
