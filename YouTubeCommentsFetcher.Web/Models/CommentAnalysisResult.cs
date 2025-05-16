namespace YouTubeCommentsFetcher.Web.Models;

public class CommentAnalysisResult
{
    public Top<TopAuthor> TopAuthors { get; init; } = new();
    public List<TopComment> TopCommentsByReplies { get; init; } = [];
    public List<TopComment> TopCommentsByLikes { get; init; } = [];
    public Top<TopWord> MostUsedWords { get; init; } = new();
}
