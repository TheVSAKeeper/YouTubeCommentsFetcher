namespace YouTubeCommentsFetcher.Core.Services;

public enum YouTubeApiErrorKind
{
    None = 0,
    QuotaExceeded = 1,
    RateLimited = 2,
}

public class YouTubeApiException(YouTubeApiErrorKind kind, Exception innerException)
    : Exception($"YouTube API error: {kind}", innerException)
{
    public YouTubeApiErrorKind Kind { get; } = kind;
}
