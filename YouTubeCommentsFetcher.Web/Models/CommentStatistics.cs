namespace YouTubeCommentsFetcher.Web.Models;

public class CommentStatistics
{
    public int TotalComments { get; init; }
    public int UniqueAuthors { get; init; }
    public double AverageCommentsPerVideo { get; init; }
    public DateTime? OldestCommentDate { get; init; }
    public DateTime? NewestCommentDate { get; init; }

    public List<TopAuthor> TopAuthorsByComments { get; set; } = [];
    public List<TopComment> TopCommentsByReplies { get; set; } = [];
    public List<TopComment> TopCommentsByLikes { get; set; } = [];
    public List<string> MostUsedWords { get; set; } = [];

    public List<TopVideo> TopCommentedVideos { get; set; } = [];
    public List<TopVideo> TopLikedCommentsVideos { get; set; } = [];
    public List<TopVideo> TopRepliedVideos { get; set; } = [];

    public List<TopVideo> TopInteractiveVideos { get; set; } = [];
    public int TotalReplies { get; set; }
}
