using Microsoft.AspNetCore.Mvc;
using YouTubeCommentsFetcher.Web.Models;
using YouTubeCommentsFetcher.Web.Services;

namespace YouTubeCommentsFetcher.Web.Controllers;

/// <summary>
/// Контроллер для управления результатами выборки комментариев
/// </summary>
public class FetchResultsController(
    IFetchResultsService fetchResultsService,
    IJobStatusService jobStatusService,
    ILogger<FetchResultsController> logger) : Controller
{
    /// <summary>
    /// Страница со списком всех результатов выборки
    /// </summary>
    public async Task<IActionResult> Index()
    {
        try
        {
            var results = await fetchResultsService.GetAllFetchResultsAsync();
            var statistics = await fetchResultsService.GetStatisticsAsync();
            var activeJobs = jobStatusService.GetAllActiveJobs();

            var runningJobs = activeJobs.Select(kvp => new RunningJobInfo
                {
                    JobId = kvp.Key,
                    Progress = kvp.Value.Progress,
                    StartTime = kvp.Value.StartTime,
                    ChannelId = kvp.Value.ChannelId,
                })
                .ToList();

            var model = new FetchResultsIndexViewModel
            {
                Results = results,
                Statistics = statistics,
                RunningJobs = runningJobs,
            };

            return View(model);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при загрузке списка результатов");
            TempData["Error"] = "Ошибка при загрузке данных";
            return RedirectToAction("Index", "Home");
        }
    }

    /// <summary>
    /// Удалить результат выборки
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Delete(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return BadRequest("Не указан идентификатор задачи");
        }

        try
        {
            var deleted = await fetchResultsService.DeleteFetchResultAsync(jobId);

            if (deleted)
            {
                logger.LogInformation("Результат выборки удален: {JobId}", jobId);
                TempData["Success"] = "Результат успешно удален";
            }
            else
            {
                logger.LogWarning("Результат выборки не найден для удаления: {JobId}", jobId);
                TempData["Warning"] = "Результат не найден";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при удалении результата: {JobId}", jobId);
            TempData["Error"] = "Ошибка при удалении результата";
        }

        return RedirectToAction("Index");
    }

    /// <summary>
    /// Удалить результаты старше указанного количества дней
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> DeleteOlderThan(int days)
    {
        if (days <= 0)
        {
            return BadRequest("Количество дней должно быть положительным");
        }

        try
        {
            var deletedCount = await fetchResultsService.DeleteOlderThanAsync(days);

            logger.LogInformation("Удалено {DeletedCount} результатов старше {Days} дней", deletedCount, days);

            if (deletedCount > 0)
            {
                TempData["Success"] = $"Удалено {deletedCount} результатов старше {days} дней";
            }
            else
            {
                TempData["Info"] = $"Не найдено результатов старше {days} дней";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при удалении старых результатов: {Days} дней", days);
            TempData["Error"] = "Ошибка при удалении старых результатов";
        }

        return RedirectToAction("Index");
    }

    /// <summary>
    /// Перестроить индекс метаданных
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RebuildIndex()
    {
        try
        {
            var processedCount = await fetchResultsService.RebuildIndexAsync();

            logger.LogInformation("Индекс перестроен. Обработано файлов: {ProcessedCount}", processedCount);
            TempData["Success"] = $"Индекс перестроен. Обработано файлов: {processedCount}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при перестройке индекса");
            TempData["Error"] = "Ошибка при перестройке индекса";
        }

        return RedirectToAction("Index");
    }

    /// <summary>
    /// Получить статистику в формате JSON
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStatistics()
    {
        try
        {
            var statistics = await fetchResultsService.GetStatisticsAsync();
            return Json(statistics);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при получении статистики");
            return StatusCode(500, "Ошибка при получении статистики");
        }
    }

    /// <summary>
    /// Проверить существование результата
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Exists(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return BadRequest("Не указан идентификатор задачи");
        }

        try
        {
            var exists = await fetchResultsService.ExistsAsync(jobId);
            return Json(new { exists });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при проверке существования результата: {JobId}", jobId);
            return StatusCode(500, "Ошибка при проверке существования результата");
        }
    }

    /// <summary>
    /// Получить метаданные результата
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMetadata(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return BadRequest("Не указан идентификатор задачи");
        }

        try
        {
            var metadata = await fetchResultsService.GetMetadataAsync(jobId);

            if (metadata == null)
            {
                return NotFound("Результат не найден");
            }

            return Json(metadata);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при получении метаданных результата: {JobId}", jobId);
            return StatusCode(500, "Ошибка при получении метаданных");
        }
    }
}
