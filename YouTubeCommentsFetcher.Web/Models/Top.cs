namespace YouTubeCommentsFetcher.Web.Models;

public class Top<T>
{
    public List<T> ByComments { get; init; } = [];
    public List<T> ByReplies { get; init; } = [];
    public List<T> ByActivity { get; init; } = [];
}
