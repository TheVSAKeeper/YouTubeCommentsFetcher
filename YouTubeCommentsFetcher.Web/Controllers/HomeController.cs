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
    public async Task<IActionResult> FetchComments(string channelId, int pageSize = 5, int maxPages = 1)
    {
        if (string.IsNullOrEmpty(channelId))
        {
            ModelState.AddModelError("channelId", "Invalid Channel URL.");
            return View("Index", new YouTubeCommentsViewModel());
        }

        ChannelsResource.ListRequest? channelRequest = youtubeService.Channels.List("contentDetails");
        channelRequest.Id = channelId;
        ChannelListResponse? channelResponse = await channelRequest.ExecuteAsync();

        string? uploadsPlaylistId = channelResponse.Items[0].ContentDetails.RelatedPlaylists.Uploads;

        List<string> videoIds = [];
        string? nextPageToken = null;
        int page = 0;

        do
        {
            PlaylistItemsResource.ListRequest? playlistRequest = youtubeService.PlaylistItems.List("contentDetails,snippet");
            playlistRequest.PlaylistId = uploadsPlaylistId;
            playlistRequest.MaxResults = pageSize;
            playlistRequest.PageToken = nextPageToken;

            PlaylistItemListResponse? playlistResponse = await playlistRequest.ExecuteAsync();
            videoIds.AddRange(playlistResponse.Items.Select(item => item.ContentDetails.VideoId));
            nextPageToken = playlistResponse.NextPageToken;
            page++;

            logger.LogInformation("Page {Page} of {ItemsCount}: {NextPageToken}", page, channelResponse.Items.Count, nextPageToken);

            if (page >= maxPages)
            {
                break;
            }
        } while (nextPageToken != null);

        YouTubeCommentsViewModel model = new();

        foreach (string videoId in videoIds)
        {
            VideosResource.ListRequest? videoRequest = youtubeService.Videos.List("snippet");
            videoRequest.Id = videoId;
            VideoListResponse? videoResponse = await videoRequest.ExecuteAsync();
            string? videoTitle = videoResponse.Items[0].Snippet.Title;
            logger.LogInformation("Video {VideoId}: {VideoTitle}", videoId, videoTitle);
            string videoUrl = $"https://www.youtube.com/watch?v={videoId}";

            CommentThreadsResource.ListRequest? commentsRequest = youtubeService.CommentThreads.List("snippet,replies");
            commentsRequest.VideoId = videoId;
            commentsRequest.MaxResults = 100;

            CommentThreadListResponse? commentsResponse = await commentsRequest.ExecuteAsync();

            VideoComments videoComments = new()
            {
                VideoTitle = videoTitle,
                VideoUrl = videoUrl,
                Comments = commentsResponse.Items.Select(commentThread => new Comment
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
                    .ToList()
            };

            model.Videos.Add(videoComments);
        }

        List<Comment> allComments = model.Videos.SelectMany(video => video.Comments).OrderByDescending(comment => comment.PublishedAt).ToList();
        model.Comments = allComments;

        foreach (VideoComments video in model.Videos)
        {
            video.Comments = allComments.Where(comment => video.Comments.Any(c => c.TextDisplay == comment.TextDisplay)).ToList();
        }

        return View("Comments", model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
