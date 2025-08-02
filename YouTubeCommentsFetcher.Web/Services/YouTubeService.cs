using YouTubeCommentsFetcher.Web.Models;
using Comment = YouTubeCommentsFetcher.Web.Models.Comment;

namespace YouTubeCommentsFetcher.Web.Services;

public interface IYouTubeService
{
    Task<string?> GetUploadsPlaylistIdAsync(string channelId, CancellationToken cancellationToken = default);
    Task<List<string>> GetVideoIdsFromPlaylistAsync(string uploadsPlaylistId, int pageSize, int maxPages, CancellationToken cancellationToken = default);
    Task<VideoComments> GetVideoCommentsAsync(string videoId, CancellationToken cancellationToken = default);
}

public class YouTubeService(Google.Apis.YouTube.v3.YouTubeService youtubeService, ILogger<YouTubeService> logger) : IYouTubeService
{
    public async Task<string?> GetUploadsPlaylistIdAsync(string channelId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Запрос идентификатора плейлиста загрузок для канала с ID: {ChannelId}", channelId);

        try
        {
            var channelRequest = youtubeService.Channels.List("contentDetails");
            channelRequest.Id = channelId;
            var channelResponse = await channelRequest.ExecuteAsync(cancellationToken);
            var uploadsPlaylistId = channelResponse?.Items.FirstOrDefault()?.ContentDetails?.RelatedPlaylists?.Uploads;

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
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при получении идентификатора плейлиста загрузок для канала с ID: {ChannelId}", channelId);
            return null;
        }
    }

    public async Task<List<string>> GetVideoIdsFromPlaylistAsync(string uploadsPlaylistId, int pageSize, int maxPages, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Получение идентификаторов видео из плейлиста с ID: {UploadsPlaylistId}", uploadsPlaylistId);

        List<string> videoIds = [];
        string? nextPageToken = null;
        var page = 0;

        do
        {
            try
            {
                var playlistRequest = youtubeService.PlaylistItems.List("contentDetails,snippet");
                playlistRequest.PlaylistId = uploadsPlaylistId;
                playlistRequest.MaxResults = pageSize;
                playlistRequest.PageToken = nextPageToken;

                logger.LogInformation("Отправка запроса на получение видео, страница: {Page}, размер страницы: {PageSize}", page + 1, pageSize);
                var playlistResponse = await playlistRequest.ExecuteAsync(cancellationToken);

                if (playlistResponse.Items.Count == 0)
                {
                    logger.LogWarning("Плейлист с ID: {UploadsPlaylistId} не содержит видео на странице {Page}", uploadsPlaylistId, page + 1);
                    break; // Прерываем цикл, если нет видео
                }

                videoIds.AddRange(playlistResponse.Items.Select(item => item.ContentDetails.VideoId));
                nextPageToken = playlistResponse.NextPageToken;
                page++;

                logger.LogInformation("Страница {Page} из {MaxPages}: {NextPageToken}, найдено видео: {VideoCount}", page, maxPages, nextPageToken, playlistResponse.Items.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при получении видео из плейлиста с ID: {UploadsPlaylistId}, страница: {Page}", uploadsPlaylistId, page);
                break;
            }
        } while (nextPageToken != null && page < maxPages);

        logger.LogInformation("Получено {VideoCount} идентификаторов видео из плейлиста с ID: {UploadsPlaylistId}", videoIds.Count, uploadsPlaylistId);
        return videoIds;
    }

    public async Task<VideoComments> GetVideoCommentsAsync(string videoId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Получение комментариев для видео с ID: {VideoId}", videoId);

        try
        {
            var videoRequest = youtubeService.Videos.List("snippet");
            videoRequest.Id = videoId;
            var videoResponse = await videoRequest.ExecuteAsync(cancellationToken);
            var videoTitle = videoResponse?.Items.FirstOrDefault()?.Snippet?.Title;
            var videoUrl = $"https://www.youtube.com/watch?v={videoId}";

            var commentsRequest = youtubeService.CommentThreads.List("snippet,replies");
            commentsRequest.VideoId = videoId;
            commentsRequest.MaxResults = 100;

            logger.LogInformation("Отправка запроса на получение комментариев для видео с ID: {VideoId}", videoId);

            var commentsResponse = await commentsRequest.ExecuteAsync(cancellationToken);

            if (commentsResponse.Items.Count == 0)
            {
                logger.LogWarning("Не найдено комментариев для видео с ID: {VideoId}", videoId);
            }

            var comments = commentsResponse.Items.Select(commentThread => new Comment
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

            return new()
            {
                VideoTitle = videoTitle,
                VideoUrl = videoUrl,
                ThumbnailUrl = $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg",
                Comments = comments,
                VideoId = videoId,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при получении комментариев для видео с ID: {VideoId}", videoId);

            return new()
            {
                VideoTitle = null,
                VideoUrl = null,
                ThumbnailUrl = null,
                Comments = [],
                VideoId = null,
            };
        }
    }
}
