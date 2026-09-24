using Quartz;
using YouTubeCommentsFetcher.Core.Services;

namespace YouTubeCommentsFetcher.Web.Services;

public class FetchCommentsJob(CommentsFetcher commentsFetcher, IJobStatusService statusService) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var dataMap = context.MergedJobDataMap;
        var jobId = dataMap.GetString("jobId")!;
        var channelId = dataMap.GetString("channelId")!;
        var maxPages = dataMap.GetInt("maxPages");
        var userId = dataMap.GetString("userId");

        var outcome = await commentsFetcher.RunAsync(jobId, channelId, maxPages, userId,
            percent => statusService.ReportProgress(jobId, percent));

        switch (outcome.Status)
        {
            case FetchOutcomeStatus.Completed:
                statusService.MarkCompleted(jobId);
                break;

            case FetchOutcomeStatus.Incomplete:
                statusService.MarkIncomplete(jobId, outcome.Message!);
                break;

            default:
                statusService.MarkFailed(jobId, outcome.Message!);
                break;
        }
    }
}
