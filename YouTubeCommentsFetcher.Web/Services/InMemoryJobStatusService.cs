using System.Collections.Concurrent;

namespace YouTubeCommentsFetcher.Web.Services;

public interface IJobStatusService
{
    void Init(string jobId);
    void Init(string jobId, string? channelId);
    void ReportProgress(string jobId, int percent);
    void MarkCompleted(string jobId);
    JobStatus GetStatus(string jobId);
    Dictionary<string, JobStatus> GetAllActiveJobs();
}

public class InMemoryJobStatusService : IJobStatusService
{
    private readonly ConcurrentDictionary<string, JobStatus> _statuses = new();

    public void Init(string jobId)
    {
        _statuses[jobId] = new(0, false);
    }

    public void Init(string jobId, string? channelId)
    {
        _statuses[jobId] = new(0, false, DateTime.UtcNow, channelId);
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

    public Dictionary<string, JobStatus> GetAllActiveJobs()
    {
        return _statuses
            .Where(kvp => kvp.Value.Completed == false)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}
