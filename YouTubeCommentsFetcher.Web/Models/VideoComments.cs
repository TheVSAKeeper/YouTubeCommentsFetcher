namespace YouTubeCommentsFetcher.Web.Models;

public class VideoComments
{
    public string VideoTitle { get; init; }
    public string VideoUrl { get; init; }
    public string ThumbnailUrl { get; init; }
    public string VideoId { get; init; }
    public List<Comment> Comments { get; init; }
}
