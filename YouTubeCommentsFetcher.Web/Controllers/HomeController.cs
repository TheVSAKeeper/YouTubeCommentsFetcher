using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using YouTubeCommentsFetcher.Web.Models;
using YouTubeCommentsFetcher.Web.Services;

namespace YouTubeCommentsFetcher.Web.Controllers;

public class HomeController(ILogger<HomeController> logger, IYouTubeService youTubeService) : Controller
{
    public IActionResult Index()
    {
        return View(new YouTubeCommentsViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> FetchComments(string channelId, int pageSize = 5, int maxPages = 1, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Запрос комментариев для канала с ID: {ChannelId}, размер страницы: {PageSize}, максимальное количество страниц: {MaxPages}", channelId, pageSize, maxPages);

        if (string.IsNullOrEmpty(channelId))
        {
            logger.LogWarning("Получен недействительный идентификатор канала");
            ModelState.AddModelError("channelId", "Invalid Channel URL.");
            return View("Index", new YouTubeCommentsViewModel());
        }

        string? uploadsPlaylistId = await youTubeService.GetUploadsPlaylistIdAsync(channelId, cancellationToken);

        if (uploadsPlaylistId == null)
        {
            logger.LogWarning("Канал с ID: {ChannelId} не найден или не содержит загрузок", channelId);
            ModelState.AddModelError("channelId", "Channel not found or no uploads.");
            return View("Index", new YouTubeCommentsViewModel());
        }

        logger.LogInformation("Получен идентификатор плейлиста загрузок: {UploadsPlaylistId}", uploadsPlaylistId);
        List<string> videoIds = await youTubeService.GetVideoIdsFromPlaylistAsync(uploadsPlaylistId, pageSize, maxPages, cancellationToken);

        logger.LogInformation("Получено {VideoCount} идентификаторов видео из плейлиста", videoIds.Count);

        YouTubeCommentsViewModel model = new()
        {
            Videos = [],
        };

        foreach (string videoId in videoIds)
        {
            logger.LogInformation("Получение комментариев для видео с ID: {VideoId}", videoId);
            VideoComments videoComments = await youTubeService.GetVideoCommentsAsync(videoId, cancellationToken);
            model.Videos.Add(videoComments);
        }

        model.Comments = model.Videos
            .SelectMany(video => video.Comments)
            .OrderByDescending(comment => comment.PublishedAt)
            .ToList();

        logger.LogInformation("Получено {CommentCount} комментариев из {VideoCount} видео", model.Comments.Count, model.Videos.Count);
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
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
        });
    }
}
