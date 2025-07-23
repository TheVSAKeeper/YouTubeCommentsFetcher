using Microsoft.AspNetCore.Mvc;
using Quartz;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using YouTubeCommentsFetcher.Web.Models;
using YouTubeCommentsFetcher.Web.Services;

namespace YouTubeCommentsFetcher.Web.Controllers;

public class HomeController(
    ISchedulerFactory schedulerFactory,
    IJobStatusService statusService,
    ILogger<HomeController> logger) : Controller
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> FetchCommentsBackground(string channelId, int pageSize = 5, int maxPages = 1)
    {
        if (string.IsNullOrEmpty(channelId))
        {
            TempData["Error"] = "Invalid channel ID.";
            return RedirectToAction("Index");
        }

        var jobId = Guid.NewGuid().ToString();
        statusService.Init(jobId);

        var scheduler = await schedulerFactory.GetScheduler();

        var job = JobBuilder.Create<FetchCommentsJob>()
            .WithIdentity($"job_{jobId}")
            .UsingJobData("jobId", jobId)
            .UsingJobData("channelId", channelId)
            .UsingJobData("pageSize", pageSize)
            .UsingJobData("maxPages", maxPages)
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"trigger_{jobId}")
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger);
        return RedirectToAction("JobQueued", new { jobId });
    }

    [HttpGet]
    public IActionResult JobQueued(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return RedirectToAction("Index");
        }

        return View(model: jobId);
    }

    [HttpGet]
    public IActionResult GetJobStatus(string jobId)
    {
        var status = statusService.GetStatus(jobId);
        return Ok(status);
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
    public async Task<IActionResult> UploadDataForAnalysis(IFormFile? jsonFile)
    {
        logger.LogInformation("Начало загрузки файла для анализа");

        if (jsonFile == null || jsonFile.Length == 0)
        {
            logger.LogWarning("Попытка загрузки пустого файла");
            TempData["Error"] = "Пожалуйста, выберите файл для загрузки";
            return View("Index");
        }

        if (Path.GetExtension(jsonFile.FileName).Equals(".json", StringComparison.InvariantCultureIgnoreCase) == false)
        {
            logger.LogWarning("Неправильный формат файла: {FileName}", jsonFile.FileName);
            TempData["Error"] = "Поддерживаются только JSON-файлы";
            return View("Index");
        }

        if (jsonFile.Length > 50 * 1024 * 1024)
        {
            logger.LogWarning("Файл слишком большой: {FileSize} байт", jsonFile.Length);
            TempData["Error"] = "Размер файла не должен превышать 50 МБ";
            return View("Index");
        }

        try
        {
            var jobId = Guid.NewGuid().ToString();
            statusService.Init(jobId);

            var tempDir = Path.Combine(Path.GetTempPath(), "YouTubeCommentsFetcher");
            Directory.CreateDirectory(tempDir);
            var tempFilePath = Path.Combine(tempDir, $"upload_{jobId}.json");

            using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
            {
                await jsonFile.CopyToAsync(fileStream);
            }

            logger.LogInformation("Файл сохранен временно: {TempFilePath}, размер: {FileSize} байт",
                tempFilePath, jsonFile.Length);

            try
            {
                var json = await System.IO.File.ReadAllTextAsync(tempFilePath);
                var model = JsonSerializer.Deserialize<YouTubeCommentsViewModel>(json);

                if (model == null)
                {
                    System.IO.File.Delete(tempFilePath);
                    TempData["Error"] = "Некорректный формат JSON-файла";
                    return View("Index");
                }

                logger.LogInformation("JSON валидация прошла успешно. Комментариев: {CommentsCount}, видео: {VideosCount}",
                    model.Comments.Count, model.Videos.Count);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Ошибка валидации JSON");
                System.IO.File.Delete(tempFilePath);
                TempData["Error"] = "Некорректный формат JSON-файла";
                return View("Index");
            }

            var scheduler = await schedulerFactory.GetScheduler();

            var job = JobBuilder.Create<AnalyzeDataJob>()
                .WithIdentity($"analyze_job_{jobId}")
                .UsingJobData("jobId", jobId)
                .UsingJobData("filePath", tempFilePath)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"analyze_trigger_{jobId}")
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            logger.LogInformation("Задача анализа поставлена в очередь с ID: {JobId}", jobId);

            return RedirectToAction("AnalysisQueued", new { jobId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при загрузке файла");
            TempData["Error"] = "Ошибка при обработке файла";
            return View("Index");
        }
    }

    [HttpGet]
    public IActionResult AnalysisQueued(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return RedirectToAction("Index");
        }

        return View(model: jobId);
    }

    [HttpPost]
    public async Task<IActionResult> LoadAnalyzedData(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return BadRequest("Job ID is required");
        }

        try
        {
            var dataPath = Path.Combine("Data", $"analyzed_{jobId}.json");

            if (!System.IO.File.Exists(dataPath))
            {
                return NotFound("Analyzed data not found");
            }

            var json = await System.IO.File.ReadAllTextAsync(dataPath);

            var deserializeOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
            };

            var model = JsonSerializer.Deserialize<YouTubeCommentsViewModel>(json, deserializeOptions);

            if (model == null)
            {
                logger.LogError("Deserialized model is null for job {JobId}", jobId);
                return BadRequest("Invalid data format");
            }

            if (model.Comments == null)
            {
                logger.LogWarning("Comments collection is null for job {JobId}, initializing empty list", jobId);
                model.Comments = new();
            }

            if (model.Videos == null)
            {
                logger.LogWarning("Videos collection is null for job {JobId}, this shouldn't happen", jobId);
            }

            logger.LogInformation("Загружены проанализированные данные для job {JobId}: {CommentsCount} комментариев, {VideosCount} видео",
                jobId, model.Comments.Count, model.Videos.Count);

            return View("Comments", model);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при загрузке проанализированных данных для job {JobId}", jobId);
            return BadRequest("Error loading analyzed data");
        }
    }
}
