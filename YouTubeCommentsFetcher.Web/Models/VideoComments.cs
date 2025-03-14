namespace YouTubeCommentsFetcher.Web.Models;

public class VideoComments
{
    public required string VideoTitle { get; init; }
    public required string VideoUrl { get; init; }
    public required string ThumbnailUrl { get; set; }
    public required string VideoId { get; set; }
    public required List<Comment> Comments { get; set; }
}
