namespace YouTubeCommentsFetcher.Web.Models;

public class VideoAnalysisResult
{
    public List<TopVideo> TopCommentedVideos { get; init; } = [];
    public List<TopVideo> TopLikedCommentsVideos { get; init; } = [];
    public List<TopVideo> TopRepliedVideos { get; init; } = [];
    public List<TopVideo> TopInteractiveVideos { get; init; } = [];
}
