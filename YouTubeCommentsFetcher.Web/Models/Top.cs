namespace YouTubeCommentsFetcher.Web.Models;

public class Top<T>
{
    public List<T> ByComments { get; set; }
    public List<T> ByReplies { get; set; }
    public List<T> ByActivity { get; set; }
}