namespace YouTubeCommentsFetcher.Web.Models;

public record PlaylistPage(List<PlaylistVideo> Videos, string? NextPageToken, int? TotalResults);
