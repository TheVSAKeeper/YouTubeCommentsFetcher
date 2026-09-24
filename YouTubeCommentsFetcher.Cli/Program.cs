using System.CommandLine;
using System.Text;
using YouTubeCommentsFetcher.Cli;
using YouTubeCommentsFetcher.Core.Services;

Console.OutputEncoding = Encoding.UTF8;

var dataPathOption = new Option<string?>("--data-path")
{
    Description = "Каталог данных; по умолчанию – настройка DataPath:DataDirectory относительно текущего каталога (YouTubeCommentsFetcher.Web/Data)",
    Recursive = true,
};

var channelArgument = new Argument<string>("channel")
{
    Description = "Идентификатор канала YouTube",
};

var fetchUserOption = new Option<string>("--user")
{
    Description = "Имя пользователя, которому будет принадлежать результат",
    Required = true,
};

var maxPagesOption = new Option<int>("--max-pages")
{
    Description = $"Сколько страниц плейлиста загрузок собрать, по {YouTubeService.PlaylistPageSize} видео (1–{CommentsFetcher.MaxPlaylistPages})",
    DefaultValueFactory = _ => 1,
};

maxPagesOption.Validators.Add(result =>
{
    var maxPages = result.GetValueOrDefault<int>();

    if (maxPages < 1 || maxPages > CommentsFetcher.MaxPlaylistPages)
    {
        result.AddError($"--max-pages должно быть от 1 до {CommentsFetcher.MaxPlaylistPages}.");
    }
});

var fetchCommand = new Command("fetch",
    "Собрать комментарии канала и сохранить результат для пользователя. "
    + $"Код выхода: {CliCommands.SuccessExitCode} – готово, {CliCommands.FailureExitCode} – сбой, {CliCommands.IncompleteExitCode} – сохранён неполный результат")
{
    channelArgument,
    fetchUserOption,
    maxPagesOption,
};

fetchCommand.SetAction((parseResult, _) => CliCommands.FetchAsync(
    parseResult.GetValue(dataPathOption),
    parseResult.GetValue(channelArgument)!,
    parseResult.GetValue(fetchUserOption)!,
    parseResult.GetValue(maxPagesOption)));

var listUserOption = new Option<string?>("--user")
{
    Description = "Показать только результаты этого пользователя",
};

var listCommand = new Command("list", "Показать сохранённые результаты")
{
    listUserOption,
};

listCommand.SetAction((parseResult, _) => CliCommands.ListAsync(
    parseResult.GetValue(dataPathOption),
    parseResult.GetValue(listUserOption)));

var resultArgument = new Argument<string>("result")
{
    Description = "Идентификатор результата из первой колонки вывода list",
};

var statsCommand = new Command("stats", "Показать статистику авторов сохранённого результата")
{
    resultArgument,
};

statsCommand.SetAction((parseResult, _) => CliCommands.StatsAsync(
    parseResult.GetValue(dataPathOption),
    parseResult.GetValue(resultArgument)!));

var rootCommand = new RootCommand("Сбор и просмотр комментариев YouTube без веб-интерфейса")
{
    fetchCommand,
    listCommand,
    statsCommand,
};

rootCommand.Options.Add(dataPathOption);

return await rootCommand.Parse(args).InvokeAsync();
