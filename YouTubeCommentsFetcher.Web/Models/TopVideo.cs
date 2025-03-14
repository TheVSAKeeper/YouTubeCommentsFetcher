namespace YouTubeCommentsFetcher.Web.Models;

public class TopVideo
{
    public required string VideoTitle { get; init; }
    public required string VideoId { get; init; }
    public required long? CommentsCount { get; init; }
    public int RepliesCount { get; set; }
    public int TotalInteractions { get; set; }
    public required string ThumbnailUrl { get; init; }
}
