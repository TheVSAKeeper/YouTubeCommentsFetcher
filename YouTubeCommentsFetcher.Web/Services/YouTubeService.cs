using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using YouTubeCommentsFetcher.Web.Models;
using Comment = YouTubeCommentsFetcher.Web.Models.Comment;

namespace YouTubeCommentsFetcher.Web.Services;

public class YouTubeService(Google.Apis.YouTube.v3.YouTubeService youtubeService, ILogger<YouTubeService> logger) : IYouTubeService
{
    public async Task<string?> GetUploadsPlaylistIdAsync(string channelId, CancellationToken cancellationToken = default)
    {
        ChannelsResource.ListRequest? channelRequest = youtubeService.Channels.List("contentDetails");
        channelRequest.Id = channelId;
        ChannelListResponse? channelResponse = await channelRequest.ExecuteAsync(cancellationToken);
        return channelResponse?.Items.FirstOrDefault()?.ContentDetails?.RelatedPlaylists?.Uploads;
    }

    public async Task<List<string>> GetVideoIdsFromPlaylistAsync(string uploadsPlaylistId, int pageSize, int maxPages, CancellationToken cancellationToken = default)
    {
        List<string> videoIds = [];
        string? nextPageToken = null;
        int page = 0;

        do
        {
            PlaylistItemsResource.ListRequest? playlistRequest = youtubeService.PlaylistItems.List("contentDetails,snippet");
            playlistRequest.PlaylistId = uploadsPlaylistId;
            playlistRequest.MaxResults = pageSize;
            playlistRequest.PageToken = nextPageToken;

            PlaylistItemListResponse? playlistResponse = await playlistRequest.ExecuteAsync(cancellationToken);
            videoIds.AddRange(playlistResponse.Items.Select(item => item.ContentDetails.VideoId));
            nextPageToken = playlistResponse.NextPageToken;
            page++;

            logger.LogInformation("Page {Page} of {ItemsCount}: {NextPageToken}", page, playlistResponse.Items.Count, nextPageToken);
        } while (nextPageToken != null && page < maxPages);

        return videoIds;
    }

    public async Task<VideoComments> GetVideoCommentsAsync(string videoId, CancellationToken cancellationToken = default)
    {
        VideosResource.ListRequest? videoRequest = youtubeService.Videos.List("snippet");
        videoRequest.Id = videoId;
        VideoListResponse? videoResponse = await videoRequest.ExecuteAsync(cancellationToken);
        string? videoTitle = videoResponse?.Items.FirstOrDefault()?.Snippet?.Title;
        string videoUrl = $"https://www.youtube.com/watch?v={videoId}";

        CommentThreadsResource.ListRequest? commentsRequest = youtubeService.CommentThreads.List("snippet,replies");
        commentsRequest.VideoId = videoId;
        commentsRequest.MaxResults = 100;

        CommentThreadListResponse? commentsResponse = await commentsRequest.ExecuteAsync(cancellationToken);

        List<Comment> comments = commentsResponse.Items.Select(commentThread => new Comment
            {
                AuthorDisplayName = commentThread.Snippet.TopLevelComment.Snippet.AuthorDisplayName,
                TextDisplay = commentThread.Snippet.TopLevelComment.Snippet.TextDisplay,
                PublishedAt = commentThread.Snippet.TopLevelComment.Snippet.PublishedAt,
                Replies = commentThread.Replies?.Comments?.Select(reply => new Comment
                              {
                                  AuthorDisplayName = reply.Snippet.AuthorDisplayName,
                                  TextDisplay = reply.Snippet.TextDisplay,
                                  PublishedAt = reply.Snippet.PublishedAt
                              })
                              .ToList()
                          ?? []
            })
            .ToList();

        return new VideoComments
        {
            VideoTitle = videoTitle,
            VideoUrl = videoUrl,
            Comments = comments
        };
    }
}
