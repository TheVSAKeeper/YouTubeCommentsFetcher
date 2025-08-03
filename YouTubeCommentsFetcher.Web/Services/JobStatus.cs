namespace YouTubeCommentsFetcher.Web.Services;

public record JobStatus(int Progress, bool Completed, DateTime StartTime = default, string? ChannelId = null, string? UserId = null)
{
    public DateTime StartTime { get; init; } = StartTime == default ? DateTime.UtcNow : StartTime;
}
