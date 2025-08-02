namespace YouTubeCommentsFetcher.Web.Services;

public record JobStatus(int Progress, bool Completed, DateTime StartTime = default, string? ChannelId = null)
{
    public DateTime StartTime { get; init; } = StartTime == default ? DateTime.UtcNow : StartTime;
}
