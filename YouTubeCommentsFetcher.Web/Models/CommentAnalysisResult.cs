namespace YouTubeCommentsFetcher.Web.Models;

public class CommentAnalysisResult
{
    public Top<TopAuthor> TopAuthors { get; set; } = new();
    public List<TopComment> TopCommentsByReplies { get; set; } = new();
    public List<TopComment> TopCommentsByLikes { get; set; } = new();
    public Top<TopWord> MostUsedWords { get; set; } = new();
}
