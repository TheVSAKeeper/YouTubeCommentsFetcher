using Hangfire;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using YouTubeCommentsFetcher.Web.Models;
using YouTubeCommentsFetcher.Web.Services;

namespace YouTubeCommentsFetcher.Web.Controllers;

public class HomeController(ILogger<HomeController> logger, IYouTubeService youTubeService, IBackgroundJobClient jobClient) : Controller
{
    private const int Count = 3;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Dictionary<string, bool> JobStatus = new();

    public static void MarkJobAsCompleted(string jobId)
    {
        JobStatus[jobId] = true;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public IActionResult FetchCommentsBackground(string channelId, int pageSize = 5, int maxPages = 1)
    {
        if (string.IsNullOrEmpty(channelId))
        {
            TempData["Error"] = "Invalid channel ID.";
            return RedirectToAction("Index");
        }

        var jobId = Guid.NewGuid().ToString();
        JobStatus[jobId] = false;

        jobClient.Enqueue<FetchCommentsJob>(job => job.ExecuteAsync(channelId, pageSize, maxPages, jobId));

        return View("JobQueued", jobId);
    }

    [HttpGet]
    public IActionResult CheckJobStatus(string jobId)
    {
        if (JobStatus.TryGetValue(jobId, out var completed))
        {
            return Json(new { completed });
        }

        return NotFound();
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

        var uploadsPlaylistId = await youTubeService.GetUploadsPlaylistIdAsync(channelId, cancellationToken);

        if (uploadsPlaylistId == null)
        {
            logger.LogWarning("Канал с ID: {ChannelId} не найден или не содержит загрузок", channelId);
            ModelState.AddModelError("channelId", "Channel not found or no uploads.");
            return View("Index", new YouTubeCommentsViewModel());
        }

        logger.LogInformation("Получен идентификатор плейлиста загрузок: {UploadsPlaylistId}", uploadsPlaylistId);
        var videoIds = await youTubeService.GetVideoIdsFromPlaylistAsync(uploadsPlaylistId, pageSize, maxPages, cancellationToken);

        logger.LogInformation("Получено {VideoCount} идентификаторов видео из плейлиста", videoIds.Count);

        YouTubeCommentsViewModel model = new()
        {
            Videos = [],
        };

        foreach (var videoId in videoIds)
        {
            var videoComments = await youTubeService.GetVideoCommentsAsync(videoId, cancellationToken);
            model.Videos.Add(videoComments);
        }

        model.Comments = model.Videos
            .SelectMany(video => video.Comments)
            .OrderByDescending(comment => comment.PublishedAt)
            .ToList();

        model.Statistics = Analyze(model.Comments, model.Videos);

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

    [HttpPost]
    public IActionResult SaveData([FromBody] YouTubeCommentsViewModel model)
    {
        try
        {
            if (model?.Comments == null || model.Comments.Count == 0)
            {
                logger.LogWarning("Попытка сохранения пустой модели");
                return BadRequest("Нет данных для сохранения");
            }

            logger.LogInformation("Сохранение данных: {CommentsCount} комментариев", model.Comments.Count);

            var json = JsonSerializer.Serialize(model, Options);
            var byteArray = Encoding.UTF8.GetBytes(json);

            logger.LogInformation("Данные успешно сериализованы. Размер: {ByteArrayLength} байт", byteArray.Length);

            return File(byteArray, "application/json", $"youtube_comments_{DateTime.Now:yyyyMMddHHmmss}.json");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при сохранении данных");
            TempData["Error"] = "Произошла ошибка при сохранении данных";
            return StatusCode(500, "Ошибка сервера");
        }
    }

    [HttpPost]
    public async Task<IActionResult> LoadData(IFormFile jsonFile)
    {
        logger.LogInformation("Начало загрузки данных из файла");

        if (jsonFile == null || jsonFile.Length == 0)
        {
            logger.LogWarning("Попытка загрузки пустого файла");
            TempData["Error"] = "Пожалуйста, выберите файл для загрузки";
            return View("Index");
        }

        if (!Path.GetExtension(jsonFile.FileName).Equals(".json", StringComparison.InvariantCultureIgnoreCase))
        {
            logger.LogWarning("Неправильный формат файла: {FileName}", jsonFile.FileName);
            TempData["Error"] = "Поддерживаются только JSON-файлы";
            return View("Index");
        }

        try
        {
            using MemoryStream stream = new();
            await jsonFile.CopyToAsync(stream);
            var json = Encoding.UTF8.GetString(stream.ToArray());

            var model = JsonSerializer.Deserialize<YouTubeCommentsViewModel>(json);
            model.Statistics = Analyze(model.Comments, model.Videos);

            logger.LogInformation("Успешно загружен файл: {FileName}", jsonFile.FileName);

            return View("Comments", model);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Ошибка десериализации JSON");
            TempData["Error"] = "Некорректный формат JSON-файла";
            return View("Index");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при загрузке данных");
            TempData["Error"] = "Ошибка при обработке файла";
            return View("Index");
        }
    }

    private CommentStatistics Analyze(List<Comment> comments, List<VideoComments> videos)
    {
        CommentStatistics statistics = new()
        {
            TotalComments = comments.Count,
            UniqueAuthors = comments.Select(c => c.AuthorDisplayName).Distinct().Count(),
            AverageCommentsPerVideo = videos.Count > 0
                ? Math.Round((double)comments.Count / videos.Count, 2)
                : 0,
            OldestCommentDate = comments.Min(c => c.PublishedAt),
            NewestCommentDate = comments.Max(c => c.PublishedAt),
            CommentAnalysis = AnalyzeComments(comments),
        };

        var videoAnalysis = AnalyzeVideos(videos);

        statistics.TopCommentedVideos = videoAnalysis.TopCommentedVideos;
        statistics.TopLikedCommentsVideos = videoAnalysis.TopLikedCommentsVideos;
        statistics.TopRepliedVideos = videoAnalysis.TopRepliedVideos;
        statistics.TopInteractiveVideos = videoAnalysis.TopInteractiveVideos;
        statistics.TotalReplies = videos.Sum(v => v.Comments.Sum(c => c.Replies.Count));

        return statistics;
    }

    private VideoAnalysisResult AnalyzeVideos(List<VideoComments> videos)
    {
        VideoAnalysisResult analysis = new()
        {
            TopCommentedVideos = videos
                .Where(x => x.Comments.Count > 0)
                .Select(v => new TopVideo
                {
                    VideoTitle = v.VideoTitle,
                    VideoId = v.VideoId,
                    CommentsCount = v.Comments.Count,
                    ThumbnailUrl = v.ThumbnailUrl,
                })
                .OrderByDescending(v => v.CommentsCount)
                .Take(Count)
                .ToList(),
            TopLikedCommentsVideos = videos
                .Where(x => x.Comments.Count > 0)
                .Select(v => new TopVideo
                {
                    VideoTitle = v.VideoTitle,
                    VideoId = v.VideoId,
                    CommentsCount = v.Comments.Sum(c => c.LikeCount),
                    ThumbnailUrl = v.ThumbnailUrl,
                })
                .OrderByDescending(v => v.CommentsCount)
                .Take(Count)
                .ToList(),
            TopRepliedVideos = videos
                .Where(x => x.Comments.Count > 0)
                .Select(v => new TopVideo
                {
                    VideoTitle = v.VideoTitle,
                    VideoId = v.VideoId,
                    CommentsCount = v.Comments.Sum(c => c.Replies.Count),
                    ThumbnailUrl = v.ThumbnailUrl,
                })
                .OrderByDescending(v => v.CommentsCount)
                .Take(Count)
                .ToList(),
            TopInteractiveVideos = videos
                .Where(x => x.Comments.Count > 0)
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
                .Take(Count)
                .ToList(),
        };

        return analysis;
    }

    private CommentAnalysisResult AnalyzeComments(List<Comment> comments)
    {
        Top<TopWord> worldTop = new()
        {
            ByComments = TopWords(comments),
            ByReplies = TopWords(comments.SelectMany(x => x.Replies)),
            ByActivity = TopWords(comments.SelectMany(x => x.Replies.Append(x))),
        };

        Top<TopAuthor> authorTop = new()
        {
            ByComments = TopAuthors(comments),
            ByReplies = TopAuthors(comments.SelectMany(x => x.Replies)),
            ByActivity = TopAuthors(comments.SelectMany(x => x.Replies.Append(x))),
        };

        CommentAnalysisResult analysis = new()
        {
            TopAuthors = authorTop,
            TopCommentsByReplies = comments
                .Where(c => c.Replies.Count > 0)
                .OrderByDescending(c => c.Replies.Count)
                .Take(Count)
                .Select(c => new TopComment
                {
                    CommentText = c.TextDisplay,
                    Count = c.Replies.Count,
                    Author = c.AuthorDisplayName,
                })
                .ToList(),
            TopCommentsByLikes = comments
                .Where(c => c.LikeCount is > 0)
                .OrderByDescending(c => c.LikeCount)
                .Take(Count)
                .Select(c => new TopComment
                {
                    CommentText = c.TextDisplay,
                    Count = c.LikeCount,
                    Author = c.AuthorDisplayName,
                })
                .ToList(),
            MostUsedWords = worldTop,
        };

        return analysis;

        List<TopWord> TopWords(IEnumerable<Comment> all)
        {
            var topWords = all
                .SelectMany(c => c.TextDisplay.Split([" ", "<br>"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(x => x.Trim(',', '.', ';', '-', '!', '?', '(', ')'))
                .Where(word => word.Length > 3 && !word.Contains("href", StringComparison.InvariantCultureIgnoreCase))
                .GroupBy(word => word.ToLowerInvariant())
                .Select(g => new TopWord(g.Key, g.Count()))
                .OrderByDescending(g => g.Count)
                .Take(15)
                .ToList();

            return topWords;
        }

        List<TopAuthor> TopAuthors(IEnumerable<Comment> all)
        {
            var topAuthors = all
                .GroupBy(c => c.AuthorDisplayName)
                .Select(g => new TopAuthor
                {
                    AuthorName = g.First().AuthorDisplayName,
                    CommentsCount = g.Count(),
                })
                .OrderByDescending(a => a.CommentsCount)
                .Take(Count)
                .ToList();

            return topAuthors;
        }
    }
}
