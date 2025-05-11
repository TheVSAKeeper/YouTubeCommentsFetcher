namespace YouTubeCommentsFetcher.Web.Models;

public class CommentStatistics
{
    public int TotalComments { get; init; }
    public int UniqueAuthors { get; init; }
    public double AverageCommentsPerVideo { get; init; }
    public DateTime? OldestCommentDate { get; init; }
    public DateTime? NewestCommentDate { get; init; }

    public Top<TopAuthor> TopAuthorsByComments => CommentAnalysis.TopAuthors;
    public List<TopComment> TopCommentsByReplies => CommentAnalysis.TopCommentsByReplies;
    public List<TopComment> TopCommentsByLikes => CommentAnalysis.TopCommentsByLikes;
    public Top<TopWord> MostUsedWords => CommentAnalysis.MostUsedWords;

    public List<TopVideo> TopCommentedVideos { get; set; } = [];
    public List<TopVideo> TopLikedCommentsVideos { get; set; } = [];
    public List<TopVideo> TopRepliedVideos { get; set; } = [];

    public List<TopVideo> TopInteractiveVideos { get; set; } = [];
    public int TotalReplies { get; set; }

    public CommentAnalysisResult CommentAnalysis { get; set; }
}
