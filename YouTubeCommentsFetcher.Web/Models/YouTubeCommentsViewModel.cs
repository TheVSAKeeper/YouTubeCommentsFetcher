namespace YouTubeCommentsFetcher.Web.Models;

public class YouTubeCommentsViewModel
{
    public List<VideoComments> Videos { get; init; } = [];
    public List<Comment> Comments { get; set; } = [];
    public CommentStatistics? Statistics { get; set; }
}
