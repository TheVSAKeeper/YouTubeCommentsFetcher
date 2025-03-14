namespace YouTubeCommentsFetcher.Web.Models;

public class CommentAnalysisResult
{
    public List<TopAuthor> TopAuthors { get; set; } = new();
    public List<TopComment> TopCommentsByReplies { get; set; } = new();
    public List<TopComment> TopCommentsByLikes { get; set; } = new();
    public List<string> MostUsedWords { get; set; } = new();
}