using Microsoft.Extensions.DependencyInjection;
using YouTubeCommentsFetcher.Core;
using YouTubeCommentsFetcher.Core.Models;
using YouTubeCommentsFetcher.Core.Services;

namespace YouTubeCommentsFetcher.Cli;

public static class CliCommands
{
    public const int SuccessExitCode = 0;
    public const int FailureExitCode = 1;
    public const int IncompleteExitCode = 2;

    private const string UnknownOwnerName = "неизвестен";

    public static Task<int> FetchAsync(string? dataPath, string channelId, string userName, int maxPages)
    {
        return RunAsync(async () =>
        {
            channelId = channelId.Trim();

            if (channelId.Length == 0)
            {
                throw new CliException("Не указан идентификатор канала.");
            }

            using var host = CliHost.Create(dataPath);
            var user = await FindUserAsync(host, userName);

            if (user.IsActive == false)
            {
                throw new CliException($"Пользователь «{user.UserName}» отключён, собирать для него нельзя.");
            }

            if (string.IsNullOrWhiteSpace(host.Configuration[ServiceCollectionExtensions.YouTubeApiKeySetting]))
            {
                throw new CliException("Не задан ключ YouTube API. Задайте его так же, как для веб-приложения: "
                                       + $"dotnet user-secrets или переменная окружения {ServiceCollectionExtensions.YouTubeApiKeySetting}.");
            }

            using var scope = host.Services.CreateScope();
            var fetcher = scope.ServiceProvider.GetRequiredService<CommentsFetcher>();
            var jobId = Guid.NewGuid().ToString();

            Console.WriteLine($"Сбор комментариев канала {channelId} для пользователя «{user.UserName}», страниц плейлиста: {maxPages}.");

            var progress = new ConsoleProgress();
            var outcome = await fetcher.RunAsync(jobId, channelId, maxPages, user.ApiKey, progress.Report);
            progress.Finish();

            switch (outcome.Status)
            {
                case FetchOutcomeStatus.Completed:
                    Console.WriteLine($"Готово. Результат {jobId} сохранён в {host.DataDirectory}.");
                    return SuccessExitCode;

                case FetchOutcomeStatus.Incomplete:
                    Console.Error.WriteLine(outcome.Message);
                    Console.WriteLine($"Результат {jobId} сохранён в {host.DataDirectory}.");
                    return IncompleteExitCode;

                default:
                    Console.Error.WriteLine(outcome.Message);
                    return FailureExitCode;
            }
        });
    }

    public static Task<int> ListAsync(string? dataPath, string? userName)
    {
        return RunAsync(async () =>
        {
            using var host = CliHost.Create(dataPath);
            var fetchResultsService = host.Services.GetRequiredService<IFetchResultsService>();

            var results = userName == null
                ? await fetchResultsService.GetAllFetchResultsAsync()
                : await fetchResultsService.GetUserFetchResultsAsync((await FindUserAsync(host, userName)).ApiKey);

            if (results.Count == 0)
            {
                Console.WriteLine($"Сохранённых результатов нет ({host.DataDirectory}).");
                return SuccessExitCode;
            }

            var ownerNames = await GetOwnerNamesAsync(host);

            var rows = results.Select(result => (
                    Result: result,
                    Owner: result.UserId != null && ownerNames.TryGetValue(result.UserId, out var name) ? name : UnknownOwnerName))
                .ToList();

            var ownerWidth = rows.Max(row => row.Owner.Length);

            Console.WriteLine($"{"Результат",-36}  {"Создан",-16}  {"Видео",6}  {"Коммент.",8}  {"Владелец".PadRight(ownerWidth)}  Канал");

            foreach (var (result, owner) in rows)
            {
                var channel = result.ChannelName ?? result.ChannelId;
                var incomplete = result.IsIncomplete ? " (неполный)" : string.Empty;

                Console.WriteLine($"{result.JobId,-36}  {result.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}  {result.TotalVideos,6}  {result.TotalComments,8}  {owner.PadRight(ownerWidth)}  {channel}{incomplete}");
            }

            Console.WriteLine($"Всего: {results.Count}.");
            return SuccessExitCode;
        });
    }

