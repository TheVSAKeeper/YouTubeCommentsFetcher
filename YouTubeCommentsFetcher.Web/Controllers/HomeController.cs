using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using YouTubeCommentsFetcher.Web.Models;
using YouTubeCommentsFetcher.Web.Services;

namespace YouTubeCommentsFetcher.Web.Controllers;

public class HomeController(ILogger<HomeController> logger, IYouTubeService youTubeService) : Controller
{
    private readonly ILogger<HomeController> _logger = logger;

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

        string? uploadsPlaylistId = await youTubeService.GetUploadsPlaylistIdAsync(channelId, cancellationToken);

        if (uploadsPlaylistId == null)
        {
            ModelState.AddModelError("channelId", "Channel not found or no uploads.");
            return View("Index", new YouTubeCommentsViewModel());
        }

        List<string> videoIds = await youTubeService.GetVideoIdsFromPlaylistAsync(uploadsPlaylistId, pageSize, maxPages, cancellationToken);

        YouTubeCommentsViewModel model = new()
        {
            Videos = []
        };

        foreach (string videoId in videoIds)
        {
            VideoComments videoComments = await youTubeService.GetVideoCommentsAsync(videoId, cancellationToken);
            model.Videos.Add(videoComments);
        }

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
}
