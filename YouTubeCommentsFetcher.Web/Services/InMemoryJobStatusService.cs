using System.Collections.Concurrent;

namespace YouTubeCommentsFetcher.Web.Services;

public interface IJobStatusService
{
    string StartOrGetRunning(string jobId, string channelId, string userId, int maxPages);
    void ReportProgress(string jobId, int percent);
    void MarkCompleted(string jobId);
    void MarkIncomplete(string jobId, string message);
    void MarkFailed(string jobId, string message);
    JobStatus? GetStatus(string jobId);
    Dictionary<string, JobStatus> GetAllActiveJobs();
    Dictionary<string, JobStatus> GetUserActiveJobs(string userId);
}

public class InMemoryJobStatusService : IJobStatusService
{
    private readonly ConcurrentDictionary<string, JobStatus> _statuses = new();
    private readonly Lock _startLock = new();

    public string StartOrGetRunning(string jobId, string channelId, string userId, int maxPages)
    {
        lock (_startLock)
        {
            var running = _statuses.FirstOrDefault(kvp => kvp.Value.IsActive && kvp.Value.ChannelId == channelId);

            if (running.Key != null)
            {
                return running.Key;
            }

            _statuses[jobId] = new(0, false, DateTime.UtcNow, channelId, userId) { MaxPages = maxPages };
            return jobId;
        }
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
            (_, old) => old with { Progress = 100, Completed = true });
    }

    public void MarkIncomplete(string jobId, string message)
    {
        _statuses.AddOrUpdate(jobId,
            new JobStatus(100, true) { Incomplete = true, Message = message },
            (_, old) => old with { Progress = 100, Completed = true, Incomplete = true, Message = message });
    }

    public void MarkFailed(string jobId, string message)
    {
        _statuses.AddOrUpdate(jobId,
            new JobStatus(0, false) { Failed = true, Message = message },
            (_, old) => old with { Failed = true, Message = message });
    }

    public JobStatus? GetStatus(string jobId)
    {
        return _statuses.TryGetValue(jobId, out var status)
            ? status
            : null;
    }

    public Dictionary<string, JobStatus> GetAllActiveJobs()
    {
        return _statuses
            .Where(kvp => kvp.Value.IsActive)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public Dictionary<string, JobStatus> GetUserActiveJobs(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new();
        }

        return _statuses
            .Where(kvp => kvp.Value.IsActive && kvp.Value.UserId == userId)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}
