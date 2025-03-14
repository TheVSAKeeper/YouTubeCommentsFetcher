namespace YouTubeCommentsFetcher.Web.Models;

public class TopComment
{
    public required string CommentText { get; init; }
    public required long? Count { get; init; }
    public required string Author { get; init; }
}
