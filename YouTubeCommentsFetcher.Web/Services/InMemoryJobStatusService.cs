using System.Collections.Concurrent;

namespace YouTubeCommentsFetcher.Web.Services;

public interface IJobStatusService
{
    void Init(string jobId);
    void ReportProgress(string jobId, int percent);
    void MarkCompleted(string jobId);
    JobStatus GetStatus(string jobId);
}

public class InMemoryJobStatusService : IJobStatusService
{
    private readonly ConcurrentDictionary<string, JobStatus> _statuses = new();

    public void Init(string jobId)
    {
        _statuses[jobId] = new(0, false);
    }

    public void ReportProgress(string jobId, int percent)
    {
        _statuses.AddOrUpdate(jobId,
            new JobStatus(percent, false),
            (_, old) => old with { Progress = percent });
    }

    public void MarkCompleted(string jobId)
    {
        _statuses.AddOrUpdate(jobId,
            new JobStatus(100, true),
            (_, _) => new(100, true));
    }

    public JobStatus GetStatus(string jobId)
    {
        return _statuses.TryGetValue(jobId, out var status)
            ? status
            : new(0, false);
    }
}
