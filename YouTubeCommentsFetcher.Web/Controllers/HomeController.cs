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
            VideoComments videoComments = await youTubeService.GetVideoCommentsAsync(videoId, cancellationToken);
            model.Videos.Add(videoComments);
        }

        model.Comments = model.Videos
            .SelectMany(video => video.Comments)
            .OrderByDescending(comment => comment.PublishedAt)
            .ToList();

        if (model.Comments.Any())
        {
            model.Statistics = new CommentStatistics
            {
                TotalComments = model.Comments.Count,
                UniqueAuthors = model.Comments.Select(c => c.AuthorDisplayName).Distinct().Count(),
                AverageCommentsPerVideo = model.Videos.Count > 0
                    ? Math.Round((double)model.Comments.Count / model.Videos.Count, 2)
                    : 0,
                OldestCommentDate = model.Comments.Min(c => c.PublishedAt),
                NewestCommentDate = model.Comments.Max(c => c.PublishedAt),
            };

            CommentAnalysisResult analysis = AnalyzeComments(model.Comments);

            model.Statistics.TopAuthorsByComments = analysis.TopAuthors;
            model.Statistics.TopCommentsByReplies = analysis.TopCommentsByReplies;
            model.Statistics.TopCommentsByLikes = analysis.TopCommentsByLikes;
            model.Statistics.MostUsedWords = analysis.MostUsedWords;

            VideoAnalysisResult videoAnalysis = AnalyzeVideos(model.Videos);

            model.Statistics.TopCommentedVideos = videoAnalysis.TopCommentedVideos;
            model.Statistics.TopLikedCommentsVideos = videoAnalysis.TopLikedCommentsVideos;
            model.Statistics.TopRepliedVideos = videoAnalysis.TopRepliedVideos;
            model.Statistics.TopInteractiveVideos = videoAnalysis.TopInteractiveVideos;
            model.Statistics.TotalReplies = model.Videos.Sum(v => v.Comments.Sum(c => c.Replies.Count));
        }

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

    private VideoAnalysisResult AnalyzeVideos(List<VideoComments> videos)
    {
        VideoAnalysisResult analysis = new()
        {
            TopCommentedVideos = videos
                .Select(v => new TopVideo
                {
                    VideoTitle = v.VideoTitle,
                    VideoId = v.VideoId,
                    CommentsCount = v.Comments.Count,
                    ThumbnailUrl = v.ThumbnailUrl,
                })
                .OrderByDescending(v => v.CommentsCount)
                .Take(3)
                .ToList(),
            TopLikedCommentsVideos = videos
                .Select(v => new TopVideo
                {
                    VideoTitle = v.VideoTitle,
                    VideoId = v.VideoId,
                    CommentsCount = v.Comments.Sum(c => c.LikeCount),
                    ThumbnailUrl = v.ThumbnailUrl,
                })
                .OrderByDescending(v => v.CommentsCount)
                .Take(3)
                .ToList(),
            TopRepliedVideos = videos
                .Select(v => new TopVideo
                {
                    VideoTitle = v.VideoTitle,
                    VideoId = v.VideoId,
                    CommentsCount = v.Comments.Sum(c => c.Replies.Count),
                    ThumbnailUrl = v.ThumbnailUrl,
                })
                .OrderByDescending(v => v.CommentsCount)
                .Take(3)
                .ToList(),
            TopInteractiveVideos = videos
                .Select(v => new TopVideo
                {
                    VideoTitle = v.VideoTitle,
                    VideoId = v.VideoId,
                    CommentsCount = v.Comments.Count,
                    RepliesCount = v.Comments.Sum(c => c.Replies.Count),
                    TotalInteractions = v.Comments.Count + v.Comments.Sum(c => c.Replies.Count),
                    ThumbnailUrl = v.ThumbnailUrl,
                })
                .OrderByDescending(v => v.TotalInteractions)
                .Take(3)
                .ToList(),
        };

        return analysis;
    }

    private CommentAnalysisResult AnalyzeComments(List<Comment> comments)
    {
        CommentAnalysisResult analysis = new()
        {
            TopAuthors = comments
                .GroupBy(c => c.AuthorDisplayName)
                .Select(g => new TopAuthor
                {
                    AuthorName = g.First().AuthorDisplayName,
                    CommentsCount = g.Count(),
                })
                .OrderByDescending(a => a.CommentsCount)
                .Take(3)
                .ToList(),
            TopCommentsByReplies = comments
                .Where(c => c.Replies.Count > 0)
                .OrderByDescending(c => c.Replies.Count)
                .Take(3)
                .Select(c => new TopComment
                {
                    CommentText = c.TextDisplay,
                    Count = c.Replies.Count,
                    Author = c.AuthorDisplayName,
                })
                .ToList(),
            TopCommentsByLikes = comments
                .OrderByDescending(c => c.LikeCount)
                .Take(3)
                .Select(c => new TopComment
                {
                    CommentText = c.TextDisplay,
                    Count = c.LikeCount,
                    Author = c.AuthorDisplayName,
                })
                .ToList(),
        };

        List<string> words = comments
            .SelectMany(c => c.TextDisplay.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(word => word.Length > 3)
            .GroupBy(word => word.ToLower())
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => g.Key)
            .ToList();

        analysis.MostUsedWords = words;

        return analysis;
    }
}
