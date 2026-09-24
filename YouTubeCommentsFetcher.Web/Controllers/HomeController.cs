using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quartz;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using YouTubeCommentsFetcher.Core.Configuration;
using YouTubeCommentsFetcher.Core.Models;
using YouTubeCommentsFetcher.Core.Services;
using YouTubeCommentsFetcher.Web.Models;
using YouTubeCommentsFetcher.Web.Services;

namespace YouTubeCommentsFetcher.Web.Controllers;

public class HomeController(
    ISchedulerFactory schedulerFactory,
    IJobStatusService statusService,
    IDataPathService dataPathService,
    IFetchResultsService fetchResultsService,
    ILogger<HomeController> logger) : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> FetchCommentsBackground(string channelId, int maxPages = 1)
    {
        channelId = channelId?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(channelId))
        {
            TempData["Error"] = "Invalid channel ID.";
            return RedirectToAction("Index");
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            TempData["Error"] = "Пользователь не аутентифицирован.";
            return RedirectToAction("Index");
        }

        maxPages = Math.Clamp(maxPages, 1, CommentsFetcher.MaxPlaylistPages);

        var jobId = Guid.NewGuid().ToString();
        var runningJobId = statusService.StartOrGetRunning(jobId, channelId, userId, maxPages);

        if (runningJobId != jobId)
        {
            var runningJob = statusService.GetStatus(runningJobId);

            if (runningJob?.UserId == userId || User.HasClaim("IsAdmin", "true"))
            {
                logger.LogInformation("Канал {ChannelId} уже собирается задачей {JobId}, новая задача не создана", channelId, runningJobId);

                if (runningJob != null && runningJob.MaxPages != maxPages)
                {
                    TempData["Warning"] = $"Этот канал уже собирается, но с другим числом страниц: {runningJob.MaxPages} вместо {maxPages}. "
                                          + "Новый сбор не запущен, ниже – ход текущего. Чтобы собрать с другим числом страниц, запустите сбор ещё раз, когда этот закончится.";
                }

                return RedirectToAction("JobQueued", new { jobId = runningJobId });
            }

            TempData["Error"] = "Этот канал сейчас уже собирается по запросу другого пользователя. Запустите сбор, когда он закончится.";
            return RedirectToAction("Index");
        }

        try
        {
            var scheduler = await schedulerFactory.GetScheduler();

            var job = JobBuilder.Create<FetchCommentsJob>()
                .WithIdentity($"job_{jobId}")
                .UsingJobData("jobId", jobId)
                .UsingJobData("channelId", channelId)
                .UsingJobData("maxPages", maxPages)
                .UsingJobData("userId", userId)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"trigger_{jobId}")
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось поставить задачу {JobId} в очередь", jobId);
            statusService.MarkFailed(jobId, "Не удалось запустить сбор. Попробуйте ещё раз.");
            TempData["Error"] = "Не удалось запустить сбор. Попробуйте ещё раз.";
            return RedirectToAction("Index");
        }

        return RedirectToAction("JobQueued", new { jobId });
    }

    [HttpGet]
    public IActionResult JobQueued(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return RedirectToAction("Index");
        }

        var model = new JobQueuedViewModel
        {
            JobId = jobId,
            DataFileUrl = dataPathService.GetCommentsFileUrl(jobId),
            FileName = dataPathService.GetCommentsFileName(jobId),
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> GetJobStatus(string jobId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User.HasClaim("IsAdmin", "true");
        bool CanView(string? ownerId) => isAdmin || (string.IsNullOrEmpty(userId) == false && ownerId == userId);

        var status = statusService.GetStatus(jobId);

        if (status != null && CanView(status.UserId))
        {
            return Ok(status);
        }

        var metadata = await fetchResultsService.GetMetadataAsync(jobId);

        if (metadata != null && CanView(metadata.UserId))
        {
            return Ok(new JobStatus(100, true)
            {
                Incomplete = metadata.IsIncomplete,
                Message = metadata.IsIncomplete ? "Неполный результат: сбор был остановлен досрочно. Всё, что успели собрать, сохранено." : null,
            });
        }

        return Ok(new JobStatus(0, false)
        {
            Failed = true,
            Message = "Задача не найдена. Возможно, сервер перезапускался во время сбора – запустите сбор ещё раз.",
        });
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

            var json = JsonSerializer.Serialize(model, JsonConfiguration.Export);
            var byteArray = Encoding.UTF8.GetBytes(json);

            logger.LogInformation("Данные успешно сериализованы. Размер: {ByteArrayLength} байт", byteArray.Length);

            return File(byteArray, "application/json", dataPathService.GetExportFileName());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при сохранении данных");
            TempData["Error"] = "Произошла ошибка при сохранении данных";
            return StatusCode(500, "Ошибка сервера");
        }
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> LoadData(string? jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return RedirectToAction("Index");
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            TempData["Error"] = "Пользователь не аутентифицирован.";
            return RedirectToAction("Index");
        }

        logger.LogInformation("Начало загрузки данных для задачи: {JobId}", jobId);

        try
        {
            var metadata = await fetchResultsService.GetMetadataAsync(jobId);

            if (metadata == null)
            {
                logger.LogWarning("Результат не найден для задачи: {JobId}", jobId);
                TempData["Error"] = "Файл с результатами не найден";
                return RedirectToAction("Index");
            }

            var isAdmin = User.HasClaim("IsAdmin", "true");

            if (metadata.UserId != userId && !isAdmin)
            {
                logger.LogWarning("Попытка доступа к чужому результату. UserId: {UserId}, JobId: {JobId}", userId, jobId);
                TempData["Error"] = "Нет прав для просмотра этого результата";
                return RedirectToAction("Index");
            }

            if (isAdmin && metadata.UserId != userId)
            {
                logger.LogInformation("Администратор {AdminUserId} просматривает результат пользователя {OwnerUserId}, JobId: {JobId}",
                    userId, metadata.UserId, jobId);
            }

            var model = await fetchResultsService.GetFetchResultAsync(jobId);

            if (model == null)
            {
                logger.LogWarning("Результат не найден для задачи: {JobId}", jobId);
                TempData["Error"] = "Файл с результатами не найден";
                return RedirectToAction("Index");
            }

            logger.LogInformation("Успешно загружены данные для задачи: {JobId}", jobId);
            return View("Comments", model);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при загрузке данных для задачи: {JobId}", jobId);
            TempData["Error"] = "Ошибка при обработке файла";
            return RedirectToAction("Index");
        }
    }

    [HttpPost]
    public async Task<IActionResult> LoadData(IFormFile? jsonFile)
    {
        logger.LogInformation("Начало загрузки данных из файла");

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

        try
        {
            using MemoryStream stream = new();
            await jsonFile.CopyToAsync(stream);
            var json = Encoding.UTF8.GetString(stream.ToArray());

            var model = JsonSerializer.Deserialize<YouTubeCommentsViewModel>(json, JsonConfiguration.Default);

            if (model == null)
            {
                TempData["Error"] = "Некорректный формат JSON-файла";
                return View("Index");
            }

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
}
