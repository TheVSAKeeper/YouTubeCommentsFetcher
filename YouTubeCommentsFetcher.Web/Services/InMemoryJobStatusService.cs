using System.Collections.Concurrent;

namespace YouTubeCommentsFetcher.Web.Services;

public interface IJobStatusService
{
    void Init(string jobId);
    void ReportProgress(string jobId, int percent);
    void MarkCompleted(string jobId);
    void ReportError(string jobId, string errorMessage);
    JobStatus GetStatus(string jobId);
}

public class InMemoryJobStatusService : IJobStatusService
{
    private readonly ConcurrentDictionary<string, JobStatus> _statuses = new();

    public void Init(string jobId)
    {
        _statuses[jobId] = new(0, false, null);
    }

    public void ReportProgress(string jobId, int percent)
    {
        _statuses.AddOrUpdate(jobId,
            new JobStatus(percent, false, null),
            (_, old) => old with { Progress = percent });
    }

    public void MarkCompleted(string jobId)
    {
        _statuses.AddOrUpdate(jobId,
            new JobStatus(100, true, null),
            (_, _) => new(100, true, null));
    }

    public void ReportError(string jobId, string errorMessage)
    {
        _statuses.AddOrUpdate(jobId,
            new JobStatus(0, true, errorMessage),
            (_, old) => old with { Completed = true, ErrorMessage = errorMessage });
    }

    public JobStatus GetStatus(string jobId)
    {
        return _statuses.TryGetValue(jobId, out var status)
            ? status
            : new(0, false, null);
    }
}

public record JobStatus(int Progress, bool Completed, string? ErrorMessage);