    public static Task<int> StatsAsync(string? dataPath, string result)
    {
        return RunAsync(async () =>
        {
            if (Guid.TryParse(result.Trim(), out var jobGuid) == false)
            {
                throw new CliException($"«{result}» – не идентификатор результата. Возьмите его из первой колонки вывода list.");
            }

            var jobId = jobGuid.ToString();

            using var host = CliHost.Create(dataPath);
            var fetchResultsService = host.Services.GetRequiredService<IFetchResultsService>();
            var model = await fetchResultsService.GetFetchResultAsync(jobId);

            if (model == null)
            {
                throw new CliException($"Результат {jobId} не найден или не читается в {host.DataDirectory}.");
            }

            var metadata = (await fetchResultsService.GetAllFetchResultsAsync()).FirstOrDefault(m => m.JobId == jobId);
            var channel = model.ChannelName ?? model.ChannelId ?? metadata?.ChannelName ?? metadata?.ChannelId ?? "неизвестен";
            var statistics = model.Statistics ?? Analyzer.Analyze(model.Comments, model.Videos);

            Console.WriteLine($"Результат {jobId}, канал {channel}{(model.IsIncomplete ? " (неполный)" : string.Empty)}");
            Console.WriteLine($"Видео: {model.Videos.Count}, комментариев: {statistics.TotalComments}, ответов: {statistics.TotalReplies}, авторов: {statistics.UniqueAuthors}");
            Console.WriteLine($"В среднем комментариев на видео: {statistics.AverageCommentsPerVideo}");

            if (statistics.OldestCommentDate != null && statistics.NewestCommentDate != null)
            {
                Console.WriteLine($"Комментарии с {statistics.OldestCommentDate.Value.ToLocalTime():yyyy-MM-dd} по {statistics.NewestCommentDate.Value.ToLocalTime():yyyy-MM-dd}");
            }

            PrintAuthors("Авторы по комментариям", statistics.TopAuthorsByComments.ByComments);
            PrintAuthors("Авторы по ответам", statistics.TopAuthorsByComments.ByReplies);
            PrintAuthors("Авторы по всей активности", statistics.TopAuthorsByComments.ByActivity);
            PrintWords("Частые слова", statistics.MostUsedWords.ByActivity);
            PrintVideos("Самые комментируемые видео", statistics.TopCommentedVideos);

            return SuccessExitCode;
        });
    }

    private static async Task<int> RunAsync(Func<Task<int>> command)
    {
        try
        {
            return await command();
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return FailureExitCode;
        }
    }

    private static async Task<ApiUser> FindUserAsync(CliHost host, string userName)
    {
        userName = userName.Trim();

        var authService = host.Services.GetRequiredService<IApiAuthService>();
        var (users, isComplete) = await authService.GetAllUsersWithCompletenessAsync();
        var matches = users.Where(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            throw new CliException($"Пользователей с именем «{userName}» несколько ({matches.Count}), по имени владельца не выбрать.");
        }

        if (isComplete == false)
        {
            throw new CliException($"Файл пользователей не найден или не читается в {host.DataDirectory}. Проверьте каталог данных (--data-path).");
        }

        var knownNames = string.Join(", ", users.Select(u => $"«{u.UserName}»"));
        throw new CliException($"Пользователь «{userName}» не найден. Известные пользователи: {knownNames}.");
    }

    private static async Task<Dictionary<string, string>> GetOwnerNamesAsync(CliHost host)
    {
        var users = await host.Services.GetRequiredService<IApiAuthService>().GetAllUsersAsync();

        return users.GroupBy(u => u.ApiKey)
            .ToDictionary(g => g.Key, g => g.First().UserName);
    }

    private static void PrintAuthors(string title, List<TopAuthor> authors)
    {
        PrintTop(title, authors.Select(a => (a.AuthorName, (long?)a.CommentsCount)));
    }

    private static void PrintWords(string title, List<TopWord> words)
    {
        PrintTop(title, words.Select(w => (w.Word, (long?)w.Count)));
    }

    private static void PrintVideos(string title, List<TopVideo> videos)
    {
        PrintTop(title, videos.Select(v => (v.VideoTitle, v.CommentsCount)));
    }

    private static void PrintTop(string title, IEnumerable<(string Name, long? Count)> items)
    {
        var list = items.ToList();

        if (list.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"{title}:");

        for (var i = 0; i < list.Count; i++)
        {
            Console.WriteLine($"{i + 1,3}. {list[i].Name} – {list[i].Count}");
        }
    }

    private sealed class ConsoleProgress
    {
        private int _lastPercent = -1;

        public void Report(int percent)
        {
            if (percent == _lastPercent)
            {
                return;
            }

            _lastPercent = percent;

            if (Console.IsOutputRedirected)
            {
                Console.WriteLine($"Собрано {percent}%");
            }
            else
            {
                Console.Write($"\rСобрано {percent}%   ");
            }
        }

        public void Finish()
        {
            if (_lastPercent >= 0 && Console.IsOutputRedirected == false)
            {
                Console.WriteLine();
            }
        }
    }
}
