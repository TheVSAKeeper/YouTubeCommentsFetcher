namespace YouTubeCommentsFetcher.Web.Models;

public class VideoComments
{
    public required string VideoTitle { get; init; }
    public required string VideoUrl { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string VideoId { get; init; }
    public required List<Comment> Comments { get; init; }
}
