using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.AspNetCore.Mvc;
using YouTubeCommentsFetcher.Web.Models;
using Activity = System.Diagnostics.Activity;
using Comment = YouTubeCommentsFetcher.Web.Models.Comment;

namespace YouTubeCommentsFetcher.Web.Controllers;

public class HomeController(ILogger<HomeController> logger, YouTubeService youtubeService) : Controller
{
    public IActionResult Index()
    {
        return View(new YouTubeCommentsViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> FetchComments(string channelId, int pageSize = 5, int maxPages = 1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(channelId))
        {
            ModelState.AddModelError("channelId", "Invalid Channel URL.");
            return View("Index", new YouTubeCommentsViewModel());
        }

        string? uploadsPlaylistId = await GetUploadsPlaylistId(channelId, cancellationToken);

        if (uploadsPlaylistId == null)
        {
            ModelState.AddModelError("channelId", "Channel not found or no uploads.");
            return View("Index", new YouTubeCommentsViewModel());
        }

        List<string> videoIds = await GetVideoIdsFromPlaylist(uploadsPlaylistId, pageSize, maxPages, cancellationToken);

        YouTubeCommentsViewModel model = new()
        {
            Videos = await GetVideoComments(videoIds, cancellationToken)
        };

        model.Comments = model.Videos
            .SelectMany(video => video.Comments)
            .OrderByDescending(comment => comment.PublishedAt)
            .ToList();

        return View("Comments", model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }

    private async Task<string?> GetUploadsPlaylistId(string channelId, CancellationToken cancellationToken = default)
    {
        ChannelsResource.ListRequest? channelRequest = youtubeService.Channels.List("contentDetails");
        channelRequest.Id = channelId;
        ChannelListResponse? channelResponse = await channelRequest.ExecuteAsync(cancellationToken);
        return channelResponse?.Items.FirstOrDefault()?.ContentDetails?.RelatedPlaylists?.Uploads;
    }

    private async Task<List<string>> GetVideoIdsFromPlaylist(string uploadsPlaylistId, int pageSize, int maxPages, CancellationToken cancellationToken = default)
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

    private async Task<List<VideoComments>> GetVideoComments(List<string> videoIds, CancellationToken cancellationToken = default)
    {
        List<VideoComments> videos = [];

        foreach (string videoId in videoIds)
        {
            string? videoTitle = await GetVideoTitle(videoId, cancellationToken);
            string videoUrl = $"https://www.youtube.com/watch?v={videoId}";

            List<Comment> comments = await GetCommentsForVideo(videoId, cancellationToken);

            videos.Add(new VideoComments
            {
                VideoTitle = videoTitle ?? "Not found",
                VideoUrl = videoUrl,
                Comments = comments
            });
        }

        return videos;
    }

    private async Task<string?> GetVideoTitle(string videoId, CancellationToken cancellationToken = default)
    {
        VideosResource.ListRequest? videoRequest = youtubeService.Videos.List("snippet");
        videoRequest.Id = videoId;
        VideoListResponse? videoResponse = await videoRequest.ExecuteAsync(cancellationToken);
        return videoResponse?.Items.FirstOrDefault()?.Snippet?.Title;
    }

    private async Task<List<Comment>> GetCommentsForVideo(string videoId, CancellationToken cancellationToken = default)
    {
        CommentThreadsResource.ListRequest? commentsRequest = youtubeService.CommentThreads.List("snippet,replies");
        commentsRequest.VideoId = videoId;
        commentsRequest.MaxResults = 100;

        CommentThreadListResponse? commentsResponse = await commentsRequest.ExecuteAsync(cancellationToken);

        return commentsResponse.Items.Select(commentThread => new Comment
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
    }
}
