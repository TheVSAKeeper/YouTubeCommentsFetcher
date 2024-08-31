namespace YouTubeCommentsFetcher.Web.Models;

public class Comment
{
    public required string AuthorDisplayName { get; init; }
    public required string TextDisplay { get; init; }
    public required DateTime? PublishedAt { get; init; }
    public List<Comment> Replies { get; init; } = [];
}
