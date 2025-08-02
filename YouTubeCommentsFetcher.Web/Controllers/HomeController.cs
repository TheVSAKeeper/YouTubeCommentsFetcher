using Microsoft.AspNetCore.Mvc;
using Quartz;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using YouTubeCommentsFetcher.Web.Configuration;
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
    public async Task<IActionResult> FetchCommentsBackground(string channelId, int pageSize = 5, int maxPages = 1)
    {
        if (string.IsNullOrEmpty(channelId))
        {
            TempData["Error"] = "Invalid channel ID.";
            return RedirectToAction("Index");
        }

        var jobId = Guid.NewGuid().ToString();
        statusService.Init(jobId, channelId);

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

        var model = new JobQueuedViewModel
        {
            JobId = jobId,
            DataFileUrl = dataPathService.GetCommentsFileUrl(jobId),
            FileName = dataPathService.GetCommentsFileName(jobId),
        };

        return View(model);
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
    public async Task<IActionResult> LoadData(string? jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return RedirectToAction("Index");
        }

        logger.LogInformation("Начало загрузки данных для задачи: {JobId}", jobId);

        try
        {
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
