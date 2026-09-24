namespace YouTubeCommentsFetcher.Core.Services;

public record JobStatus(int Progress, bool Completed, DateTime StartTime = default, string? ChannelId = null, string? UserId = null)
{
    public DateTime StartTime { get; init; } = StartTime == default ? DateTime.UtcNow : StartTime;
    public int MaxPages { get; init; }
    public bool Failed { get; init; }
    public bool Incomplete { get; init; }
    public string? Message { get; init; }
    public bool IsActive => Completed == false && Failed == false;
}
