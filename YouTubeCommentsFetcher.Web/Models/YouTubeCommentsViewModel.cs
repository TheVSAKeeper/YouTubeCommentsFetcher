namespace YouTubeCommentsFetcher.Web.Models;

public class YouTubeCommentsViewModel
{
    public List<VideoComments> Videos { get; set; } = [];
    public List<Comment> Comments { get; set; } = [];
}
