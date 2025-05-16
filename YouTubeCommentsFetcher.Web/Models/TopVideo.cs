namespace YouTubeCommentsFetcher.Web.Models;

public class TopVideo
{
    public required string VideoTitle { get; init; }
    public required string VideoId { get; init; }
    public required long? CommentsCount { get; init; }
    public int RepliesCount { get; init; }
    public int TotalInteractions { get; init; }
    public required string ThumbnailUrl { get; init; }
}
