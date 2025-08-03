using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using YouTubeCommentsFetcher.Web.Models;
using YouTubeCommentsFetcher.Web.Services;

namespace YouTubeCommentsFetcher.Web.Controllers;

/// <summary>
/// Контроллер для административных функций
/// </summary>
[Authorize]
public class AdminController(
    IFetchResultsService fetchResultsService,
    IApiAuthService apiAuthService,
    ILogger<AdminController> logger) : Controller
{
    /// <summary>
    /// Страница со списком результатов выборки всех пользователей (только для администраторов)
    /// </summary>
    public async Task<IActionResult> AllResults()
    {
        try
        {
            var apiKey = User.FindFirst("ApiKey")?.Value;

            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogWarning("Попытка доступа к административной панели без API ключа");
                TempData["Error"] = "Доступ запрещен.";
                return RedirectToAction("Index", "Home");
            }

            if (apiAuthService.IsAdminApiKey(apiKey) == false)
            {
                logger.LogWarning("Попытка доступа к административной панели с неадминистративным API ключом: {ApiKey}", apiKey);
                TempData["Error"] = "Доступ запрещен. Только администраторы могут просматривать эту страницу.";
                return RedirectToAction("Index", "Home");
            }

            var allResults = await fetchResultsService.GetAllFetchResultsAsync();
            var statistics = await fetchResultsService.GetStatisticsAsync();
            var allUsers = await apiAuthService.GetAllUsersAsync();

            var userNames = allUsers.ToDictionary(u => u.ApiKey, u => u.UserName);

            var model = new AdminAllResultsViewModel
            {
                Results = allResults,
                Statistics = statistics,
                UserNames = userNames,
            };

            logger.LogInformation("Администратор {UserName} просматривает все результаты ({Count} результатов)",
                User.FindFirst(ClaimTypes.Name)?.Value, allResults.Count);

            return View(model);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при загрузке административной панели");
            TempData["Error"] = "Ошибка при загрузке данных";
            return RedirectToAction("Index", "Home");
        }
    }
}
